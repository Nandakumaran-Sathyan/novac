"""Does treating stockout days as censored matter? Trains the chosen configuration twice on train + validation:

  censoring-aware (production):  label = sales on days that did NOT stock out; lags skip stockout days
  naive:                         label = all sales (stockout days included as if they were demand); lags use raw sales

and scores both against latent demand on the test period, overall and on the days that stocked out.

    .venv/bin/python scripts/censoring_ablation.py
"""

import json
import sys
from dataclasses import replace
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from novac_forecast.db import get_engine                                            # noqa: E402
from novac_forecast.features import build_panel, categorical_levels, load_history, to_model_matrix  # noqa: E402
from novac_forecast.metrics import scores                                           # noqa: E402
from novac_forecast.train import CANDIDATES, fit, split_days, training_rows         # noqa: E402

report = json.loads((ROOT / "reports" / "model_evaluation.json").read_text())
params, rounds = CANDIDATES[report["objective"]], report["trees"]
panel = build_panel(load_history(get_engine()))
levels = categorical_levels(panel.series)
split = split_days(panel.shape[1])
naive_panel = replace(panel, signal=np.where(panel.present, panel.units_sold, np.nan),
                      stockout=np.where(panel.present, 0.0, np.nan))

out = {}
for name, p in (("censoring_aware", panel), ("naive", naive_panel)):
    rows = training_rows(p, 0, split["validation"][1])
    booster = fit(rows, levels, params, rounds)
    test = training_rows(p, *split["test"])
    pred = np.clip(booster.predict(to_model_matrix(test, levels)), 0, None)
    stocked_out = panel.stockout[test["series"], test["day_index"]] == 1
    out[name] = {"all_test_days": {k: round(v, 4) for k, v in scores(test["latent"], pred).items()},
                 "stockout_days": {k: round(v, 4) for k, v in scores(test["latent"][stocked_out], pred[stocked_out]).items()}}
print(json.dumps(out, indent=2))
(ROOT / "reports" / "censoring_ablation.json").write_text(json.dumps(out, indent=2))
