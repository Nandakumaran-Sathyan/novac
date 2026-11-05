import os
import uuid

import pytest
from sqlalchemy import create_engine, text

from novac_forecast.db import DECLARED_SCHEMA
from novac_forecast.synthetic.generator import GeneratorConfig, generate

URL = os.environ.get("FORECAST_TEST_DATABASE_URL") or os.environ.get("FORECAST_DATABASE_URL")


@pytest.fixture(scope="session")
def small_dataset():
    """3 stores x 20 products x 120 days: fast enough for most tests."""
    return generate(GeneratorConfig(days=120, store_ids=("STR001", "STR008", "STR010"), product_limit=20))


@pytest.fixture(scope="session")
def full_dataset():
    """The real configuration (10 stores, 105 products, 745 days). About 15 s; shared by the whole session."""
    return generate(GeneratorConfig())


def migrate(schema: str, url: str) -> None:
    from alembic import command
    from alembic.config import Config

    from novac_forecast.cli import ROOT
    cfg = Config(str(ROOT / "alembic.ini"))
    cfg.set_main_option("script_location", str(ROOT / "migrations"))
    cfg.attributes.update(schema=schema, url=url, skip_logging=True)
    command.upgrade(cfg, "head")


@pytest.fixture(scope="session")
def pg_engine():
    """A throwaway, migrated forecasting schema (forecasting_test_<random>), dropped at the end of the session."""
    if not URL:
        pytest.skip("set FORECAST_TEST_DATABASE_URL to run PostgreSQL tests")
    schema = f"forecasting_test_{uuid.uuid4().hex[:8]}"
    migrate(schema, URL)
    engine = create_engine(URL).execution_options(schema_translate_map={DECLARED_SCHEMA: schema})
    yield engine
    with create_engine(URL).begin() as conn:
        conn.execute(text(f'DROP SCHEMA "{schema}" CASCADE'))


@pytest.fixture(scope="session")
def shops(pg_engine):
    """A throwaway schema shaped like the SmartPOS tables, filled with known shop data (see tests/smartpos_fixture.py)."""
    from tests.smartpos_fixture import create_and_seed

    schema = f"smartpos_test_{uuid.uuid4().hex[:8]}"
    fixture = create_and_seed(create_engine(URL), schema)
    yield fixture
    with create_engine(URL).begin() as conn:
        conn.execute(text(f'DROP SCHEMA "{schema}" CASCADE'))
