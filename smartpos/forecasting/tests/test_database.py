"""PostgreSQL persistence: migration, transaction-safe loading, and the database's own integrity rules.

Each test session migrates a throwaway schema (forecasting_test_<random>) and drops it afterwards, so these tests
never touch the real forecasting schema. Needs FORECAST_TEST_DATABASE_URL (or FORECAST_DATABASE_URL); skipped otherwise.
"""

from datetime import date

import pytest
from sqlalchemy import text
from sqlalchemy.exc import IntegrityError

from novac_forecast import load as loader
from novac_forecast.db import DECLARED_SCHEMA
from tests.conftest import URL

pytestmark = pytest.mark.skipif(not URL, reason="set FORECAST_TEST_DATABASE_URL to run PostgreSQL tests")


@pytest.fixture
def empty(pg_engine):
    yield pg_engine
    schema = pg_engine.get_execution_options()["schema_translate_map"][DECLARED_SCHEMA]
    with pg_engine.begin() as conn:
        tables = ", ".join(f'"{schema}"."{t}"' for t in (*loader.DERIVED_TABLES, *loader.SHOP_TABLES, "model_evaluation_metrics",
                                                          "model_metadata", "dataset_runs", *loader.TABLE_COLUMNS))
        conn.execute(text(f"TRUNCATE {tables} RESTART IDENTITY"))


def schema(engine) -> str:
    """Raw SQL isn't schema-translated, so every text() query names the throwaway schema explicitly."""
    return engine.get_execution_options()["schema_translate_map"][DECLARED_SCHEMA]


def count(engine, table):
    with engine.connect() as conn:
        return conn.execute(text(f'SELECT count(*) FROM "{schema(engine)}".{table}')).scalar_one()


def test_load_writes_every_table_and_records_the_dataset(empty, small_dataset):
    counts = loader.load_dataset(empty, small_dataset)
    assert counts["daily_sales"] == len(small_dataset.daily_sales) > 0
    for table, n in counts.items():
        assert count(empty, table) == n
    with empty.connect() as conn:
        seed, days = conn.execute(text(f'SELECT seed, days FROM "{schema(empty)}".dataset_runs')).one()
        assert (seed, days) == (small_dataset.config.seed, small_dataset.config.days)
        # Empty holiday names become NULL, not "".
        assert conn.execute(text(f"SELECT count(*) FROM \"{schema(empty)}\".daily_sales WHERE holiday_name = ''")).scalar_one() == 0


def test_a_second_load_is_refused_unless_replacing(empty, small_dataset):
    loader.load_dataset(empty, small_dataset)
    with pytest.raises(loader.DatasetAlreadyLoaded):
        loader.load_dataset(empty, small_dataset)
    loader.load_dataset(empty, small_dataset, replace=True)
    assert count(empty, "daily_sales") == len(small_dataset.daily_sales)
    assert count(empty, "dataset_runs") == 1


def test_a_failure_part_way_through_leaves_nothing_behind(empty, small_dataset, monkeypatch):
    real_copy = loader._copy
    def failing_copy(cur, schema, table, df, **kw):
        if table == "inventory_history":
            raise RuntimeError("simulated failure")
        real_copy(cur, schema, table, df, **kw)
    monkeypatch.setattr(loader, "_copy", failing_copy)
    with pytest.raises(RuntimeError):
        loader.load_dataset(empty, small_dataset)
    assert count(empty, "stores") == 0 and count(empty, "daily_sales") == 0


def test_duplicates_bad_ledgers_and_orphans_are_rejected_by_the_database(empty, small_dataset):
    loader.load_dataset(empty, small_dataset)
    row = small_dataset.daily_sales.iloc[0]
    sc = schema(empty)
    insert_sale = text(f'INSERT INTO "{sc}".daily_sales VALUES (:s, :p, :d, 1, 1, 0, 10, 10, false, 0, false, NULL)')
    with pytest.raises(IntegrityError):      # same store/product/date twice
        with empty.begin() as conn:
            conn.execute(insert_sale, {"s": row["store_id"], "p": row["product_id"], "d": row["date"]})
    with pytest.raises(IntegrityError):      # unknown store
        with empty.begin() as conn:
            conn.execute(insert_sale, {"s": "STR999", "p": row["product_id"], "d": date(2030, 1, 1)})
    with pytest.raises(IntegrityError):      # sold more than demanded
        with empty.begin() as conn:
            conn.execute(text(f"INSERT INTO \"{sc}\".daily_sales VALUES (:s, :p, '2030-01-01', 5, 3, -2, 10, 50, false, 0, false, NULL)"),
                         {"s": row["store_id"], "p": row["product_id"]})
    with pytest.raises(IntegrityError):      # stock ledger that doesn't add up
        with empty.begin() as conn:
            conn.execute(text(f"INSERT INTO \"{sc}\".inventory_history VALUES (:s, :p, '2030-01-01', 10, 0, 2, 0, 9, false)"),
                         {"s": row["store_id"], "p": row["product_id"]})


def test_indexes_cover_store_product_date_queries(pg_engine):
    schema = pg_engine.get_execution_options()["schema_translate_map"][DECLARED_SCHEMA]
    with pg_engine.connect() as conn:
        defs = conn.execute(text("SELECT indexdef FROM pg_indexes WHERE schemaname = :s"), {"s": schema}).scalars().all()
    joined = "\n".join(defs)
    assert '(store_id, product_id, date)' in joined          # composite primary keys on the daily tables
    assert 'daily_sales USING btree (product_id, date)' in joined
    assert 'demand_forecasts USING btree (store_id, forecast_date)' in joined


def test_replacing_the_synthetic_dataset_keeps_the_shops_model_and_runs(empty, small_dataset):
    loader.load_dataset(empty, small_dataset)
    sc = schema(empty)
    with empty.begin() as conn:
        for version, source in (("lgbm-synthetic", "synthetic"), ("shops-lgbm-x", "smartpos")):
            conn.execute(text(f"""INSERT INTO "{sc}".model_metadata (model_version, model_type, target, train_start, train_end,
                training_rows, features, params, artifact_path, artifact_sha256, is_active, data_source)
                VALUES (:v, 'lightgbm', 't', '2026-01-01', '2026-02-01', 1, '[]', '{{}}', 'a', 'b', true, :src)"""),
                         {"v": version, "src": source})
        conn.execute(text(f"""INSERT INTO "{sc}".shop_forecast_runs (run_id, shop_id, model_version, as_of_date, forecast_start,
            horizon, trading_days, series_count) VALUES (gen_random_uuid(), 1, 'shops-lgbm-x', '2026-02-01', '2026-02-02', 7, 30, 4)"""))
    loader.load_dataset(empty, small_dataset, replace=True)
    with empty.connect() as conn:
        assert conn.execute(text(f'SELECT array_agg(model_version) FROM "{sc}".model_metadata')).scalar_one() == ["shops-lgbm-x"]
    assert count(empty, "shop_forecast_runs") == 1


def test_one_active_model_per_data_source(empty):
    sc = schema(empty)
    insert = text(f"""INSERT INTO "{sc}".model_metadata (model_version, model_type, target, train_start, train_end, training_rows,
        features, params, artifact_path, artifact_sha256, is_active, data_source)
        VALUES (:v, 'lightgbm', 't', '2026-01-01', '2026-02-01', 1, '[]', '{{}}', 'a', 'b', true, :src)""")
    with empty.begin() as conn:
        conn.execute(insert, {"v": "a", "src": "synthetic"})
        conn.execute(insert, {"v": "b", "src": "smartpos"})
    with pytest.raises(IntegrityError):
        with empty.begin() as conn:
            conn.execute(insert, {"v": "c", "src": "smartpos"})
