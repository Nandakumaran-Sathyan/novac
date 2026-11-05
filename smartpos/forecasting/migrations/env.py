"""Alembic environment for the forecasting schema.

The target schema is FORECAST_DB_SCHEMA (default "forecasting"), or config.attributes["schema"] when migrations
are run from code (the tests use a throwaway schema). Tables are declared under the name "forecasting" and a
schema_translate_map points that name at the target, so one migration works for any schema.
"""

from logging.config import fileConfig

from alembic import context
from sqlalchemy import create_engine, text

from novac_forecast.config import get_settings
from novac_forecast.db import DECLARED_SCHEMA, check_schema_name
from novac_forecast.models import Base

config = context.config
if config.config_file_name is not None and not config.attributes.get("skip_logging"):
    fileConfig(config.config_file_name)

schema = check_schema_name(config.attributes.get("schema") or get_settings().db_schema)
url = config.attributes.get("url") or get_settings().require_database_url()


def include_name(name, type_, parent_names):
    # Only ever look at our own schema: the SmartPOS tables in "public" belong to EF Core.
    if type_ == "schema":
        return name in (DECLARED_SCHEMA, schema)
    return True


def run_migrations_online() -> None:
    engine = create_engine(url)
    with engine.begin() as connection:
        connection.execute(text(f'CREATE SCHEMA IF NOT EXISTS "{schema}"'))
        connection = connection.execution_options(schema_translate_map={DECLARED_SCHEMA: schema})
        context.configure(connection=connection, target_metadata=Base.metadata, version_table_schema=schema,
                          include_schemas=True, include_name=include_name, compare_type=True)
        with context.begin_transaction():
            context.run_migrations()
    engine.dispose()


if context.is_offline_mode():
    raise SystemExit("Offline (SQL script) mode is not supported; run against a database.")
run_migrations_online()
