"""Response shapes of the forecasting API. Every response is for one shop and says which model and run made it."""

from __future__ import annotations

from datetime import date, datetime
from typing import Literal

from pydantic import BaseModel, Field

Risk = Literal["HIGH", "MEDIUM", "LOW"]
BETA_NOTE = ("Forecasting is in beta. Forecasts are learned from your shop's own sales history and get better as more "
             "sales are recorded; check them against what you know before ordering.")


class RunInfo(BaseModel):
    run_id: str
    model_version: str
    generated_at: datetime
    as_of_date: date
    forecast_start: date
    horizon: int
    trading_days: int


class Health(BaseModel):
    status: Literal["ok", "degraded"]
    database: Literal["ok", "unreachable"]
    model_version: str | None
    service_key_configured: bool


class ShopStatus(BaseModel):
    shop_id: int
    beta: bool = True
    note: str = BETA_NOTE
    model_version: str | None
    model_trained_at: datetime | None
    latest_run: RunInfo | None


class DailyPoint(BaseModel):
    forecast_date: date
    predicted_demand: float


class ProductForecast(BaseModel):
    product_id: int
    sku: str
    product_name: str
    category: str
    method: str
    current_stock: int
    cumulative_predicted_demand: float
    stockout_risk: Risk
    predicted_stockout_date: date | None
    recommended_reorder_quantity: int
    model_version: str
    daily: list[DailyPoint]


class ShopForecast(BaseModel):
    run: RunInfo
    shop_id: int
    horizon: int
    total_predicted_demand: float
    daily_totals: list[DailyPoint]
    items: list[ProductForecast]


class CategoryForecastItem(BaseModel):
    category: str
    cumulative_predicted_demand: float
    product_count: int
    daily: list[DailyPoint]


class CategoryForecast(BaseModel):
    run: RunInfo
    shop_id: int
    horizon: int
    items: list[CategoryForecastItem]


class StockoutAlert(BaseModel):
    product_id: int
    sku: str
    product_name: str
    category: str
    current_stock: int
    inventory_known: bool
    cumulative_predicted_demand: float
    days_of_cover: float | None
    predicted_stockout_date: date | None
    stockout_risk: Risk
    lead_days: int
    safety_stock: float
    reason: str
    model_version: str


class StockoutAlerts(BaseModel):
    run: RunInfo
    shop_id: int
    risk_note: str = Field(default="Risk bands are transparent rules (stock, forecast, lead time, safety stock), not calibrated probabilities.")
    counts: dict[str, int]
    items: list[StockoutAlert]


class Recommendation(BaseModel):
    product_id: int
    sku: str
    product_name: str
    category: str
    current_stock: int
    on_order: int
    cumulative_predicted_demand: float
    safety_stock: float
    reorder_point: float
    order_up_to: float
    lead_days: int
    minimum_order_quantity: int
    recommended_reorder_quantity: int
    order_by_date: date | None
    stockout_risk: Risk
    reason: str
    model_version: str


class Recommendations(BaseModel):
    run: RunInfo
    shop_id: int
    items: list[Recommendation]


class GenerateRequest(BaseModel):
    horizon: int = Field(default=7, ge=1, le=7)


class GenerateResult(BaseModel):
    run_id: str
    shop_id: int
    model_version: str
    as_of_date: date
    forecast_start: date
    horizon: int
    trading_days: int
    series: int
    forecast_rows: int
    total_predicted_units: float
    risk_counts: dict[str, int]
    products_to_reorder: int
    seconds: float


class MetricRow(BaseModel):
    evaluation: str
    method: str
    target: str
    scope: Literal["overall", "shop"]
    scope_id: str
    mae: float | None
    rmse: float | None
    wape: float | None
    bias: float | None
    n_obs: int


class ModelMetrics(BaseModel):
    model_version: str
    model_type: str
    trained_at: datetime
    train_start: date
    train_end: date
    validation_end: date | None
    test_end: date | None
    training_rows: int
    best_iteration: int | None
    evaluation_note: str = ("Measured on the most recent days of sales, which the model did not see while learning. Days "
                            "that ended out of stock are left out, because their sales understate demand.")
    metrics: list[MetricRow]
