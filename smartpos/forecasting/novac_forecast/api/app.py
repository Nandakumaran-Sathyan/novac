"""The forecasting service (FastAPI). Run it on the loopback interface only:

    .venv/bin/uvicorn novac_forecast.api.app:app --host 127.0.0.1 --port 8090

Only the SmartPOS API calls it, sending the shared secret in X-Service-Key; the browser never does. /health is the
one public endpoint and reveals nothing sensitive. Database credentials never appear in any response or log line.

Every other endpoint is under /shops/{shop_id}. The service trusts the shop id it is given: the SmartPOS API takes it
from the signed-in owner's token, never from the browser, and each query reads only that shop's runs.
"""

from __future__ import annotations

import hmac
import logging
import threading
import time
from typing import Annotated

from fastapi import Depends, FastAPI, Header, HTTPException, Path, Query, Request
from fastapi.responses import JSONResponse
from sqlalchemy import text

from .. import __version__
from ..config import get_settings
from ..db import get_engine
from ..pipeline import NoActiveModel, run_shop_forecasts
from ..train import NotEnoughHistory
from . import queries
from .schemas import (CategoryForecast, GenerateRequest, GenerateResult, Health, ModelMetrics, ProductForecast,
                      Recommendations, ShopForecast, ShopStatus, StockoutAlerts)

log = logging.getLogger("novac_forecast.api")
ShopId = Annotated[int, Path(ge=1, le=2_147_483_647, description="SmartPOS shop id")]
ProductId = Annotated[int, Path(ge=1, le=2_147_483_647, description="SmartPOS product id")]
Horizon = Annotated[int, Query(ge=1, le=7, description="Days ahead (1-7)")]
Category = Annotated[str | None, Query(min_length=1, max_length=100)]

app = FastAPI(title="SmartPOS Forecasting Service (beta)", version=__version__,
              description="Demand, stockout and replenishment forecasts for each SmartPOS shop, from its own sales.")
_generate_lock = threading.Lock()


def engine():
    return get_engine()


def require_service_key(x_service_key: Annotated[str | None, Header()] = None) -> None:
    configured = get_settings().service_key
    if configured is None:
        raise HTTPException(503, "The service key is not configured (FORECAST_SERVICE_KEY); refusing all requests.")
    if x_service_key is None or not hmac.compare_digest(x_service_key.encode(), configured.get_secret_value().encode()):
        raise HTTPException(401, "Missing or invalid X-Service-Key.")


Protected = [Depends(require_service_key)]


@app.middleware("http")
async def log_requests(request: Request, call_next):
    started = time.perf_counter()
    response = await call_next(request)
    log.info("%s %s -> %d in %.0f ms", request.method, request.url.path, response.status_code, (time.perf_counter() - started) * 1000)
    return response


@app.exception_handler(queries.NotFound)
async def not_found(_: Request, exc: queries.NotFound):
    return JSONResponse(status_code=404, content={"detail": str(exc)})


@app.exception_handler(queries.NoForecastYet)
async def no_forecast(_: Request, exc: queries.NoForecastYet):
    return JSONResponse(status_code=404, content={"detail": str(exc)})


@app.get("/health", response_model=Health)
def health() -> dict:
    try:
        eng = engine()
        with eng.connect() as conn:
            conn.execute(text("SELECT 1"))
        model = queries.active_model(eng)
        db_ok = True
    except Exception:          # never leak the reason (it can contain host names); the log has it
        log.exception("Health check: database unreachable")
        model, db_ok = None, False
    key = get_settings().service_key is not None
    return {"status": "ok" if db_ok and model and key else "degraded", "database": "ok" if db_ok else "unreachable",
            "model_version": model["model_version"] if model else None, "service_key_configured": key}


@app.get("/shops/{shop_id}/status", response_model=ShopStatus, dependencies=Protected)
def shop_status(shop_id: ShopId) -> dict:
    eng = engine()
    model = queries.active_model(eng)
    return {"shop_id": shop_id, "model_version": model["model_version"] if model else None,
            "model_trained_at": model["trained_at"] if model else None, "latest_run": queries.run_or_none(eng, shop_id)}


@app.get("/shops/{shop_id}/forecast", response_model=ShopForecast, dependencies=Protected)
def shop_forecast(shop_id: ShopId, horizon: Horizon = 7, category: Category = None) -> dict:
    run, items = queries.product_forecasts(engine(), shop_id, horizon, category=category)
    return {"run": run, "shop_id": shop_id, "horizon": min(horizon, run["horizon"]),
            "total_predicted_demand": round(sum(i["cumulative_predicted_demand"] for i in items), 2),
            "daily_totals": queries.daily_totals(items), "items": items}


@app.get("/shops/{shop_id}/categories/forecast", response_model=CategoryForecast, dependencies=Protected)
def category_forecast(shop_id: ShopId, horizon: Horizon = 7) -> dict:
    run, items = queries.product_forecasts(engine(), shop_id, horizon)
    return {"run": run, "shop_id": shop_id, "horizon": min(horizon, run["horizon"]), "items": queries.category_rollup(items)}


@app.get("/shops/{shop_id}/stockout-alerts", response_model=StockoutAlerts, dependencies=Protected)
def stockout_alerts(shop_id: ShopId,
                    risk: Annotated[list[str] | None, Query(description="HIGH, MEDIUM and/or LOW (default HIGH and MEDIUM)")] = None) -> dict:
    wanted = [r.upper() for r in (risk or ["HIGH", "MEDIUM"])]
    if not set(wanted) <= {"HIGH", "MEDIUM", "LOW"}:
        raise HTTPException(422, "risk must be HIGH, MEDIUM or LOW")
    run, items, counts = queries.stockout_alerts(engine(), shop_id, wanted)
    return {"run": run, "shop_id": shop_id, "counts": counts, "items": items}


@app.get("/shops/{shop_id}/replenishment-recommendations", response_model=Recommendations, dependencies=Protected)
def replenishment(shop_id: ShopId, only_needed: bool = True) -> dict:
    run, items = queries.recommendations(engine(), shop_id, only_needed)
    return {"run": run, "shop_id": shop_id, "items": items}


@app.get("/shops/{shop_id}/products/{product_id}/forecast", response_model=ProductForecast, dependencies=Protected)
def product_forecast(shop_id: ShopId, product_id: ProductId, horizon: Horizon = 7) -> dict:
    _, items = queries.product_forecasts(engine(), shop_id, horizon, product_id=product_id)
    return items[0]


@app.post("/shops/{shop_id}/forecast/generate", response_model=GenerateResult, dependencies=Protected)
def generate(shop_id: ShopId, body: GenerateRequest = GenerateRequest()) -> dict:
    if not _generate_lock.acquire(blocking=False):
        raise HTTPException(409, "A forecast run is already in progress. Try again in a moment.")
    try:
        return run_shop_forecasts(engine(), get_settings().artifact_dir, horizon=body.horizon, shop_id=shop_id,
                                  notes="generated by the shop owner")[0]
    except NoActiveModel as exc:
        raise HTTPException(503, "Forecasting is not set up yet: no model has been trained.") from exc
    except NotEnoughHistory as exc:
        raise HTTPException(409, str(exc)) from exc
    finally:
        _generate_lock.release()


@app.get("/shops/{shop_id}/model-metrics", response_model=ModelMetrics, dependencies=Protected)
def model_metrics(shop_id: ShopId,
                  evaluation: Annotated[str | None, Query(pattern=r"^(validation|test|backtest)$")] = None) -> dict:
    return queries.model_metrics(engine(), shop_id, evaluation)
