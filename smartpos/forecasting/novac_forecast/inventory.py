"""Stockout projection and replenishment: deterministic and transparent (MVP).

For each store/product: start from the stock on hand at the end of the last history day, add open purchase orders
on the day they are expected, subtract the forecast demand day by day, and see when the shelf would run empty.

Risk bands are RULES, not calibrated probabilities:
  HIGH    projected to run out before an order placed today could arrive (within the supplier lead time),
          or already out of stock while demand is expected
  MEDIUM  projected to run out within the forecast horizon but after the lead time (avoidable if ordered now),
          or projected to fall below safety stock by the end of the horizon
  LOW     neither

Replenishment uses a periodic-review order-up-to policy:
  safety stock   = z * sigma_daily * sqrt(lead days)          (z = 1.65, about 95% cycle service if demand were normal)
  reorder point  = forecast demand over the lead time + safety stock
  order-up-to    = forecast demand over (lead time + review period) + safety stock
  order quantity = order-up-to - (on hand + on order), rounded UP to the supplier's minimum order quantity,
                   recommended only when the inventory position is at or below the reorder point.
Lead times longer than the forecast horizon extend the forecast at its average daily rate.
"""

from __future__ import annotations

import math
from dataclasses import dataclass
from datetime import date, timedelta

import numpy as np

Z_SERVICE = 1.65
REVIEW_DAYS = 7


@dataclass(frozen=True)
class SeriesInventory:
    current_stock: int
    inventory_known: bool
    lead_days: int
    moq: int
    sigma_daily: float                  # std of daily demand (uncensored history)
    arrivals: dict[int, int]            # forecast day (1..H) -> units expected from open orders


@dataclass(frozen=True)
class StockoutResult:
    predicted_stockout_date: date | None
    days_of_cover: float | None
    cumulative_demand: float
    on_order_in_horizon: int
    risk: str
    safety_stock: float
    reason: str


@dataclass(frozen=True)
class Replenishment:
    recommended_quantity: int
    reorder_point: float
    order_up_to: float
    safety_stock: float
    on_order: int
    order_by_date: date | None
    reason: str


def demand_over(daily: np.ndarray, days: int) -> float:
    """Forecast demand over `days` days; beyond the horizon continue at the horizon's average daily rate."""
    h = len(daily)
    if days <= h:
        return float(daily[:days].sum())
    return float(daily.sum() + (days - h) * (daily.mean() if h else 0.0))


def safety_stock(sigma_daily: float, lead_days: int) -> float:
    return Z_SERVICE * max(sigma_daily, 0.0) * math.sqrt(max(lead_days, 1))


def project_stockout(daily: np.ndarray, inv: SeriesInventory, first_day: date) -> StockoutResult:
    """Day-by-day projection over the forecast horizon."""
    ss = safety_stock(inv.sigma_daily, inv.lead_days)
    stock = float(inv.current_stock)
    stockout_day = None
    cover = None
    for h, demand in enumerate(daily, start=1):
        stock += inv.arrivals.get(h, 0)
        if stockout_day is None and demand > 0 and stock < demand:
            stockout_day = h
            cover = (h - 1) + max(stock, 0.0) / demand       # fraction of day h still covered
        stock -= demand
    end_stock = stock
    total = float(np.sum(daily))
    on_order = int(sum(inv.arrivals.values()))
    stockout_date = first_day + timedelta(days=stockout_day - 1) if stockout_day else None
    if cover is None and total > 0:
        # Lasts beyond the horizon: estimate the cover at the horizon's average daily demand.
        cover = (inv.current_stock + on_order) / (total / len(daily))

    if not inv.inventory_known:
        risk, reason = "HIGH", "No inventory record for this product: stock is unknown, so it is treated as empty."
    elif inv.current_stock <= 0 and total > 0 and not inv.arrivals.get(1):
        risk, reason = "HIGH", "Out of stock now and demand is expected."
    elif stockout_day is not None and stockout_day <= inv.lead_days:
        risk = "HIGH"
        reason = (f"Projected to run out on day {stockout_day}, before an order placed today could arrive "
                  f"(supplier lead time {inv.lead_days} days).")
        later = sorted(d for d in inv.arrivals if d > stockout_day)
        if later:
            reason += f" {sum(inv.arrivals[d] for d in later)} units already on order arrive on day {later[0]}, too late to prevent it."
    elif stockout_day is not None:
        risk = "MEDIUM"
        reason = f"Projected to run out on day {stockout_day}; ordering now (lead time {inv.lead_days} days) avoids it."
    elif end_stock < ss:
        risk = "MEDIUM"
        reason = f"Stock stays positive but falls below safety stock ({ss:.1f}) within {len(daily)} days."
    else:
        risk, reason = "LOW", f"Enough stock for the next {len(daily)} days, above safety stock."
    return StockoutResult(stockout_date, None if cover is None else round(cover, 2), round(total, 2), on_order, risk,
                          round(ss, 2), reason)


def recommend(daily: np.ndarray, inv: SeriesInventory, first_day: date) -> Replenishment:
    ss = safety_stock(inv.sigma_daily, inv.lead_days)
    rop = demand_over(daily, inv.lead_days) + ss
    order_up_to = demand_over(daily, inv.lead_days + REVIEW_DAYS) + ss
    on_order = int(sum(inv.arrivals.values()))
    on_hand = inv.current_stock if inv.inventory_known else 0
    position = on_hand + on_order
    if position <= rop:
        need = max(order_up_to - position, 0.0)
        qty = int(math.ceil(need / inv.moq) * inv.moq) if need > 0 else 0
        reason = (f"Inventory position {position} is at or below the reorder point {rop:.1f}: order up to {order_up_to:.1f}, "
                  f"rounded up to the minimum order of {inv.moq}.")
        return Replenishment(qty, round(rop, 2), round(order_up_to, 2), round(ss, 2), on_order, first_day, reason)
    # Not yet: when will the projected position reach the reorder point?
    projected = float(position)
    order_by = None
    for h, demand in enumerate(daily, start=1):
        projected -= demand
        if projected <= rop:
            order_by = first_day + timedelta(days=h - 1)
            break
    reason = (f"Inventory position {position} is above the reorder point {rop:.1f}"
              + (f"; reorder by {order_by.isoformat()}." if order_by else "; no order needed this week."))
    return Replenishment(0, round(rop, 2), round(order_up_to, 2), round(ss, 2), on_order, order_by, reason)
