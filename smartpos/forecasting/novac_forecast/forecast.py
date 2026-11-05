"""Recursive multi-day forecasting with the trained model, plus fallbacks and the two baselines.

Recursive strategy: predict day 1 from history, write the prediction into the demand signal as if it had been
observed, then predict day 2 (whose lag_1 is now the day-1 prediction), and so on. The features come from the same
`features_for_days` used in training, and only columns before the predicted day are ever read, so no future
information can leak in even when the panel already contains actuals (backtests).

Exogenous inputs for the forecast window follow what production can know: the price stays at its last observed
value and no promotion is assumed (there is no promotion calendar yet). Backtests use exactly the same assumption.
"""

from __future__ import annotations

from dataclasses import dataclass, replace

import numpy as np
import pandas as pd

from .features import Panel, features_for_days, to_model_matrix

MIN_HISTORY_DAYS = 7          # fewer observed days than this and the model is not trusted for the series


@dataclass
class ForecastResult:
    predictions: np.ndarray       # (S, H)
    method: np.ndarray            # (S,) 'lightgbm' or the fallback used


def freeze_exogenous(panel: Panel, origin: int, horizon: int) -> Panel:
    """Copy of the panel where everything after `origin` is unknown: price carried forward, no promotions."""
    end = origin + horizon + 1
    price = panel.price.copy(); discount = panel.discount.copy()
    price[:, origin + 1:end] = price[:, [origin]]
    discount[:, origin + 1:end] = 0.0
    signal = panel.signal.copy(); revenue = panel.revenue.copy(); stockout = panel.stockout.copy()
    signal[:, origin + 1:] = np.nan; revenue[:, origin + 1:] = np.nan; stockout[:, origin + 1:] = np.nan
    return replace(panel, price=price, discount=discount, signal=signal, revenue=revenue, stockout=stockout)


def fallback_values(panel: Panel, origin: int) -> tuple[np.ndarray, np.ndarray]:
    """Daily demand to use for series with too little history, and which fallback produced it.

    - a few observed days: their average (it's all there is);
    - none at all: the median daily demand of the same category in the same store, halved (a new product rarely
      sells like an established one in its first week).
    """
    window = panel.signal[:, max(0, origin - 27):origin + 1]
    observed = np.sum(~np.isnan(window), axis=1)
    own = np.where(observed > 0, np.nansum(window, axis=1) / np.maximum(observed, 1), np.nan)
    frame = panel.series[["store_id", "category_id"]].copy()
    frame["own"] = np.where(observed >= MIN_HISTORY_DAYS, own, np.nan)
    peer = frame.groupby(["store_id", "category_id"])["own"].transform("median").to_numpy()
    values = np.where(observed > 0, own, 0.5 * np.nan_to_num(peer, nan=0.0))
    method = np.where(observed > 0, "fallback_recent_average", "fallback_category_average")
    return np.nan_to_num(values, nan=0.0), method


def recursive_forecast(booster, levels, panel: Panel, origin: int, horizon: int) -> ForecastResult:
    if origin + horizon >= panel.shape[1]:
        raise ValueError("the panel needs a column for every forecast day")
    work = freeze_exogenous(panel, origin, horizon)
    S = panel.shape[0]
    preds = np.zeros((S, horizon))
    for h in range(1, horizon + 1):
        d = origin + h
        rows = features_for_days(work, np.array([d]))
        p = np.clip(booster.predict(to_model_matrix(rows, levels)), 0.0, None)
        preds[:, h - 1] = p
        work.signal[:, d] = p
        work.stockout[:, d] = 0.0
        work.revenue[:, d] = p * work.price[:, d]
    history_days = np.sum(~np.isnan(panel.signal[:, :origin + 1]), axis=1)
    thin = history_days < MIN_HISTORY_DAYS
    method = np.full(S, "lightgbm", dtype=object)
    if thin.any():
        values, why = fallback_values(panel, origin)
        preds[thin] = values[thin, None]
        method[thin] = why[thin]
    return ForecastResult(predictions=preds, method=method)


def baseline_moving_average(panel: Panel, origin: int, horizon: int, window: int = 7) -> np.ndarray:
    """Mean of the last `window` observed (uncensored) days, repeated for every horizon."""
    w = panel.signal[:, max(0, origin - window + 1):origin + 1]
    count = np.sum(~np.isnan(w), axis=1)
    mean = np.where(count > 0, np.nansum(w, axis=1) / np.maximum(count, 1), 0.0)
    return np.repeat(mean[:, None], horizon, axis=1)


def baseline_same_day_last_week(panel: Panel, origin: int, horizon: int) -> np.ndarray:
    """The same weekday one week earlier (always inside the history for horizons up to 7); falls back to the
    7-day moving average when that day was censored or missing."""
    ma = baseline_moving_average(panel, origin, horizon)
    out = np.empty_like(ma)
    for h in range(1, horizon + 1):
        src = origin + h - 7
        col = panel.signal[:, src] if src >= 0 else np.full(panel.shape[0], np.nan)
        out[:, h - 1] = np.where(np.isnan(col), ma[:, h - 1], col)
    return out


def long_format(panel: Panel, origin: int, values: np.ndarray, name: str) -> pd.DataFrame:
    S, H = values.shape
    out = pd.DataFrame({
        "series": np.repeat(np.arange(S), H), "horizon_day": np.tile(np.arange(1, H + 1), S),
        name: values.reshape(-1),
    })
    out["day_index"] = origin + out["horizon_day"]
    return out
