"""Settings, read only from environment variables (never from files in the repository).

FORECAST_DATABASE_URL   SQLAlchemy URL, e.g. postgresql+psycopg://smartpos:<password>@127.0.0.1:5432/smartpos_dev
FORECAST_DB_SCHEMA      PostgreSQL schema that holds every forecasting table (default "forecasting")
FORECAST_SERVICE_KEY    Shared secret the SmartPOS API sends in the X-Service-Key header (required to serve the API)
FORECAST_ARTIFACT_DIR   Where trained models are written (default ./artifacts)
FORECAST_SMARTPOS_SCHEMA  Schema of the SmartPOS application tables, read-only (default "public")
FORECAST_SHOP_TIMEZONE  IANA time zone that defines a shop's calendar day (default "Asia/Kolkata", as the SmartPOS API)
"""

from functools import lru_cache
from pathlib import Path
from zoneinfo import ZoneInfo

from pydantic import Field, SecretStr, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

PACKAGE_ROOT = Path(__file__).resolve().parent.parent


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="FORECAST_", extra="ignore")

    database_url: SecretStr | None = None
    db_schema: str = Field(default="forecasting", pattern=r"^[a-z_][a-z0-9_]{0,62}$")
    service_key: SecretStr | None = None
    artifact_dir: Path = PACKAGE_ROOT / "artifacts"
    report_dir: Path = PACKAGE_ROOT / "reports"
    smartpos_schema: str = Field(default="public", pattern=r"^[a-z_][a-z0-9_]{0,62}$")
    shop_timezone: str = "Asia/Kolkata"

    @field_validator("shop_timezone")
    @classmethod
    def _known_zone(cls, value: str) -> str:
        ZoneInfo(value)
        return value

    @field_validator("service_key")
    @classmethod
    def _long_enough(cls, value: SecretStr | None) -> SecretStr | None:
        if value is not None and len(value.get_secret_value()) < 32:
            raise ValueError("FORECAST_SERVICE_KEY must be at least 32 characters")
        return value

    def require_database_url(self) -> str:
        if self.database_url is None:
            raise RuntimeError(
                "FORECAST_DATABASE_URL is not set. Export it in your shell, or run "
                "`eval \"$(.venv/bin/python scripts/dotnet_secrets_env.py)\"` to derive it from the SmartPOS API's user-secrets."
            )
        return self.database_url.get_secret_value()


@lru_cache
def get_settings() -> Settings:
    return Settings()
