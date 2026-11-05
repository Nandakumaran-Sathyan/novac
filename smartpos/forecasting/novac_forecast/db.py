"""Database access: one SQLAlchemy engine per schema, with every table living in the forecasting schema.

Tables are declared with schema "forecasting" and the engine maps that name to FORECAST_DB_SCHEMA at execution
time (schema_translate_map), so tests can run against a throwaway schema without touching the real one.
"""

from __future__ import annotations

import re
from functools import lru_cache

from sqlalchemy import Engine, create_engine, text
from sqlalchemy.orm import sessionmaker

from .config import get_settings

DECLARED_SCHEMA = "forecasting"
_SCHEMA_NAME = re.compile(r"^[a-z_][a-z0-9_]{0,62}$")


def check_schema_name(schema: str) -> str:
    if not _SCHEMA_NAME.match(schema):
        raise ValueError(f"Invalid schema name {schema!r}")
    return schema


@lru_cache
def get_engine(schema: str | None = None, url: str | None = None) -> Engine:
    settings = get_settings()
    schema = check_schema_name(schema or settings.db_schema)
    engine = create_engine(url or settings.require_database_url(), pool_pre_ping=True, future=True)
    return engine.execution_options(schema_translate_map={DECLARED_SCHEMA: schema})


def session_factory(engine: Engine) -> sessionmaker:
    return sessionmaker(bind=engine, expire_on_commit=False, future=True)


def ensure_schema(engine: Engine, schema: str) -> None:
    with engine.begin() as conn:
        conn.execute(text(f'CREATE SCHEMA IF NOT EXISTS "{check_schema_name(schema)}"'))


def schema_of(engine: Engine) -> str:
    return engine.get_execution_options().get("schema_translate_map", {}).get(DECLARED_SCHEMA, DECLARED_SCHEMA)
