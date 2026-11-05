"""The FastAPI service end to end, on throwaway schemas: known shop data -> train -> forecast -> every endpoint.
The main property: a shop only ever sees its own forecasts."""

import os

import pytest
from fastapi.testclient import TestClient

from novac_forecast import config
from novac_forecast.api import app as api
from novac_forecast.pipeline import run_shop_forecasts
from novac_forecast.train import train_and_evaluate
from tests.conftest import URL
from tests.smartpos_fixture import PRODUCTS, THROUGH

pytestmark = pytest.mark.skipif(not URL, reason="set FORECAST_TEST_DATABASE_URL to run API tests")
KEY = "test-service-key-0123456789abcdef-xyz"
SHOP1 = {p[0] for p in PRODUCTS if p[1] == 1}
SHOP2_ACTIVE = {p[0] for p in PRODUCTS if p[1] == 2 and p[8]}


@pytest.fixture(scope="module")
def client(pg_engine, shops, tmp_path_factory):
    artifacts = tmp_path_factory.mktemp("artifacts")
    os.environ.update(FORECAST_SERVICE_KEY=KEY, FORECAST_ARTIFACT_DIR=str(artifacts), FORECAST_SMARTPOS_SCHEMA=shops.schema)
    config.get_settings.cache_clear()
    train_and_evaluate(pg_engine, artifacts, tmp_path_factory.mktemp("reports"), quick=True, source="smartpos", through=THROUGH)
    run_shop_forecasts(pg_engine, artifacts, through=THROUGH)
    original = api.engine
    api.engine = lambda: pg_engine
    yield TestClient(api.app, headers={"X-Service-Key": KEY})
    api.engine = original
    for name in ("FORECAST_SERVICE_KEY", "FORECAST_ARTIFACT_DIR", "FORECAST_SMARTPOS_SCHEMA"):
        os.environ.pop(name, None)
    config.get_settings.cache_clear()


def test_health_is_public_and_reveals_nothing_secret(client):
    r = TestClient(api.app).get("/health")
    assert r.status_code == 200 and r.json()["status"] == "ok" and r.json()["model_version"].startswith("shops-")
    body = r.text.lower()
    assert "password" not in body and "postgresql" not in body and KEY not in r.text and "shop" not in r.json()


def test_every_other_endpoint_needs_the_service_key(client):
    anonymous = TestClient(api.app)
    for path in ("/shops/1/status", "/shops/1/forecast", "/shops/1/stockout-alerts", "/shops/1/model-metrics"):
        assert anonymous.get(path).status_code == 401
        assert anonymous.get(path, headers={"X-Service-Key": "wrong" * 8}).status_code == 401
    assert anonymous.post("/shops/1/forecast/generate").status_code == 401


def test_status_says_beta_and_shows_only_this_shops_run(client):
    one, three = client.get("/shops/1/status").json(), client.get("/shops/3/status").json()
    assert one["beta"] is True and "beta" in one["note"].lower()
    assert one["latest_run"]["as_of_date"] == str(THROUGH) and one["latest_run"]["trading_days"] == 63
    assert three["latest_run"] is None and three["model_version"] == one["model_version"]


def test_a_shop_sees_only_its_own_products(client):
    one = client.get("/shops/1/forecast").json()
    two = client.get("/shops/2/forecast").json()
    assert {i["product_id"] for i in one["items"]} == SHOP1
    assert {i["product_id"] for i in two["items"]} == SHOP2_ACTIVE              # the inactive product is not forecast
    assert one["run"]["run_id"] != two["run"]["run_id"]
    for alerts in (client.get("/shops/2/stockout-alerts", params={"risk": ["HIGH", "MEDIUM", "LOW"]}).json()["items"],
                   client.get("/shops/2/replenishment-recommendations", params={"only_needed": False}).json()["items"]):
        assert {a["product_id"] for a in alerts} == SHOP2_ACTIVE


def test_another_shops_product_is_not_found(client):
    other = next(iter(SHOP1))
    assert client.get(f"/shops/1/products/{other}/forecast").status_code == 200
    r = client.get(f"/shops/2/products/{other}/forecast")
    assert r.status_code == 404 and str(other) in r.json()["detail"]


def test_a_shop_without_a_forecast_gets_404(client):
    for path in ("/shops/3/forecast", "/shops/3/stockout-alerts", "/shops/999/forecast"):
        r = client.get(path)
        assert r.status_code == 404 and "No forecast" in r.json()["detail"]


def test_product_rows_add_up(client):
    r = client.get("/shops/1/forecast", params={"horizon": 7}).json()
    item = r["items"][0]
    assert len(item["daily"]) == 7 and all(d["predicted_demand"] >= 0 for d in item["daily"])
    assert abs(item["cumulative_predicted_demand"] - sum(d["predicted_demand"] for d in item["daily"])) < 0.05
    assert abs(r["total_predicted_demand"] - sum(d["predicted_demand"] for d in r["daily_totals"])) < 0.5
    dairy = client.get("/shops/1/forecast", params={"category": "Dairy"}).json()["items"]
    assert {i["category"] for i in dairy} == {"Dairy"} and len(dairy) == 2


def test_shorter_horizons_are_slices_of_the_same_run(client):
    seven = client.get("/shops/1/forecast", params={"horizon": 7}).json()
    three = client.get("/shops/1/forecast", params={"horizon": 3}).json()
    assert three["run"]["run_id"] == seven["run"]["run_id"]
    assert all(len(i["daily"]) == 3 for i in three["items"])
    assert three["items"][0]["daily"] == seven["items"][0]["daily"][:3]


def test_category_forecast_is_exactly_the_sum_of_its_products(client):
    products = client.get("/shops/1/forecast").json()["items"]
    categories = client.get("/shops/1/categories/forecast").json()["items"]
    assert {c["category"] for c in categories} == {"Dairy", "Bakery", "Beverages"}
    for c in categories:
        mine = [p for p in products if p["category"] == c["category"]]
        assert c["product_count"] == len(mine)
        for day_index, day in enumerate(c["daily"]):
            assert abs(day["predicted_demand"] - sum(p["daily"][day_index]["predicted_demand"] for p in mine)) < 0.05


def test_stockout_alerts_and_recommendations(client):
    alerts = client.get("/shops/1/stockout-alerts").json()
    assert sum(alerts["counts"].values()) == 4 and "not calibrated probabilities" in alerts["risk_note"]
    assert all(a["stockout_risk"] in ("HIGH", "MEDIUM") for a in alerts["items"])
    everything = client.get("/shops/1/stockout-alerts", params={"risk": ["HIGH", "MEDIUM", "LOW"]}).json()["items"]
    order = {"HIGH": 0, "MEDIUM": 1, "LOW": 2}
    assert [order[a["stockout_risk"]] for a in everything] == sorted(order[a["stockout_risk"]] for a in everything)
    recs = client.get("/shops/1/replenishment-recommendations", params={"only_needed": False}).json()["items"]
    tea = next(r for r in recs if r["sku"] == "TEA-250")
    assert tea["lead_days"] == 10 and tea["minimum_order_quantity"] == 12 and tea["recommended_reorder_quantity"] % 12 == 0


def test_metrics_show_overall_and_this_shop_only(client):
    m = client.get("/shops/2/model-metrics", params={"evaluation": "test"}).json()
    assert m["metrics"] and {r["scope"] for r in m["metrics"]} == {"overall", "shop"}
    assert {r["scope_id"] for r in m["metrics"] if r["scope"] == "shop"} == {"2"}
    assert {r["target"] for r in m["metrics"]} == {"units_sold"}
    assert {r["method"] for r in m["metrics"]} == {"lightgbm", "moving_average_7", "same_day_last_week"}


def test_validation(client):
    assert client.get("/shops/0/forecast").status_code == 422
    assert client.get("/shops/abc/forecast").status_code == 422
    assert client.get("/shops/1/forecast", params={"horizon": 0}).status_code == 422
    assert client.get("/shops/1/forecast", params={"horizon": 8}).status_code == 422
    assert client.get("/shops/1/forecast", params={"category": "x" * 101}).status_code == 422
    assert client.get("/shops/1/stockout-alerts", params={"risk": "SEVERE"}).status_code == 422
    assert client.post("/shops/1/forecast/generate", json={"horizon": 30}).status_code == 422


def test_generate_makes_a_new_run_for_that_shop_only(client):
    before_1 = client.get("/shops/1/status").json()["latest_run"]["run_id"]
    before_2 = client.get("/shops/2/status").json()["latest_run"]["run_id"]
    r = client.post("/shops/2/forecast/generate", json={"horizon": 7})
    assert r.status_code == 200 and r.json()["shop_id"] == 2 and r.json()["forecast_rows"] == len(SHOP2_ACTIVE) * 7
    assert client.get("/shops/2/status").json()["latest_run"]["run_id"] not in (before_2, before_1)
    assert client.get("/shops/1/status").json()["latest_run"]["run_id"] == before_1


def test_generate_refuses_a_shop_with_too_little_history(client):
    r = client.post("/shops/3/forecast/generate", json={"horizon": 7})
    assert r.status_code == 409 and "at least 7 days" in r.json()["detail"]
