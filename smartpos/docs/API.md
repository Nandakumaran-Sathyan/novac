# API reference

Base URL (development): `http://localhost:5080`. Interactive docs: **`/swagger`** (Development
environment only). All endpoints except `POST /api/auth/login` and `GET /health` require
`Authorization: Bearer <accessToken>`.

Enums are sent and returned as strings (`"Admin"`, `"Cash"`, `"Low"`…). Timestamps are UTC ISO-8601.
Date filters (`from`, `to`) are calendar dates (`yyyy-MM-dd`), inclusive, in the store time zone.

## Endpoints

| Method | Path | Role | Description |
|---|---|---|---|
| POST | `/api/auth/login` | public | `{ email, password }` → `{ accessToken, expiresAt, user }`. Rate limited. |
| GET | `/api/auth/me` | any | Current user profile |
| GET | `/api/dashboard/summary` | any | Today's totals, counts, recent sales, low-stock list |
| GET | `/api/settings/pos` | any | Store name, currency, tax rate, caller's max discount |
| GET | `/api/products` | any | `search`, `categoryId`, `includeInactive`, `page`, `pageSize` |
| GET | `/api/products/{id}` | any | One product |
| POST | `/api/products` | Admin | Create (`initialStock` recorded as a movement) → 201 |
| PUT | `/api/products/{id}` | Admin | Update details/prices/active flag (not stock) |
| DELETE | `/api/products/{id}` | Admin | 204; 409 if the product has sales |
| GET | `/api/categories` | any | All categories with product counts |
| POST | `/api/categories` | Admin | Create → 201; 409 on duplicate name |
| GET | `/api/inventory` | any | `search`, `filter` (`All`/`Low`/`OutOfStock`), `categoryId`, paging |
| POST | `/api/inventory/adjustments` | Admin | `{ productId, quantityChange, reason }` → 201; 409 if stock would go negative |
| GET | `/api/inventory/movements` | any | `productId`, `type`, `from`, `to`, paging |
| POST | `/api/sales` | any | `{ items: [{ productId, quantity }], discountPercent, paymentMethod }` → 201 |
| GET | `/api/sales` | any | `from`, `to`, `search` (invoice no.), `cashierId` (admin), paging. Cashiers see own sales |
| GET | `/api/sales/{id}` | any | Sale with items (cashiers: own only, else 404) |
| GET | `/api/reports/daily-sales` | Admin | `from`, `to` (default last 7 days, max 366) |
| GET | `/api/reports/top-products` | Admin | `from`, `to`, `limit` (1–50, default 10) |
| GET | `/api/users` | Admin | `search`, `role`, paging |
| GET | `/api/users/{id}` | Admin | One user |
| POST | `/api/users` | Admin | `{ fullName, email, password, role }` → 201 |
| PUT | `/api/users/{id}` | Admin | `{ fullName, role, isActive, newPassword? }`; can't demote/deactivate yourself |
| GET | `/health` | public | `{ status, checks: [{ name, status }] }`; 503 when the DB is unreachable |

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
| 403 | Role not allowed, or discount above the role's limit | `detail` for discount |
| 404 | Record not found (or not visible to a cashier) | `detail` |
| 409 | Duplicate SKU/email/category, insufficient stock, deleting a sold product | `detail` |
| 429 | Too many login attempts | — |
| 500 | Unexpected error (details only in server log) | generic `detail` + `traceId` |

## Sale arithmetic

```
line total = round(unit price × quantity)
subtotal   = Σ line totals
discount   = round(subtotal × discount% / 100)
tax        = round((subtotal − discount) × tax% / 100)
total      = subtotal − discount + tax
```

Rounding: 2 decimals, half away from zero. The tax rate in effect is stored on each sale.
