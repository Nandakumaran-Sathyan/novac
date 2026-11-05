"""Feature engineering, shared by training, backtesting and the 7-day recursive forecast.

Data is held as wide matrices: one row per store/product series, one column per calendar day. Every feature for
day d is computed from columns strictly before d (lags, rolling windows) or from things known in advance for d
(calendar, festivals, planned price and promotion). Using the SAME function for training and for each recursive
forecasting step is what prevents train/serve skew.

The demand signal behind the lag and rolling features is observed sales on days that did NOT stock out. A stockout
day's sales are censored (demand was higher than what could be sold), and a missing day (store closed, product not
launched) says nothing, so both are NaN rather than a misleading low number. LightGBM handles NaN natively.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import date, timedelta

import numpy as np
import pandas as pd
from numpy.lib.stride_tricks import sliding_window_view
from sqlalchemy import Engine, text

from .db import check_schema_name, schema_of
from .synthetic.calendar import FESTIVAL_NAMES, days_to_next_festival, festival_on

LAGS = (1, 7, 14, 28)
ROLL_MEAN = (7, 14, 28)
ROLL_STD = (7, 28)
MAX_WINDOW = 28

CATEGORICAL = ["store_id", "product_id", "category_id", "city", "region", "store_type", "festival"]
NUMERIC = ([f"lag_{k}" for k in LAGS] + [f"rolling_mean_{w}" for w in ROLL_MEAN] + [f"rolling_std_{w}" for w in ROLL_STD]
           + ["day_of_week", "day_of_month", "month", "week_of_year", "is_weekend", "holiday_flag", "days_to_festival",
              "unit_price", "discount_percentage", "promotion_flag", "price_change", "revenue_rolling_28",
              "censored_share_28", "series_age"])
FEATURES = CATEGORICAL + NUMERIC


@dataclass
class Panel:
    """Wide matrices for S series x T days, plus static attributes of each series."""
    dates: list[date]
    series: pd.DataFrame                 # S rows: store_id, product_id, category_id, city, region, store_type, lead days...
    signal: np.ndarray                   # observed uncensored sales, NaN when censored/missing (feature source)
    units_sold: np.ndarray               # observed sales (NaN when missing)
    latent: np.ndarray                   # SYNTHETIC ground-truth demand (NaN when missing); evaluation only
    stockout: np.ndarray                 # 1.0 censored day, 0.0 normal, NaN missing
    price: np.ndarray                    # shelf price (forward/back filled)
    discount: np.ndarray                 # 0 when no promotion or missing
    revenue: np.ndarray                  # observed revenue (NaN when missing)
    present: np.ndarray                  # bool: a row exists for this series and day
    first_day: np.ndarray                # index of each series' first observed day

    @property
    def shape(self) -> tuple[int, int]:
        return self.signal.shape

    def index_of(self, day: date) -> int:
        return (day - self.dates[0]).days


def load_history(engine: Engine, start: date | None = None, end: date | None = None) -> pd.DataFrame:
    s = f'"{check_schema_name(schema_of(engine))}"'
    where, params = [], {}
    if start:
        where.append("d.date >= :start"); params["start"] = start
    if end:
        where.append("d.date <= :end"); params["end"] = end
    sql = f"""
        SELECT d.date, d.store_id, d.product_id, p.category_id, st.city, st.region, st.store_type,
               d.units_sold, d.latent_demand, d.unit_price::float AS unit_price,
               d.discount_percentage::float AS discount_percentage, d.revenue::float AS revenue, i.stockout_flag
        FROM {s}.daily_sales d
        JOIN {s}.products p USING (product_id)
        JOIN {s}.stores st USING (store_id)
        LEFT JOIN {s}.inventory_history i USING (store_id, product_id, date)
        {"WHERE " + " AND ".join(where) if where else ""}"""
    with engine.connect() as conn:
        df = pd.read_sql_query(text(sql), conn, params=params)
    df["date"] = pd.to_datetime(df["date"]).dt.date
    df["stockout_flag"] = df["stockout_flag"].fillna(False).astype(bool)   # missing inventory row: assume not censored
    return df


def build_panel(history: pd.DataFrame, series: pd.DataFrame | None = None, end: date | None = None,
                extra_days: int = 0) -> Panel:
    """Long history -> wide matrices. `series` fixes which series (and in what order); `extra_days` appends empty
    future columns (for forecasting) whose price is carried forward and promotions are off."""
    if series is None:
        series = history[["store_id", "product_id", "category_id", "city", "region", "store_type"]].drop_duplicates()
    series = series.sort_values(["store_id", "product_id"]).reset_index(drop=True)
    first = min(history["date"]) if len(history) else end
    last = max(end or max(history["date"]), max(history["date"]) if len(history) else end)
    T = (last - first).days + 1 + extra_days
    dates = [first + timedelta(days=i) for i in range(T)]
    S = len(series)
    key = pd.MultiIndex.from_frame(series[["store_id", "product_id"]])

    def matrix(column: str, fill=np.nan) -> np.ndarray:
        wide = history.pivot_table(index=["store_id", "product_id"], columns="date", values=column, aggfunc="first")
        wide = wide.reindex(index=key, columns=dates)
        out = wide.to_numpy(dtype=float)
        if not np.isnan(fill):
            out = np.where(np.isnan(out), fill, out)
        return out

    units = matrix("units_sold")
    stock = matrix("stockout_flag")
    present = ~np.isnan(units)
    price = pd.DataFrame(matrix("unit_price")).ffill(axis=1).bfill(axis=1).to_numpy()
    signal = np.where(present & (stock == 0), units, np.nan)
    first_day = np.where(present.any(axis=1), present.argmax(axis=1), T)
    return Panel(dates=dates, series=series, signal=signal, units_sold=units, latent=matrix("latent_demand"),
                 stockout=stock, price=price, discount=matrix("discount_percentage", 0.0),
                 revenue=matrix("revenue"), present=present, first_day=first_day)


# --------------------------------------------------------------------------------------------- feature maths
def _windows(values: np.ndarray, width: int, days: np.ndarray) -> np.ndarray:
    """values[:, d-width:d] for each d in days -> (S, len(days), width), NaN-padded before day 0."""
    padded = np.concatenate([np.full((values.shape[0], width), np.nan), values], axis=1)
    view = sliding_window_view(padded, width, axis=1)     # view[:, j] == padded[:, j:j+width] == values[:, j-width:j]
    return view[:, days]


def _nanmean(w: np.ndarray, min_count: int) -> np.ndarray:
    count = np.sum(~np.isnan(w), axis=-1)
    total = np.nansum(w, axis=-1)
    with np.errstate(invalid="ignore", divide="ignore"):
        return np.where(count >= min_count, total / np.maximum(count, 1), np.nan)


def _nanstd(w: np.ndarray, min_count: int) -> np.ndarray:
    count = np.sum(~np.isnan(w), axis=-1)
    mean = _nanmean(w, 1)[..., None]
    with np.errstate(invalid="ignore", divide="ignore"):
        var = np.nansum((w - mean) ** 2, axis=-1) / np.maximum(count - 1, 1)
    return np.where(count >= min_count, np.sqrt(var), np.nan)


def calendar_frame(days: list[date]) -> pd.DataFrame:
    idx = pd.to_datetime(pd.Series(days))
    fest = [festival_on(d)[0] for d in days]
    return pd.DataFrame({
        "day_of_week": idx.dt.dayofweek.to_numpy(), "day_of_month": idx.dt.day.to_numpy(), "month": idx.dt.month.to_numpy(),
        "week_of_year": idx.dt.isocalendar().week.astype(int).to_numpy(), "is_weekend": (idx.dt.dayofweek >= 5).astype(int).to_numpy(),
        "holiday_flag": np.array([f is not None for f in fest], dtype=int), "festival": [f or "None" for f in fest],
        "days_to_festival": [days_to_next_festival(d) for d in days],
    })


def features_for_days(panel: Panel, days: np.ndarray) -> pd.DataFrame:
    """Feature rows for every series on each day index in `days` (S * len(days) rows, series-major)."""
    days = np.asarray(days)
    S = panel.shape[0]
    cols: dict[str, np.ndarray] = {}
    for k in LAGS:
        src = days - k
        cols[f"lag_{k}"] = np.where(src >= 0, panel.signal[:, np.clip(src, 0, None)], np.nan)
    win = _windows(panel.signal, MAX_WINDOW, days)
    for w in ROLL_MEAN:
        cols[f"rolling_mean_{w}"] = _nanmean(win[..., -w:], max(2, w // 3))
    for w in ROLL_STD:
        cols[f"rolling_std_{w}"] = _nanstd(win[..., -w:], max(3, w // 3))
    cols["revenue_rolling_28"] = _nanmean(_windows(panel.revenue, 28, days), 7)
    stock_win = _windows(panel.stockout, 28, days)
    cols["censored_share_28"] = _nanmean(stock_win, 1)
    price_win = _windows(panel.price, 28, days)
    with np.errstate(invalid="ignore", divide="ignore"):
        cols["price_change"] = panel.price[:, days] / _nanmean(price_win, 1) - 1.0
    cols["unit_price"] = panel.price[:, days]
    cols["discount_percentage"] = panel.discount[:, days]
    cols["promotion_flag"] = (panel.discount[:, days] > 0).astype(float)
    cols["series_age"] = np.clip(days[None, :] - panel.first_day[:, None], 0, 365).astype(float)

    out = pd.DataFrame({name: values.reshape(-1) for name, values in cols.items()})
    cal = calendar_frame([panel.dates[d] for d in days])
    for c in cal.columns:
        out[c] = np.tile(cal[c].to_numpy(), S)
    static = panel.series[["store_id", "product_id", "category_id", "city", "region", "store_type"]]
    for c in static.columns:
        out[c] = np.repeat(static[c].to_numpy(), len(days))
    out["series"] = np.repeat(np.arange(S), len(days))
    out["day_index"] = np.tile(days, S)
    return out


def categorical_levels(series: pd.DataFrame) -> dict[str, list[str]]:
    """Fixed category levels, saved with the model, so codes are identical at training and inference."""
    levels = {c: sorted(series[c].astype(str).unique()) for c in ["store_id", "product_id", "category_id", "city", "region", "store_type"]}
    levels["festival"] = ["None", *FESTIVAL_NAMES]
    return levels


def to_model_matrix(rows: pd.DataFrame, levels: dict[str, list[str]]) -> pd.DataFrame:
    X = rows[FEATURES].copy()
    for c in CATEGORICAL:
        X[c] = pd.Categorical(X[c].astype(str), categories=levels[c])
    for c in NUMERIC:
        X[c] = X[c].astype(float)
    return X
