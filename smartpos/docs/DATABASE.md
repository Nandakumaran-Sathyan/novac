# Database setup

SmartPOS uses **MySQL 8.0.16 or newer** (CHECK constraints are enforced from 8.0.16; verified on
MySQL 9.6). Schema changes are managed by **EF Core migrations**; `database/schema.sql` is a
generated copy for review or DBA-run deployments.

Provider: `MySql.EntityFrameworkCore` 10.0.12 (Oracle's provider; the community Pomelo provider
has no EF Core 10 release yet). MariaDB, including XAMPP's bundled server, is not supported by this provider.

## 1. Install and start MySQL

macOS (Homebrew):

```bash
brew install mysql
brew services start mysql          # listens on 3306
mysql_secure_installation          # set a root password
```

> **Port 3306 already taken?** On this team's Macs, XAMPP often runs MariaDB on 3306. Either stop
> XAMPP's MySQL, or run Homebrew MySQL on another port (add `port=3307` under `[mysqld]` in
> `/opt/homebrew/etc/my.cnf`, restart, and use `Port=3307` in the connection string).

Windows/Linux: install MySQL Community Server 8.x and make sure the service is running.

## 2. Create the database and an application user

Connect as an administrator (`mysql -u root -p`) and run:

```sql
CREATE DATABASE smartpos_dev CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE USER 'smartpos'@'localhost' IDENTIFIED BY '<strong-password>';
CREATE USER 'smartpos'@'127.0.0.1' IDENTIFIED BY '<strong-password>';
GRANT ALL PRIVILEGES ON smartpos_dev.* TO 'smartpos'@'localhost';
GRANT ALL PRIVILEGES ON smartpos_dev.* TO 'smartpos'@'127.0.0.1';
```

The app user only has rights on its own database. Production should use a separate user for
migrations (DDL) and a runtime user limited to `SELECT, INSERT, UPDATE, DELETE`.

## 3. Point the API at it

```bash
cd backend/src/SmartPOS.API
dotnet user-secrets set "ConnectionStrings:SmartPos" \
  "Server=127.0.0.1;Port=3306;Database=smartpos_dev;User=smartpos;Password=<strong-password>"
```

or set the environment variable `ConnectionStrings__SmartPos`. Never commit the connection string.

## 4. Apply migrations

```bash
cd backend
dotnet tool restore
dotnet ef database update --project src/SmartPOS.Infrastructure --startup-project src/SmartPOS.API
```

`dotnet ef` runs with the same configuration as the API (user-secrets or environment variables).

### Changing the schema

1. Edit the entity in `SmartPOS.Core` and its configuration in `SmartPOS.Infrastructure/Persistence/Configurations`.
2. Add a migration and review the generated code:
   ```bash
   dotnet ef migrations add <Name> --project src/SmartPOS.Infrastructure --startup-project src/SmartPOS.API --output-dir Persistence/Migrations
   ```
   (`migrations add` does not connect to the database, but needs a connection string to be
   configured; any syntactically valid value works.)
3. Apply with `dotnet ef database update`.
4. Regenerate the reference script:
   ```bash
   dotnet ef migrations script --project src/SmartPOS.Infrastructure --startup-project src/SmartPOS.API -o ../database/schema.sql
   ```
   Don't use `--idempotent` with this provider: it emits `IF NOT EXISTS … BEGIN … END` blocks,
   which MySQL rejects outside stored programs.

## 5. Seed data

- **Sample catalogue** (6 categories, 16 products with opening-stock movements) is inserted at
  startup when `Database:SeedSampleData` is `true` (Development default) **and** the catalogue is empty.
- **No users or sales are seeded.** The first admin comes from `Bootstrap:AdminEmail` /
  `Bootstrap:AdminPassword` configuration, created only if no admin exists. There is no default
  password anywhere in the code or migrations.
- Seeding is skipped (with a log warning) if the database is unreachable or migrations are pending.

To start over in development: `DROP DATABASE smartpos_dev;`, recreate it, run `dotnet ef database update`.

## Schema

| Table | Purpose | Key rules |
|---|---|---|
| `Users` | Staff accounts | Unique `Email` (stored lower-case); `PasswordHash` (PBKDF2); `Role` stored as text |
| `Categories` | Product grouping | Unique `Name` |
| `Products` | Catalogue + current stock | Unique `Sku`; `decimal(18,2)` prices; CHECK `StockQuantity >= 0` and non-negative prices; FK → Categories (RESTRICT) |
| `Sales` | One row per checkout | Unique `InvoiceNumber`; index on `CreatedAt`; stores subtotal, discount %, discount, **tax rate used**, tax, total; FK → Users (RESTRICT) |
| `SaleItems` | Lines of a sale | **Snapshot** of product name, SKU, unit price and unit cost at sale time; CHECK `Quantity > 0`; FK → Sales (CASCADE), → Products (RESTRICT) |
| `StockMovements` | Append-only stock ledger | `QuantityChange`, `QuantityAfter`, `Type` (InitialStock/Sale/Adjustment), `Reason`; optional FK to the sale and user |

All timestamps are `datetime(6)` in **UTC**. "Today" and date filters are interpreted in the
store's time zone (`Store:TimeZoneId`) by the API, not by MySQL.

### How overselling is prevented

A sale runs in one database transaction:

1. Load products and prices (prices never come from the client).
2. For each line, in product-ID order:
   `UPDATE Products SET StockQuantity = StockQuantity - @qty WHERE Id = @id AND StockQuantity >= @qty`.
   If no row is updated, stock ran out → the transaction rolls back → HTTP 409.
3. Insert the sale, its items and one stock movement per line; commit.

The conditional UPDATE checks and decrements in one statement, and InnoDB holds the row lock
until commit, so concurrent checkouts for the same product are serialised. Locking in a fixed
order avoids deadlocks between multi-item sales. The CHECK constraint on `StockQuantity` is a
final safety net. Verified against MySQL 9.6 with 12 concurrent sales for 5 units: exactly 5
succeeded (see TESTING.md).
