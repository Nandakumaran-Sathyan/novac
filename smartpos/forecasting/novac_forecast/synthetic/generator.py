"""FABRICATED but realistic daily retail data for the fictional Novac chain (see catalog.py).

Demand is simulated first and independently of stock ("latent demand"). A separate inventory simulation, run with
a realistic store reorder policy, supplier lead times and delays, then decides how much of that demand could be
sold. So:  units_sold = min(latent_demand, stock available)  and  lost_demand = latent_demand - units_sold.
Keeping both lets the model be trained on what a real shop can observe (sales) while being evaluated against what
customers actually wanted (latent demand), which only synthetic data can provide.

Everything is driven by one seed: the same config always produces exactly the same dataset.
"""

from __future__ import annotations

import math
from dataclasses import asdict, dataclass, field
from datetime import date, timedelta

import numpy as np
import pandas as pd

from . import catalog as cat
from .calendar import FESTIVALS, festival_on


@dataclass(frozen=True)
class GeneratorConfig:
    end_date: date = date(2026, 11, 4)     # last day of history (the day before the first forecast day)
    days: int = 745                       # 21 Oct 2024 - 4 Nov 2026: two Diwalis and two Pongals
    seed: int = 20261105
    dispersion: float = 6.0                # gamma-Poisson shape: lower = noisier day-to-day demand
    annual_growth: float = 0.06            # chain-wide like-for-like growth per year
    store_ids: tuple[str, ...] | None = None    # restrict (tests); None = all stores
    product_limit: int | None = None            # restrict (tests); None = all products

    @property
    def start_date(self) -> date:
        return self.end_date - timedelta(days=self.days - 1)


@dataclass
class SyntheticDataset:
    config: GeneratorConfig
    stores: pd.DataFrame
    categories: pd.DataFrame
    suppliers: pd.DataFrame
    products: pd.DataFrame
    daily_sales: pd.DataFrame
    inventory_history: pd.DataFrame
    purchase_orders: pd.DataFrame
    closures: list[tuple[str, date]] = field(default_factory=list)

    def summary(self) -> dict:
        s = self.daily_sales
        return {
            "stores": len(self.stores), "products": len(self.products), "suppliers": len(self.suppliers),
            "days": self.config.days, "from": str(self.config.start_date), "to": str(self.config.end_date),
            "sales_rows": len(s), "inventory_rows": len(self.inventory_history), "purchase_orders": len(self.purchase_orders),
            "open_purchase_orders": int((self.purchase_orders["status"] == "open").sum()),
            "units_sold": int(s["units_sold"].sum()), "latent_demand": int(s["latent_demand"].sum()),
            "lost_demand": int(s["lost_demand"].sum()),
            "lost_demand_pct": round(100 * s["lost_demand"].sum() / max(s["latent_demand"].sum(), 1), 2),
            "stockout_row_pct": round(100 * self.inventory_history["stockout_flag"].mean(), 2),
            "revenue_inr": round(float(s["revenue"].sum()), 2),
            "promotion_row_pct": round(100 * s["promotion_flag"].mean(), 2),
            "store_closure_days": len(self.closures),
        }


# --------------------------------------------------------------------------------------------- helpers
def _festival_multiplier(seed: cat.ProductSeed, day: date, metro: bool) -> float:
    name, days_to = festival_on(day)
    if name is None:
        return 1.0
    fest = next(f for f in FESTIVALS if f.name == name and abs((f.day - day).days) <= f.build_up_days + f.after_days)
    # Weight peaks the day before the festival (last-minute shopping), stays high on the day, tails off after it.
    if days_to > 0:
        weight = max(0.25, 1.0 - (days_to - 1) / (fest.build_up_days + 1))
    elif days_to == 0:
        weight = 0.85
    else:
        weight = 0.5
    general = 1.15 if name == "Diwali" else 1.06          # every shopper buys a bit more in festival weeks
    peak = seed.festivals.get(name, 1.0)
    intensity = 1.0
    if name == "Pongal":
        intensity = 0.85 if metro else 1.2                # Pongal is bigger outside Chennai
    elif name in ("Christmas", "New Year"):
        intensity = 1.3 if metro else 0.8
    lift = (peak - 1.0) * intensity
    return (1.0 + (general - 1.0) * weight) * (1.0 + max(lift, 0.0) * weight)


def _price_path(rng: np.random.Generator, seed: cat.ProductSeed, n: int) -> np.ndarray:
    """Shelf price per day: one or two inflation steps over two years; vegetables and fruit also move weekly."""
    price = np.full(n, seed.price)
    for _ in range(rng.integers(1, 3)):
        step_day = int(rng.integers(min(30, n // 4), max(n - 30, n // 4 + 1)))
        price[step_day:] *= 1.0 + rng.uniform(0.03, 0.09)
    if seed.category_id == "CAT07":
        weeks = n // 7 + 1
        weekly = np.repeat(np.clip(rng.normal(1.0, 0.08, weeks), 0.8, 1.25), 7)[:n]
        price = price * weekly
    return price


def _promotions(rng: np.random.Generator, seed: cat.ProductSeed, store: cat.Store, n: int) -> np.ndarray:
    """Discount percentage per day (0 = no promotion). Hypermarkets run more promotions; fresh food rarely."""
    intensity = {"Hypermarket": 1.3, "Supermarket": 1.0, "Neighbourhood": 0.6}[store.store_type]
    if seed.category_id in ("CAT02", "CAT07"):
        intensity *= 0.4
        choices = (5.0, 10.0)
    else:
        choices = (5.0, 10.0, 15.0, 20.0, 25.0)
    discount = np.zeros(n)
    for _ in range(rng.poisson(n / 55 * intensity)):
        start = int(rng.integers(0, n))
        length = int(rng.integers(3, 8))
        discount[start:start + length] = rng.choice(choices)
    return discount


def _supplier_disruptions(rng: np.random.Generator, n: int) -> list[tuple[int, int, int]]:
    """(start, end, extra days) windows when a supplier is slow (strikes, rain, transport)."""
    out = []
    for _ in range(int(rng.integers(1, 3))):
        start = int(rng.integers(0, max(n - 10, 1)))
        out.append((start, start + int(rng.integers(5, 11)), int(rng.integers(3, 7))))
    return out


# --------------------------------------------------------------------------------------------- generator
def generate(config: GeneratorConfig = GeneratorConfig()) -> SyntheticDataset:
    root = np.random.SeedSequence(config.seed)
    n = config.days
    dates = [config.start_date + timedelta(days=i) for i in range(n)]
    dow = np.array([d.weekday() for d in dates])
    month = np.array([d.month for d in dates])
    t_years = np.arange(n) / 365.0
    holiday = [festival_on(d) for d in dates]
    holiday_name = np.array([h[0] if h[0] else "" for h in holiday], dtype=object)

    stores = [s for s in cat.STORES if config.store_ids is None or s.store_id in config.store_ids]
    seeds = list(cat.PRODUCTS[: config.product_limit] if config.product_limit else cat.PRODUCTS)
    suppliers = {s.supplier_id: s for s in cat.SUPPLIERS}

    # Independent random streams, so the result doesn't depend on loop order.
    product_streams = root.spawn(len(cat.PRODUCTS))
    store_streams = root.spawn(len(cat.STORES) + 1)
    supplier_rng = np.random.default_rng(store_streams[-1])
    disruptions = {sid: _supplier_disruptions(supplier_rng, n) for sid in suppliers}

    # A few days each store was shut (heavy rain, maintenance): those days are simply missing from the data.
    closures: dict[str, set[int]] = {}
    for idx, store in enumerate(cat.STORES):
        rng = np.random.default_rng(store_streams[idx])
        candidates = np.arange(min(40, n // 3), max(n - 10, n // 3 + 1))
        closures[store.store_id] = set(int(x) for x in rng.choice(candidates, size=min(int(rng.integers(1, 4)), len(candidates)), replace=False))
    flood_day = (date(2025, 12, 2) - config.start_date).days      # a cyclone day: every Chennai store closed
    if 0 <= flood_day < n:
        for store in cat.STORES:
            if store.city == "Chennai":
                closures[store.store_id].add(flood_day)

    product_rows, sales_parts, inventory_parts, po_rows = [], [], [], []
    for p_index, seed in enumerate(cat.PRODUCTS):
        if seed not in seeds:
            continue
        pid = cat.product_id(p_index)
        supplier = suppliers[seed.supplier_id]
        product_rows.append({
            "product_id": pid, "product_name": seed.name, "category_id": seed.category_id,
            "category_name": cat.CATEGORY_NAME[seed.category_id], "unit_price": round(seed.price, 2),
            "supplier_id": supplier.supplier_id, "supplier_lead_days": supplier.lead_days,
            "minimum_order_quantity": seed.moq, "is_perishable": seed.perishable,
            "launched_on": seed.launched,
        })
        prng = np.random.default_rng(product_streams[p_index])
        base_price = _price_path(prng, seed, n)
        season = np.array(cat.CATEGORY_SEASON[seed.category_id])[month - 1]
        summer = np.where((month >= 4) & (month <= 6), seed.summer, 1.0)
        launch_index = (seed.launched - config.start_date).days if seed.launched else 0

        for s_index, store in enumerate(cat.STORES):
            if store not in stores:
                continue
            rng = np.random.default_rng([config.seed, p_index, s_index])
            metro = store.region == "Chennai Metro"
            price = np.round(base_price * cat.REGION_PRICE_INDEX[store.region], 2)
            discount = _promotions(rng, seed, store, n)
            effective = price * (1 - discount / 100.0)
            reference = seed.price * cat.REGION_PRICE_INDEX[store.region]
            elasticity = cat.CATEGORY_ELASTICITY[seed.category_id] * cat.REGION_PRICE_SENSITIVITY[store.region]

            weekday = np.array(cat.WEEKDAY_PROFILE["metro" if metro else "town"])[dow]
            festival = np.array([_festival_multiplier(seed, d, metro) for d in dates])
            lam = (seed.base_daily * cat.STORE_TYPE_SCALE[store.store_type]
                   * cat.REGION_CATEGORY_AFFINITY[store.region][seed.category_id]
                   * rng.lognormal(0.0, 0.25)                      # this store's taste for this product
                   * (1.12 if metro else 1.0)
                   * weekday * season * summer * festival
                   * (1 + config.annual_growth) ** t_years
                   * (effective / reference) ** elasticity
                   * np.where(discount > 0, 1.12, 1.0))            # shelf display effect of a promotion
            if seed.launched:
                ramp = np.clip((np.arange(n) - launch_index + 1) / 21.0, 0.0, 1.0)
                lam = lam * ramp
            demand = rng.poisson(lam * rng.gamma(config.dispersion, 1.0 / config.dispersion, n)).astype(np.int64)

            open_days = np.ones(n, dtype=bool)
            for c in closures[store.store_id]:
                open_days[c] = False
            if seed.launched:
                open_days[:max(launch_index, 0)] = False

            inv, pos = _simulate_inventory(rng, demand, open_days, supplier, seed, disruptions[supplier.supplier_id], n)
            keep = open_days
            idx = np.flatnonzero(keep)
            sold = inv["units_sold"][idx]
            sales_parts.append(pd.DataFrame({
                "date": [dates[i] for i in idx], "store_id": store.store_id, "product_id": pid,
                "units_sold": sold, "latent_demand": demand[idx], "lost_demand": demand[idx] - sold,
                "expected_demand": np.round(lam[idx], 3),      # noise-free mean; analysis only, never loaded
                "unit_price": price[idx], "revenue": np.round(sold * price[idx] * (1 - discount[idx] / 100.0), 2),
                "promotion_flag": discount[idx] > 0, "discount_percentage": discount[idx],
                "holiday_flag": holiday_name[idx] != "", "holiday_name": holiday_name[idx],
            }))
            inventory_parts.append(pd.DataFrame({
                "date": [dates[i] for i in idx], "store_id": store.store_id, "product_id": pid,
                **{k: v[idx] for k, v in inv.items() if k != "units_sold"}, "units_sold": sold,
            }))
            for o in pos:
                po_rows.append({
                    "store_id": store.store_id, "product_id": pid, "supplier_id": supplier.supplier_id,
                    "ordered_on": dates[o["ordered"]], "expected_on": config.start_date + timedelta(days=o["ordered"] + supplier.lead_days),
                    "received_on": dates[o["arrive"]] if o["received"] else None,
                    "quantity": o["qty"], "status": "received" if o["received"] else "open",
                })

    daily_sales = pd.concat(sales_parts, ignore_index=True)
    inventory = pd.concat(inventory_parts, ignore_index=True)[
        ["date", "store_id", "product_id", "opening_stock", "stock_received", "units_sold",
         "inventory_adjustment", "closing_stock", "stockout_flag"]]
    purchase_orders = pd.DataFrame(po_rows)
    purchase_orders.insert(0, "po_id", [f"PO{i + 1:07d}" for i in range(len(purchase_orders))])

    return SyntheticDataset(
        config=config,
        stores=pd.DataFrame([asdict(s) for s in stores])[["store_id", "store_name", "city", "state", "region", "store_type", "latitude", "longitude"]],
        categories=pd.DataFrame(cat.CATEGORIES, columns=["category_id", "category_name"]),
        suppliers=pd.DataFrame([{"supplier_id": s.supplier_id, "supplier_name": s.supplier_name, "lead_days": s.lead_days,
                                 "delay_probability": s.delay_probability} for s in cat.SUPPLIERS]),
        products=pd.DataFrame(product_rows),
        daily_sales=daily_sales,
        inventory_history=inventory,
        purchase_orders=purchase_orders,
        closures=sorted((sid, config.start_date + timedelta(days=d)) for sid, ds in closures.items() for d in ds
                        if any(s.store_id == sid for s in stores)),
    )


def _simulate_inventory(rng, demand, open_days, supplier, seed, disruptions, n):
    """A store's own reorder policy, run day by day. Returns per-day arrays and the purchase orders placed.

    The store estimates demand from its last 28 days of SALES (not the true demand it can't see), so it reacts late
    to festival spikes and to lost sales: exactly how real stockouts happen. Orders arrive after the supplier's lead
    time, sometimes later (random delays, disruption windows), and are rounded up to the minimum order quantity.
    """
    lead = supplier.lead_days
    cover = 2 if seed.perishable else 7
    spoil_rate = 0.04 if seed.category_id == "CAT07" else 0.03 if seed.perishable else 0.0
    first = int(np.argmax(open_days)) if open_days.any() else n
    warm = demand[first:first + 28]
    estimate = max(float(warm.mean()) if len(warm) else 1.0, 0.2)

    opening = np.zeros(n, dtype=np.int64); received = np.zeros(n, dtype=np.int64); sold = np.zeros(n, dtype=np.int64)
    adjust = np.zeros(n, dtype=np.int64); closing = np.zeros(n, dtype=np.int64); stockout = np.zeros(n, dtype=bool)
    on_hand = int(math.ceil(estimate * (lead + cover)))
    arrivals: dict[int, int] = {}
    orders: list[dict] = []
    history: list[int] = []
    carry = 0
    u = rng.random((n, 4))

    for t in range(n):
        due = arrivals.pop(t, 0) + carry
        if not open_days[t]:
            carry = due                       # deliveries wait for the next day the store is open
            continue
        carry = 0
        opening[t] = on_hand
        received[t] = due
        available = on_hand + due
        sold[t] = min(int(demand[t]), available)
        stockout[t] = demand[t] > available
        left = available - sold[t]
        if spoil_rate and left > 0:
            adjust[t] = -int(rng.binomial(left, spoil_rate))
        elif u[t, 0] < 0.004 and left > 0:
            adjust[t] = -min(left, 1 + int(u[t, 1] * 2))          # damage / shrinkage
        elif u[t, 0] > 0.998:
            adjust[t] = 1 + int(u[t, 1] * 3)                      # stock count found extra units
        on_hand = left + adjust[t]
        closing[t] = on_hand

        history.append(int(sold[t]))
        if len(history) >= 7:
            estimate = max(float(np.mean(history[-28:])), 0.2)
        on_order = sum(q for day, q in arrivals.items() if day > t)
        reorder_point = estimate * (lead + 1) + 1.3 * math.sqrt(estimate * (lead + 1)) * 1.2
        order_up_to = reorder_point + estimate * cover
        if on_hand + on_order <= reorder_point:
            qty = int(math.ceil((order_up_to - on_hand - on_order) / seed.moq) * seed.moq)
            delay = 0
            if u[t, 2] < supplier.delay_probability:
                delay += 1 + int(u[t, 3] * 4)
            for start, end, extra in disruptions:
                if start <= t <= end:
                    delay += extra
            arrive = t + max(lead + delay, 1)        # ordered at close of day t, received at the start of that day
            arrivals[arrive] = arrivals.get(arrive, 0) + qty
            orders.append({"ordered": t, "arrive": min(arrive, n - 1), "qty": qty, "received": arrive < n})

    return ({"opening_stock": opening, "stock_received": received, "units_sold": sold, "inventory_adjustment": adjust,
             "closing_stock": closing, "stockout_flag": stockout}, orders)
