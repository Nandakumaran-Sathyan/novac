"""SQLAlchemy ORM models for the forecasting schema. The Alembic migration in migrations/versions creates these.

Two families of tables:
- the synthetic benchmark chain (stores, products, daily_sales, ... and forecast_runs with its results), used
  offline to measure the method against known ground truth;
- shop_* tables holding forecasts for the real SmartPOS shops. Each shop_forecast_runs row belongs to exactly one
  shop, and every result row hangs off a run, so a shop's forecasts can only be read through its own runs.

Conventions: snake_case names, natural string keys for the synthetic master data (STR001, PRD0001...), NUMERIC for
money and quantities that can be fractional, DATE for calendar days, TIMESTAMPTZ for instants. Composite primary
keys on (store_id, product_id, date) make duplicate daily records impossible.
"""

from __future__ import annotations

import uuid
from datetime import date, datetime

from sqlalchemy import (Boolean, CheckConstraint, Date, DateTime, ForeignKey, ForeignKeyConstraint, Index, Integer,
                        Numeric, SmallInteger, String, Text, UniqueConstraint, func, text)
from sqlalchemy.dialects.postgresql import JSONB, UUID
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column

from .db import DECLARED_SCHEMA

S = DECLARED_SCHEMA


class Base(DeclarativeBase):
    pass


def _fk(column: str) -> str:
    return f"{S}.{column}"


# ------------------------------------------------------------------------------------------ master data
class Store(Base):
    __tablename__ = "stores"
    __table_args__ = (CheckConstraint("store_type IN ('Hypermarket','Supermarket','Neighbourhood')", name="ck_stores_type"),
                      {"schema": S})
    store_id: Mapped[str] = mapped_column(String(10), primary_key=True)
    store_name: Mapped[str] = mapped_column(String(100))
    city: Mapped[str] = mapped_column(String(50), index=True)
    state: Mapped[str] = mapped_column(String(50))
    region: Mapped[str] = mapped_column(String(50))
    store_type: Mapped[str] = mapped_column(String(20))
    latitude: Mapped[float] = mapped_column(Numeric(9, 6))
    longitude: Mapped[float] = mapped_column(Numeric(9, 6))


class ProductCategory(Base):
    __tablename__ = "product_categories"
    __table_args__ = {"schema": S}
    category_id: Mapped[str] = mapped_column(String(10), primary_key=True)
    category_name: Mapped[str] = mapped_column(String(60), unique=True)


class Supplier(Base):
    __tablename__ = "suppliers"
    __table_args__ = (CheckConstraint("lead_days > 0", name="ck_suppliers_lead_days"), {"schema": S})
    supplier_id: Mapped[str] = mapped_column(String(10), primary_key=True)
    supplier_name: Mapped[str] = mapped_column(String(100))
    lead_days: Mapped[int] = mapped_column(Integer)
    delay_probability: Mapped[float] = mapped_column(Numeric(4, 3))


class Product(Base):
    __tablename__ = "products"
    __table_args__ = (CheckConstraint("unit_price >= 0", name="ck_products_price"),
                      CheckConstraint("supplier_lead_days > 0", name="ck_products_lead_days"),
                      CheckConstraint("minimum_order_quantity > 0", name="ck_products_moq"),
                      {"schema": S})
    product_id: Mapped[str] = mapped_column(String(10), primary_key=True)
    product_name: Mapped[str] = mapped_column(String(120))
    category_id: Mapped[str] = mapped_column(ForeignKey(_fk("product_categories.category_id")), index=True)
    unit_price: Mapped[float] = mapped_column(Numeric(10, 2))
    supplier_id: Mapped[str] = mapped_column(ForeignKey(_fk("suppliers.supplier_id")), index=True)
    supplier_lead_days: Mapped[int] = mapped_column(Integer)
    minimum_order_quantity: Mapped[int] = mapped_column(Integer)
    is_perishable: Mapped[bool] = mapped_column(Boolean)
    launched_on: Mapped[date | None] = mapped_column(Date)


class DatasetRun(Base):
    """Which synthetic dataset is loaded: seed, dates and a summary, so every result can be reproduced."""
    __tablename__ = "dataset_runs"
    __table_args__ = {"schema": S}
    dataset_id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    seed: Mapped[int] = mapped_column(Integer)
    start_date: Mapped[date] = mapped_column(Date)
    end_date: Mapped[date] = mapped_column(Date)
    days: Mapped[int] = mapped_column(Integer)
    is_synthetic: Mapped[bool] = mapped_column(Boolean, server_default=text("true"))
    summary: Mapped[dict] = mapped_column(JSONB)
    loaded_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())


# ------------------------------------------------------------------------------------------ history
class DailySales(Base):
    __tablename__ = "daily_sales"
    __table_args__ = (
        CheckConstraint("units_sold >= 0 AND latent_demand >= units_sold AND lost_demand = latent_demand - units_sold",
                        name="ck_daily_sales_censoring"),
        CheckConstraint("discount_percentage >= 0 AND discount_percentage <= 100", name="ck_daily_sales_discount"),
        CheckConstraint("unit_price >= 0 AND revenue >= 0", name="ck_daily_sales_money"),
        Index("ix_daily_sales_date", "date"),
        Index("ix_daily_sales_product_date", "product_id", "date"),
        {"schema": S})
    store_id: Mapped[str] = mapped_column(ForeignKey(_fk("stores.store_id")), primary_key=True)
    product_id: Mapped[str] = mapped_column(ForeignKey(_fk("products.product_id")), primary_key=True)
    date: Mapped[date] = mapped_column(Date, primary_key=True)
    units_sold: Mapped[int] = mapped_column(Integer)
    latent_demand: Mapped[int] = mapped_column(Integer)      # SYNTHETIC ONLY: what customers wanted
    lost_demand: Mapped[int] = mapped_column(Integer)        # latent_demand - units_sold (stockouts)
    unit_price: Mapped[float] = mapped_column(Numeric(10, 2))
    revenue: Mapped[float] = mapped_column(Numeric(14, 2))
    promotion_flag: Mapped[bool] = mapped_column(Boolean)
    discount_percentage: Mapped[float] = mapped_column(Numeric(5, 2))
    holiday_flag: Mapped[bool] = mapped_column(Boolean)
    holiday_name: Mapped[str | None] = mapped_column(String(40))


class InventoryHistory(Base):
    __tablename__ = "inventory_history"
    __table_args__ = (
        CheckConstraint("closing_stock = opening_stock + stock_received - units_sold + inventory_adjustment",
                        name="ck_inventory_ledger"),
        CheckConstraint("closing_stock >= 0 AND opening_stock >= 0 AND stock_received >= 0", name="ck_inventory_non_negative"),
        Index("ix_inventory_history_date", "date"),
        Index("ix_inventory_history_product_date", "product_id", "date"),
        {"schema": S})
    store_id: Mapped[str] = mapped_column(ForeignKey(_fk("stores.store_id")), primary_key=True)
    product_id: Mapped[str] = mapped_column(ForeignKey(_fk("products.product_id")), primary_key=True)
    date: Mapped[date] = mapped_column(Date, primary_key=True)
    opening_stock: Mapped[int] = mapped_column(Integer)
    stock_received: Mapped[int] = mapped_column(Integer)
    units_sold: Mapped[int] = mapped_column(Integer)
    inventory_adjustment: Mapped[int] = mapped_column(Integer)
    closing_stock: Mapped[int] = mapped_column(Integer)
    stockout_flag: Mapped[bool] = mapped_column(Boolean)


class PurchaseOrder(Base):
    __tablename__ = "purchase_orders"
    __table_args__ = (
        CheckConstraint("quantity > 0", name="ck_purchase_orders_quantity"),
        CheckConstraint("status IN ('open','received')", name="ck_purchase_orders_status"),
        CheckConstraint("(status = 'received') = (received_on IS NOT NULL)", name="ck_purchase_orders_received"),
        Index("ix_purchase_orders_store_product_status", "store_id", "product_id", "status"),
        {"schema": S})
    po_id: Mapped[str] = mapped_column(String(12), primary_key=True)
    store_id: Mapped[str] = mapped_column(ForeignKey(_fk("stores.store_id")))
    product_id: Mapped[str] = mapped_column(ForeignKey(_fk("products.product_id")))
    supplier_id: Mapped[str] = mapped_column(ForeignKey(_fk("suppliers.supplier_id")))
    ordered_on: Mapped[date] = mapped_column(Date)
    expected_on: Mapped[date] = mapped_column(Date)
    received_on: Mapped[date | None] = mapped_column(Date)
    quantity: Mapped[int] = mapped_column(Integer)
    status: Mapped[str] = mapped_column(String(10))


# ------------------------------------------------------------------------------------------ models and results
class ModelMetadata(Base):
    __tablename__ = "model_metadata"
    __table_args__ = (Index("ux_model_metadata_one_active_per_source", "data_source", unique=True,
                            postgresql_where=text("is_active")),
                      CheckConstraint("data_source IN ('synthetic','smartpos')", name="ck_model_metadata_data_source"),
                      {"schema": S})
    model_version: Mapped[str] = mapped_column(String(40), primary_key=True)
    model_type: Mapped[str] = mapped_column(String(40))
    trained_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())
    target: Mapped[str] = mapped_column(String(40))
    train_start: Mapped[date] = mapped_column(Date)
    train_end: Mapped[date] = mapped_column(Date)
    validation_end: Mapped[date | None] = mapped_column(Date)
    test_end: Mapped[date | None] = mapped_column(Date)
    training_rows: Mapped[int] = mapped_column(Integer)
    best_iteration: Mapped[int | None] = mapped_column(Integer)
    features: Mapped[list] = mapped_column(JSONB)
    params: Mapped[dict] = mapped_column(JSONB)
    artifact_path: Mapped[str] = mapped_column(String(300))
    artifact_sha256: Mapped[str] = mapped_column(String(64))
    dataset_id: Mapped[int | None] = mapped_column(ForeignKey(_fk("dataset_runs.dataset_id"), ondelete="SET NULL"))
    is_active: Mapped[bool] = mapped_column(Boolean, server_default=text("false"))
    notes: Mapped[str | None] = mapped_column(Text)
    # 'synthetic' = the offline benchmark chain; 'smartpos' = the real shops. One active model per source.
    data_source: Mapped[str] = mapped_column(String(10), server_default=text("'synthetic'"))


class ModelEvaluationMetric(Base):
    __tablename__ = "model_evaluation_metrics"
    __table_args__ = (
        UniqueConstraint("model_version", "evaluation", "method", "target", "scope", "scope_id", name="uq_model_metrics"),
        CheckConstraint("evaluation IN ('validation','test','backtest')", name="ck_metrics_evaluation"),
        CheckConstraint("scope IN ('overall','store','category','product')", name="ck_metrics_scope"),
        {"schema": S})
    metric_id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    model_version: Mapped[str] = mapped_column(ForeignKey(_fk("model_metadata.model_version"), ondelete="CASCADE"), index=True)
    evaluation: Mapped[str] = mapped_column(String(20))
    method: Mapped[str] = mapped_column(String(40))
    target: Mapped[str] = mapped_column(String(20))
    scope: Mapped[str] = mapped_column(String(20))
    scope_id: Mapped[str] = mapped_column(String(100), server_default=text("''"))
    mae: Mapped[float] = mapped_column(Numeric(14, 4))
    rmse: Mapped[float] = mapped_column(Numeric(14, 4))
    wape: Mapped[float | None] = mapped_column(Numeric(10, 4))
    bias: Mapped[float | None] = mapped_column(Numeric(10, 4))
    n_obs: Mapped[int] = mapped_column(Integer)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())


class ForecastRun(Base):
    __tablename__ = "forecast_runs"
    __table_args__ = (CheckConstraint("horizon BETWEEN 1 AND 28", name="ck_forecast_runs_horizon"),
                      Index("ix_forecast_runs_generated_at", "generated_at"), {"schema": S})
    run_id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    model_version: Mapped[str] = mapped_column(ForeignKey(_fk("model_metadata.model_version")))
    as_of_date: Mapped[date] = mapped_column(Date)          # last day of history used
    forecast_start: Mapped[date] = mapped_column(Date)
    horizon: Mapped[int] = mapped_column(Integer)
    generated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())
    series_count: Mapped[int] = mapped_column(Integer)
    notes: Mapped[str | None] = mapped_column(Text)


class DemandForecast(Base):
    __tablename__ = "demand_forecasts"
    __table_args__ = (
        CheckConstraint("predicted_demand >= 0", name="ck_demand_forecasts_non_negative"),
        ForeignKeyConstraint(["run_id"], [_fk("forecast_runs.run_id")], ondelete="CASCADE"),
        Index("ix_demand_forecasts_store_date", "store_id", "forecast_date"),
        Index("ix_demand_forecasts_product_date", "product_id", "forecast_date"),
        {"schema": S})
    run_id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), primary_key=True)
    store_id: Mapped[str] = mapped_column(ForeignKey(_fk("stores.store_id")), primary_key=True)
    product_id: Mapped[str] = mapped_column(ForeignKey(_fk("products.product_id")), primary_key=True)
    forecast_date: Mapped[date] = mapped_column(Date, primary_key=True)
    horizon_day: Mapped[int] = mapped_column(SmallInteger)
    predicted_demand: Mapped[float] = mapped_column(Numeric(10, 2))
    method: Mapped[str] = mapped_column(String(30))         # lightgbm, or the fallback used for thin history


class StockoutPrediction(Base):
    __tablename__ = "stockout_predictions"
    __table_args__ = (
        CheckConstraint("stockout_risk IN ('HIGH','MEDIUM','LOW')", name="ck_stockout_risk"),
        ForeignKeyConstraint(["run_id"], [_fk("forecast_runs.run_id")], ondelete="CASCADE"),
        Index("ix_stockout_predictions_store_risk", "store_id", "stockout_risk"),
        {"schema": S})
    run_id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), primary_key=True)
    store_id: Mapped[str] = mapped_column(ForeignKey(_fk("stores.store_id")), primary_key=True)
    product_id: Mapped[str] = mapped_column(ForeignKey(_fk("products.product_id")), primary_key=True)
    current_stock: Mapped[int] = mapped_column(Integer)
    inventory_known: Mapped[bool] = mapped_column(Boolean)
    on_order_in_horizon: Mapped[int] = mapped_column(Integer)
    predicted_demand_horizon: Mapped[float] = mapped_column(Numeric(10, 2))
    days_of_cover: Mapped[float | None] = mapped_column(Numeric(8, 2))
    predicted_stockout_date: Mapped[date | None] = mapped_column(Date)
    stockout_risk: Mapped[str] = mapped_column(String(6))
    lead_days: Mapped[int] = mapped_column(Integer)
    safety_stock: Mapped[float] = mapped_column(Numeric(10, 2))
    reason: Mapped[str] = mapped_column(Text)


class ReplenishmentRecommendation(Base):
    __tablename__ = "replenishment_recommendations"
    __table_args__ = (
        CheckConstraint("recommended_quantity >= 0", name="ck_replenishment_quantity"),
        ForeignKeyConstraint(["run_id"], [_fk("forecast_runs.run_id")], ondelete="CASCADE"),
        Index("ix_replenishment_store_quantity", "store_id", "recommended_quantity"),
        {"schema": S})
    run_id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), primary_key=True)
    store_id: Mapped[str] = mapped_column(ForeignKey(_fk("stores.store_id")), primary_key=True)
    product_id: Mapped[str] = mapped_column(ForeignKey(_fk("products.product_id")), primary_key=True)
    recommended_quantity: Mapped[int] = mapped_column(Integer)
    current_stock: Mapped[int] = mapped_column(Integer)
    on_order: Mapped[int] = mapped_column(Integer)
    safety_stock: Mapped[float] = mapped_column(Numeric(10, 2))
    reorder_point: Mapped[float] = mapped_column(Numeric(10, 2))
    order_up_to: Mapped[float] = mapped_column(Numeric(10, 2))
    lead_days: Mapped[int] = mapped_column(Integer)
    minimum_order_quantity: Mapped[int] = mapped_column(Integer)
    order_by_date: Mapped[date | None] = mapped_column(Date)
    reason: Mapped[str] = mapped_column(Text)


# ------------------------------------------------------------------------------------------ real SmartPOS shops
class ShopForecastRun(Base):
    __tablename__ = "shop_forecast_runs"
    __table_args__ = (CheckConstraint("horizon BETWEEN 1 AND 7", name="ck_shop_forecast_runs_horizon"),
                      Index("ix_shop_forecast_runs_shop_generated", "shop_id", "generated_at"), {"schema": S})
    run_id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    shop_id: Mapped[int] = mapped_column(Integer)            # SmartPOS "Shops"."Id"
    model_version: Mapped[str] = mapped_column(ForeignKey(_fk("model_metadata.model_version")))
    as_of_date: Mapped[date] = mapped_column(Date)           # last complete day of sales used
    forecast_start: Mapped[date] = mapped_column(Date)
    horizon: Mapped[int] = mapped_column(SmallInteger)
    trading_days: Mapped[int] = mapped_column(Integer)       # days with at least one completed bill
    series_count: Mapped[int] = mapped_column(Integer)
    generated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())
    notes: Mapped[str | None] = mapped_column(Text)


class ShopDemandForecast(Base):
    __tablename__ = "shop_demand_forecasts"
    __table_args__ = (CheckConstraint("predicted_demand >= 0", name="ck_shop_demand_forecasts_non_negative"),
                      {"schema": S})
    run_id: Mapped[uuid.UUID] = mapped_column(ForeignKey(_fk("shop_forecast_runs.run_id"), ondelete="CASCADE"), primary_key=True)
    product_id: Mapped[int] = mapped_column(Integer, primary_key=True)    # SmartPOS "Products"."Id"
    forecast_date: Mapped[date] = mapped_column(Date, primary_key=True)
    horizon_day: Mapped[int] = mapped_column(SmallInteger)
    predicted_demand: Mapped[float] = mapped_column(Numeric(10, 2))
    method: Mapped[str] = mapped_column(String(30))


class ShopStockoutPrediction(Base):
    """One row per product per run; also snapshots the product's name, SKU and category at forecast time."""
    __tablename__ = "shop_stockout_predictions"
    __table_args__ = (CheckConstraint("stockout_risk IN ('HIGH','MEDIUM','LOW')", name="ck_shop_stockout_risk"),
                      {"schema": S})
    run_id: Mapped[uuid.UUID] = mapped_column(ForeignKey(_fk("shop_forecast_runs.run_id"), ondelete="CASCADE"), primary_key=True)
    product_id: Mapped[int] = mapped_column(Integer, primary_key=True)
    sku: Mapped[str] = mapped_column(String(50))
    product_name: Mapped[str] = mapped_column(String(150))
    category: Mapped[str] = mapped_column(String(100))
    current_stock: Mapped[int] = mapped_column(Integer)
    inventory_known: Mapped[bool] = mapped_column(Boolean)
    predicted_demand_horizon: Mapped[float] = mapped_column(Numeric(10, 2))
    days_of_cover: Mapped[float | None] = mapped_column(Numeric(8, 2))
    predicted_stockout_date: Mapped[date | None] = mapped_column(Date)
    stockout_risk: Mapped[str] = mapped_column(String(6))
    lead_days: Mapped[int] = mapped_column(Integer)
    safety_stock: Mapped[float] = mapped_column(Numeric(10, 2))
    reason: Mapped[str] = mapped_column(Text)


class ShopReplenishmentRecommendation(Base):
    __tablename__ = "shop_replenishment_recommendations"
    __table_args__ = (CheckConstraint("recommended_quantity >= 0", name="ck_shop_replenishment_quantity"), {"schema": S})
    run_id: Mapped[uuid.UUID] = mapped_column(ForeignKey(_fk("shop_forecast_runs.run_id"), ondelete="CASCADE"), primary_key=True)
    product_id: Mapped[int] = mapped_column(Integer, primary_key=True)
    recommended_quantity: Mapped[int] = mapped_column(Integer)
    current_stock: Mapped[int] = mapped_column(Integer)
    on_order: Mapped[int] = mapped_column(Integer)
    safety_stock: Mapped[float] = mapped_column(Numeric(10, 2))
    reorder_point: Mapped[float] = mapped_column(Numeric(10, 2))
    order_up_to: Mapped[float] = mapped_column(Numeric(10, 2))
    lead_days: Mapped[int] = mapped_column(Integer)
    minimum_order_quantity: Mapped[int] = mapped_column(Integer)
    order_by_date: Mapped[date | None] = mapped_column(Date)
    reason: Mapped[str] = mapped_column(Text)
