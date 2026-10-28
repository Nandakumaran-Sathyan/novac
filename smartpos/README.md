# SmartPOS

Point-of-sale and inventory management system: ASP.NET Core 10 Web API + EF Core + PostgreSQL on the
back end, Angular 22 on the front end, JWT authentication. One kind of account: each user registers and owns a shop.

This is the **foundation** for the team to build on: authentication, multi-shop data isolation, catalogue,
customers, inventory with a stock-movement ledger, POS checkout (cash change, UPI), sales history, bills,
reports and user management, all connected to a real database. It does **not** work offline (see [Roadmap](#roadmap--known-limitations)).

```
smartpos/
├── backend/            ASP.NET Core solution (SmartPOS.slnx)
│   ├── src/
│   │   ├── SmartPOS.API             Controllers, auth, middleware, Program.cs
│   │   ├── SmartPOS.Application     Services, DTOs, validation, business rules
│   │   ├── SmartPOS.Core            Entities and enums (no dependencies)
│   │   └── SmartPOS.Infrastructure  EF Core DbContext, PostgreSQL, migrations, password hashing
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
| PostgreSQL | 14+ (verified on 16) | `brew install postgresql@16` on macOS |

No global Angular CLI is needed; the project's local CLI runs through `npx ng` / `npm` scripts.

## Quick start

### 1. Database

Create a database and a dedicated application user (full guide: [docs/DATABASE.md](docs/DATABASE.md)):

```sql
CREATE ROLE smartpos LOGIN PASSWORD '<choose-a-password>';
CREATE DATABASE smartpos_dev OWNER smartpos;
```

### 2. Backend configuration (secrets stay out of the repo)

```bash
cd backend/src/SmartPOS.API
dotnet user-secrets set "ConnectionStrings:SmartPos" "Host=127.0.0.1;Port=5432;Database=smartpos_dev;Username=smartpos;Password=<your-password>"
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"
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

There is no seeded account: open the web app and choose **Create an account** (or `POST /api/auth/register`).
Registering creates your user and your shop; in Development the new shop also gets a sample catalogue with
stock and 90 days of **fabricated** sales history (16 invented customers, about 2,900 bills, a consistent stock
ledger) so the dashboards have something to show. Turn it off with `Database:SeedDemoHistory=false`.

To add the same fabricated history to a shop that **already exists** (Development only, real records are not
touched, refuses to run twice):

```bash
cd backend
dotnet run --project src/SmartPOS.API -- seed-demo-history --email you@example.com --days 90
```

### 5. Run the frontend

```bash
cd frontend
npm install
npm start                                  # ng serve → http://localhost:4200
```

The dev build calls `http://localhost:5080/api` (see `src/environments/environment.development.ts`).

## Tests

```bash
cd backend && dotnet test SmartPOS.slnx    # 46 unit + 31 integration tests, no database needed
cd frontend && npm test                    # Vitest: cart arithmetic + HTTP helpers
cd frontend && npm run build               # production build
```

See [docs/TESTING.md](docs/TESTING.md) for what each suite covers and how the PostgreSQL-specific
behaviour (concurrency, generated SQL) was verified.

## Configuration reference

| Key (env var form) | Required | Default | Purpose |
|---|---|---|---|
| `ConnectionStrings__SmartPos` | yes | — | PostgreSQL connection string |
| `Jwt__SigningKey` | yes | — | HMAC key, ≥ 32 chars |
| `Jwt__Issuer` / `Jwt__Audience` | no | `SmartPOS.Api` / `SmartPOS.Web` | Token validation |
| `Jwt__ExpiryMinutes` | no | 480 | Token lifetime (one shift) |
| `Store__Name`, `Store__CurrencyCode`, `Store__TimeZoneId` | no | `SmartPOS Store`, `INR`, `Asia/Kolkata` | Money format and what "today" means (same for all shops) |
| `Pos__TaxRatePercent` | no | 5 | Tax applied after discount |
| `Pos__MaxDiscountPercent` | no | 100 | Highest discount a bill may carry |
| `Cors__AllowedOrigins__0` | no | `http://localhost:4200` in Development | Browser origins allowed to call the API |
| `Database__SeedSampleData` | no | true in Development | Sample catalogue for each newly registered shop |
| `Database__SeedDemoHistory` / `Database__DemoHistoryDays` | no | true / 90 in Development | **Fabricated** bills, customers and stock movements for the sample shop, so dashboards have data. Never enable in production |
| `RateLimiting__LoginPermitsPerMinute` | no | 10 | Login and registration attempts per IP per minute |

The API refuses to start if `Jwt:SigningKey` is missing or too short, or if `Store:TimeZoneId` is unknown.

## Features

- **Multi-shop**: every shop's users, products, stock, customers and sales are isolated from the others (JWT shop claim + EF Core global query filter).
- **Accounts**: one login per user, created by registering (which also creates the user's shop); PBKDF2
  password hashing (ASP.NET Core Identity hasher), JWT, login/registration rate limiting. No roles.
- **Dashboard**: today's figures, then a 7/30/90-day **sales performance** view: six KPIs that count up and
  are compared with the previous period (revenue, bills, average bill, items, gross profit, customers),
  auto-written highlights (best day, busiest slot, leading category), a revenue trend against the previous
  period, payment mix, sales by category, busiest hours, a weekday × hour heatmap, top products and top
  customers, plus recent transactions and the low-stock list. Charts are dependency-free SVG with tooltips,
  keyboard support and entrance animations (switched off under `prefers-reduced-motion`).
- **Products**: search (name/SKU/barcode), category filter, pagination, create/edit with validation,
  unique SKU and barcode per shop, free-text category, optional expiry date, delete only when never sold
  (otherwise deactivate).
- **Customers**: name and phone per shop, search, attach to a sale at the POS.
- **Inventory**: per-product stock rows with a low-stock threshold, sorted by urgency, low/out-of-stock
  filters, adjustments with a reason, full stock-movement history.
- **POS**: product search/barcode scan with Enter-to-add, cart with quantity limits, discount within the
  configured limit, tax, payment by Cash/Card/UPI/Other, cash received and change due, optional customer,
  keyboard shortcuts, printable bill after checkout.
- **Sales**: history with date filters and bill-number search, itemised bill with
  each line's discount and tax.
- **Reports**: daily sales and top products by revenue/profit for any date range.

## Forgot your password?

There is no self-service reset yet (see the roadmap). In Development you can set a new one from the terminal;
you are asked for it with a hidden prompt, so it never appears in your shell history:

```bash
cd backend
dotnet run --project src/SmartPOS.API -- reset-password --email you@example.com
```

(Or set the `NEW_PASSWORD` environment variable to skip the prompt.) It refuses to run outside Development.

## Roadmap / known limitations

- **No offline mode.** Checkout needs the API. The design leaves room for it: the server already
  re-prices and re-validates every sale, so an offline client could queue `CreateSaleRequest`s
  (with a client-generated idempotency key) and sync them later. That needs an idempotency key on
  `Sale`, conflict handling for stock that ran out meanwhile, and a service worker.
- Refunds/returns and sale voiding are not implemented. `Sales.Status` already has `Cancelled` (and
  `Pending`), and reports ignore anything but `Completed`, but nothing sets them yet.
- **No staff accounts.** One login per shop owner; there is no way to give an employee their own login or limit what they can do. Currency, time zone and tax rate are global settings, not per shop.
- **Public sign-up has no email verification, and there is no self-service password reset or change-password screen** yet (use the `reset-password` command above in Development).
- Loyalty points are stored on customers but nothing earns or spends them yet.
- Single tax rate, sale-level discount only (it is split across lines, but there are no per-line
  discounts or promotions).
- No barcode-scanner hardware integration beyond "scanner types SKU + Enter" into the search box.
- JWTs are stored in `localStorage`; a refresh-token/HTTP-only-cookie flow would reduce XSS exposure.
- Reports group days in memory for the selected range (≤ 366 days); very large stores would want
  pre-aggregated daily tables.
- Integration tests use SQLite; PostgreSQL behaviour was verified manually (see docs/TESTING.md). A CI
  job with a PostgreSQL service container is the next step.

## Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): layers, request flow, key decisions and trade-offs
- [docs/DATABASE.md](docs/DATABASE.md): PostgreSQL setup, migrations, schema and integrity rules
- [docs/API.md](docs/API.md): endpoint reference, error format
- [docs/TESTING.md](docs/TESTING.md): test suites and manual verification
