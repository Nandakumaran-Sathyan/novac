# SmartPOS forecasting service (beta)

Demand, stockout and replenishment forecasting for **each SmartPOS shop, from its own sales and stock**: it reads
the SmartPOS tables (read-only), trains one LightGBM model on every shop's sales (the shop is a feature), and
produces a 7-day recursive forecast, stockout risk and reorder suggestions per shop. A FastAPI app serves them
under `/shops/{shop_id}/...`; only the SmartPOS API calls it, with the shop taken from the owner's token.

A synthetic 10-store chain is kept as an **offline benchmark** of the method (known true demand). It is never
served or shown in the app.

Design, assumptions and measured results: [docs/FORECASTING.md](../docs/FORECASTING.md).

## Requirements

- macOS on Apple Silicon (tested on a MacBook Air M4, 16 GB), CPU only. No GPU or CUDA is needed.
- Python **3.12** (`brew install python@3.12`) and OpenMP for LightGBM (`brew install libomp`).
- The SmartPOS PostgreSQL database (`smartpos_dev`). The service keeps its own tables in the `forecasting` schema
  and only ever SELECTs from the SmartPOS tables.
- The .NET SDK (`~/.dotnet`), only to read the connection string from the SmartPOS API's user-secrets.

## Setup (once)

```bash
cd smartpos/forecasting
/opt/homebrew/bin/python3.12 -m venv .venv
.venv/bin/pip install -r requirements.txt

# A shared secret between the SmartPOS API and this service, kept in the API's user-secrets (outside the repo).
cd ../backend && dotnet user-secrets set "Forecasting:ServiceKey" "$(openssl rand -base64 48 | tr -d '\n')" --project src/SmartPOS.API && cd ../forecasting
```

## Every new terminal

```bash
cd smartpos/forecasting
export PATH="$HOME/.dotnet:$PATH"
eval "$(.venv/bin/python scripts/dotnet_secrets_env.py)"   # sets FORECAST_DATABASE_URL and FORECAST_SERVICE_KEY; prints nothing secret
```

No password is typed or stored: it comes from `ConnectionStrings:SmartPos` in the API's user-secrets.

## Build everything

```bash
.venv/bin/python -m novac_forecast migrate              # create/upgrade the forecasting schema (Alembic)
.venv/bin/python -m novac_forecast train                # train on every shop's sales, evaluate, store the model (~2 s)
.venv/bin/python -m novac_forecast forecast             # 7-day forecast, stockout risk and reorders, one run per shop
.venv/bin/python -m novac_forecast forecast --shop 3    # one shop only
```

History is read up to yesterday in `FORECAST_SHOP_TIMEZONE` (default `Asia/Kolkata`). A shop needs 7 trading days
for a forecast; training needs 42 days of history. The report goes to `reports/shops/model_evaluation.json`.

Benchmark (synthetic chain, offline only):

```bash
.venv/bin/python -m novac_forecast benchmark            # migrate, load --replace, validate, train and forecast the chain
.venv/bin/python scripts/noise_floor.py                 # best possible WAPE on that data
.venv/bin/python scripts/censoring_ablation.py          # how much treating stockout days as censored helps
```

`load --replace` replaces only the benchmark's data, models and runs; the shops' model and runs are kept.

## Run the service

```bash
.venv/bin/python -m novac_forecast serve        # http://127.0.0.1:8090 (loopback only); docs at /docs
```

Then start the SmartPOS API and the web app as usual. The **Forecasting** page (Beta) calls `/api/forecasting/*`
on the SmartPOS API, which forwards to `/shops/{the caller's shop}/...` here with the service key.

Quick check without the web app:

```bash
curl -s http://127.0.0.1:8090/health
curl -s -H "X-Service-Key: $FORECAST_SERVICE_KEY" "http://127.0.0.1:8090/shops/1/stockout-alerts" | python3 -m json.tool | head -40
```

## Tests

```bash
.venv/bin/python -m pytest                                    # pure tests; the PostgreSQL tests are skipped
FORECAST_TEST_DATABASE_URL="$FORECAST_DATABASE_URL" .venv/bin/python -m pytest   # all 71 tests
```

The PostgreSQL and API tests migrate a **throwaway schema** (`forecasting_test_<random>`) and build a throwaway
SmartPOS-shaped schema (`smartpos_test_<random>`, see `tests/smartpos_fixture.py`) with known shop data, and drop
both afterwards, so they never touch the real `forecasting` schema or the SmartPOS tables.

## Retraining

`forecast` daily (owners can also press **Update forecast**, which runs it for their shop) and `train` weekly as
sales accumulate. `train` makes the new model the active `smartpos` model in `model_metadata` and writes
`artifacts/<model_version>.joblib` (checked by SHA-256 when loaded). Older artifacts can be deleted; their metadata
and metrics stay in the database.

## Layout

```
novac_forecast/
  smartpos.py  the SmartPOS shops as a read-only data source (daily series, stockouts, lead time, MOQ)
  synthetic/   benchmark chain: calendar.py (Tamil Nadu festivals), catalog.py, generator.py
  config.py    settings from environment variables only
  db.py, models.py, load.py, validation.py
  features.py  lag/rolling/calendar/price features (shared by training and forecasting)
  forecast.py  recursive 7-day forecast, fallbacks, baselines
  train.py     split, model selection, evaluation, production model, metrics
  inventory.py stockout projection and replenishment rules
  pipeline.py  forecast runs: one per shop (run_shop_forecasts) and the benchmark's (run_forecast)
  api/         FastAPI app, queries, response schemas
migrations/    Alembic (owns the forecasting schema)
scripts/       dotnet_secrets_env.py, noise_floor.py, censoring_ablation.py
tests/         pytest
artifacts/     trained models (git-ignored; reproducible)
reports/       shops/model_evaluation.json (real shops); benchmark: data_quality.json, model_evaluation.json, ...
```
