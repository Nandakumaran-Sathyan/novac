"""Forecast accuracy metrics. All of them ignore pairs where the actual value is unknown (NaN).

MAE   mean absolute error, in units
RMSE  root mean squared error, in units (punishes big misses)
WAPE  weighted absolute percentage error = sum|forecast - actual| / sum(actual); robust to zero-demand days
Bias  sum(forecast - actual) / sum(actual); negative = the model under-forecasts
"""

from __future__ import annotations

import numpy as np
import pandas as pd


def scores(actual, forecast) -> dict[str, float]:
    a = np.asarray(actual, dtype=float)
    f = np.asarray(forecast, dtype=float)
    ok = ~np.isnan(a) & ~np.isnan(f)
    a, f = a[ok], f[ok]
    if len(a) == 0:
        return {"mae": float("nan"), "rmse": float("nan"), "wape": float("nan"), "bias": float("nan"), "n_obs": 0}
    err = f - a
    total = a.sum()
    return {
        "mae": float(np.mean(np.abs(err))),
        "rmse": float(np.sqrt(np.mean(err ** 2))),
        "wape": float(np.abs(err).sum() / total) if total > 0 else float("nan"),
        "bias": float(err.sum() / total) if total > 0 else float("nan"),
        "n_obs": int(len(a)),
    }


def scores_by(frame: pd.DataFrame, actual: str, forecast: str, by: str | None) -> pd.DataFrame:
    if by is None:
        return pd.DataFrame([{"scope_id": "", **scores(frame[actual], frame[forecast])}])
    rows = [{"scope_id": str(key), **scores(g[actual], g[forecast])} for key, g in frame.groupby(by, observed=True)]
    return pd.DataFrame(rows)
