# Testing

## Automated

| Suite | Command | Count | Covers |
|---|---|---|---|
| Backend unit | `cd backend && dotnet test tests/SmartPOS.UnitTests` | 38 | `SaleCalculator` (totals, rounding, discount-then-tax, invalid input), `StockRules` (availability, adjustments, status), `DiscountPolicy`, `StoreCalendar` (time-zone day boundaries), LIKE escaping, decimal-places validation |
| Backend integration | `cd backend && dotnet test tests/SmartPOS.IntegrationTests` | 25 | Real HTTP pipeline via `WebApplicationFactory` on in-memory SQLite: login/401/403 per role, product validation and duplicate SKU, pagination, sale pricing + stock deduction + movements, oversell rejection with full rollback, discount limits, historical prices after edits, delete rules, adjustments, cashier visibility, dashboard/report figures |
| Frontend unit | `cd frontend && npm test` | 14 | Cart totals (mirrors the server example to the cent, float-drift cases), cart line rules, API error-message mapping, query-param building, date helpers |

`dotnet test SmartPOS.slnx` runs both backend suites. None of the automated tests need MySQL.

## Why SQLite in integration tests, and what it doesn't cover

SQLite lets the whole API run in-process with no server, so the tests run anywhere in seconds.
It does **not** reproduce MySQL's SQL dialect or InnoDB locking. Those were verified manually
against MySQL 9.6 during the initial build:

- `dotnet ef database update` applied `InitialCreate` cleanly (all tables, indexes, CHECK constraints).
- A scripted end-to-end run (48 checks) covering every endpoint, role restrictions, validation,
  arithmetic, rollback, history preservation and token revocation, all passing.
- **Concurrency:** 12 parallel single-unit sales against 5 units of stock → exactly 5 × 201,
  7 × 409, final stock 0.
- This run found a real MySQL-only bug (the provider's `ESCAPE '\'` clause is invalid MySQL),
  fixed by using `!` as the LIKE escape character. Add a CI job with a MySQL service container
  to catch this class of issue automatically.

A headless-Chrome walkthrough of the UI (login, dashboard, product form, POS checkout, invoice,
inventory adjustment, reports, user creation, confirm dialog, mobile layout, cashier restrictions)
also passed with no console errors or failed requests.
