# Database setup

SmartPOS uses **PostgreSQL 14 or newer** (developed and verified on PostgreSQL 16). Schema changes are
managed by **EF Core migrations**; `database/schema.sql` is a generated copy for review or DBA-run
deployments.

Provider: `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 with EF Core 10.0.12.

## 1. Install and start PostgreSQL

macOS (Homebrew):

```bash
brew install postgresql@16
brew services start postgresql@16        # listens on 5432
```

Windows/Linux: install PostgreSQL 14+ from postgresql.org or your package manager and start the service.

If something else already uses port 5432, run PostgreSQL on another port (`port = 5433` in
`postgresql.conf`) and use that port in the connection string.

## 2. Create the database and an application user

Connect as a superuser (`psql -U postgres`, or just `psql postgres` for a Homebrew install) and run:

```sql
CREATE ROLE smartpos LOGIN PASSWORD '<strong-password>';
CREATE DATABASE smartpos_dev OWNER smartpos;
```

The app user owns only its own database. Production should use a separate role for migrations (DDL)
and a runtime role limited to `SELECT, INSERT, UPDATE, DELETE` on the tables.

## 3. Point the API at it

```bash
cd backend/src/SmartPOS.API
dotnet user-secrets set "ConnectionStrings:SmartPos" \
  "Host=127.0.0.1;Port=5432;Database=smartpos_dev;Username=smartpos;Password=<strong-password>"
```

or set the environment variable `ConnectionStrings__SmartPos`. Never commit the connection string.

## 4. Apply migrations

```bash
cd backend
dotnet tool restore
dotnet ef database update --project src/SmartPOS.Infrastructure --startup-project src/SmartPOS.API
```

`dotnet ef` runs with the same configuration as the API (user-secrets or environment variables).
Alternatively, run the plain script `psql -U smartpos -d smartpos_dev -f database/schema.sql`; it also
records the migration, so a later `dotnet ef database update` sees it as applied.

### Changing the schema

1. Edit the entity in `SmartPOS.Core` and its configuration in `SmartPOS.Infrastructure/Persistence/Configurations`.
2. Add a migration and review the generated code:
   ```bash
   dotnet ef migrations add <Name> --project src/SmartPOS.Infrastructure --startup-project src/SmartPOS.API --output-dir Persistence/Migrations
   ```
3. Apply with `dotnet ef database update`.
4. Regenerate the reference script:
   ```bash
   dotnet ef migrations script --project src/SmartPOS.Infrastructure --startup-project src/SmartPOS.API -o ../database/schema.sql
   ```

PostgreSQL folds unquoted identifiers to lower case, so **column names inside CHECK constraints must be
quoted** (`"\"Price\" >= 0"` in C#); the configurations already do this.

## 5. Accounts and seed data

- **There are no seeded users.** Every account comes from registration (`POST /api/auth/register`, or the
  "Create an account" page): it creates the user and the shop they own in one transaction. There is no
  default password anywhere in the code or migrations.
- **Sample catalogue** (16 products, each with an inventory row and an opening-stock movement; the dairy
  and bakery items get expiry dates) is added to each **newly registered shop** when
  `Database:SeedSampleData` is `true` (Development default). Shops registered with it off start empty.
- **Fabricated demo history** (Development only): with `Database:SeedDemoHistory` on (set in
  `appsettings.Development.json`), each newly registered shop also gets `Database:DemoHistoryDays` (default 90)
  days of invented bills, 16 customers with obviously fake phone numbers (`90000001xx`) and the matching stock
  movements. The generator (`DemoHistory.cs`) is seeded, so the history is the same every time; it shapes
  the data realistically (busier weekends, lunch and evening peaks, slow growth, mostly cash and UPI, a few
  popular products) and keeps the ledger consistent: each product's opening stock is its sample stock plus
  everything sold, so the sales run it down to exactly the sample level. Leave it off in production.
- **Backfilling an existing shop:** `dotnet run --project src/SmartPOS.API -- seed-demo-history --email <account email> [--days 90]`
  (Development only). The history is placed entirely before the shop's first real activity, each product's
  opening-stock movement is moved back to the start so the ledger stays consistent into the real sales, and
  it refuses to run if the shop already has anything dated before its own creation. It is all-or-nothing (one transaction).
- The generator is seeded per shop, so a given shop always gets the same history but two shops never share a
  bill number (`BillNumber` is unique across the whole database).
- Without that option, no sales or customers are seeded.

To start over in development: `DROP DATABASE smartpos_dev;`, recreate it, run `dotnet ef database update`.

## Schema

Every table except `Shops` carries a `ShopId`; see [Multi-shop isolation](#multi-shop-isolation). There is one kind of account: no roles.

| Table | Purpose | Key rules |
|---|---|---|
| `Shops` | A store (tenant) | `ShopName`, `Address` |
| `Users` | The login. Exactly one per shop: the shop's owner | FK → Shops (unique, so 1:1); `Email` unique across **all** shops (it is the login name, stored lower-case); `PasswordHash` (PBKDF2); `EmailVerifiedAt` (null until confirmed) and, while a link is pending, the SHA-256 hash of its secret plus an expiry (never the secret itself); no role column |
| `Customers` | People the shop sells to | Unique `(ShopId, Phone)`; `LoyaltyPoints` (CHECK ≥ 0; the sum of the points its bills earned, see the README for the rule) |
| `Products` | Catalogue | Unique `(ShopId, Sku)` and `(ShopId, Barcode)`; `Category` is plain text; `Price`/`CostPrice` `numeric(18,2)`, CHECK ≥ 0; optional `ExpiryDate` (`date`) |
| `Inventories` | Stock on hand, one row per product | Unique `ProductId` (cascade delete); `StockQuantity` (CHECK ≥ 0), `LowStockThreshold` (default 20), `LastUpdated` |
| `Sales` | One bill per checkout | Unique `BillNumber`; `UserId` (who made the bill), optional `CustomerId`; `Subtotal`, `DiscountPercent`, `DiscountAmount`, **tax rate used**, `TaxAmount`, `TotalAmount`, `LoyaltyPointsEarned` (CHECK ≥ 0), `PaymentMethod` (Cash/Card/UPI/Other), `AmountPaid`, `ChangeAmount`, `Status` (Pending/Completed/Cancelled); money columns `decimal(12,2)`; index `(ShopId, CreatedAt)` |
| `SaleItems` | Lines of a bill | **Snapshot** of product name, SKU, unit price and unit cost at sale time; per-line `DiscountAmount`, `TaxAmount` and `LineTotal` (the lines add up exactly to the bill); CHECK `Quantity > 0`; FK → Sales (CASCADE), → Products (RESTRICT) |
| `StockMovements` | Append-only stock ledger | `QuantityChange`, `QuantityAfter`, `Type` (InitialStock/Sale/Adjustment), `Reason`; optional FK to the sale and user |

The structure follows the `Shop`/`Owner`/`Customer`/`Product`/`Inventory` models plus the `Sales`
and `SaleItems` specification. Differences from those models:

- **No separate `Owner` table.** `Users` is the login and each shop has exactly one user, which is the
  `Owner` of the model (a unique index on `Users.ShopId` keeps it 1:1). `Sales.UserId` points at that user.
- `Sales` also carries `ShopId` and `CustomerId`, which the sales specification did not list.

All timestamps are `timestamp with time zone`, always written and read as **UTC**. "Today" and date filters are interpreted in the
store's time zone (`Store:TimeZoneId`) by the API, not by PostgreSQL.

### Multi-shop isolation

The JWT carries a `shop` claim. The DbContext applies an EF Core **global query filter**
(`ShopId == current shop`) to `Users`, `Customers`, `Products`, `Inventories`, `Sales`,
`StockMovements` (and `SaleItems` through their sale). With no current shop the filter matches
nothing, so forgetting to set one fails closed rather than leaking rows. Code that must span shops
(login, JWT validation, startup seeding) opts out explicitly with `IgnoreQueryFilters()`; that is
the only place it appears. SKU, barcode and customer phone are unique **per shop**, so two shops
can both stock `COLA-500`.

Registering creates a new shop, so any number of shops can share one database.

### How overselling is prevented

A sale runs in one database transaction:

1. Load products and prices (prices never come from the client).
2. For each line, in product-ID order:
   `UPDATE Inventories SET StockQuantity = StockQuantity - @qty WHERE ProductId = @id AND StockQuantity >= @qty`.
   If no row is updated, stock ran out → the transaction rolls back → HTTP 409.
3. Insert the sale, its items and one stock movement per line; commit.

The update runs through the shop filter too, so one shop can never change another shop's stock.

The conditional UPDATE checks and decrements in one statement, and the database holds the row lock
until commit (PostgreSQL's `READ COMMITTED` re-checks the `WHERE` clause after waiting for the lock), so concurrent checkouts for the same product are serialised. Locking in a fixed
order avoids deadlocks between multi-item sales. The CHECK constraint on `Inventories.StockQuantity` is a
final safety net. Verified against PostgreSQL 16 with 12 concurrent sales for 5 units: exactly 5
succeeded (see TESTING.md).
