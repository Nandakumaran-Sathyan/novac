"""Prints `export` lines for the forecasting service, derived from the SmartPOS API's dotnet user-secrets.

    eval "$(.venv/bin/python scripts/dotnet_secrets_env.py)"

The PostgreSQL connection (ConnectionStrings:SmartPos) and the shared service key (Forecasting:ServiceKey) already
live in the API's user-secrets, outside the repository. This turns them into FORECAST_DATABASE_URL and
FORECAST_SERVICE_KEY for the current shell only; nothing is written to disk and the password is not echoed.
"""

import os
import shlex
import shutil
import subprocess
import sys
from pathlib import Path
from urllib.parse import quote

API_PROJECT = Path(__file__).resolve().parents[2] / "backend" / "src" / "SmartPOS.API"


def dotnet() -> str:
    found = shutil.which("dotnet") or str(Path.home() / ".dotnet" / "dotnet")
    if not Path(found).exists():
        sys.exit("echo 'dotnet not found (expected on PATH or in ~/.dotnet)' >&2; false")
    return found


def secrets() -> dict[str, str]:
    out = subprocess.run([dotnet(), "user-secrets", "list", "--project", str(API_PROJECT)],
                         capture_output=True, text=True, check=True).stdout
    pairs = {}
    for line in out.splitlines():
        if " = " in line:
            key, value = line.split(" = ", 1)
            pairs[key.strip()] = value.strip()
    return pairs


def to_url(connection_string: str) -> str:
    parts = {}
    for item in connection_string.split(";"):
        if "=" in item:
            k, v = item.split("=", 1)
            parts[k.strip().lower()] = v.strip()
    host = parts.get("host", "127.0.0.1")
    port = parts.get("port", "5432")
    user = quote(parts.get("username", parts.get("user id", "")), safe="")
    password = quote(parts.get("password", ""), safe="")
    database = parts.get("database", "")
    auth = f"{user}:{password}@" if password else f"{user}@"
    return f"postgresql+psycopg://{auth}{host}:{port}/{database}"


def main() -> None:
    values = secrets()
    cs = values.get("ConnectionStrings:SmartPos")
    if not cs:
        sys.exit("echo 'ConnectionStrings:SmartPos is not in the API user-secrets' >&2; false")
    print(f"export FORECAST_DATABASE_URL={shlex.quote(to_url(cs))}")
    if key := values.get("Forecasting:ServiceKey"):
        print(f"export FORECAST_SERVICE_KEY={shlex.quote(key)}")
    print("echo 'Forecasting environment set from the SmartPOS API user-secrets.' >&2")


if __name__ == "__main__":
    main()
