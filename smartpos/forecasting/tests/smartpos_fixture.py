"""Known SmartPOS-shaped data for the shop tests: the tables (only the columns the reader uses) and a simulation
whose expected answers are recorded alongside it.

Shop 1  trades every day. Product 13 (Curd) starts with little stock, runs out, and is restocked on RESTOCK_DAY.
        Product 14 (Tea) has a 10-day lead time and a case size of 12. One VOIDED bill must be ignored, and one bill
        at 00:30 IST on TZ_DAY (still the previous day in UTC) must count on TZ_DAY.
Shop 2  is closed on Sundays (no bills). Product 23 is inactive: in the history, never forecast.
Shop 3  has only 3 trading days, too few for a forecast.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import date, datetime, time, timedelta
from zoneinfo import ZoneInfo

import numpy as np
from sqlalchemy import Engine, text

IST = ZoneInfo("Asia/Kolkata")
THROUGH = date(2026, 10, 31)
DAYS = 63
START = THROUGH - timedelta(days=DAYS - 1)
RESTOCK_DAY = START + timedelta(days=40)
TZ_DAY = date(2026, 10, 10)

# id, shop, sku, name, category, price, lead, moq, active, daily mean, opening stock
PRODUCTS = [
    (11, 1, "MILK-1L", "Toned Milk 1 L", "Dairy", 56, 2, 1, True, 8.0, 5000),
    (12, 1, "BREAD-400", "White Bread 400 g", "Bakery", 45, 1, 1, True, 5.0, 5000),
    (13, 1, "CURD-400", "Curd 400 g", "Dairy", 60, 3, 1, True, 4.0, 90),
    (14, 1, "TEA-250", "Black Tea 250 g", "Beverages", 140, 10, 12, True, 2.0, 5000),
    (21, 2, "MILK-500", "Milk 500 ml", "Dairy", 30, 2, 1, True, 6.0, 5000),
    (22, 2, "SOAP-100", "Bath Soap 100 g", "Personal Care", 38, 4, 6, True, 4.0, 5000),
    (23, 2, "OLD-ITEM", "Discontinued item", "Personal Care", 20, 3, 1, False, 1.0, 5000),
    (31, 3, "PEN-BLUE", "Blue Pen", "Stationery", 10, 3, 1, True, 3.0, 5000),
]

DDL = """
CREATE TABLE {s}."Shops" ("Id" integer PRIMARY KEY, "ShopName" varchar(150) NOT NULL);
CREATE TABLE {s}."Products" ("Id" integer PRIMARY KEY, "ShopId" integer NOT NULL, "Sku" varchar(50) NOT NULL,
    "Name" varchar(150) NOT NULL, "Category" varchar(100) NOT NULL, "Price" numeric(18,2) NOT NULL,
    "LeadTimeDays" integer NOT NULL, "MinimumOrderQuantity" integer NOT NULL, "IsActive" boolean NOT NULL);
CREATE TABLE {s}."Inventories" ("Id" serial PRIMARY KEY, "ShopId" integer NOT NULL, "ProductId" integer NOT NULL,
    "StockQuantity" integer NOT NULL);
CREATE TABLE {s}."Sales" ("Id" integer PRIMARY KEY, "ShopId" integer NOT NULL, "Status" varchar(20) NOT NULL,
    "CreatedAt" timestamptz NOT NULL);
CREATE TABLE {s}."SaleItems" ("Id" serial PRIMARY KEY, "SaleId" integer NOT NULL, "ProductId" integer NOT NULL,
    "Quantity" integer NOT NULL, "UnitPrice" numeric(12,2) NOT NULL, "LineTotal" numeric(12,2) NOT NULL);
CREATE TABLE {s}."StockMovements" ("Id" serial PRIMARY KEY, "ShopId" integer NOT NULL, "ProductId" integer NOT NULL,
    "QuantityChange" integer NOT NULL, "QuantityAfter" integer NOT NULL, "Type" varchar(20) NOT NULL,
    "CreatedAt" timestamptz NOT NULL);
"""


@dataclass
class ShopFixture:
    schema: str
    sold: dict[tuple[int, date], int] = field(default_factory=dict)          # (product, local day) -> units, completed bills
    closing: dict[tuple[int, date], int] = field(default_factory=dict)       # (product, local day) -> closing stock
    trading: dict[int, set[date]] = field(default_factory=dict)              # shop -> trading days


def _at(day: date, hh: int, mm: int = 0) -> datetime:
    return datetime.combine(day, time(hh, mm), tzinfo=IST)


def create_and_seed(engine: Engine, schema: str) -> ShopFixture:
    rng = np.random.default_rng(7)
    fx = ShopFixture(schema=schema)
    s = f'"{schema}"'
    sales, items, moves = [], [], []
    stock = {p[0]: p[10] for p in PRODUCTS}
    for p in PRODUCTS:
        moves.append({"shop": p[1], "product": p[0], "change": p[10], "after": p[10], "type": "InitialStock",
                      "at": _at(START, 8)})
    for d in range(DAYS):
        day = START + timedelta(days=d)
        for shop in (1, 2, 3):
            if shop == 2 and day.weekday() == 6:
                continue                                   # closed on Sundays
            if shop == 3 and day < THROUGH - timedelta(days=2):
                continue                                   # only the last 3 days
            if shop == 1 and day == RESTOCK_DAY:
                stock[13] += 100
                moves.append({"shop": 1, "product": 13, "change": 100, "after": stock[13], "type": "Restock", "at": _at(day, 9)})
            # Bill time: 10:00 IST, except shop 1 on TZ_DAY at 00:30 IST (18:00 UTC the day before).
            at = _at(day, 0, 30) if (shop == 1 and day == TZ_DAY) else _at(day, 10)
            sale_id = len(sales) + 1
            lines = []
            for p in (p for p in PRODUCTS if p[1] == shop):
                mean = p[9] * (1.4 if day.weekday() >= 5 else 1.0)
                qty = min(int(rng.poisson(mean)), stock[p[0]])
                if qty <= 0:
                    continue
                stock[p[0]] -= qty
                lines.append({"sale": sale_id, "product": p[0], "qty": qty, "price": p[5], "total": qty * p[5]})
                moves.append({"shop": shop, "product": p[0], "change": -qty, "after": stock[p[0]], "type": "Sale", "at": at})
                fx.sold[(p[0], day)] = qty
            if not lines:                                  # keep every planned day a trading day
                p = next(p for p in PRODUCTS if p[1] == shop and stock[p[0]] > 0)
                stock[p[0]] -= 1
                lines.append({"sale": sale_id, "product": p[0], "qty": 1, "price": p[5], "total": p[5]})
                moves.append({"shop": shop, "product": p[0], "change": -1, "after": stock[p[0]], "type": "Sale", "at": at})
                fx.sold[(p[0], day)] = 1
            sales.append({"id": sale_id, "shop": shop, "status": "Completed", "at": at})
            items.extend(lines)
            fx.trading.setdefault(shop, set()).add(day)
        for p in PRODUCTS:
            fx.closing[(p[0], day)] = stock[p[0]]
    # A voided bill: no stock movement (voids restore stock in SmartPOS) and must not count as sales.
    void_id = len(sales) + 1
    sales.append({"id": void_id, "shop": 1, "status": "Voided", "at": _at(THROUGH - timedelta(days=5), 12)})
    items.append({"sale": void_id, "product": 11, "qty": 50, "price": 56, "total": 50 * 56})

    with engine.begin() as conn:
        conn.execute(text(f"CREATE SCHEMA {s}"))
        for statement in DDL.format(s=s).split(";"):
            if statement.strip():
                conn.execute(text(statement))
        conn.execute(text(f'INSERT INTO {s}."Shops" VALUES (:i, :n)'),
                     [{"i": 1, "n": "Shop One"}, {"i": 2, "n": "Shop Two"}, {"i": 3, "n": "Shop Three"}])
        conn.execute(text(f'INSERT INTO {s}."Products" VALUES (:i, :shop, :sku, :name, :cat, :price, :lead, :moq, :active)'),
                     [{"i": p[0], "shop": p[1], "sku": p[2], "name": p[3], "cat": p[4], "price": p[5], "lead": p[6],
                       "moq": p[7], "active": p[8]} for p in PRODUCTS])
        conn.execute(text(f'INSERT INTO {s}."Inventories" ("ShopId", "ProductId", "StockQuantity") VALUES (:shop, :p, :q)'),
                     [{"shop": p[1], "p": p[0], "q": stock[p[0]]} for p in PRODUCTS])
        conn.execute(text(f'INSERT INTO {s}."Sales" VALUES (:id, :shop, :status, :at)'), sales)
        conn.execute(text(f'INSERT INTO {s}."SaleItems" ("SaleId", "ProductId", "Quantity", "UnitPrice", "LineTotal") '
                          "VALUES (:sale, :product, :qty, :price, :total)"), items)
        conn.execute(text(f'INSERT INTO {s}."StockMovements" ("ShopId", "ProductId", "QuantityChange", "QuantityAfter", "Type", '
                          '"CreatedAt") VALUES (:shop, :product, :change, :after, :type, :at)'), moves)
    return fx


def table_fingerprint(engine: Engine, schema: str) -> dict[str, str]:
    """md5 of every row of every SmartPOS table: proves the forecasting code wrote nothing there."""
    out = {}
    with engine.connect() as conn:
        for table in ("Shops", "Products", "Inventories", "Sales", "SaleItems", "StockMovements"):
            out[table] = conn.execute(text(f'SELECT md5(string_agg(t::text, \'|\' ORDER BY t::text)) FROM "{schema}"."{table}" t')).scalar_one()
    return out
