"""Training, model selection and evaluation of the LightGBM demand model.

1. Chronological split of the calendar: first 75% train, next 15% validation, last 10% test.
2. Model selection on the validation period only: a few objectives, early stopping. Selection uses observed,
   uncensored sales (what a real shop can measure), never the synthetic latent demand.
3. Final evaluation, ONCE, on the test period with the chosen configuration refit on train + validation:
   a) day-ahead: each test day predicted from the actual history up to the day before;
   b) rolling-origin backtest: 7-day recursive forecasts from origins spread through the test period.
   Both are scored against latent demand (synthetic ground truth, including demand lost to stockouts) and against
   observed uncensored sales, and compared with a 7-day moving average and the same day last week.
4. The production model is the chosen configuration refit on ALL history, saved with its metadata.

Two data sources, each with its own active model:
- "smartpos": the real shops (one pooled model, the shop is a feature). There is no ground truth for lost demand,
  so evaluation is against observed uncensored sales only. Smaller trees suit the small dataset.
- "synthetic": the offline benchmark chain, where latent demand is known, used to validate the method itself.

Training label: observed sales on days that did not stock out. Stockout days are dropped from the label (their
sales are censored) and appear as gaps in the lag features, so the model learns demand rather than "what happened
to be on the shelf".
"""

from __future__ import annotations

import hashlib
import json
import logging
import time
from datetime import date, datetime, timezone
from pathlib import Path

import joblib
import lightgbm as lgb
import numpy as np
import pandas as pd
from sqlalchemy import Engine, text

from .db import check_schema_name, schema_of
from .features import FEATURES, Panel, build_panel, categorical_levels, features_for_days, load_history, to_model_matrix
from .forecast import (baseline_moving_average, baseline_same_day_last_week, long_format, recursive_forecast)
from .metrics import scores, scores_by
from .smartpos import load_shops

log = logging.getLogger(__name__)
MIN_SERIES_AGE = 7
BASE_PARAMS = dict(learning_rate=0.05, num_leaves=63, min_child_samples=40, feature_fraction=0.85,
                   bagging_fraction=0.8, bagging_freq=1, lambda_l2=1.0, seed=42, deterministic=True,
                   force_row_wise=True, verbose=-1, num_threads=8)
SOURCES = ("synthetic", "smartpos")
SOURCE_PARAMS = {"synthetic": {}, "smartpos": dict(num_leaves=15, min_child_samples=20)}
MIN_TRAIN_DAYS = 42           # 6 weeks: enough for the 28-day windows plus a validation and a test period


class NotEnoughHistory(ValueError):
    pass


CANDIDATES = {
    "poisson": dict(objective="poisson"),
    "tweedie_1.2": dict(objective="tweedie", tweedie_variance_power=1.2),
    "l2": dict(objective="regression"),
}


def split_days(T: int) -> dict[str, tuple[int, int]]:
    train_end = int(T * 0.75)
    valid_end = train_end + int(T * 0.15)
    return {"train": (0, train_end), "validation": (train_end, valid_end), "test": (valid_end, T)}


def training_rows(panel: Panel, start: int, end: int, chunk: int = 60) -> pd.DataFrame:
    parts = []
    for a in range(start, end, chunk):
        days = np.arange(a, min(a + chunk, end))
        rows = features_for_days(panel, days)
        rows["target"] = panel.signal[rows["series"], rows["day_index"]]
        rows["latent"] = panel.latent[rows["series"], rows["day_index"]]
        rows["units_sold"] = panel.units_sold[rows["series"], rows["day_index"]]
        keep = panel.present[rows["series"], rows["day_index"]] & (rows["series_age"] >= MIN_SERIES_AGE)
        parts.append(rows[keep.to_numpy()])
    return pd.concat(parts, ignore_index=True)


def fit(rows: pd.DataFrame, levels, params: dict, rounds: int, valid: pd.DataFrame | None = None):
    labelled = rows[rows["target"].notna()]
    train_set = lgb.Dataset(to_model_matrix(labelled, levels), label=labelled["target"], free_raw_data=False)
    callbacks, valid_sets = [], []
    if valid is not None:
        v = valid[valid["target"].notna()]
        valid_sets = [lgb.Dataset(to_model_matrix(v, levels), label=v["target"], reference=train_set)]
        callbacks = [lgb.early_stopping(50, verbose=False)]
    booster = lgb.train({**BASE_PARAMS, **params, "metric": "l1"}, train_set, num_boost_round=rounds,
                        valid_sets=valid_sets, callbacks=callbacks)
    return booster


def _attach(frame: pd.DataFrame, panel: Panel) -> pd.DataFrame:
    s = panel.series.iloc[frame["series"]].reset_index(drop=True)
    out = frame.reset_index(drop=True).copy()
    out["store_id"] = s["store_id"].to_numpy(); out["product_id"] = s["product_id"].to_numpy()
    out["category_id"] = s["category_id"].to_numpy()
    return out


def _metric_rows(frame: pd.DataFrame, evaluation: str, methods: dict[str, str], targets: dict[str, str],
                 category_names: dict[str, str], per_product: bool = True) -> list[dict]:
    out = []
    for method, column in methods.items():
        for target, actual in targets.items():
            for scope, by in (("overall", None), ("store", "store_id"), ("category", "category_id"),
                              *((("product", "product_id"),) if per_product else ())):
                for r in scores_by(frame, actual, column, by).to_dict("records"):
                    out.append({"evaluation": evaluation, "method": method, "target": target, "scope": scope, **r})
    return out


def backtest(booster, levels, panel: Panel, test_start: int, test_end: int, horizon: int = 7, step: int = 7) -> pd.DataFrame:
    """Rolling-origin backtest: forecast `horizon` days from each origin; origins every `step` days."""
    parts = []
    for origin in range(test_start - 1, test_end - horizon, step):
        result = recursive_forecast(booster, levels, panel, origin, horizon)
        frame = long_format(panel, origin, result.predictions, "lightgbm")
        frame["moving_average_7"] = baseline_moving_average(panel, origin, horizon).reshape(-1)
        frame["same_day_last_week"] = baseline_same_day_last_week(panel, origin, horizon).reshape(-1)
        frame["origin"] = panel.dates[origin]
        frame["latent"] = panel.latent[frame["series"], frame["day_index"]]
        frame["observed"] = panel.signal[frame["series"], frame["day_index"]]
        frame["present"] = panel.present[frame["series"], frame["day_index"]]
        parts.append(frame[frame["present"]])
    return pd.concat(parts, ignore_index=True)


def train_and_evaluate(engine: Engine, artifact_dir: Path, report_dir: Path, quick: bool = False,
                       source: str = "synthetic", through: date | None = None, smartpos_schema: str | None = None) -> dict:
    if source not in SOURCES:
        raise ValueError(f"source must be one of {SOURCES}")
    started = time.time()
    real = source == "smartpos"
    if real:
        shops = load_shops(engine, through=through, schema=smartpos_schema)
        history, data_summary = shops.history, {"shops": len(shops.trading_days), "trading_days": shops.trading_days,
                                                "through": str(shops.through)}
        if history.empty:
            raise NotEnoughHistory("No completed sales in any shop yet; nothing to train on.")
        panel = build_panel(history, end=shops.through)
    else:
        history, data_summary = load_history(engine), {}
        panel = build_panel(history)
    T = panel.shape[1]
    if T < MIN_TRAIN_DAYS:
        raise NotEnoughHistory(f"Training needs at least {MIN_TRAIN_DAYS} days of sales history; there are {T}.")
    split = split_days(T)
    levels = categorical_levels(panel.series)
    rounds = 300 if quick else 3000
    log.info("Panel: %d series x %d days (%s to %s)", *panel.shape, panel.dates[0], panel.dates[-1])

    rows = {name: training_rows(panel, a, b) for name, (a, b) in split.items()}
    log.info("Rows: %s", {k: len(v) for k, v in rows.items()})

    # ---- 1. model selection on validation (observable target only)
    selection = {}
    candidates = {k: {**SOURCE_PARAMS[source], **v} for k, v in CANDIDATES.items()}
    for name, params in (list(candidates.items())[:1] if quick else candidates.items()):
        booster = fit(rows["train"], levels, params, rounds, valid=rows["validation"])
        v = rows["validation"]
        pred = np.clip(booster.predict(to_model_matrix(v, levels), num_iteration=booster.best_iteration), 0, None)
        selection[name] = {**scores(v["target"], pred), "best_iteration": int(booster.best_iteration or rounds)}
        log.info("Candidate %s: validation WAPE %.4f at %d trees", name, selection[name]["wape"], selection[name]["best_iteration"])
    best_name = min(selection, key=lambda k: selection[k]["wape"])
    best_params = candidates[best_name]
    best_rounds = selection[best_name]["best_iteration"]

    # ---- 2. evaluation model: train + validation, fixed number of trees from early stopping
    train_valid = pd.concat([rows["train"], rows["validation"]], ignore_index=True)
    eval_booster = fit(train_valid, levels, best_params, best_rounds)
    test = rows["test"].copy()
    test["lightgbm"] = np.clip(eval_booster.predict(to_model_matrix(test, levels)), 0, None)
    test["moving_average_7"] = test["rolling_mean_7"].fillna(test["rolling_mean_28"]).fillna(0.0)
    test["same_day_last_week"] = test["lag_7"].fillna(test["moving_average_7"])
    test = _attach(test, panel)
    category_names = dict(zip(panel.series["category_id"], panel.series["category_id"]))
    methods = {"lightgbm": "lightgbm", "moving_average_7": "moving_average_7", "same_day_last_week": "same_day_last_week"}
    # Real shops: only what was actually observed. Synthetic: also the known latent demand.
    targets = {"units_sold": "target"} if real else {"latent_demand": "latent", "units_sold": "target"}
    metric_rows = _metric_rows(test, "test", methods, targets, category_names)

    bt = backtest(eval_booster, levels, panel, split["test"][0], split["test"][1], step=7 if not quick else 21)
    bt = _attach(bt, panel)
    bt_targets = {"units_sold": "observed"} if real else {"latent_demand": "latent", "units_sold": "observed"}
    metric_rows += _metric_rows(bt, "backtest", methods, bt_targets, category_names)
    horizon_target = "observed" if real else "latent"
    by_horizon = {m: {int(h): round(scores(g[horizon_target], g[m])["wape"], 4) for h, g in bt.groupby("horizon_day")} for m in methods}

    # ---- 3. production model: same configuration, all history
    all_rows = pd.concat([train_valid, rows["test"]], ignore_index=True)
    prod_booster = fit(all_rows, levels, best_params, best_rounds)
    importance = dict(sorted(zip(prod_booster.feature_name(), prod_booster.feature_importance("gain").round(0)),
                             key=lambda kv: -kv[1])[:15])
    as_of = panel.dates[-1]
    artifact_dir.mkdir(parents=True, exist_ok=True)
    payload = {"booster": prod_booster.model_to_string(), "levels": levels, "features": FEATURES,
               "params": {**BASE_PARAMS, **best_params}, "best_iteration": best_rounds, "trained_through": as_of}
    tmp = artifact_dir / "model.tmp.joblib"
    joblib.dump(payload, tmp)
    sha = hashlib.sha256(tmp.read_bytes()).hexdigest()
    version = f"{'shops-' if real else ''}lgbm-{as_of:%Y%m%d}-{sha[:8]}"
    path = artifact_dir / f"{version}.joblib"
    tmp.replace(path)

    split_dates = {k: (str(panel.dates[a]), str(panel.dates[b - 1])) for k, (a, b) in split.items()}
    _store(engine, version, best_name, best_params, best_rounds, path, sha, panel, split, all_rows, metric_rows,
           selection, split_dates, source)

    overall = {f"{r['evaluation']}/{r['target']}/{r['method']}": {k: round(r[k], 4) for k in ("mae", "rmse", "wape", "bias")} | {"n": r["n_obs"]}
               for r in metric_rows if r["scope"] == "overall"}
    report = {
        "model_version": version, "data_source": source, **({"data": data_summary} if data_summary else {}),
        "artifact": str(path), "objective": best_name, "trees": best_rounds,
        "splits": split_dates, "rows": {k: len(v) for k, v in rows.items()},
        "validation_selection": selection, "overall": overall, f"backtest_wape_by_horizon_{'observed' if real else 'latent'}": by_horizon,
        "backtest_origins": sorted({str(o) for o in bt["origin"]}),
        "top_features_by_gain": importance, "seconds": round(time.time() - started, 1),
    }
    if real:
        report_dir = report_dir / "shops"
    report_dir.mkdir(parents=True, exist_ok=True)
    (report_dir / "model_evaluation.json").write_text(json.dumps(report, indent=2, default=str))
    pd.DataFrame(metric_rows).to_csv(report_dir / "model_metrics.csv", index=False)
    return report


def _store(engine, version, objective, params, rounds, path, sha, panel, split, all_rows, metric_rows, selection, split_dates,
           source):
    s = f'"{check_schema_name(schema_of(engine))}"'
    real = source == "smartpos"
    data_note = ("Real SmartPOS shop sales; metrics are against observed (uncensored) sales." if real
                 else "SYNTHETIC benchmark data.")
    with engine.begin() as conn:
        dataset_id = None if real else conn.execute(text(f"SELECT max(dataset_id) FROM {s}.dataset_runs")).scalar()
        conn.execute(text(f"UPDATE {s}.model_metadata SET is_active = false WHERE is_active AND data_source = :src"),
                     {"src": source})
        conn.execute(text(f"""
            INSERT INTO {s}.model_metadata (model_version, model_type, target, train_start, train_end, validation_end,
                test_end, training_rows, best_iteration, features, params, artifact_path, artifact_sha256, dataset_id,
                is_active, notes, data_source)
            VALUES (:v, 'lightgbm', 'demand_units (observed uncensored sales)', :ts, :te, :ve, :xe, :n, :it,
                CAST(:f AS jsonb), CAST(:p AS jsonb), :path, :sha, :ds, true, :notes, :src)"""),
            {"v": version, "ts": panel.dates[0], "te": panel.dates[split["train"][1] - 1],
             "ve": panel.dates[split["validation"][1] - 1], "xe": panel.dates[-1], "n": int(all_rows["target"].notna().sum()),
             "it": rounds, "f": json.dumps(FEATURES), "p": json.dumps({"objective_choice": objective, **BASE_PARAMS, **params,
                                                                        "validation_selection": selection}),
             "path": str(path), "sha": sha, "ds": dataset_id,
             "notes": ("Production model trained on all history. Test/backtest metrics come from the same configuration "
                       f"trained on train+validation only ({split_dates['train'][0]} to {split_dates['validation'][1]}), "
                       f"evaluated on {split_dates['test'][0]} to {split_dates['test'][1]}. {data_note}"),
             "src": source})
        clean = [{**r, "model_version": version, **{k: (None if r[k] != r[k] else r[k]) for k in ("mae", "rmse", "wape", "bias")}}
                 for r in metric_rows]
        conn.execute(text(f"""
            INSERT INTO {s}.model_evaluation_metrics (model_version, evaluation, method, target, scope, scope_id, mae, rmse, wape, bias, n_obs)
            VALUES (:model_version, :evaluation, :method, :target, :scope, :scope_id, :mae, :rmse, :wape, :bias, :n_obs)"""), clean)


def load_model(path: Path):
    payload = joblib.load(path)
    booster = lgb.Booster(model_str=payload["booster"])
    return booster, payload
