# Testing

## Automated

| Suite | Command | Count | Covers |
|---|---|---|---|
| Backend unit | `cd backend && dotnet test tests/SmartPOS.UnitTests` | 63 | `LoyaltyRules` (rounding down, rates, nonsense input), `DemoHistory` (loyalty balances, stock ledger ends exactly on the target and never goes negative, bills internally consistent, deterministic, weekend/customer patterns), `SaleCalculator` (totals, rounding, discount-then-tax, line-level discount/tax allocation that sums exactly, payment settlement and change, invalid input), `StockRules` (availability, adjustments, status), `DiscountPolicy`, `StoreCalendar` (time-zone day boundaries), LIKE escaping, decimal-places validation |
| Backend integration | `cd backend && dotnet test tests/SmartPOS.IntegrationTests` | 51 | Real HTTP pipeline via `WebApplicationFactory` on in-memory SQLite, with two shops: registration (duplicate email, weak password, new shop starts empty), login/401, product validation, duplicate SKU and barcode, pagination, sale pricing + stock deduction + movements, cash change and payment rules, customers on sales, points earned per bill and added atomically to the balance, email verification (link works once, replaced links die, forged tokens fail, resend, throwaway and undeliverable domains refused, failed lookups allowed), dashboard insights (KPIs, heatmap, payments, categories, shop isolation, range validation), **one shop cannot see, change or sell another shop's data**, oversell rejection with full rollback, discount limits, historical prices after edits, delete rules, adjustments, cashier visibility, dashboard/report figures |
| Frontend unit | `cd frontend && npm test` | 22 | Cart totals (mirrors the server example to the cent, float-drift cases), cart line rules, chart axis scaling and percentage-change maths, API error-message mapping, query-param building, date helpers |

`dotnet test SmartPOS.slnx` runs both backend suites. None of the automated tests need PostgreSQL.

## Why SQLite in integration tests, and what it doesn't cover

SQLite lets the whole API run in-process with no server, so the tests run anywhere in seconds.
It does **not** reproduce PostgreSQL's SQL dialect, case-sensitivity or locking. Those were verified manually
against PostgreSQL 16:

- `dotnet ef database update` applied `InitialCreate` cleanly to an empty database (8 tables, indexes,
  CHECK constraints), and so did the plain `database/schema.sql` script.
- A scripted end-to-end run (70 checks) covering registration, every endpoint, validation, arithmetic,
  line-level discount/tax sums, cash change, customers, barcode search, rollback, history preservation,
  case-insensitive search, and isolation between two registered shops, all passing.
- **Concurrency:** 12 parallel single-unit sales against 5 units of stock → exactly 5 × 201,
  7 × 409, final stock 0.
- Real PostgreSQL found things the SQLite tests could not: unquoted column names in CHECK constraints are
  folded to lower case and fail (they are now quoted), and `LIKE` is case-sensitive (searches now lower-case
  both sides). Add a CI job with a PostgreSQL service container to catch this class of issue automatically.
- SQLite stores decimals as text and compares them as strings, so a CHECK like `DiscountPercent <= 100`
  rejects values such as 12.5 there (PostgreSQL compares numerically). Integration tests therefore use
  round percentages; the unit tests cover awkward ones.

A headless-Chrome walkthrough of the UI (registration with validation, sign-in, dashboard, product form with
barcode and expiry, customers, POS checkout with customer and cash change, bill, inventory adjustment,
reports, confirm dialog, mobile layout, discount limit; 29 checks) also passed with no console errors or
failed requests.

The analytics dashboard was checked separately against a registered shop with 90 days of demo history, at desktop (1440 px) and phone (390 px) widths in headless Chrome: charts draw fully, tooltips and the heatmap readout respond to hover, switching 30 → 7 days refetches and redraws, there is no horizontal overflow and no console errors. Screenshots were reviewed by eye; there are no automated visual tests.

Email verification was also checked end to end with the real pieces: the **live Google DNS lookup** (a real domain accepted, a made-up one refused with NXDOMAIN, a disposable one refused), the message delivered over a **real SMTP connection** to a local sink and decoded the way a mail client would, then the link opened in headless Chrome (banner → verify → banner gone; re-opening the link shows an error). Loyalty was checked against PostgreSQL, including eight concurrent bills for one customer losing no points, and in the browser (the POS previews and then confirms the points). Two things this turned up: the DNS lookup can take several seconds on a slow network (so answers are now cached and the timeout is 6 s), and when it does time out sign-up is deliberately allowed, so a made-up domain can slip through on a bad connection.
