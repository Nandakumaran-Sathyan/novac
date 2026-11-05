"""Read-only queries behind the API. Every query is for ONE shop and reads only that shop's latest run
(shop_forecast_runs.shop_id), so one shop's forecasts can never appear in another shop's response. Category
figures are sums of the product rows of the same run, so they always equal the sum of their products."""

from __future__ import annotations

from collections import defaultdict
from datetime import timedelta

from sqlalchemy import Engine, text

from ..db import check_schema_name, schema_of


class NotFound(LookupError):
    pass


class NoForecastYet(LookupError):
    pass


def _s(engine: Engine) -> str:
    return f'"{check_schema_name(schema_of(engine))}"'


def latest_run(conn, s: str, shop_id: int) -> dict:
    row = conn.execute(text(f"""
        SELECT run_id::text, model_version, generated_at, as_of_date, forecast_start, horizon, trading_days
        FROM {s}.shop_forecast_runs WHERE shop_id = :shop ORDER BY generated_at DESC LIMIT 1"""),
        {"shop": shop_id}).mappings().first()
    if row is None:
        raise NoForecastYet("No forecast has been generated for this shop yet.")
    return dict(row)


def run_or_none(engine: Engine, shop_id: int) -> dict | None:
    with engine.connect() as conn:
        try:
            return latest_run(conn, _s(engine), shop_id)
        except NoForecastYet:
            return None


def active_model(engine: Engine) -> dict | None:
    with engine.connect() as conn:
        row = conn.execute(text(f"SELECT model_version, trained_at, train_start, test_end FROM {_s(engine)}.model_metadata "
                                "WHERE is_active AND data_source = 'smartpos'")).mappings().first()
    return dict(row) if row else None


def _end_date(run: dict, horizon: int):
    return run["forecast_start"] + timedelta(days=min(horizon, run["horizon"]) - 1)


def product_forecasts(engine: Engine, shop_id: int, horizon: int, product_id: int | None = None,
                      category: str | None = None) -> tuple[dict, list[dict]]:
    s = _s(engine)
    with engine.connect() as conn:
        run = latest_run(conn, s, shop_id)
        end = _end_date(run, horizon)
        rows = conn.execute(text(f"""
            SELECT f.product_id, so.sku, so.product_name, so.category, f.method, f.forecast_date,
                   f.predicted_demand::float AS predicted_demand, so.current_stock, so.stockout_risk,
                   so.predicted_stockout_date, rr.recommended_quantity
            FROM {s}.shop_demand_forecasts f
            JOIN {s}.shop_stockout_predictions so ON so.run_id = f.run_id AND so.product_id = f.product_id
            JOIN {s}.shop_replenishment_recommendations rr ON rr.run_id = f.run_id AND rr.product_id = f.product_id
            WHERE f.run_id = CAST(:run AS uuid) AND f.forecast_date <= :end
              AND (CAST(:product AS integer) IS NULL OR f.product_id = CAST(:product AS integer))
              AND (CAST(:category AS text) IS NULL OR so.category = CAST(:category AS text))
            ORDER BY so.category, so.product_name, f.product_id, f.forecast_date"""),
            {"run": run["run_id"], "end": end, "product": product_id, "category": category}).mappings().all()
    if product_id is not None and not rows:
        raise NotFound(f"Product {product_id} is not in this shop's latest forecast.")
    items: dict[int, dict] = {}
    for r in rows:
        item = items.setdefault(r["product_id"], {
            "product_id": r["product_id"], "sku": r["sku"], "product_name": r["product_name"], "category": r["category"],
            "method": r["method"], "current_stock": r["current_stock"], "stockout_risk": r["stockout_risk"],
            "predicted_stockout_date": r["predicted_stockout_date"] if r["predicted_stockout_date"] and r["predicted_stockout_date"] <= end else None,
            "recommended_reorder_quantity": r["recommended_quantity"], "model_version": run["model_version"],
            "daily": [], "cumulative_predicted_demand": 0.0})
        item["daily"].append({"forecast_date": r["forecast_date"], "predicted_demand": r["predicted_demand"]})
        item["cumulative_predicted_demand"] = round(item["cumulative_predicted_demand"] + r["predicted_demand"], 2)
    return run, list(items.values())


def daily_totals(items: list[dict]) -> list[dict]:
    totals: dict = defaultdict(float)
    for it in items:
        for d in it["daily"]:
            totals[d["forecast_date"]] += d["predicted_demand"]
    return [{"forecast_date": k, "predicted_demand": round(v, 2)} for k, v in sorted(totals.items())]


def category_rollup(items: list[dict]) -> list[dict]:
    groups: dict[str, dict] = {}
    for it in items:
        g = groups.setdefault(it["category"], {"category": it["category"], "products": set(), "by_day": defaultdict(float)})
        g["products"].add(it["product_id"])
        for d in it["daily"]:
            g["by_day"][d["forecast_date"]] += d["predicted_demand"]
    out = []
    for g in sorted(groups.values(), key=lambda g: g["category"]):
        daily = [{"forecast_date": k, "predicted_demand": round(v, 2)} for k, v in sorted(g["by_day"].items())]
        out.append({"category": g["category"], "product_count": len(g["products"]), "daily": daily,
                    "cumulative_predicted_demand": round(sum(g["by_day"].values()), 2)})
    return out


def stockout_alerts(engine: Engine, shop_id: int, risks: list[str]) -> tuple[dict, list[dict], dict]:
    s = _s(engine)
    with engine.connect() as conn:
        run = latest_run(conn, s, shop_id)
        rows = conn.execute(text(f"""
            SELECT so.product_id, so.sku, so.product_name, so.category, so.current_stock, so.inventory_known,
                   so.predicted_demand_horizon::float AS cumulative_predicted_demand, so.days_of_cover::float AS days_of_cover,
                   so.predicted_stockout_date, so.stockout_risk, so.lead_days, so.safety_stock::float AS safety_stock, so.reason
            FROM {s}.shop_stockout_predictions so
            WHERE so.run_id = CAST(:run AS uuid)
            ORDER BY CASE so.stockout_risk WHEN 'HIGH' THEN 0 WHEN 'MEDIUM' THEN 1 ELSE 2 END,
                     so.predicted_stockout_date NULLS LAST, so.days_of_cover NULLS LAST, so.product_name"""),
            {"run": run["run_id"]}).mappings().all()
    counts = {"HIGH": 0, "MEDIUM": 0, "LOW": 0}
    for r in rows:
        counts[r["stockout_risk"]] += 1
    items = [{**dict(r), "model_version": run["model_version"]} for r in rows if r["stockout_risk"] in risks]
    return run, items, counts


def recommendations(engine: Engine, shop_id: int, only_needed: bool) -> tuple[dict, list[dict]]:
    s = _s(engine)
    with engine.connect() as conn:
        run = latest_run(conn, s, shop_id)
        rows = conn.execute(text(f"""
            SELECT rr.product_id, so.sku, so.product_name, so.category, rr.current_stock, rr.on_order,
                   so.predicted_demand_horizon::float AS cumulative_predicted_demand, rr.safety_stock::float AS safety_stock,
                   rr.reorder_point::float AS reorder_point, rr.order_up_to::float AS order_up_to, rr.lead_days,
                   rr.minimum_order_quantity, rr.recommended_quantity AS recommended_reorder_quantity, rr.order_by_date,
                   so.stockout_risk, rr.reason
            FROM {s}.shop_replenishment_recommendations rr
            JOIN {s}.shop_stockout_predictions so ON so.run_id = rr.run_id AND so.product_id = rr.product_id
            WHERE rr.run_id = CAST(:run AS uuid) AND (NOT :only OR rr.recommended_quantity > 0)
            ORDER BY rr.order_by_date NULLS LAST, CASE so.stockout_risk WHEN 'HIGH' THEN 0 WHEN 'MEDIUM' THEN 1 ELSE 2 END,
                     rr.recommended_quantity DESC, so.product_name"""),
            {"run": run["run_id"], "only": only_needed}).mappings().all()
    return run, [{**dict(r), "model_version": run["model_version"]} for r in rows]


def model_metrics(engine: Engine, shop_id: int, evaluation: str | None) -> dict:
    """The pooled model's overall accuracy, plus its accuracy on THIS shop only (never another shop's)."""
    s = _s(engine)
    with engine.connect() as conn:
        meta = conn.execute(text(f"""
            SELECT model_version, model_type, trained_at, train_start, train_end, validation_end, test_end, training_rows,
                   best_iteration FROM {s}.model_metadata WHERE is_active AND data_source = 'smartpos'""")).mappings().first()
        if meta is None:
            raise NotFound("No forecasting model has been trained yet.")
        rows = conn.execute(text(f"""
            SELECT evaluation, method, target, scope, scope_id, mae::float AS mae, rmse::float AS rmse, wape::float AS wape,
                   bias::float AS bias, n_obs
            FROM {s}.model_evaluation_metrics WHERE model_version = :v
              AND (CAST(:e AS text) IS NULL OR evaluation = CAST(:e AS text))
              AND (scope = 'overall' OR (scope = 'store' AND scope_id = :shop))
            ORDER BY evaluation, scope, method"""),
            {"v": meta["model_version"], "e": evaluation, "shop": str(shop_id)}).mappings().all()
    return {**dict(meta), "metrics": [{**dict(r), "scope": "shop" if r["scope"] == "store" else r["scope"]} for r in rows]}
