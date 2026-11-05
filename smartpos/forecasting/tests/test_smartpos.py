"""The real-shop data source and forecast run, on a throwaway SmartPOS-shaped schema with known answers."""

from datetime import datetime, timedelta, timezone

import pytest
from sqlalchemy import create_engine, text

from novac_forecast.pipeline import run_shop_forecasts
from novac_forecast.smartpos import last_complete_day, load_shops
from novac_forecast.train import NotEnoughHistory, train_and_evaluate
from tests.conftest import URL
from tests.smartpos_fixture import RESTOCK_DAY, THROUGH, TZ_DAY, table_fingerprint

pytestmark = pytest.mark.skipif(not URL, reason="set FORECAST_TEST_DATABASE_URL to run PostgreSQL tests")


@pytest.fixture(scope="module")
def data(shops, pg_engine):
    return load_shops(pg_engine, through=THROUGH, schema=shops.schema)


def rows_for(data, product):
    return data.history[data.history["product_id"] == str(product)].set_index("date")


def test_units_are_completed_bills_per_local_day(data, shops):
    h = data.history
    assert h["units_sold"].sum() == sum(shops.sold.values())                # the voided bill is not counted
    for product in (11, 12, 21):
        got = rows_for(data, product)["units_sold"]
        for day, units in got.items():
            assert units == shops.sold.get((product, day), 0)


def test_a_bill_just_after_midnight_counts_on_the_local_day(data, shops):
    assert shops.sold[(12, TZ_DAY)] == rows_for(data, 12).loc[TZ_DAY, "units_sold"]
    assert TZ_DAY in set(data.history[data.history["store_id"] == "1"]["date"])


def test_days_without_any_bill_are_missing_not_zero(data, shops):
    shop2_days = set(data.history[data.history["store_id"] == "2"]["date"])
    assert shop2_days == shops.trading[2] and all(d.weekday() != 6 for d in shop2_days)
    assert data.trading_days == {1: len(shops.trading[1]), 2: len(shops.trading[2]), 3: 3}
    tea = rows_for(data, 14)                                                  # a trading day with no tea sold is a zero
    zero_days = [d for d in shops.trading[1] if (14, d) not in shops.sold]
    assert zero_days and all(tea.loc[d, "units_sold"] == 0 for d in zero_days)


def test_days_that_end_out_of_stock_are_censored(data, shops):
    curd = rows_for(data, 13)
    expected = {d for (p, d), closing in shops.closing.items() if p == 13 and closing == 0}
    assert expected and set(curd.index[curd["stockout_flag"]]) == expected
    assert not curd.loc[RESTOCK_DAY, "stockout_flag"]


def test_one_shop_only(shops, pg_engine):
    one = load_shops(pg_engine, through=THROUGH, shop_id=2, schema=shops.schema)
    assert set(one.history["store_id"]) == {"2"} and set(one.products["shop_id"]) == {2}


def test_last_complete_day_is_yesterday_in_the_shop_time_zone():
    late_utc = datetime(2026, 11, 4, 19, 0, tzinfo=timezone.utc)                # 00:30 on 5 Nov in India
    assert str(last_complete_day("Asia/Kolkata", late_utc)) == "2026-11-04"
    assert str(last_complete_day("UTC", late_utc)) == "2026-11-03"


@pytest.fixture(scope="module")
def forecast(shops, pg_engine, tmp_path_factory):
    before = table_fingerprint(create_engine(URL), shops.schema)
    artifacts = tmp_path_factory.mktemp("shop_artifacts")
    report = train_and_evaluate(pg_engine, artifacts, tmp_path_factory.mktemp("shop_reports"), quick=True,
                                source="smartpos", through=THROUGH, smartpos_schema=shops.schema)
    runs = run_shop_forecasts(pg_engine, artifacts, through=THROUGH, smartpos_schema=shops.schema)
    return {"report": report, "runs": runs, "artifacts": artifacts, "before": before}


def test_training_reports_observed_sales_only(forecast):
    report = forecast["report"]
    assert report["data_source"] == "smartpos" and report["model_version"].startswith("shops-lgbm-")
    assert all("/units_sold/" in k for k in report["overall"])               # no latent demand for real shops


def test_one_run_per_shop_with_enough_history_and_only_active_products(forecast, pg_engine):
    runs = {r["shop_id"]: r for r in forecast["runs"]}
    assert set(runs) == {1, 2}                                               # shop 3 has 3 trading days: skipped
    assert runs[1]["series"] == 4 and runs[2]["series"] == 2                 # product 23 is inactive
    with pg_engine.connect() as conn:
        s = pg_engine.get_execution_options()["schema_translate_map"]["forecasting"]
        products = conn.execute(text(f'SELECT DISTINCT product_id FROM "{s}".shop_demand_forecasts WHERE run_id = CAST(:r AS uuid)'),
                                {"r": runs[2]["run_id"]}).scalars().all()
    assert sorted(products) == [21, 22]


def test_lead_time_and_case_size_come_from_the_product(forecast, pg_engine):
    s = pg_engine.get_execution_options()["schema_translate_map"]["forecasting"]
    with pg_engine.connect() as conn:
        run = next(r["run_id"] for r in forecast["runs"] if r["shop_id"] == 1)
        lead, moq, qty = conn.execute(text(f"""SELECT lead_days, minimum_order_quantity, recommended_quantity
            FROM "{s}".shop_replenishment_recommendations WHERE run_id = CAST(:r AS uuid) AND product_id = 14"""),
            {"r": run}).one()
    assert (lead, moq) == (10, 12) and qty % 12 == 0


def test_a_shop_with_too_little_history_is_refused(forecast, shops, pg_engine):
    with pytest.raises(NotEnoughHistory, match="at least 7 days"):
        run_shop_forecasts(pg_engine, forecast["artifacts"], through=THROUGH, shop_id=3, smartpos_schema=shops.schema)


def test_the_smartpos_tables_are_never_written(forecast, shops):
    assert table_fingerprint(create_engine(URL), shops.schema) == forecast["before"]


def test_training_needs_six_weeks(shops, pg_engine, tmp_path):
    with pytest.raises(NotEnoughHistory):
        train_and_evaluate(pg_engine, tmp_path, tmp_path, quick=True, source="smartpos",
                           through=THROUGH - timedelta(days=30), smartpos_schema=shops.schema)
