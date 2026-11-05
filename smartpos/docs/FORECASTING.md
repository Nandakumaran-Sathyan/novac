# Retail demand and stockout forecasting (beta)

Status: **beta, working end to end on the shops' own data** (5 November 2026). Each SmartPOS shop owner sees, on
the **Forecasting** page, a 7-day forecast, stockout risk and reorder suggestions for **their own shop**, learned
from that shop's recorded sales and stock. One model is trained on every shop's sales (the shop is a feature), and
an owner only ever sees their own shop's forecasts. Everything in this document was run and measured.

A synthetic retail chain is kept **only as an offline benchmark** of the method (section 9): there, the true demand
is known, so the method can be checked in ways real data doesn't allow. It is never shown to shop owners.

Setup and commands: [forecasting/README.md](../forecasting/README.md).

## 1. Architecture

```
Browser (Angular)                         SmartPOS API (ASP.NET Core 10)                 Forecasting service (Python 3.12)
 /forecasting page  ──JWT──▶  /api/forecasting/*  ──X-Service-Key──▶  FastAPI on 127.0.0.1:8090
                               shop = the caller's token                 /shops/{shop_id}/...
                               values validated, rate-limited            │  LightGBM model (artifacts/)
                               errors -> problem+json                    ▼
                                                                         PostgreSQL smartpos_dev
                                                                          public: SmartPOS tables (READ-ONLY)
                                                                          forecasting: models, runs, results
```

- The forecasting engine is a **separate Python service**, the same pattern as the AI assistant's Ollama: the
  browser never talks to it, it listens on loopback only, and the API checks its address at startup.
- **Shop isolation.** The SmartPOS API builds every forwarded path from the caller's JWT `shop` claim
  (`/shops/{currentUser.ShopId}/...`); no browser value can name a shop. In the service, each forecast run belongs
  to exactly one shop and every query reads only that shop's latest run. Tests prove both halves (section 8).
- **Data:** the service reads the SmartPOS tables with SELECTs only (a test fingerprints every row before and
  after training and forecasting). Its own tables live in the `forecasting` schema, which **Alembic** owns; EF Core
  still owns `public`. No parallel database.

| Piece | Where |
|---|---|
| SmartPOS reader, features, model, inventory rules, runs, API | `forecasting/novac_forecast/` (`smartpos.py`, `features.py`, `train.py`, `inventory.py`, `pipeline.py`, `api/`) |
| Schema migrations | `forecasting/migrations/versions/0001_*.py`, `0002_shop_forecasts.py` |
| Lead time and minimum order on products | EF Core migration `20261105064430_AddProductLeadTimeAndMoq` |
| .NET proxy | `backend/src/SmartPOS.API/Controllers/ForecastingController.cs`, `Infrastructure/Forecasting/ForecastingClient.cs` |
| Web page | `frontend/src/app/features/forecasting/` |
| Benchmark chain (offline only) | `forecasting/novac_forecast/synthetic/`, `load.py`, `validation.py` |

## 2. From SmartPOS data to daily series (`smartpos.py`)

| Question | Answer |
|---|---|
| What is a day? | The shop's local day, `Asia/Kolkata` (`FORECAST_SHOP_TIMEZONE`, same as the API's `Store:TimeZoneId`). A bill at 00:30 IST counts on that day, although it is the previous day in UTC. |
| Units sold | Sum of `SaleItems.Quantity` on bills with `Status = 'Completed'`. |
| A day with no bills at all | The shop was closed or didn't record sales, so every product's demand that day is **missing, not zero**. A trading day without a sale of a product **is** a zero for that product. |
| Where a product's history starts | Its first stock movement (opening stock) or first sale, whichever is earlier. |
| Stockout days | Closing stock = `QuantityAfter` of the product's last stock movement that day, carried forward. A day that ends at 0 is censored: its sales understate demand, so it is a gap in the training label and the lag features. |
| Price | Average selling price that day, carried forward; catalogue price if never sold. Till discounts are decided at the counter, so they are not a feature. |
| Lead time, minimum order | The product's new `LeadTimeDays` (1-90, default 3) and `MinimumOrderQuantity` (≥ 1, default 1), editable on the product form. |
| Which products are forecast | Active products of the shop. Inactive ones still count as history. |
| History used | Up to yesterday (today is still trading). A shop needs at least 7 trading days for a forecast, and training needs at least 42 days of history. |

The data on 5 November 2026: 3 shops, 92 / 90 / 89 trading days (31 Jul to 3 Nov), 50 products, 7,794 completed
bills, 5 / 1 / 1 stockout days. Shops 2 and 3 have no bills since 28 and 29 Oct; those days are missing, not zero.

## 3. PostgreSQL schema (`forecasting`)

| Table | Key | Holds |
|---|---|---|
| `model_metadata` | model_version | type, dates, features, parameters, artifact path + SHA-256, `data_source` (`smartpos` or `synthetic`), one active model **per source** |
| `model_evaluation_metrics` | id (unique per version/evaluation/method/target/scope) | MAE, RMSE, WAPE, bias, n; scope overall / store (= shop) / category / product |
| `shop_forecast_runs` | run_id (uuid) | **shop_id**, model version, as-of date, horizon, trading days used, generation time |
| `shop_demand_forecasts` | (run_id, product_id, forecast_date) | daily predicted demand and the method used |
| `shop_stockout_predictions` | (run_id, product_id) | SKU, name and category (snapshot), stock, 7-day demand, cover, stockout date, risk, lead time, safety stock, reason |
| `shop_replenishment_recommendations` | (run_id, product_id) | quantity, safety stock, reorder point, order-up-to, lead time, minimum order, order-by date, reason |
| benchmark tables | | `stores`, `product_categories`, `suppliers`, `products`, `daily_sales`, `inventory_history`, `purchase_orders`, `dataset_runs`, `forecast_runs`, `demand_forecasts`, `stockout_predictions`, `replenishment_recommendations` (section 9) |

Result rows hang off a run, and a run belongs to one shop, so a shop's results can only be reached through its own
runs. `shop_id` and `product_id` are the SmartPOS ids, without a cross-schema foreign key, so the two migration
tools stay independent. Migration `0002` names its target schema explicitly, because Alembic's
`add_column`/`alter_column` bypass the schema translate map. A test caught this: in a test run it pointed at the
real schema and failed safely in its transaction.

## 4. Features (`features.py`)

| Group | Features |
|---|---|
| Lags | lag_1, lag_7, lag_14, lag_28 |
| Rolling | rolling_mean_7/14/28, rolling_std_7/28 |
| Calendar | day_of_week, day_of_month, month, week_of_year, is_weekend, holiday_flag, festival (name), days_to_festival |
| Product | product_id, category_id, unit_price, discount_percentage, promotion_flag, price_change (vs 28-day mean) |
| Shop | store_id (the SmartPOS shop id); city, region, store_type (benchmark only; constant for real shops) |
| History | revenue_rolling_28, censored_share_28 (stockout days in the window), series_age |

- **No leakage:** every lag and window for day *d* reads only days before *d*; calendar, price and promotion are
  known in advance. A test poisons every value on and after *d* and checks that no feature changes.
- **Censoring:** the series behind the lags is observed sales on days that did *not* stock out. Stockout days and
  missing days are gaps (NaN), not low numbers. LightGBM handles gaps natively.
- **One implementation:** features are computed on a series × days matrix by one function, used by training,
  backtests and each step of the recursive forecast, so training and serving cannot drift apart.
- Categorical columns use fixed category levels saved with the model, so codes are identical at inference.

## 5. Model and training (`train.py`, `python -m novac_forecast train`)

- **One pooled model for all shops**, with the shop (`store_id`), product and category as categorical features, so a
  small shop borrows strength from the others while keeping its own level and pattern.
- **Label:** observed sales on trading days that did not end out of stock. Evaluation is against the same
  observable sales (real shops have no "true demand").
- **Split (chronological, 97 calendar days):** train 31 Jul to 10 Oct (75%), validation 11 to 24 Oct (15%), test 25 Oct
  to 4 Nov (10%). Smaller trees than the benchmark suit the small data (15 leaves, 20 rows per leaf).
- **Selection on validation only:** Poisson 0.388 (120 trees, chosen), Tweedie 1.2 0.390, squared error 0.390.
- **Production model:** the chosen configuration refit on all history: `shops-lgbm-20261104-44474e72` (about 1.5 s).
  Report: `forecasting/reports/shops/model_evaluation.json`.

**Results, test period** (25 Oct to 4 Nov, held out; WAPE = total absolute error / total sales, lower is better):

| | LightGBM | 7-day moving average | Same day last week | n |
|---|---|---|---|---|
| All shops | **62.9%** (bias +13%) | 63.3% | 75.2% | 243 |
| Shop 1 | 70.1% | **68.5%** | 76.6% | 91 |
| Shop 2 | 47.3% | **45.3%** | 64.6% | 63 |
| Shop 3 | **65.8%** | 73.5% | 84.8% | 89 |

7-day recursive **backtest** from 24 Oct: LightGBM 58.0%, moving average 61.0%, same day last week 72.5%.

**Reading the numbers.** With three months of history and a test period of only 243 product-days, the model is
**level with a 7-day moving average overall**: slightly better for shop 3, slightly worse for shops 1 and 2. That is
the honest state of a beta: the value today is mostly in turning a reasonable demand estimate plus each product's
stock, lead time and minimum order into concrete stockout dates and order quantities. Accuracy should improve with
more history (weekly and seasonal patterns, and festivals, need a year or more). Forecasts also run **high** (bias
+13% overall, +25% for shops 2 and 3), partly because shops 2 and 3 stopped recording sales in late October. The
page shows these figures to the owner, for their shop and overall.

## 6. Forecasting, stockout risk and replenishment

**7-day forecast** (`forecast.py`, `pipeline.py`): recursive. Day 1 is predicted from history, its prediction becomes
the most recent value, then day 2 is predicted, and so on. The price is held at its last value and no promotion is
assumed (there is no promotion calendar yet). Series with fewer than 7 observed days use a fallback: their own
recent average, or for a product with no history the median of its category in that store, halved. The method is
stored per row. Each run is saved with its model version and timestamp in one transaction.

**Category forecasts** are not modelled separately: they are sums of the product forecasts of the same run, so they
always equal the total of their products (tested in the API and the web app).

**Stockout projection** (`inventory.py`, deterministic): stock on hand now (`Inventories.StockQuantity`, what the
owner sees on the Inventory page), minus the forecast, day by day. The first day demand can't be met is the
predicted stockout date. SmartPOS has no purchase orders, so nothing is "on order" (the benchmark chain adds open
orders on their expected day). The forecast starts today and today's sales so far are not subtracted, so today
counts in full: slightly cautious.

Risk bands are **transparent rules, not probabilities**:

| Band | Rule |
|---|---|
| HIGH | runs out before an order placed today could arrive (within the supplier lead time), or out of stock now, or no inventory record |
| MEDIUM | runs out within 7 days but after the lead time (ordering now avoids it), or ends the week below safety stock |
| LOW | neither |

**Replenishment:** safety stock = 1.65 × σ(daily demand, last 28 uncensored days) × √(lead days); reorder point = forecast over the lead time
+ safety stock; order-up-to = forecast over lead time + 7 review days + safety stock. When stock + on order is at or
below the reorder point, order the difference, rounded up to a multiple of the product's minimum order quantity. Lead times longer
than 7 days extend the forecast at its average daily rate. Every row carries a plain-language reason.

**Current runs** (history to 4 Nov 2026, forecasting 5-11 Nov), one per shop:

| Shop | Products | Trading days | 7-day forecast | HIGH / MEDIUM / LOW | To reorder |
|---|---|---|---|---|---|
| 1 Pandian Stores | 16 | 92 | 872 units | 7 / 6 / 3 | 11 |
| 2 Octonauts infotech | 16 | 90 | 982 units | 9 / 4 / 3 | 11 |
| 3 Novac Department Stores | 18 | 89 | 848 units | 6 / 5 / 7 | 10 |

Many products are HIGH because the recorded stock has only gone down since the opening stock: no restocks have been
entered in three months, so several products are at or near zero.

## 7. API

Forecasting service (`X-Service-Key` required except `/health`; OpenAPI at `http://127.0.0.1:8090/docs`). The shop id
comes from the SmartPOS API, never from a browser:

| Method + path | Returns |
|---|---|
| `GET /health` | database, active shop model, key configured (public, nothing secret, nothing per shop) |
| `GET /shops/{shop_id}/status` | beta note, active model, this shop's latest run (or null) |
| `GET /shops/{shop_id}/forecast?horizon=7&category=` | per product: daily demand, 7-day total, stock, risk, stockout date, reorder quantity, model version; daily totals |
| `GET /shops/{shop_id}/categories/forecast?horizon=7` | per category: daily and total demand (sums of its products) |
| `GET /shops/{shop_id}/stockout-alerts?risk=HIGH&risk=MEDIUM` | alerts with stock, cover, stockout date, lead time, reason |
| `GET /shops/{shop_id}/replenishment-recommendations?only_needed=true` | quantities, reorder point, order-up-to, order-by date, reason |
| `GET /shops/{shop_id}/products/{product_id}/forecast?horizon=7` | one product; 404 unless it is in this shop's run |
| `POST /shops/{shop_id}/forecast/generate` `{"horizon": 7}` | a new run for this shop (one at a time; 409 if under 7 trading days) |
| `GET /shops/{shop_id}/model-metrics?evaluation=test` | active model metadata; metrics overall and for this shop only |

No forecast yet for the shop, or an unknown product, is 404; no trained model is 503; bad values are 422.

SmartPOS API (JWT required; values validated before forwarding; service errors become the usual problem+json
400/404/409/503): `GET /api/forecasting/status | forecast | categories | stockout-alerts | replenishment |
products/{id}/forecast | metrics` and `POST /api/forecasting/generate` (3 per minute per shop). Settings:
`Forecasting:BaseUrl` (loopback unless `AllowRemoteHost`), `Forecasting:ServiceKey` (user-secrets; empty =
switched off, 503), `TimeoutSeconds`. `/health` includes a `forecasting` check (Degraded, never Unhealthy).

## 8. Web app and tests

The **Forecasting** page (sidebar) carries a **Beta** badge and a short note to treat forecasts as a guide. There
is no shop or city selection: it is always the signed-in owner's shop.

- **Filters:** category, product (name or SKU), forecast date.
- **Overview:** forecast demand, high-risk products, products to reorder, days of sales history used.
- **Demand:** 7-day chart, demand by category, product table (seven daily values, total, stock, risk, stockout date, reorder).
- **Inventory:** HIGH/MEDIUM/LOW stockout alerts with reasons; recommended orders with lead time and minimum order.
- **Accuracy:** this shop's and the overall test error, next to the 7-day average.
- **Empty states:** "being set up" (no model), and "no forecast yet" with **Create forecast** (a new shop with under
  a week of sales gets the service's explanation).
- The product form has **Supplier lead time (days)** and **Minimum order quantity**.

Checked in a real browser on a copy of the database, signed in as the owners of shop 1 (grocery) and shop 3
(pharmacy). Each saw only its own products and categories; the page calls only shop-less URLs; Update forecast
works; there are no console errors and no horizontal scrolling at phone width.

| Suite | Count | Covers |
|---|---|---|
| Python, `forecasting/tests` | 71 (38 without PostgreSQL) | SmartPOS reader on a throwaway SmartPOS-shaped schema with known answers (completed bills only, IST day boundary, closed days missing, zero-sale days, stockout censoring, shop filter, SmartPOS tables never written); shop runs (one per shop, inactive products excluded, lead time and case size, too little history refused, six weeks to train); API (key, shop isolation, another shop's product is 404, no run is 404, horizons, category = sum of products, alerts, reorders, metrics only overall + own shop, generate touches only that shop, 409 on thin history); migrations and loading (replacing the benchmark keeps the shops' model and runs, one active model per source); features, model, inventory and benchmark generator as before |
| .NET, `ForecastingTests` + product tests | 23 + 1 (of 110 integration) | sign-in required, key sent, JSON unchanged, every route goes to `/shops/{caller's shop}/`, two owners reach only their own shops, a shop in the query or path is ignored or 404, bad values refused, category escaped, 404/400/409/503 mapping, offline, wrong key, rate limit, not configured; lead time and minimum order default, save and validate |
| Angular, `forecast-view.spec.ts` | 5 (of 45) | filtering by name/SKU/category, categories, category totals = product sums, daily totals, risk counts |

## 9. Method benchmark (synthetic chain, offline only)

Real shops can't tell us how much demand went unmet on stockout days, or how close a forecast is to the best
possible one. A simulated 10-store Tamil Nadu chain with known demand can, so it is kept as a benchmark for the
method: `python -m novac_forecast benchmark` (or `load`, `validate`, `train --source synthetic`, `forecast --source
synthetic`). It has its own active model (`data_source = 'synthetic'`), is never served by the API and never shown in
the app. `load --replace` clears only the benchmark's data, models and runs; the shops' are kept.

### 9.1 Dataset

| | |
|---|---|
| Stores | 10, in Chennai (3), Coimbatore (2), Madurai, Trichy, Salem, Erode, Tirunelveli; Hypermarket / Supermarket / Neighbourhood |
| Products | 105 in 7 categories (15 each): Grocery, Dairy, Beverages, Snacks, Personal Care, Household Products, Fruits and Vegetables |
| Suppliers | 12, lead times 1 to 10 days (two longer than the 7-day horizon), with delay probabilities |
| History | **745 days, 21 Oct 2024 to 4 Nov 2026** (two Diwalis, two Pongals) |
| Rows | 759,590 daily sales, 759,590 inventory days, 112,824 purchase orders (583 still open) |
| Totals | 6.66 M units demanded, 6.51 M sold, **154,617 lost to stockouts (2.32%)**; stockouts on 1.97% of store-product-days |
| Seed | 20261105 (fully reproducible; recorded in `dataset_runs`) |

**How demand is simulated** (`synthetic/generator.py`). Daily latent demand per store and product is a
gamma-Poisson draw around a mean built from:

- **product base volume** × **store size** (hypermarket 2.0, supermarket 1.2, neighbourhood 0.75) × a random per-store taste;
- **region mix**: Chennai buys more drinks, snacks and personal care, smaller towns more staples and produce;
- **weekday**: Chennai weekends +47% over weekdays, other cities +15% (measured in the loaded data);
- **season by category**: summer drinks about 2.2× winter; dairy and talc up in summer; snacks and household up in the monsoon and festival season;
- **festivals** (`synthetic/calendar.py`): Diwali, Pongal, Tamil New Year, Vinayagar Chaturthi, Ayudha Pooja, Christmas
  and New Year, with build-up days before each and product-specific peaks (e.g. Murukku ×3.5 at Diwali; jaggery ×3.5,
  sugarcane and fresh turmeric ×30 at Pongal; puffed rice ×6 at Ayudha Pooja). Pongal is stronger outside Chennai,
  Christmas and New Year stronger in Chennai;
- **promotions**: 3-7 day discounts of 5-25% (fewer on fresh food, more in hypermarkets); the lift follows each category's
  price elasticity plus a display effect (measured: +24% dairy to +66% beverages);
- **price**: one or two inflation steps over two years, weekly market swings for produce; small towns are more price sensitive;
- **growth** of 6% a year, 20 store-closure days (missing rows), a cyclone day closing every Chennai store, and
  three products launched recently (missing early history).

**Stock and sales.** A separate inventory simulation runs the store's own reorder policy, estimated from its last 28 days
of *sales* (it cannot see true demand), with supplier lead times, random delays, supplier disruption windows,
minimum order quantities, spoilage of perishables and occasional count corrections. So
`units_sold = min(latent_demand, stock available)` and `lost_demand = latent_demand - units_sold`. Both are stored:
the model trains on what a real shop can see (sales), and is scored against what customers wanted (latent demand).

**Festival dates** are the commonly published Tamil Nadu dates; check them against an official list before real use.

### 9.2 Data quality

`validate` runs 14 SQL checks on the loaded data: duplicates, nulls, negatives, sold > demanded, ledger balance,
sales without an inventory row, units disagreeing between tables, stockout flag vs lost demand, revenue arithmetic,
orphans, purchase-order dates and lead times. All return 0. It also reports 2,040 missing store-product-days
(the closures and launches, as intended) and the patterns quoted in 9.1.

### 9.3 Model selection on the benchmark

- **Label:** observed sales on days without a stockout ("demand_units"). Stockout days are dropped from the label.
- **Split (chronological):** train 21 Oct 2024 to 1 May 2026 (75%), validation 2 May to 20 Aug 2026 (15%), test 21 Aug
  to 4 Nov 2026 (10%).
- **Selection on validation only**, by WAPE against observable sales (never the latent demand), with early stopping:

  | Objective | Validation WAPE | Trees |
  |---|---|---|
  | Poisson | 0.4091 | 697 |
  | **Tweedie (1.2)**, chosen | **0.4080** | 546 |
  | Squared error | 0.4127 | 323 |

- **Evaluation model:** the chosen configuration refit on train + validation; the **test set is used once**.
- **Production model:** the same configuration refit on all 745 days: `lgbm-20261104-9591d4d6`
  (learning rate 0.05, 63 leaves, deterministic, 8 threads; 44 s on the M4, about 3.3 GB peak memory).
- Most important features (gain): rolling means 28/14/7, product, weekday, store, discount, price change, lag_1, festival.

### 9.4 Benchmark results

**Day-ahead, test period**, against latent demand (all 78,910 store-product-days):

| Method | MAE | RMSE | WAPE | Bias |
|---|---|---|---|---|
| **LightGBM** | **3.51** | **6.22** | **41.75%** | -1.5% |
| 7-day moving average | 3.85 | 6.93 | 45.81% | -1.6% |
| Same day last week | 4.86 | 8.74 | 57.81% | -2.0% |
| *Noise floor: a forecaster that knows each day's true mean* | 3.45 | 6.07 | 41.09% | -0.2% |

**Rolling-origin backtest:** 10 weekly origins through the test period, each a full 7-day recursive forecast with
the price frozen and no promotions known (exactly what production can know):

| Method | WAPE | Bias |
|---|---|---|
| **LightGBM** | **42.33%** | -3.3% |
| 7-day moving average | 46.45% | -0.9% |
| Same day last week | 58.19% | -0.9% |

By forecast day, LightGBM's WAPE is 41-44%, below both baselines at every one of the seven days.

**Reading the numbers.** WAPE looks high because single store-product-days are very noisy (a 40%-ish WAPE remains
even for a perfect forecaster). On this data LightGBM gets to within 0.7 points of that floor, closing **86% of the
gap** between the moving average and the best possible forecast. It wins in **every store** (WAPE 0.38 in the
hypermarkets to 0.47 in the neighbourhood stores, whose low volumes are noisier) and **every category** (0.38 dairy
to 0.46 household). The backtest bias (-3.3%) is mostly promotion days the forecast cannot know about.

**Censoring matters** (`scripts/censoring_ablation.py`, same model, test period):

| Training | All days WAPE / bias | The 1,846 stockout days: WAPE / bias |
|---|---|---|
| **Censoring-aware** (production) | 41.8% / -1.4% | 40.8% / **-21.6%** |
| Naive (stockout sales taken as demand) | 41.9% / -3.4% | 42.4% / **-28.8%** |

Even censoring-aware, stockout days are under-forecast: stockouts happen mostly on spike days (festival build-ups),
which are also the hardest to predict.

**This is synthetic evaluation.** The model family matches how the data was generated, so near-floor accuracy is
expected here. Real data has effects no simulation includes; measure on real sales before relying on these figures.
All metrics, overall and per store, category and product, are in `model_evaluation_metrics` and `reports/model_metrics.csv`.

## 10. Retraining and operations

- **Daily:** `python -m novac_forecast forecast` after the shops close (or owners press Update forecast). Each shop
  gets its own run.
- **Weekly:** `python -m novac_forecast train`, then `forecast`. The new model becomes the active `smartpos` model;
  artifacts are checked by SHA-256 before use. Watch the test WAPE and bias per shop in `model_evaluation_metrics`.
- A new shop needs 7 trading days before it gets forecasts; until the next retrain its id is unseen by the model,
  which then relies on its products' own history and categories.

## 11. Known issues and next steps

- **Little history:** three months, no festival seen twice, about level with a moving average. Retrain as history grows.
- **Restocks:** no restock movements have been recorded, so stock only falls and many products show HIGH risk.
  Recording deliveries in Inventory fixes both the alerts and the stockout-day detection.
- **No purchase orders or promotion calendar** in SmartPOS: nothing is "on order", and forecasts assume no promotions.
- **Closing stock 0** is the stockout signal; a product that sold out at noon and was restocked the same day is not
  flagged.
- Risk bands are rules, not probabilities; category forecasts are sums of products.
- The model artifacts are git-ignored (reproducible from the database in seconds).
