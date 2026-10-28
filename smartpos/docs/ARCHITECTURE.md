# Architecture

## Layers

```
SmartPOS.API ──► SmartPOS.Infrastructure ──► SmartPOS.Application ──► SmartPOS.Core
 controllers       EF Core + PostgreSQL              services, DTOs,         entities,
 JWT, CORS,        DbContext, migrations,       validation, business    enums
 Swagger, errors   password hasher, seeding     rules, interfaces
```

| Project | Contains | Depends on |
|---|---|---|
| **Core** | Entities (`Shop`, `Product`, `Inventory`, `Sale`…) and enums. No framework code. | — |
| **Application** | One service per area (`SaleService`, `ProductService`…), request/response DTOs with DataAnnotations validation, pure business rules (`SaleCalculator`, `StockRules`, `DiscountPolicy`), and the abstractions the services need (`IApplicationDbContext`, `ICurrentUser`, `ICurrentShop`, `IPasswordHasher`, `ITokenService`). | Core, EF Core (provider-agnostic) |
| **Infrastructure** | `SmartPosDbContext` (with the per-shop query filters) + entity configurations + migrations, Npgsql provider, Identity password hasher, sample-data seeder. | Application |
| **API** | Thin controllers, JWT issuing/validation, `ICurrentUser`/`ICurrentShop` from claims, global exception handler, health check, Swagger, CORS, rate limiting. | Infrastructure |

### Why services use `IApplicationDbContext` instead of repositories

The services query `DbSet`s directly through a small interface. A generic repository per entity
would wrap EF Core's LINQ with a weaker copy of itself and add files without adding safety.
The interface adds exactly one operation that needs care, `TryChangeStockAsync` (the
atomic conditional stock update), so concurrency-sensitive SQL lives in one place.

### Request flow: completing a sale

1. `POST /api/sales` → `[ApiController]` validates the DTO (items ≥ 1, quantity 1–10,000, discount 0–100, amount paid ≥ 0) → 400 with field errors if invalid.
2. `SaleService.CreateAsync`: checks the discount against the configured limit (`DiscountPolicy` → 403), merges duplicate lines, loads prices **from the database**, pre-checks stock (`StockRules` → 409 with a clear message), computes totals and each line's share of discount and tax (`SaleCalculator`), then the change due (`SaleCalculator.Settle`).
3. In one transaction: conditional stock decrement on the product's `Inventories` row per line (authoritative oversell guard), insert `Sale`, `SaleItems` (price/name snapshots) and `StockMovements`; commit.
4. Exceptions map to problem+json responses in `GlobalExceptionHandler`.

## Key decisions

| Decision | Why | Trade-off |
|---|---|---|
| One database, many shops, isolated by a global query filter on `ShopId` | A forgotten `WHERE ShopId` can't leak another shop's rows; the filter fails closed when there is no shop | Cross-shop code (login, token validation, seeding) must opt out explicitly; there is no shop-registration endpoint yet |
| One kind of account: each user owns one shop; no roles, no separate `Owner` table | One login per user, as in the `Owner` model; registering creates the shop; `Sales.UserId` points at the same table | No staff accounts or permissions within a shop yet |
| Stock lives in an `Inventories` table, not on `Products` | Matches the shop/product/inventory model; a product edit can't touch stock | An extra join when listing products |
| Category is text on the product | Matches the model; no category admin screens | Renaming a category means editing its products |
| Server prices every sale | A tampered client can't change what the customer pays | Client shows a preview only |
| Client preview mirrors `SaleCalculator` in integer cents | Preview matches the invoice to the paisa/cent; tested on both sides | Two implementations to keep in sync (tests use the same example) |
| Stock changes only via sales/adjustments, never product edit | Every unit is accounted for in `StockMovements` | Correcting stock requires an adjustment with a reason |
| Sale items snapshot name, SKU, price, cost | Old invoices and reports never change when products are edited | Slight duplication |
| Products with sales can't be deleted, only deactivated | Keeps history intact | Inactive products accumulate |
| Store time zone in config, UTC in DB | Correct "today" for a shop regardless of server location | Daily report groups in memory |
| JWT with user and shop claims + per-request user check | A deleted user or changed shop takes effect immediately | One indexed query per request |
| Self-registration, no seeded account | No known default password exists anywhere; anyone can open a shop | Public sign-up is rate limited but has no email verification |
| `localStorage` for the token | Survives refresh mid-shift | Readable by injected script; mitigated by Angular's default escaping and no `innerHTML`; move to HTTP-only cookies for higher assurance |
| PostgreSQL via Npgsql | Mature EF Core provider with native `date`, `timestamptz`, `numeric` and identity columns | Its `LIKE` is case-sensitive, so searches lower-case both sides |

## Security notes

- Passwords: ASP.NET Core Identity `PasswordHasher` (PBKDF2-HMAC-SHA512, 100k iterations, per-password salt); automatic re-hash when parameters change.
- Login: generic error for unknown email/wrong password/inactive account, constant-ish timing for unknown emails, rate limited per IP.
- Tenant isolation: the JWT carries the shop; every query is filtered to it by the DbContext, and a token is rejected if the user's shop no longer matches.
- Authorization: fallback policy requires authentication everywhere; there are no roles, only "signed in" and the shop the token belongs to. Sales, products and customers of other shops return 404.
- Secrets: never in `appsettings*.json`; startup fails fast if the signing key is missing. EF sensitive-data logging is off; the health check hides exception text; 500 responses contain only a trace ID.

## Frontend

```
src/app/
├── core/       auth service + interceptor + guards, API services, models, toast/confirm services
├── shared/     icon, modal, confirm dialog, pagination, empty/loading/error states, badges, money pipe
├── layout/     shell (sidebar + header + outlet), sidebar, header
└── features/   auth, dashboard, products, customers, inventory, pos, sales, reports, users (lazy-loaded)
```

- Standalone components, signals for state, `OnPush` everywhere, zoneless change detection.
- Reactive forms with validators that mirror the API's rules; the API's messages are still shown if it rejects something.
- Design tokens (colours, radii, spacing) live in `src/styles.css`; components use shared classes (`.card`, `.table`, `.btn`, `.badge`…), so pages stay consistent.
- Route guards only shape navigation. The API enforces every permission independently.

## Extending

- **New entity**: Core entity → Infrastructure configuration → migration → Application DTOs/service → API controller → Angular service in `core/api` → feature page + route.
- **Offline sales (future)**: add an idempotency key to `CreateSaleRequest`/`Sale`, queue requests client-side (IndexedDB + service worker), replay on reconnect; the server's re-pricing and stock checks already make replayed sales safe to accept or reject.
