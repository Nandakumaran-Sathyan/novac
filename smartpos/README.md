# SmartPOS

Point-of-sale and inventory management system: ASP.NET Core 10 Web API + EF Core + MySQL on the
back end, Angular 22 on the front end, JWT authentication with Admin and Cashier roles.

This is the **foundation** for the team to build on: authentication, catalogue, inventory with a
stock-movement ledger, POS checkout, sales history, invoices, reports and user management, all
connected to a real database. It does **not** work offline (see [Roadmap](#roadmap--known-limitations)).

```
smartpos/
├── backend/            ASP.NET Core solution (SmartPOS.slnx)
│   ├── src/
│   │   ├── SmartPOS.API             Controllers, auth, middleware, Program.cs
│   │   ├── SmartPOS.Application     Services, DTOs, validation, business rules
│   │   ├── SmartPOS.Core            Entities and enums (no dependencies)
│   │   └── SmartPOS.Infrastructure  EF Core DbContext, MySQL, migrations, password hashing
│   └── tests/
│       ├── SmartPOS.UnitTests         Sale calculation, stock rules, discount policy…
│       └── SmartPOS.IntegrationTests  Full HTTP pipeline against in-memory SQLite
├── frontend/           Angular app (standalone components, signals)
│   └── src/app/{core,shared,layout,features/*}
├── database/schema.sql  SQL generated from the EF Core migrations (reference only)
└── docs/               Architecture, database setup, API reference, testing
```

## Prerequisites

| Tool | Version used | Notes |
|---|---|---|
| .NET SDK | 10.0.x | `dotnet --list-sdks` |
| Node.js / npm | 24.x / 11.x | Angular 22 supports Node 22 and 24 |
| MySQL | 8.0.16+ (verified on 9.6) | MariaDB is **not** supported by the Oracle EF Core provider |

No global Angular CLI is needed; the project's local CLI runs through `npx ng` / `npm` scripts.

## Quick start

### 1. Database

Create a database and a dedicated application user (full guide: [docs/DATABASE.md](docs/DATABASE.md)):

```sql
CREATE DATABASE smartpos_dev CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE USER 'smartpos'@'localhost' IDENTIFIED BY '<choose-a-password>';
GRANT ALL PRIVILEGES ON smartpos_dev.* TO 'smartpos'@'localhost';
```

### 2. Backend configuration (secrets stay out of the repo)

```bash
cd backend/src/SmartPOS.API
dotnet user-secrets set "ConnectionStrings:SmartPos" "Server=127.0.0.1;Port=3306;Database=smartpos_dev;User=smartpos;Password=<your-password>"
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"
# First administrator: created once at startup if no admin exists.
dotnet user-secrets set "Bootstrap:AdminEmail" "you@example.com"
dotnet user-secrets set "Bootstrap:AdminPassword" "<8+ chars, a letter and a digit>"
```

Prefer environment variables? See [.env.example](.env.example).

### 3. Create the schema

```bash
cd backend
dotnet tool restore                        # installs dotnet-ef 10.0.12 locally
dotnet ef database update --project src/SmartPOS.Infrastructure --startup-project src/SmartPOS.API
```

### 4. Run the API

```bash
cd backend
dotnet run --project src/SmartPOS.API --launch-profile http
```

- API: http://localhost:5080
- Swagger UI (Development only): **http://localhost:5080/swagger**
- Health: http://localhost:5080/health

On first start in Development the API creates the bootstrap admin and seeds sample categories and
products (no users or sales). Once you've signed in, remove the bootstrap password:
`dotnet user-secrets remove "Bootstrap:AdminPassword"`.

### 5. Run the frontend

```bash
cd frontend
npm install
npm start                                  # ng serve → http://localhost:4200
```

The dev build calls `http://localhost:5080/api` (see `src/environments/environment.development.ts`).

## Tests

```bash
cd backend && dotnet test SmartPOS.slnx    # 38 unit + 25 integration tests, no database needed
cd frontend && npm test                    # Vitest: cart arithmetic + HTTP helpers
cd frontend && npm run build               # production build
```

See [docs/TESTING.md](docs/TESTING.md) for what each suite covers and how the MySQL-specific
behaviour (concurrency, generated SQL) was verified.

## Configuration reference

| Key (env var form) | Required | Default | Purpose |
|---|---|---|---|
| `ConnectionStrings__SmartPos` | yes | — | MySQL connection string |
| `Jwt__SigningKey` | yes | — | HMAC key, ≥ 32 chars |
| `Jwt__Issuer` / `Jwt__Audience` | no | `SmartPOS.Api` / `SmartPOS.Web` | Token validation |
| `Jwt__ExpiryMinutes` | no | 480 | Token lifetime (one shift) |
| `Bootstrap__AdminEmail` / `__AdminPassword` | first run | — | Creates the first admin |
| `Store__Name`, `Store__CurrencyCode`, `Store__TimeZoneId` | no | `SmartPOS Store`, `INR`, `Asia/Kolkata` | Invoice header, money format, what "today" means |
| `Pos__TaxRatePercent` | no | 5 | Tax applied after discount |
| `Pos__MaxCashierDiscountPercent` / `Pos__MaxAdminDiscountPercent` | no | 10 / 100 | Discount authorisation |
| `Cors__AllowedOrigins__0` | no | `http://localhost:4200` in Development | Browser origins allowed to call the API |
| `Database__SeedSampleData` | no | true in Development | Sample catalogue for an empty DB |
| `RateLimiting__LoginPermitsPerMinute` | no | 10 | Login attempts per IP per minute |

The API refuses to start if `Jwt:SigningKey` is missing or too short, or if `Store:TimeZoneId` is unknown.

## Features

- **Auth**: email/password login, PBKDF2 password hashing (ASP.NET Core Identity hasher), JWT,
  Admin/Cashier roles, login rate limiting, deactivated users rejected immediately.
- **Dashboard**: today's sales and transactions (store time zone), product and low-stock counts,
  recent transactions, low-stock list. Zeros and empty states on a fresh database.
- **Products**: search (name/SKU), category filter, pagination, create/edit with validation,
  unique SKU, inline category creation, delete only when never sold (otherwise deactivate).
- **Inventory**: stock levels sorted by urgency, low/out-of-stock filters, adjustments with a
  reason (Admin), full stock-movement history.
- **POS**: product search/scan with Enter-to-add, cart with quantity limits, discount within the
  role's limit, tax, payment method, keyboard shortcuts, printable invoice after checkout.
- **Sales**: history with date filters and invoice search (cashiers see their own), itemised invoice.
- **Reports** (Admin): daily sales and top products by revenue/profit for any date range.
- **Users** (Admin): create cashiers/admins, change role, deactivate, reset password.

## Roadmap / known limitations

- **No offline mode.** Checkout needs the API. The design leaves room for it: the server already
  re-prices and re-validates every sale, so an offline client could queue `CreateSaleRequest`s
  (with a client-generated idempotency key) and sync them later. That needs an idempotency key on
  `Sale`, conflict handling for stock that ran out meanwhile, and a service worker.
- Refunds/returns and sale voiding are not implemented (stock movements already support new types).
- Single store, single tax rate, sale-level discount only (no per-line discounts or promotions).
- No barcode-scanner hardware integration beyond "scanner types SKU + Enter" into the search box.
- JWTs are stored in `localStorage`; a refresh-token/HTTP-only-cookie flow would reduce XSS exposure.
- Reports group days in memory for the selected range (≤ 366 days); very large stores would want
  pre-aggregated daily tables.
- Integration tests use SQLite; MySQL behaviour was verified manually (see docs/TESTING.md). A CI
  job with a MySQL service container is the next step.
- The idempotent migration script generated by the Oracle provider isn't valid MySQL; use
  `dotnet ef database update` or the plain `database/schema.sql`.

## Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): layers, request flow, key decisions and trade-offs
- [docs/DATABASE.md](docs/DATABASE.md): MySQL setup, migrations, schema and integrity rules
- [docs/API.md](docs/API.md): endpoint reference, roles, error format
- [docs/TESTING.md](docs/TESTING.md): test suites and manual verification
