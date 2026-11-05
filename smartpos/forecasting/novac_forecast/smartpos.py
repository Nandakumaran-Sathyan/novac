"""The real SmartPOS shops as a data source (READ-ONLY: only SELECTs, nothing in the SmartPOS tables is written).

Reads Sales, SaleItems, Products, Inventories and StockMovements and builds the same long daily history that
`features.build_panel` expects, one series per shop x product:

- A calendar day is the shop's local day (FORECAST_SHOP_TIMEZONE, default Asia/Kolkata, the same zone as the
  SmartPOS API's Store:TimeZoneId).
- Units sold = the sum of quantities on COMPLETED bills that day.
- A trading day is a day with at least one completed bill. On a day without any bill the shop was closed or did not
  record its sales, so every product's demand that day is unknown (missing), not zero.
- A product's history starts at its first stock movement (opening stock) or first sale, whichever is earlier.
- Closing stock = QuantityAfter of the product's last stock movement that day, carried forward over days without
  movements. A day that ends with zero stock is a stockout day: its sales are censored (demand may have been higher).
- Price = the average selling price that day, carried forward over days without sales (catalogue price if the
  product has never sold). Till discounts are decided at the counter, not known in advance, so they are not used.
- Lead time and minimum order quantity come from the product (LeadTimeDays, MinimumOrderQuantity).

`store_id` in the history is the shop id as text (so the shop is a model feature) and `product_id` the product id
as text; `category_id` is the product's category name, shared across shops so categories pool their evidence.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import date, datetime, timedelta
from zoneinfo import ZoneInfo

import numpy as np
import pandas as pd
from sqlalchemy import Engine, text

from .config import get_settings
from .db import check_schema_name

UNKNOWN = "-"        # city / region / store type: SmartPOS does not record them, so the feature is constant


@dataclass
class ShopData:
    through: date                  # last complete day included
    history: pd.DataFrame          # long daily rows (trading days only), build_panel's input format
    products: pd.DataFrame         # every product of the shop(s), including inactive ones (see `is_active`)
    trading_days: dict[int, int]   # shop id -> days with at least one completed bill, up to `through`


def last_complete_day(timezone: str | None = None, now: datetime | None = None) -> date:
    """Yesterday in the shop's time zone: today is still trading, so its sales are incomplete."""
    zone = ZoneInfo(timezone or get_settings().shop_timezone)
    return ((now or datetime.now(tz=zone)).astimezone(zone)).date() - timedelta(days=1)


def series_frame(products: pd.DataFrame) -> pd.DataFrame:
    """The static series columns build_panel and the model use, for the given products."""
    return pd.DataFrame({"store_id": products["shop_id"].astype(str).to_numpy(),
                         "product_id": products["product_id"].astype(str).to_numpy(),
                         "category_id": products["category"].to_numpy(),
                         "city": UNKNOWN, "region": UNKNOWN, "store_type": UNKNOWN})


def load_shops(engine: Engine, through: date | None = None, shop_id: int | None = None,
               schema: str | None = None, timezone: str | None = None) -> ShopData:
    settings = get_settings()
    p = f'"{check_schema_name(schema or settings.smartpos_schema)}"'
    tz = timezone or settings.shop_timezone
    ZoneInfo(tz)                                              # fail fast on an unknown zone
    through = through or last_complete_day(tz)
    params = {"tz": tz, "through": through, "shop": shop_id}
    local_day = 'CAST(({t}."CreatedAt" AT TIME ZONE :tz) AS date)'
    shop_filter = '(CAST(:shop AS integer) IS NULL OR {t}."ShopId" = CAST(:shop AS integer))'

    with engine.connect() as conn:
        products = pd.read_sql_query(text(f"""
            SELECT p."Id" AS product_id, p."ShopId" AS shop_id, p."Sku" AS sku, p."Name" AS name, p."Category" AS category,
                   CAST(p."Price" AS float) AS price, p."LeadTimeDays" AS lead_days, p."MinimumOrderQuantity" AS moq,
                   p."IsActive" AS is_active, i."StockQuantity" AS stock
            FROM {p}."Products" p
            LEFT JOIN {p}."Inventories" i ON i."ProductId" = p."Id" AND i."ShopId" = p."ShopId"
            WHERE {shop_filter.format(t="p")}
            ORDER BY p."ShopId", p."Id" """), conn, params=params)
        sales = pd.read_sql_query(text(f"""
            SELECT s."ShopId" AS shop_id, i."ProductId" AS product_id, {local_day.format(t="s")} AS date,
                   CAST(sum(i."Quantity") AS integer) AS units_sold, CAST(sum(i."LineTotal") AS float) AS revenue,
                   CAST(sum(i."UnitPrice" * i."Quantity") / sum(i."Quantity") AS float) AS unit_price
            FROM {p}."SaleItems" i JOIN {p}."Sales" s ON s."Id" = i."SaleId"
            WHERE s."Status" = 'Completed' AND {local_day.format(t="s")} <= :through AND {shop_filter.format(t="s")}
            GROUP BY 1, 2, 3"""), conn, params=params)
        stock = pd.read_sql_query(text(f"""
            SELECT DISTINCT ON (m."ProductId", {local_day.format(t="m")})
                   m."ProductId" AS product_id, {local_day.format(t="m")} AS date, m."QuantityAfter" AS closing_stock
            FROM {p}."StockMovements" m
            WHERE {local_day.format(t="m")} <= :through AND {shop_filter.format(t="m")}
            ORDER BY m."ProductId", {local_day.format(t="m")}, m."CreatedAt" DESC, m."Id" DESC"""), conn, params=params)

    for frame in (sales, stock):
        frame["date"] = pd.to_datetime(frame["date"]).dt.date
    products["inventory_known"] = products["stock"].notna()
    products["stock"] = products["stock"].fillna(0).astype(int)

    trading = sales[["shop_id", "date"]].drop_duplicates()
    trading_days = trading.groupby("shop_id").size().astype(int).to_dict()
    starts = pd.concat([sales.groupby("product_id")["date"].min(), stock.groupby("product_id")["date"].min()], axis=1).min(axis=1)

    # Every trading day of the shop from the product's start; days without a sale of the product are zero sales.
    rows = products[["shop_id", "product_id", "category", "price"]].merge(trading, on="shop_id")
    rows = rows[rows["product_id"].map(starts).notna()]
    rows = rows[rows["date"] >= rows["product_id"].map(starts)]
    rows = rows.merge(sales, on=["shop_id", "product_id", "date"], how="left")
    rows["units_sold"] = rows["units_sold"].fillna(0).astype(int)
    rows["revenue"] = rows["revenue"].fillna(0.0)
    rows = rows.sort_values(["product_id", "date"]).reset_index(drop=True)
    rows["unit_price"] = rows.groupby("product_id")["unit_price"].transform(lambda s: s.ffill().bfill())
    rows["unit_price"] = rows["unit_price"].fillna(rows["price"])

    # Closing stock: the last movement on or before each day (none yet -> unknown, treated as not censored).
    if len(rows) and len(stock):
        left = rows.assign(_d=pd.to_datetime(rows["date"])).sort_values("_d")
        right = stock.assign(_d=pd.to_datetime(stock["date"])).sort_values("_d")[["product_id", "_d", "closing_stock"]]
        rows = pd.merge_asof(left, right, on="_d", by="product_id", direction="backward").drop(columns="_d")
    else:
        rows["closing_stock"] = np.nan
    rows["stockout_flag"] = rows["closing_stock"].eq(0)

    history = pd.DataFrame({
        "date": rows["date"], "store_id": rows["shop_id"].astype(str), "product_id": rows["product_id"].astype(str),
        "category_id": rows["category"], "city": UNKNOWN, "region": UNKNOWN, "store_type": UNKNOWN,
        "units_sold": rows["units_sold"].astype(float), "latent_demand": np.nan,
        "unit_price": rows["unit_price"].astype(float), "discount_percentage": 0.0,
        "revenue": rows["revenue"].astype(float), "stockout_flag": rows["stockout_flag"].astype(bool),
    }).sort_values(["store_id", "product_id", "date"]).reset_index(drop=True)
    return ShopData(through=through, history=history, products=products, trading_days=trading_days)
