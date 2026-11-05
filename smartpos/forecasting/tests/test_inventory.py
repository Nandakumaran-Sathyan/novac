"""Stockout projection and replenishment, including the edge cases from the brief."""

from datetime import date

import numpy as np

from novac_forecast.inventory import SeriesInventory, project_stockout, recommend, safety_stock

DAY1 = date(2026, 11, 5)


def inv(stock=50, known=True, lead=3, moq=12, sigma=2.0, arrivals=None):
    return SeriesInventory(stock, known, lead, moq, sigma, arrivals or {})


def test_stockout_date_is_the_first_day_demand_cannot_be_met():
    daily = np.array([10.0] * 7)
    r = project_stockout(daily, inv(stock=35, lead=2), DAY1)
    assert r.predicted_stockout_date == date(2026, 11, 8)      # 35 covers 3.5 days: day 4 is short
    assert r.days_of_cover == 3.5
    assert r.cumulative_demand == 70.0
    assert r.risk == "MEDIUM"                                   # after the 2-day lead time: ordering now avoids it


def test_running_out_inside_the_lead_time_is_high_risk():
    r = project_stockout(np.array([10.0] * 7), inv(stock=15, lead=5), DAY1)
    assert r.predicted_stockout_date == date(2026, 11, 6) and r.risk == "HIGH"


def test_open_orders_arriving_in_time_prevent_the_stockout():
    r = project_stockout(np.array([10.0] * 7), inv(stock=25, lead=2, arrivals={2: 60}), DAY1)
    assert r.predicted_stockout_date is None and r.on_order_in_horizon == 60


def test_zero_inventory_is_high_risk_and_missing_inventory_is_treated_as_unknown():
    assert project_stockout(np.array([3.0] * 7), inv(stock=0), DAY1).risk == "HIGH"
    missing = project_stockout(np.array([3.0] * 7), inv(stock=0, known=False), DAY1)
    assert missing.risk == "HIGH" and "No inventory record" in missing.reason


def test_extremely_high_demand_runs_out_on_day_one():
    r = project_stockout(np.array([10_000.0] * 7), inv(stock=500, lead=1), DAY1)
    assert r.predicted_stockout_date == DAY1 and r.days_of_cover == 0.05 and r.risk == "HIGH"


def test_plenty_of_stock_is_low_risk():
    r = project_stockout(np.array([2.0] * 7), inv(stock=200, lead=3), DAY1)
    assert r.risk == "LOW" and r.predicted_stockout_date is None and r.days_of_cover == 100.0


def test_no_demand_never_stocks_out():
    r = project_stockout(np.zeros(7), inv(stock=0, lead=3), DAY1)
    assert r.predicted_stockout_date is None


def test_reorder_quantity_reaches_order_up_to_and_rounds_up_to_the_minimum_order():
    daily = np.array([10.0] * 7)
    r = recommend(daily, inv(stock=20, lead=3, moq=12, sigma=2.0), DAY1)
    ss = safety_stock(2.0, 3)
    assert abs(r.reorder_point - (30 + ss)) < 0.01
    assert abs(r.order_up_to - (100 + ss)) < 0.01                 # lead 3 + review 7 = 10 days of demand
    assert r.recommended_quantity % 12 == 0 and r.recommended_quantity >= 100 + ss - 20
    assert r.recommended_quantity - 12 < 100 + ss - 20             # the smallest multiple of 12 that is enough
    assert r.order_by_date == DAY1


def test_no_order_when_above_the_reorder_point_but_the_order_date_is_given():
    r = recommend(np.array([10.0] * 7), inv(stock=80, lead=2), DAY1)
    assert r.recommended_quantity == 0
    assert r.order_by_date is not None and r.order_by_date > DAY1


def test_lead_time_longer_than_the_horizon_extends_the_forecast():
    daily = np.array([10.0] * 7)
    r = recommend(daily, inv(stock=10, lead=10, moq=1, sigma=0.0), DAY1)
    assert r.reorder_point == 100.0                                # 7 forecast days + 3 more at the same rate
    assert r.order_up_to == 170.0                                  # 10 lead + 7 review days
    assert r.recommended_quantity == 160
    so = project_stockout(daily, inv(stock=10, lead=10), DAY1)
    assert so.risk == "HIGH"                                       # runs out long before a 10-day delivery


def test_open_orders_count_towards_the_position():
    without = recommend(np.array([10.0] * 7), inv(stock=10, lead=3, moq=1), DAY1)
    small_po = recommend(np.array([10.0] * 7), inv(stock=10, lead=3, moq=1, arrivals={2: 20}), DAY1)
    assert small_po.recommended_quantity == without.recommended_quantity - 20      # still below the reorder point
    big_po = recommend(np.array([10.0] * 7), inv(stock=10, lead=3, moq=1, arrivals={2: 40}), DAY1)
    assert big_po.recommended_quantity == 0 and big_po.on_order == 40              # position 50 > reorder point


def test_a_late_open_order_is_explained_on_a_high_risk():
    r = project_stockout(np.array([10.0] * 7), inv(stock=15, lead=4, arrivals={5: 120}), DAY1)
    assert r.risk == "HIGH" and "120 units already on order arrive on day 5, too late" in r.reason
    assert recommend(np.array([10.0] * 7), inv(stock=15, lead=4, arrivals={5: 120}), DAY1).recommended_quantity == 0
