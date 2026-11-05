"""Loads a generated SyntheticDataset into PostgreSQL in ONE transaction, using COPY for speed.

Safety: it refuses to load over existing data unless replace=True. Replacing truncates only the forecasting
schema's own tables (and, because forecasts point at stores and products, the derived forecasts too). Nothing
outside the configured forecasting schema is ever touched.
"""

from __future__ import annotations

import io
import json
import logging

import pandas as pd
from sqlalchemy import Engine, text

from .db import check_schema_name, schema_of
from .synthetic.generator import SyntheticDataset

log = logging.getLogger(__name__)

# Load order respects foreign keys.
TABLE_COLUMNS: dict[str, list[str]] = {
    "stores": ["store_id", "store_name", "city", "state", "region", "store_type", "latitude", "longitude"],
    "product_categories": ["category_id", "category_name"],
    "suppliers": ["supplier_id", "supplier_name", "lead_days", "delay_probability"],
    "products": ["product_id", "product_name", "category_id", "unit_price", "supplier_id", "supplier_lead_days",
                 "minimum_order_quantity", "is_perishable", "launched_on"],
    "daily_sales": ["store_id", "product_id", "date", "units_sold", "latent_demand", "lost_demand", "unit_price",
                    "revenue", "promotion_flag", "discount_percentage", "holiday_flag", "holiday_name"],
    "inventory_history": ["store_id", "product_id", "date", "opening_stock", "stock_received", "units_sold",
                          "inventory_adjustment", "closing_stock", "stockout_flag"],
    "purchase_orders": ["po_id", "store_id", "product_id", "supplier_id", "ordered_on", "expected_on", "received_on",
                        "quantity", "status"],
}
# Synthetic results that belong to the old dataset (TRUNCATEd on --replace). Synthetic models, their metrics and the
# dataset runs are DELETEd instead, because the real shops' models and runs live in the same tables or reference them.
DERIVED_TABLES = ("replenishment_recommendations", "stockout_predictions", "demand_forecasts", "forecast_runs")
SHOP_TABLES = ("shop_replenishment_recommendations", "shop_stockout_predictions", "shop_demand_forecasts", "shop_forecast_runs")


class DatasetAlreadyLoaded(RuntimeError):
    pass


def _frame_for(table: str, ds: SyntheticDataset) -> pd.DataFrame:
    frames = {"stores": ds.stores, "product_categories": ds.categories, "suppliers": ds.suppliers,
              "products": ds.products, "daily_sales": ds.daily_sales, "inventory_history": ds.inventory_history,
              "purchase_orders": ds.purchase_orders}
    df = frames[table][TABLE_COLUMNS[table]].copy()
    if table == "daily_sales":
        df["holiday_name"] = df["holiday_name"].replace("", None)
    return df


def _copy(raw_cursor, schema: str, table: str, df: pd.DataFrame, chunk_rows: int = 200_000) -> None:
    columns = ", ".join(f'"{c}"' for c in df.columns)
    with raw_cursor.copy(f'COPY "{schema}"."{table}" ({columns}) FROM STDIN WITH (FORMAT csv, NULL \'\')') as copy:
        for start in range(0, len(df), chunk_rows):
            buffer = io.StringIO()
            df.iloc[start:start + chunk_rows].to_csv(buffer, index=False, header=False, na_rep="")
            copy.write(buffer.getvalue())


def existing_row_counts(engine: Engine) -> dict[str, int]:
    schema = check_schema_name(schema_of(engine))
    with engine.connect() as conn:
        return {t: conn.execute(text(f'SELECT count(*) FROM "{schema}"."{t}"')).scalar_one() for t in TABLE_COLUMNS}


def load_dataset(engine: Engine, ds: SyntheticDataset, replace: bool = False) -> dict[str, int]:
    schema = check_schema_name(schema_of(engine))
    if any(existing_row_counts(engine).values()) and not replace:
        raise DatasetAlreadyLoaded(
            f"The {schema} schema already holds a synthetic dataset. Re-run with --replace to swap it (this also clears "
            "the synthetic forecasts, models and metrics, which belong to the old data; the shops' are kept).")
    counts: dict[str, int] = {}
    with engine.begin() as conn:
        if replace:
            tables = ", ".join(f'"{schema}"."{t}"' for t in (*DERIVED_TABLES, *reversed(list(TABLE_COLUMNS))))
            conn.execute(text(f"TRUNCATE {tables}"))
            synthetic = f"SELECT model_version FROM \"{schema}\".model_metadata WHERE data_source = 'synthetic'"
            conn.execute(text(f'DELETE FROM "{schema}".model_evaluation_metrics WHERE model_version IN ({synthetic})'))
            conn.execute(text(f'DELETE FROM "{schema}".model_metadata WHERE data_source = \'synthetic\''))
            conn.execute(text(f'DELETE FROM "{schema}".dataset_runs'))
        raw = conn.connection.driver_connection
        with raw.cursor() as cur:
            for table in TABLE_COLUMNS:
                df = _frame_for(table, ds)
                _copy(cur, schema, table, df)
                counts[table] = len(df)
                log.info("Loaded %s rows into %s.%s", f"{len(df):,}", schema, table)
        conn.execute(
            text(f'INSERT INTO "{schema}".dataset_runs (seed, start_date, end_date, days, summary) '
                 "VALUES (:seed, :start, :end, :days, CAST(:summary AS jsonb))"),
            {"seed": ds.config.seed, "start": ds.config.start_date, "end": ds.config.end_date, "days": ds.config.days,
             "summary": json.dumps(ds.summary())})
    return counts
