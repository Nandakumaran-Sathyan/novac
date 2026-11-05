"""The fabricated dataset: shape, integrity, and the demand patterns it is supposed to contain."""

from datetime import date

import numpy as np
import pandas as pd

from novac_forecast.synthetic import catalog
from novac_forecast.synthetic.generator import GeneratorConfig, generate


def test_same_seed_gives_identical_data_and_a_different_seed_does_not():
    cfg = GeneratorConfig(days=60, store_ids=("STR002",), product_limit=10)
    a, b = generate(cfg), generate(cfg)
    pd.testing.assert_frame_equal(a.daily_sales, b.daily_sales)
    pd.testing.assert_frame_equal(a.inventory_history, b.inventory_history)
    c = generate(GeneratorConfig(days=60, store_ids=("STR002",), product_limit=10, seed=1))
    assert not a.daily_sales["latent_demand"].equals(c.daily_sales["latent_demand"])


def test_catalogue_meets_the_brief(full_dataset):
    stores, products = full_dataset.stores, full_dataset.products
    assert len(stores) >= 10
    assert {"Chennai", "Trichy", "Coimbatore", "Madurai", "Salem", "Erode", "Tirunelveli"} <= set(stores["city"])
    assert set(stores.columns) >= {"store_id", "store_name", "city", "state", "region", "store_type", "latitude", "longitude"}
    assert len(products) >= 100
    assert set(products["category_name"]) == {"Grocery", "Dairy", "Beverages", "Snacks", "Personal Care",
                                              "Household Products", "Fruits and Vegetables"}
    assert set(products.columns) >= {"product_id", "product_name", "category_id", "category_name", "unit_price",
                                     "supplier_id", "supplier_lead_days", "minimum_order_quantity"}
    assert products["product_id"].is_unique and stores["store_id"].is_unique
    assert (products["supplier_lead_days"] > 7).any(), "need suppliers slower than the forecast horizon"


def test_history_covers_two_years_and_has_no_duplicates_or_missing_values(full_dataset):
    sales, inv = full_dataset.daily_sales, full_dataset.inventory_history
    assert sales["date"].min() == date(2024, 10, 21) and sales["date"].max() == date(2026, 11, 4)
    assert date(2024, 10, 31) in set(sales["date"]) and date(2025, 10, 20) in set(sales["date"])   # both Diwalis
    assert not sales.duplicated(["date", "store_id", "product_id"]).any()
    assert not inv.duplicated(["date", "store_id", "product_id"]).any()
    # holiday_name is "" on ordinary days, never null; every other column is always filled.
    assert not sales.isna().any().any()
    assert not inv.isna().any().any()


def test_observed_sales_are_censored_latent_demand(full_dataset):
    s = full_dataset.daily_sales
    assert (s["units_sold"] <= s["latent_demand"]).all()
    assert (s["lost_demand"] == s["latent_demand"] - s["units_sold"]).all()
    assert (s[["units_sold", "latent_demand", "lost_demand"]] >= 0).all().all()
    expected = np.round(s["units_sold"] * s["unit_price"] * (1 - s["discount_percentage"] / 100), 2)
    assert np.allclose(s["revenue"], expected)
    assert (s["promotion_flag"] == (s["discount_percentage"] > 0)).all()


def test_inventory_ledger_balances_every_day(full_dataset):
    inv = full_dataset.inventory_history
    assert (inv["closing_stock"] == inv["opening_stock"] + inv["stock_received"] - inv["units_sold"] + inv["inventory_adjustment"]).all()
    assert (inv["closing_stock"] >= 0).all()
    # Each open day starts with what the previous open day ended with.
    inv = inv.sort_values(["store_id", "product_id", "date"])
    prev = inv.groupby(["store_id", "product_id"])["closing_stock"].shift()
    assert (inv["opening_stock"][prev.notna()] == prev[prev.notna()]).all()
    merged = inv.merge(full_dataset.daily_sales, on=["date", "store_id", "product_id"], suffixes=("", "_s"))
    assert len(merged) == len(inv) and (merged["units_sold"] == merged["units_sold_s"]).all()
    assert (merged["stockout_flag"] == (merged["lost_demand"] > 0)).all()


def test_stockouts_are_occasional_not_constant(full_dataset):
    rate = full_dataset.inventory_history["stockout_flag"].mean()
    assert 0.005 < rate < 0.10
    assert (full_dataset.purchase_orders["status"] == "open").any(), "outstanding orders feed replenishment"


def test_weekends_are_busier_and_more_so_in_chennai(full_dataset):
    s = full_dataset.daily_sales.merge(full_dataset.stores[["store_id", "city"]])
    s["weekend"] = pd.to_datetime(s["date"]).dt.dayofweek >= 5
    s["metro"] = s["city"] == "Chennai"
    lift = s.groupby(["metro", "weekend"])["latent_demand"].mean().unstack()
    ratio = lift[True] / lift[False]
    assert ratio[True] > 1.2 and ratio[False] > 1.05
    assert ratio[True] > ratio[False]


def test_diwali_and_pongal_lift_their_products(full_dataset):
    s = full_dataset.daily_sales
    by_day = s.groupby(["product_id", "date"])["latent_demand"].sum().unstack(0)
    by_day.index = pd.to_datetime(by_day.index)
    pid = {p.name: catalog.product_id(i) for i, p in enumerate(catalog.PRODUCTS)}
    def lift(name, festival_day, window=3):
        series = by_day[pid[name]]
        around = series[festival_day - pd.Timedelta(days=window):festival_day - pd.Timedelta(days=1)].mean()
        baseline = series[festival_day - pd.Timedelta(days=45):festival_day - pd.Timedelta(days=15)].mean()
        return around / baseline
    assert lift("Murukku 200 g", pd.Timestamp("2025-10-20")) > 2.0
    assert lift("Jaggery 1 kg", pd.Timestamp("2026-01-15")) > 2.0
    assert lift("Sugarcane (2 pieces)", pd.Timestamp("2026-01-15")) > 8.0
    assert lift("Shampoo 180 ml", pd.Timestamp("2025-10-20")) < 1.4      # not a festival product


def test_promotions_raise_demand_and_summer_raises_drinks(full_dataset):
    s = full_dataset.daily_sales.merge(full_dataset.products[["product_id", "category_id"]])
    snacks = s[s["category_id"] == "CAT04"]
    per_series = snacks.groupby(["store_id", "product_id", "promotion_flag"])["latent_demand"].mean().unstack()
    assert (per_series[True] / per_series[False]).median() > 1.15
    drinks = s[s["category_id"] == "CAT03"].copy()
    drinks["month"] = pd.to_datetime(drinks["date"]).dt.month
    monthly = drinks.groupby("month")["latent_demand"].mean()
    assert monthly.loc[[4, 5, 6]].mean() > 1.25 * monthly.loc[[11, 12, 1]].mean()


def test_new_products_and_store_closures_leave_gaps(full_dataset):
    s = full_dataset.daily_sales
    launched = full_dataset.products.dropna(subset=["launched_on"])
    assert len(launched) == 3
    for _, p in launched.iterrows():
        assert s.loc[s["product_id"] == p["product_id"], "date"].min() >= p["launched_on"]
    store, day = full_dataset.closures[0]
    assert s[(s["store_id"] == store) & (s["date"] == day)].empty
    chennai = full_dataset.stores.loc[full_dataset.stores["city"] == "Chennai", "store_id"]
    assert all((sid, date(2025, 12, 2)) in full_dataset.closures for sid in chennai)


def test_small_configuration_restricts_stores_and_products(small_dataset):
    assert set(small_dataset.daily_sales["store_id"]) == {"STR001", "STR008", "STR010"}
    assert small_dataset.daily_sales["product_id"].nunique() == 20
