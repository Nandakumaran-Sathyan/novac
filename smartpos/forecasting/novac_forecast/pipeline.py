"""Forecast runs: 7-day demand per product, stockout risk and replenishment, saved in PostgreSQL in one transaction.

- run_shop_forecasts: the real SmartPOS shops. One shop_forecast_runs row PER SHOP, so each run (and everything
  under it) belongs to exactly one shop. Uses the active "smartpos" model.
- run_forecast: the synthetic benchmark chain (offline evaluation only), under forecast_runs.
"""

from __future__ import annotations

import hashlib
import logging
import time
import uuid
from datetime import date, timedelta
from pathlib import Path

import numpy as np
import pandas as pd
from sqlalchemy import Engine, text

from .db import check_schema_name, schema_of
from .features import _nanstd, _windows, build_panel, load_history
from .forecast import recursive_forecast
from .inventory import SeriesInventory, project_stockout, recommend
from .smartpos import load_shops, series_frame
from .train import NotEnoughHistory, load_model

log = logging.getLogger(__name__)
MIN_TRADING_DAYS = 7          # a shop needs a week of recorded sales before it gets forecasts


class NoActiveModel(RuntimeError):
    pass


def active_model(engine: Engine, source: str = "synthetic") -> dict:
    s = f'"{check_schema_name(schema_of(engine))}"'
    with engine.connect() as conn:
        row = conn.execute(text(f"SELECT model_version, artifact_path, artifact_sha256 FROM {s}.model_metadata "
                                "WHERE is_active AND data_source = :src"), {"src": source}).mappings().first()
    if row is None:
        raise NoActiveModel(f"No trained {source} model is active. Run `python -m novac_forecast train --source {source}` first.")
    return dict(row)


def load_active_booster(engine: Engine, artifact_dir: Path, source: str = "synthetic"):
    meta = active_model(engine, source)
    path = Path(meta["artifact_path"])
    if not path.exists():
        path = artifact_dir / path.name
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if digest != meta["artifact_sha256"]:
        raise RuntimeError(f"Model artifact {path.name} does not match its recorded checksum; refusing to use it.")
    booster, payload = load_model(path)
    return meta["model_version"], booster, payload


def inventory_inputs(engine: Engine, panel, as_of, horizon: int) -> list[SeriesInventory]:
    s = f'"{check_schema_name(schema_of(engine))}"'
    with engine.connect() as conn:
        stock = pd.read_sql_query(text(f"""
            SELECT DISTINCT ON (store_id, product_id) store_id, product_id, closing_stock
            FROM {s}.inventory_history WHERE date <= :d ORDER BY store_id, product_id, date DESC"""), conn, params={"d": as_of})
        orders = pd.read_sql_query(text(f"""
            SELECT store_id, product_id, expected_on, quantity FROM {s}.purchase_orders
            WHERE status = 'open' AND ordered_on <= :d"""), conn, params={"d": as_of})
        products = pd.read_sql_query(text(f"SELECT product_id, supplier_lead_days, minimum_order_quantity FROM {s}.products"), conn)
    stock_map = {(r.store_id, r.product_id): int(r.closing_stock) for r in stock.itertuples()}
    product_map = {r.product_id: (int(r.supplier_lead_days), int(r.minimum_order_quantity)) for r in products.itertuples()}
    arrivals: dict[tuple[str, str], dict[int, int]] = {}
    for r in orders.itertuples():
        # An order already overdue is assumed to arrive on day 1; later ones on their expected day.
        day = max((r.expected_on - as_of).days, 1)
        if day <= horizon:
            key = (r.store_id, r.product_id)
            arrivals.setdefault(key, {})
            arrivals[key][day] = arrivals[key].get(day, 0) + int(r.quantity)

    origin = panel.index_of(as_of)
    sigma = _nanstd(_windows(panel.signal, 28, np.array([origin + 1])), 3)[:, 0]
    out = []
    for i, row in enumerate(panel.series.itertuples()):
        key = (row.store_id, row.product_id)
        lead, moq = product_map[row.product_id]
        mean = np.nanmean(panel.signal[i, max(0, origin - 27):origin + 1]) if np.any(~np.isnan(panel.signal[i, max(0, origin - 27):origin + 1])) else 0.0
        sd = sigma[i] if not np.isnan(sigma[i]) else float(np.sqrt(max(mean, 0.0)))     # Poisson-like fallback
        out.append(SeriesInventory(current_stock=stock_map.get(key, 0), inventory_known=key in stock_map, lead_days=lead,
                                   moq=moq, sigma_daily=float(sd), arrivals=arrivals.get(key, {})))
    return out


def run_forecast(engine: Engine, artifact_dir: Path, horizon: int = 7, notes: str | None = None) -> dict:
    if not 1 <= horizon <= 7:
        raise ValueError("horizon must be between 1 and 7 days")
    started = time.time()
    version, booster, payload = load_active_booster(engine, artifact_dir)
    history = load_history(engine)
    as_of = max(history["date"])
    panel = build_panel(history, extra_days=horizon)
    origin = panel.index_of(as_of)
    result = recursive_forecast(booster, payload["levels"], panel, origin, horizon)
    first_day = as_of + timedelta(days=1)
    inventories = inventory_inputs(engine, panel, as_of, horizon)

    run_id = uuid.uuid4()
    forecasts, stockouts, replenish = [], [], []
    for i, row in enumerate(panel.series.itertuples()):
        daily = np.round(result.predictions[i], 2)
        for h in range(horizon):
            forecasts.append({"run_id": run_id, "store_id": row.store_id, "product_id": row.product_id,
                              "forecast_date": first_day + timedelta(days=h), "horizon_day": h + 1,
                              "predicted_demand": float(daily[h]), "method": str(result.method[i])})
        inv = inventories[i]
        so = project_stockout(daily, inv, first_day)
        rp = recommend(daily, inv, first_day)
        stockouts.append({"run_id": run_id, "store_id": row.store_id, "product_id": row.product_id,
                          "current_stock": inv.current_stock, "inventory_known": inv.inventory_known,
                          "on_order_in_horizon": so.on_order_in_horizon, "predicted_demand_horizon": so.cumulative_demand,
                          "days_of_cover": so.days_of_cover, "predicted_stockout_date": so.predicted_stockout_date,
                          "stockout_risk": so.risk, "lead_days": inv.lead_days, "safety_stock": so.safety_stock,
                          "reason": so.reason})
        replenish.append({"run_id": run_id, "store_id": row.store_id, "product_id": row.product_id,
                          "recommended_quantity": rp.recommended_quantity, "current_stock": inv.current_stock,
                          "on_order": rp.on_order, "safety_stock": rp.safety_stock, "reorder_point": rp.reorder_point,
                          "order_up_to": rp.order_up_to, "lead_days": inv.lead_days, "minimum_order_quantity": inv.moq,
                          "order_by_date": rp.order_by_date, "reason": rp.reason})

    s = f'"{check_schema_name(schema_of(engine))}"'
    with engine.begin() as conn:
        conn.execute(text(f"""INSERT INTO {s}.forecast_runs (run_id, model_version, as_of_date, forecast_start, horizon, series_count, notes)
                              VALUES (:r, :v, :a, :f, :h, :n, :notes)"""),
                     {"r": run_id, "v": version, "a": as_of, "f": first_day, "h": horizon, "n": len(panel.series), "notes": notes})
        conn.execute(text(f"""INSERT INTO {s}.demand_forecasts VALUES (:run_id, :store_id, :product_id, :forecast_date, :horizon_day, :predicted_demand, :method)"""), forecasts)
        conn.execute(text(f"""INSERT INTO {s}.stockout_predictions VALUES (:run_id, :store_id, :product_id, :current_stock, :inventory_known,
                              :on_order_in_horizon, :predicted_demand_horizon, :days_of_cover, :predicted_stockout_date, :stockout_risk,
                              :lead_days, :safety_stock, :reason)"""), stockouts)
        conn.execute(text(f"""INSERT INTO {s}.replenishment_recommendations VALUES (:run_id, :store_id, :product_id, :recommended_quantity,
                              :current_stock, :on_order, :safety_stock, :reorder_point, :order_up_to, :lead_days, :minimum_order_quantity,
                              :order_by_date, :reason)"""), replenish)

    risk = pd.Series([r["stockout_risk"] for r in stockouts]).value_counts().to_dict()
    methods = pd.Series(result.method).value_counts().to_dict()
    return {"run_id": str(run_id), "model_version": version, "as_of_date": str(as_of), "forecast_start": str(first_day),
            "horizon": horizon, "series": len(panel.series), "forecast_rows": len(forecasts),
            "total_predicted_units": round(float(result.predictions.sum()), 1), "risk_counts": risk, "methods": methods,
            "products_to_reorder": sum(1 for r in replenish if r["recommended_quantity"] > 0),
            "seconds": round(time.time() - started, 1)}


# ------------------------------------------------------------------------------------------ real SmartPOS shops
def _recent_sigma(panel, origin: int) -> tuple[np.ndarray, np.ndarray]:
    """Std and mean of daily demand over the last 28 days (uncensored days only), per series."""
    window = panel.signal[:, max(0, origin - 27):origin + 1]
    observed = np.sum(~np.isnan(window), axis=1)
    mean = np.where(observed > 0, np.nansum(window, axis=1) / np.maximum(observed, 1), 0.0)
    sigma = _nanstd(_windows(panel.signal, 28, np.array([origin + 1])), 3)[:, 0]
    return np.where(np.isnan(sigma), np.sqrt(np.maximum(mean, 0.0)), sigma), mean      # Poisson-like fallback


def run_shop_forecasts(engine: Engine, artifact_dir: Path, horizon: int = 7, shop_id: int | None = None,
                       through: date | None = None, notes: str | None = None, smartpos_schema: str | None = None) -> list[dict]:
    """Forecast every active product of one shop (shop_id) or of every shop. A shop with fewer than
    MIN_TRADING_DAYS days of sales is skipped, or raises NotEnoughHistory when it was asked for explicitly."""
    if not 1 <= horizon <= 7:
        raise ValueError("horizon must be between 1 and 7 days")
    version, booster, payload = load_active_booster(engine, artifact_dir, source="smartpos")
    data = load_shops(engine, through=through, shop_id=shop_id, schema=smartpos_schema)
    first_day = data.through + timedelta(days=1)
    shops = [shop_id] if shop_id is not None else sorted(int(s) for s in data.products["shop_id"].unique())
    s = f'"{check_schema_name(schema_of(engine))}"'
    summaries = []
    with engine.begin() as conn:
        for shop in shops:
            started = time.time()
            trading = data.trading_days.get(shop, 0)
            products = data.products[(data.products["shop_id"] == shop) & data.products["is_active"]].reset_index(drop=True)
            if trading < MIN_TRADING_DAYS or products.empty:
                why = (f"Forecasts need at least {MIN_TRADING_DAYS} days of recorded sales; this shop has {trading}."
                       if trading < MIN_TRADING_DAYS else "This shop has no active products to forecast.")
                if shop_id is not None:
                    raise NotEnoughHistory(why)
                log.info("Shop %s skipped: %s", shop, why)
                continue
            history = data.history[data.history["store_id"] == str(shop)]
            panel = build_panel(history, series=series_frame(products), end=data.through, extra_days=horizon)
            origin = panel.index_of(data.through)
            result = recursive_forecast(booster, payload["levels"], panel, origin, horizon)
            sigma, _ = _recent_sigma(panel, origin)
            # build_panel orders series by (shop, product id as text); line products up with that order.
            order = {pid: i for i, pid in enumerate(panel.series["product_id"])}
            products = products.assign(_i=products["product_id"].astype(str).map(order)).sort_values("_i").reset_index(drop=True)

            run_id = uuid.uuid4()
            forecasts, stockouts, replenish = [], [], []
            for i, p in enumerate(products.itertuples()):
                daily = np.round(result.predictions[i], 2)
                for h in range(horizon):
                    forecasts.append({"run_id": run_id, "product_id": int(p.product_id), "forecast_date": first_day + timedelta(days=h),
                                      "horizon_day": h + 1, "predicted_demand": float(daily[h]), "method": str(result.method[i])})
                # SmartPOS has no purchase orders, so nothing is on order.
                inv = SeriesInventory(current_stock=int(p.stock), inventory_known=bool(p.inventory_known),
                                      lead_days=int(p.lead_days), moq=int(p.moq), sigma_daily=float(sigma[i]), arrivals={})
                so = project_stockout(daily, inv, first_day)
                rp = recommend(daily, inv, first_day)
                stockouts.append({"run_id": run_id, "product_id": int(p.product_id), "sku": p.sku, "product_name": p.name,
                                  "category": p.category, "current_stock": inv.current_stock, "inventory_known": inv.inventory_known,
                                  "predicted_demand_horizon": so.cumulative_demand, "days_of_cover": so.days_of_cover,
                                  "predicted_stockout_date": so.predicted_stockout_date, "stockout_risk": so.risk,
                                  "lead_days": inv.lead_days, "safety_stock": so.safety_stock, "reason": so.reason})
                replenish.append({"run_id": run_id, "product_id": int(p.product_id), "recommended_quantity": rp.recommended_quantity,
                                  "current_stock": inv.current_stock, "on_order": rp.on_order, "safety_stock": rp.safety_stock,
                                  "reorder_point": rp.reorder_point, "order_up_to": rp.order_up_to, "lead_days": inv.lead_days,
                                  "minimum_order_quantity": inv.moq, "order_by_date": rp.order_by_date, "reason": rp.reason})

            conn.execute(text(f"""INSERT INTO {s}.shop_forecast_runs (run_id, shop_id, model_version, as_of_date, forecast_start,
                                  horizon, trading_days, series_count, notes)
                                  VALUES (:r, :shop, :v, :a, :f, :h, :t, :n, :notes)"""),
                         {"r": run_id, "shop": shop, "v": version, "a": data.through, "f": first_day, "h": horizon,
                          "t": trading, "n": len(products), "notes": notes})
            conn.execute(text(f"""INSERT INTO {s}.shop_demand_forecasts (run_id, product_id, forecast_date, horizon_day,
                                  predicted_demand, method)
                                  VALUES (:run_id, :product_id, :forecast_date, :horizon_day, :predicted_demand, :method)"""), forecasts)
            conn.execute(text(f"""INSERT INTO {s}.shop_stockout_predictions (run_id, product_id, sku, product_name, category,
                                  current_stock, inventory_known, predicted_demand_horizon, days_of_cover, predicted_stockout_date,
                                  stockout_risk, lead_days, safety_stock, reason)
                                  VALUES (:run_id, :product_id, :sku, :product_name, :category, :current_stock, :inventory_known,
                                  :predicted_demand_horizon, :days_of_cover, :predicted_stockout_date, :stockout_risk, :lead_days,
                                  :safety_stock, :reason)"""), stockouts)
            conn.execute(text(f"""INSERT INTO {s}.shop_replenishment_recommendations (run_id, product_id, recommended_quantity,
                                  current_stock, on_order, safety_stock, reorder_point, order_up_to, lead_days,
                                  minimum_order_quantity, order_by_date, reason)
                                  VALUES (:run_id, :product_id, :recommended_quantity, :current_stock, :on_order, :safety_stock,
                                  :reorder_point, :order_up_to, :lead_days, :minimum_order_quantity, :order_by_date, :reason)"""),
                         replenish)
            summaries.append({
                "run_id": str(run_id), "shop_id": shop, "model_version": version, "as_of_date": str(data.through),
                "forecast_start": str(first_day), "horizon": horizon, "trading_days": trading, "series": len(products),
                "forecast_rows": len(forecasts), "total_predicted_units": round(float(result.predictions.sum()), 1),
                "risk_counts": pd.Series([r["stockout_risk"] for r in stockouts]).value_counts().to_dict(),
                "methods": pd.Series(result.method).value_counts().to_dict(),
                "products_to_reorder": sum(1 for r in replenish if r["recommended_quantity"] > 0),
                "seconds": round(time.time() - started, 1)})
    return summaries
