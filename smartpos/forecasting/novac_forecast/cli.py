"""Command line: python -m novac_forecast <command>.

Real SmartPOS shops (the default source):
  migrate              create/upgrade the forecasting schema (Alembic)
  train                train one LightGBM model on every shop's sales, evaluate it, store metrics and the model
  forecast [--shop N]  7-day forecast, stockout risk and reorders for every active product of every shop (or one)
  serve                run the API on 127.0.0.1:8090 (loopback only)

Synthetic benchmark chain (offline method evaluation against known demand; never shown to shop owners):
  generate             generate the synthetic dataset and print its summary (nothing is written)
  load [--replace]     generate the synthetic dataset and load it into PostgreSQL in one transaction
  validate             data-quality checks and EDA summary of the loaded data (writes reports/data_quality.json)
  train --source synthetic / forecast --source synthetic
  benchmark            migrate, load --replace, validate, train and forecast on the synthetic chain
"""

from __future__ import annotations

import argparse
import json
import logging
import sys
import time
from pathlib import Path

from .config import get_settings

log = logging.getLogger("novac_forecast")
ROOT = Path(__file__).resolve().parent.parent


def cmd_migrate(_: argparse.Namespace) -> None:
    from alembic import command
    from alembic.config import Config

    cfg = Config(str(ROOT / "alembic.ini"))
    cfg.set_main_option("script_location", str(ROOT / "migrations"))
    command.upgrade(cfg, "head")
    print(f"Schema '{get_settings().db_schema}' is at the latest migration.")


def cmd_generate(args: argparse.Namespace) -> None:
    from .synthetic.generator import GeneratorConfig, generate

    started = time.time()
    ds = generate(GeneratorConfig(seed=args.seed, days=args.days))
    print(json.dumps({**ds.summary(), "seconds": round(time.time() - started, 1)}, indent=2))


def cmd_load(args: argparse.Namespace) -> None:
    from .db import get_engine
    from .load import load_dataset
    from .synthetic.generator import GeneratorConfig, generate

    started = time.time()
    ds = generate(GeneratorConfig(seed=args.seed, days=args.days))
    counts = load_dataset(get_engine(), ds, replace=args.replace)
    print(json.dumps({"loaded": counts, "summary": ds.summary(), "seconds": round(time.time() - started, 1)}, indent=2))


def cmd_validate(_: argparse.Namespace) -> None:
    from .db import get_engine
    from .validation import run_validation

    report = run_validation(get_engine(), get_settings().report_dir)
    print(json.dumps(report, indent=2, default=str))
    if not report["passed"]:
        sys.exit(1)


def cmd_train(args: argparse.Namespace) -> None:
    from .db import get_engine
    from .train import train_and_evaluate

    result = train_and_evaluate(get_engine(), get_settings().artifact_dir, get_settings().report_dir, quick=args.quick,
                                source=args.source)
    print(json.dumps(result, indent=2, default=str))


def cmd_forecast(args: argparse.Namespace) -> None:
    from .db import get_engine
    from .pipeline import run_forecast, run_shop_forecasts

    if args.source == "synthetic":
        result = run_forecast(get_engine(), get_settings().artifact_dir, horizon=args.horizon)
    else:
        result = run_shop_forecasts(get_engine(), get_settings().artifact_dir, horizon=args.horizon, shop_id=args.shop,
                                    notes="generated via CLI")
    print(json.dumps(result, indent=2, default=str))


def cmd_serve(args: argparse.Namespace) -> None:
    import uvicorn

    if args.host not in ("127.0.0.1", "localhost", "::1") and not args.allow_remote:
        sys.exit("Refusing to listen on a non-loopback address: only the SmartPOS API on this machine should call this "
                 "service. Pass --allow-remote only behind a private network and TLS.")
    if get_settings().service_key is None:
        sys.exit("FORECAST_SERVICE_KEY is not set: every request would be refused. "
                 'Run: eval "$(.venv/bin/python scripts/dotnet_secrets_env.py)"')
    uvicorn.run("novac_forecast.api.app:app", host=args.host, port=args.port, log_level="info")


def cmd_benchmark(args: argparse.Namespace) -> None:
    cmd_migrate(args)
    args.replace, args.source = True, "synthetic"
    cmd_load(args)
    cmd_validate(args)
    cmd_train(args)
    cmd_forecast(args)


def main(argv: list[str] | None = None) -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
    parser = argparse.ArgumentParser(prog="python -m novac_forecast", description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("migrate").set_defaults(func=cmd_migrate)
    for name, func in (("generate", cmd_generate), ("load", cmd_load), ("benchmark", cmd_benchmark)):
        p = sub.add_parser(name)
        p.add_argument("--seed", type=int, default=20261105)
        p.add_argument("--days", type=int, default=745)
        p.add_argument("--replace", action="store_true", help="replace an existing dataset (load only)")
        p.add_argument("--quick", action="store_true", help=argparse.SUPPRESS)
        p.add_argument("--horizon", type=int, default=7, help=argparse.SUPPRESS)
        p.set_defaults(func=func)
    sub.add_parser("validate").set_defaults(func=cmd_validate)
    p = sub.add_parser("serve")
    p.add_argument("--host", default="127.0.0.1")
    p.add_argument("--port", type=int, default=8090)
    p.add_argument("--allow-remote", action="store_true")
    p.set_defaults(func=cmd_serve)
    p = sub.add_parser("train")
    p.add_argument("--source", choices=("smartpos", "synthetic"), default="smartpos")
    p.add_argument("--quick", action="store_true", help="fewer trees and backtest origins (for a fast check)")
    p.set_defaults(func=cmd_train)
    p = sub.add_parser("forecast")
    p.add_argument("--source", choices=("smartpos", "synthetic"), default="smartpos")
    p.add_argument("--shop", type=int, help="only this SmartPOS shop id (smartpos source)")
    p.add_argument("--horizon", type=int, default=7, choices=range(1, 8), metavar="1-7")
    p.set_defaults(func=cmd_forecast)
    args = parser.parse_args(argv)
    args.func(args)


if __name__ == "__main__":
    main()
