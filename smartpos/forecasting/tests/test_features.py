"""Feature engineering: lags and rolling statistics are correct, censored days are gaps, and nothing leaks from the future."""

from dataclasses import replace

import numpy as np
import pandas as pd
import pytest

from novac_forecast.features import FEATURES, build_panel, categorical_levels, features_for_days, to_model_matrix


def history_from(ds):
    h = ds.daily_sales.merge(ds.products[["product_id", "category_id"]]).merge(ds.stores[["store_id", "city", "region", "store_type"]])
    inv = ds.inventory_history[["store_id", "product_id", "date", "stockout_flag"]]
    return h.merge(inv, on=["store_id", "product_id", "date"])[
        ["date", "store_id", "product_id", "category_id", "city", "region", "store_type", "units_sold", "latent_demand",
         "unit_price", "discount_percentage", "revenue", "stockout_flag"]]


@pytest.fixture(scope="module")
def panel(small_dataset):
    return build_panel(history_from(small_dataset))


def test_panel_holds_every_series_and_day(panel, small_dataset):
    assert panel.shape == (3 * 20, 120)
    assert panel.present.sum() == len(small_dataset.daily_sales)
    # Censored (stockout) days are gaps in the signal, not low values.
    censored = panel.stockout == 1
    assert censored.any() and np.isnan(panel.signal[censored]).all()
    assert np.array_equal(panel.signal[~np.isnan(panel.signal)], panel.units_sold[~np.isnan(panel.signal)])


def test_lags_point_at_the_right_days(panel):
    d = 60
    rows = features_for_days(panel, np.array([d]))
    for k in (1, 7, 14, 28):
        np.testing.assert_array_equal(rows[f"lag_{k}"].to_numpy(), panel.signal[:, d - k])


def test_rolling_statistics_use_only_the_previous_window(panel):
    d = 50
    rows = features_for_days(panel, np.array([d]))
    window = panel.signal[:, d - 7:d]
    expected = np.array([np.nanmean(w) if np.sum(~np.isnan(w)) >= 2 else np.nan for w in window])
    np.testing.assert_allclose(rows["rolling_mean_7"].to_numpy(), expected, equal_nan=True)
    window28 = panel.signal[:, d - 28:d]
    expected_std = np.array([np.nanstd(w, ddof=1) if np.sum(~np.isnan(w)) >= 9 else np.nan for w in window28])
    np.testing.assert_allclose(rows["rolling_std_28"].to_numpy(), expected_std, equal_nan=True)


def test_changing_the_target_day_or_anything_after_it_changes_no_feature(panel):
    """The leakage test: features for day d must be identical whatever happens on day d and later."""
    d = 70
    before = features_for_days(panel, np.array([d]))
    poisoned = replace(panel, signal=panel.signal.copy(), revenue=panel.revenue.copy(), stockout=panel.stockout.copy())
    poisoned.signal[:, d:] = 99_999.0
    poisoned.revenue[:, d:] = 99_999.0
    poisoned.stockout[:, d:] = 1.0
    after = features_for_days(poisoned, np.array([d]))
    pd.testing.assert_frame_equal(before, after)


def test_early_days_have_missing_lags_not_zeros(panel):
    rows = features_for_days(panel, np.array([0, 3]))
    assert rows.loc[rows["day_index"] == 0, "lag_1"].isna().all()
    assert rows.loc[rows["day_index"] == 3, "lag_7"].isna().all()


def test_calendar_and_festival_features(panel):
    idx = panel.index_of(pd.Timestamp("2025-01-14").date()) if panel.dates[0] <= pd.Timestamp("2025-01-14").date() <= panel.dates[-1] else None
    rows = features_for_days(panel, np.array([10]))
    assert set(rows["day_of_week"]) <= set(range(7)) and rows["is_weekend"].isin([0, 1]).all()
    assert (rows["days_to_festival"] >= 0).all() and (rows["days_to_festival"] <= 30).all()
    if idx is not None:
        assert (features_for_days(panel, np.array([idx]))["festival"] == "Pongal").all()


def test_model_matrix_has_stable_categorical_codes(panel):
    levels = categorical_levels(panel.series)
    a = to_model_matrix(features_for_days(panel, np.array([40])), levels)
    b = to_model_matrix(features_for_days(panel, np.array([41])), levels)
    assert list(a.columns) == FEATURES
    for c in ("store_id", "product_id", "festival"):
        assert list(a[c].cat.categories) == list(b[c].cat.categories)
