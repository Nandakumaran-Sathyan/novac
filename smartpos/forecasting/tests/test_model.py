"""Training, the recursive 7-day forecast, fallbacks and baselines."""

from dataclasses import replace

import numpy as np
import pytest

from novac_forecast.features import build_panel, categorical_levels, to_model_matrix
from novac_forecast.forecast import (baseline_moving_average, baseline_same_day_last_week, fallback_values,
                                     recursive_forecast)
from novac_forecast.metrics import scores
from novac_forecast.train import CANDIDATES, fit, split_days, training_rows
from tests.test_features import history_from


@pytest.fixture(scope="module")
def trained(small_dataset):
    panel = build_panel(history_from(small_dataset))
    levels = categorical_levels(panel.series)
    rows = training_rows(panel, 0, 100)
    booster = fit(rows, levels, CANDIDATES["tweedie_1.2"], rounds=80)
    return panel, levels, booster, rows


def test_training_uses_only_uncensored_days_and_is_reproducible(trained, small_dataset):
    panel, levels, booster, rows = trained
    labelled = rows["target"].notna()
    assert labelled.sum() < len(rows)                              # stockout days carry no label
    assert (rows.loc[labelled, "target"] == rows.loc[labelled, "units_sold"]).all()
    again = fit(rows, levels, CANDIDATES["tweedie_1.2"], rounds=80)
    X = to_model_matrix(rows.head(500), levels)
    np.testing.assert_array_equal(booster.predict(X), again.predict(X))


def test_the_model_beats_the_moving_average_on_held_out_days(trained):
    panel, levels, booster, _ = trained
    test = training_rows(panel, 100, 120)
    pred = np.clip(booster.predict(to_model_matrix(test, levels)), 0, None)
    model = scores(test["latent"], pred)["wape"]
    baseline = scores(test["latent"], test["rolling_mean_7"].fillna(0))["wape"]
    assert model < baseline


def test_seven_day_forecast_shape_and_values(trained):
    panel, levels, booster, _ = trained
    result = recursive_forecast(booster, levels, panel, origin=110, horizon=7)
    assert result.predictions.shape == (panel.shape[0], 7)
    assert np.isfinite(result.predictions).all() and (result.predictions >= 0).all()
    assert set(result.method) == {"lightgbm"}


def test_forecast_ignores_actuals_after_the_origin(trained):
    """Backtests run on a panel that already holds the future; the forecast must not see it."""
    panel, levels, booster, _ = trained
    base = recursive_forecast(booster, levels, panel, origin=105, horizon=7).predictions
    poisoned = replace(panel, signal=panel.signal.copy(), discount=panel.discount.copy())
    poisoned.signal[:, 106:] = 10_000.0
    poisoned.discount[:, 106:] = 50.0
    np.testing.assert_array_equal(base, recursive_forecast(booster, levels, poisoned, origin=105, horizon=7).predictions)


def test_recursion_feeds_predictions_into_later_days(trained):
    """Day 2's lag_1 is day 1's prediction, so a shock to the history changes every later day."""
    panel, levels, booster, _ = trained
    base = recursive_forecast(booster, levels, panel, origin=105, horizon=3).predictions
    shocked = replace(panel, signal=panel.signal.copy())
    shocked.signal[:, 99:106] *= 3
    changed = recursive_forecast(booster, levels, shocked, origin=105, horizon=3).predictions
    assert (changed[:, 2] > base[:, 2]).mean() > 0.8


def test_new_product_and_no_history_fallbacks(trained):
    panel, levels, booster, _ = trained
    thin = replace(panel, signal=panel.signal.copy())
    thin.signal[0, :] = np.nan                     # a series with no history at all (new product)
    thin.signal[1, :] = np.nan
    thin.signal[1, 103:106] = [4.0, 6.0, 5.0]       # only three observed days
    result = recursive_forecast(booster, levels, thin, origin=105, horizon=7)
    assert result.method[0] == "fallback_category_average" and result.method[1] == "fallback_recent_average"
    assert np.allclose(result.predictions[1], 5.0)
    values, _ = fallback_values(thin, 105)
    assert values[0] >= 0 and np.allclose(result.predictions[0], values[0])


def test_baselines(trained):
    panel, *_ = trained
    ma = baseline_moving_average(panel, 105, 7)
    assert ma.shape == (panel.shape[0], 7) and np.allclose(ma[:, 0], ma[:, 6])
    sdlw = baseline_same_day_last_week(panel, 105, 7)
    ok = ~np.isnan(panel.signal[:, 99])
    np.testing.assert_allclose(sdlw[ok, 0], panel.signal[ok, 99])


def test_split_is_chronological_75_15_10():
    split = split_days(745)
    assert split["train"] == (0, 558) and split["validation"] == (558, 669) and split["test"] == (669, 745)
