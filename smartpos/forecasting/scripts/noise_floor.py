"""How good could ANY forecast be? Regenerates the loaded synthetic dataset (same seed) and scores a "perfect"
forecaster that knows each day's noise-free expected demand against the latent demand, over the same test period
the model is evaluated on. The gap between the model's WAPE and this floor is the room left for improvement.

    .venv/bin/python scripts/noise_floor.py            # uses the dates of reports/model_evaluation.json
"""

import json
import sys
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from novac_forecast.metrics import scores                                  # noqa: E402
from novac_forecast.synthetic.generator import GeneratorConfig, generate   # noqa: E402

report = json.loads((ROOT / "reports" / "model_evaluation.json").read_text())
start, end = (date.fromisoformat(d) for d in report["splits"]["test"])
ds = generate(GeneratorConfig(end_date=end, days=(end - date.fromisoformat(report["splits"]["train"][0])).days + 1))
s = ds.daily_sales
s = s[(s["date"] >= start) & (s["date"] <= end)].merge(ds.products[["product_id", "category_name"]])
out = {"test_period": [str(start), str(end)], "overall": {k: round(v, 4) for k, v in scores(s["latent_demand"], s["expected_demand"]).items()},
       "by_category": {c: round(scores(g["latent_demand"], g["expected_demand"])["wape"], 4) for c, g in s.groupby("category_name")}}
print(json.dumps(out, indent=2))
(ROOT / "reports" / "noise_floor.json").write_text(json.dumps(out, indent=2))
