# API reference

Base URL (development): `http://localhost:5080`. Interactive docs: **`/swagger`** (Development
environment only). All endpoints except `POST /api/auth/login`, `POST /api/auth/register` and `GET /health` require
`Authorization: Bearer <accessToken>`.

Enums are sent and returned as strings (`"UPI"`, `"Completed"`, `"Low"`…). Timestamps are UTC ISO-8601. `expiryDate` is a plain calendar date (`yyyy-MM-dd`).

There are no roles: every signed-in user has the same access, to their own shop's data. Everything is scoped to the signed-in user's shop: a record that belongs to another shop returns 404, exactly as if it did not exist.
Date filters (`from`, `to`) are calendar dates (`yyyy-MM-dd`), inclusive, in the store time zone.

## Endpoints

| Method | Path | Description |
|---|---|---|
| POST | `/api/auth/login` | Public. `{ email, password }` → `{ accessToken, expiresAt, user }`. Rate limited. |
| POST | `/api/auth/verify-email` | Public. `{ token }` from the emailed link → 204; 400 if it is wrong, used, replaced or expired. |
| POST | `/api/auth/resend-verification` | Emails the signed-in user a new link (replacing the old one); 400 if already verified. Rate limited. |
| POST | `/api/auth/register` | Public. `{ fullName, email, password, shopName, shopAddress? }` → 201 with the same body as login. Creates the user and the shop they own and emails a verification link; 409 if the email is taken; 400 for a throwaway-mailbox domain or a domain that cannot receive mail. Rate limited. |
| GET | `/api/auth/me` | Current user profile |
| GET | `/api/dashboard/summary` | Today's totals, counts, recent sales, low-stock list |
| GET | `/api/dashboard/insights` | `days` (1–180, default 30). Sales analytics for the last N store days: KPIs with the previous N days for comparison (revenue, bills, average bill, items sold, customers, gross profit), a daily series with the matching previous-period day, a weekday × hour bill-count heatmap, payment mix, net sales by category, top products and customers, and customer vs walk-in bills. Completed bills only; days, weekdays and hours are in the store time zone. 400 outside 1–180 |
| GET | `/api/settings/pos` | Shop name and address, currency, tax rate, caller's max discount |
| GET | `/api/products` | `search` (name, SKU or barcode), `category` (exact name), `includeInactive`, `page`, `pageSize` |
| GET | `/api/products/{id}` | One product |
| POST | `/api/products` | `{ sku, barcode?, name, description?, category, price, costPrice, lowStockThreshold, expiryDate?, initialStock }` → 201 (creates its inventory row; `initialStock` recorded as a movement) |
| PUT | `/api/products/{id}` | Full replace of details/prices/threshold/active flag (not stock); omitted optional fields are cleared |
| DELETE | `/api/products/{id}` | 204; 409 if the product has sales |
| GET | `/api/categories` | Distinct category names in use, with product counts (a category exists once a product uses it) |
| GET | `/api/customers` | `search` (name or phone), paging |
| GET | `/api/customers/{id}` | One customer |
| POST | `/api/customers` | `{ name, phone }` → 201; 409 if the phone is taken in this shop |
| PUT | `/api/customers/{id}` | `{ name, phone }` |
| GET | `/api/inventory` | `search`, `filter` (`All`/`Low`/`OutOfStock`), `category`, paging |
| POST | `/api/inventory/adjustments` | `{ productId, quantityChange, reason }` → 201; 409 if stock would go negative |
| GET | `/api/inventory/movements` | `productId`, `type`, `from`, `to`, paging |
| POST | `/api/sales` | `{ items: [{ productId, quantity }], discountPercent, paymentMethod, amountPaid?, customerId? }` → 201 |
| GET | `/api/sales` | `from`, `to`, `search` (bill no.), paging |
| GET | `/api/sales/{id}` | Sale with items (404 if it belongs to another shop) |
| GET | `/api/reports/daily-sales` | `from`, `to` (default last 7 days, max 366) |
| GET | `/api/reports/top-products` | `from`, `to`, `limit` (1–50, default 10) |
| POST | `/api/ai/chat` | `{ message, history?: [{ role: "user"|"assistant", content }] }` → `{ reply, toolsUsed, truncated, durationMs }`. Message 1-1000 chars; up to 40 history turns of 4000 chars (only the newest 12 are used); roles `system`/`tool` are rejected. Rate limited per user (10/min); 429 if your previous question is still running; 503 if the AI model is unavailable. See docs/AI.md |
| GET | `/api/ai/status` | Whether the AI assistant can answer: Ollama reachable, model installed, tool calling supported (see docs/AI.md) |
| GET | `/health` | Public. `{ status, checks: [{ name, status }] }`; 503 when the DB is unreachable |

## Pagination

List endpoints accept `page` (≥ 1, default 1) and `pageSize` (1–100, default 20) and return:

```json
{ "items": [ … ], "page": 1, "pageSize": 20, "totalCount": 57, "totalPages": 3 }
```

## Errors

Errors use RFC 7807 problem details (`application/problem+json`) with a `traceId`.

| Status | When | Body |
|---|---|---|
| 400 | Validation failed | `errors: { "Field": ["message"] }` |
| 400 | Business rule (e.g. zero adjustment, bad date range) | `detail` |
| 401 | Missing/expired token, or wrong credentials on login | `detail` on login |
| 403 | Discount above the configured limit (`Pos:MaxDiscountPercent`) | `detail` |
| 404 | Record not found (or belongs to another shop) | `detail` |
| 409 | Duplicate SKU/barcode/email/customer phone, insufficient stock, deleting a sold product | `detail` |
| 429 | Too many login attempts | — |
| 500 | Unexpected error (details only in server log) | generic `detail` + `traceId` |

## Sale arithmetic

```
line subtotal = round(unit price × quantity)
subtotal      = Σ line subtotals
discount      = round(subtotal × discount% / 100)
tax           = round((subtotal − discount) × tax% / 100)
total         = subtotal − discount + tax
```

Rounding: 2 decimals, half away from zero. The tax rate in effect is stored on each sale.

The discount and tax are then shared out across the lines in proportion to their value, to the
cent (largest-remainder), so each `SaleItem` has its own `discountAmount`, `taxAmount` and
`lineTotal` (= subtotal − discount + tax) and the lines always add up exactly to the bill.

## Loyalty points

A bill with a `customerId` earns `floor(totalAmount / Loyalty:SpendPerPoint)` points (default 100): ₹99.99 → 0, ₹100 → 1,
₹456.75 → 4. The sale response carries `loyaltyPointsEarned` and `customerLoyaltyPoints` (the balance afterwards; null for a
walk-in); `GET /api/settings/pos` returns `loyaltySpendPerPoint`. The balance is updated atomically in the sale's transaction.

## Payment

`amountPaid` is optional; leaving it out means "exactly the total".

| Method | Rule |
|---|---|
| `Cash` | May be more than the total; the difference is returned as `changeAmount`. Less than the total → 400. |
| `Card`, `UPI`, `Other` | Must equal the total (no change can be given) → 400 otherwise. |

New sales are created with `status: "Completed"`. `Pending` and `Cancelled` exist in the schema but
no endpoint creates or cancels a bill yet; reports and the dashboard count only `Completed` bills.
