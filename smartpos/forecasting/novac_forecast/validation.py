"""Data-quality checks and an exploratory summary of the dataset loaded in PostgreSQL.

Every check is a SQL query that counts violations (0 = pass). The EDA part summarises the demand patterns the
model is expected to learn, so a broken generator or a bad load shows up before training.
"""

from __future__ import annotations

import json
from pathlib import Path

import pandas as pd
from sqlalchemy import Engine, text

from .db import check_schema_name, schema_of

CHECKS = {
    "duplicate_daily_sales": "SELECT count(*) FROM (SELECT 1 FROM {s}.daily_sales GROUP BY store_id, product_id, date HAVING count(*) > 1) x",
    "duplicate_inventory_rows": "SELECT count(*) FROM (SELECT 1 FROM {s}.inventory_history GROUP BY store_id, product_id, date HAVING count(*) > 1) x",
    "null_required_sales_values": "SELECT count(*) FROM {s}.daily_sales WHERE units_sold IS NULL OR latent_demand IS NULL OR unit_price IS NULL OR revenue IS NULL OR promotion_flag IS NULL",
    "null_required_inventory_values": "SELECT count(*) FROM {s}.inventory_history WHERE opening_stock IS NULL OR closing_stock IS NULL OR stockout_flag IS NULL",
    "negative_quantities": "SELECT count(*) FROM {s}.daily_sales WHERE units_sold < 0 OR latent_demand < 0 OR lost_demand < 0",
    "sold_more_than_demanded": "SELECT count(*) FROM {s}.daily_sales WHERE units_sold > latent_demand",
    "ledger_does_not_balance": "SELECT count(*) FROM {s}.inventory_history WHERE closing_stock <> opening_stock + stock_received - units_sold + inventory_adjustment",
    "sales_without_inventory_row": "SELECT count(*) FROM {s}.daily_sales d LEFT JOIN {s}.inventory_history i USING (store_id, product_id, date) WHERE i.store_id IS NULL",
    "units_differ_between_sales_and_inventory": "SELECT count(*) FROM {s}.daily_sales d JOIN {s}.inventory_history i USING (store_id, product_id, date) WHERE d.units_sold <> i.units_sold",
    "stockout_flag_disagrees_with_lost_demand": "SELECT count(*) FROM {s}.daily_sales d JOIN {s}.inventory_history i USING (store_id, product_id, date) WHERE (d.lost_demand > 0) <> i.stockout_flag",
    "revenue_mismatch": "SELECT count(*) FROM {s}.daily_sales WHERE abs(revenue - round(units_sold * unit_price * (1 - discount_percentage / 100), 2)) > 0.01",
    "orphan_sales_rows": "SELECT count(*) FROM {s}.daily_sales d LEFT JOIN {s}.products p USING (product_id) LEFT JOIN {s}.stores st USING (store_id) WHERE p.product_id IS NULL OR st.store_id IS NULL",
    "purchase_orders_received_before_ordered": "SELECT count(*) FROM {s}.purchase_orders WHERE received_on < ordered_on",
    "products_without_supplier_lead_time": "SELECT count(*) FROM {s}.products WHERE supplier_lead_days IS NULL OR supplier_lead_days <= 0",
}


def _q(conn, sql: str, s: str) -> pd.DataFrame:
    return pd.read_sql_query(text(sql.format(s=s)), conn)


def run_validation(engine: Engine, report_dir: Path | None = None) -> dict:
    s = f'"{check_schema_name(schema_of(engine))}"'
    with engine.connect() as conn:
        checks = {name: int(conn.execute(text(sql.format(s=s))).scalar_one()) for name, sql in CHECKS.items()}
        shape = _q(conn, """
            SELECT (SELECT count(*) FROM {s}.stores) AS stores, (SELECT count(*) FROM {s}.products) AS products,
                   (SELECT count(*) FROM {s}.daily_sales) AS sales_rows, min(date) AS first_day, max(date) AS last_day,
                   count(DISTINCT date) AS days, sum(units_sold) AS units_sold, sum(latent_demand) AS latent_demand,
                   sum(lost_demand) AS lost_demand FROM {s}.daily_sales""", s).iloc[0].to_dict()
        # Missing days: a series should have a row for every day from its first appearance to the end.
        gaps = _q(conn, """
            SELECT count(*) AS series, sum(expected - present) AS missing_days,
                   count(*) FILTER (WHERE expected > present) AS series_with_gaps
            FROM (SELECT store_id, product_id, count(*) AS present, (max(date) - min(date) + 1) AS expected
                  FROM {s}.daily_sales GROUP BY 1, 2) x""", s).iloc[0].to_dict()
        by_category = _q(conn, """
            SELECT c.category_name, round(avg(d.latent_demand), 2) AS mean_daily_demand,
                   round(100.0 * sum(d.lost_demand) / nullif(sum(d.latent_demand), 0), 2) AS lost_demand_pct,
                   round(100.0 * avg(d.promotion_flag::int), 2) AS promo_day_pct
            FROM {s}.daily_sales d JOIN {s}.products p USING (product_id) JOIN {s}.product_categories c USING (category_id)
            GROUP BY 1 ORDER BY 2 DESC""", s)
        by_store = _q(conn, """
            SELECT st.store_id, st.city, st.store_type, round(sum(d.latent_demand)::numeric / count(DISTINCT d.date), 0) AS units_per_day,
                   round(100.0 * sum(d.lost_demand) / nullif(sum(d.latent_demand), 0), 2) AS lost_demand_pct
            FROM {s}.daily_sales d JOIN {s}.stores st USING (store_id) GROUP BY 1, 2, 3 ORDER BY 1""", s)
        weekday = _q(conn, """
            SELECT CASE WHEN st.city = 'Chennai' THEN 'Chennai' ELSE 'Other cities' END AS area,
                   extract(isodow FROM d.date)::int AS iso_dow, avg(d.latent_demand) AS demand
            FROM {s}.daily_sales d JOIN {s}.stores st USING (store_id) GROUP BY 1, 2""", s)
        festival = _q(conn, """
            WITH daily AS (SELECT date, holiday_name, sum(latent_demand) AS demand FROM {s}.daily_sales GROUP BY 1, 2)
            SELECT coalesce(holiday_name, 'Ordinary day') AS period, round(avg(demand), 0) AS chain_units_per_day, count(*) AS days
            FROM daily GROUP BY 1 ORDER BY 2 DESC""", s)
        promo = _q(conn, """
            SELECT c.category_name, round(avg(d.latent_demand) FILTER (WHERE d.promotion_flag) / nullif(avg(d.latent_demand) FILTER (WHERE NOT d.promotion_flag), 0), 2) AS promo_lift
            FROM {s}.daily_sales d JOIN {s}.products p USING (product_id) JOIN {s}.product_categories c USING (category_id)
            GROUP BY 1 ORDER BY 2 DESC""", s)
        monthly = _q(conn, """
            SELECT c.category_name, extract(month FROM d.date)::int AS month, avg(d.latent_demand) AS demand
            FROM {s}.daily_sales d JOIN {s}.products p USING (product_id) JOIN {s}.product_categories c USING (category_id)
            GROUP BY 1, 2""", s)
        stockouts_by_supplier = _q(conn, """
            SELECT su.supplier_name, su.lead_days, round(100.0 * avg(i.stockout_flag::int), 2) AS stockout_day_pct
            FROM {s}.inventory_history i JOIN {s}.products p USING (product_id) JOIN {s}.suppliers su USING (supplier_id)
            GROUP BY 1, 2 ORDER BY 3 DESC""", s)

    wk = weekday.pivot(index="iso_dow", columns="area", values="demand")
    weekend_ratio = (wk.loc[[6, 7]].mean() / wk.loc[[1, 2, 3, 4, 5]].mean()).round(3).to_dict()
    season = monthly.pivot(index="month", columns="category_name", values="demand")
    summer_vs_winter = (season.loc[[4, 5, 6]].mean() / season.loc[[11, 12, 1]].mean()).round(3).to_dict()

    report = {
        "passed": all(v == 0 for v in checks.values()),
        "checks": checks,
        "shape": {k: (int(v) if hasattr(v, "__int__") and not isinstance(v, str) and k not in ("first_day", "last_day") else str(v)) for k, v in shape.items()},
        "missing_days": {k: int(v) for k, v in gaps.items()},
        "lost_demand_pct": round(100 * float(shape["lost_demand"]) / float(shape["latent_demand"]), 2),
        "weekend_vs_weekday_ratio": weekend_ratio,
        "summer_vs_winter_ratio_by_category": summer_vs_winter,
        "festival_vs_ordinary_chain_demand": festival.to_dict("records"),
        "promotion_lift_by_category": promo.to_dict("records"),
        "by_category": by_category.to_dict("records"),
        "by_store": by_store.to_dict("records"),
        "stockouts_by_supplier": stockouts_by_supplier.to_dict("records"),
    }
    if report_dir:
        report_dir.mkdir(parents=True, exist_ok=True)
        (report_dir / "data_quality.json").write_text(json.dumps(report, indent=2, default=str))
    return report
