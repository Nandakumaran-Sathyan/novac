"""Create the forecasting schema: synthetic master data and history, models, metrics, forecasts and recommendations.

Tables are declared under schema "forecasting"; env.py maps that name to FORECAST_DB_SCHEMA.

Revision ID: 0001
Revises: 
Create Date: 2026-11-05 11:26:56.571574
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

revision: str = '0001'
down_revision: Union[str, None] = None
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table('dataset_runs',
    sa.Column('dataset_id', sa.Integer(), autoincrement=True, nullable=False),
    sa.Column('seed', sa.Integer(), nullable=False),
    sa.Column('start_date', sa.Date(), nullable=False),
    sa.Column('end_date', sa.Date(), nullable=False),
    sa.Column('days', sa.Integer(), nullable=False),
    sa.Column('is_synthetic', sa.Boolean(), server_default=sa.text('true'), nullable=False),
    sa.Column('summary', postgresql.JSONB(astext_type=sa.Text()), nullable=False),
    sa.Column('loaded_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.PrimaryKeyConstraint('dataset_id'),
    schema='forecasting'
    )
    op.create_table('product_categories',
    sa.Column('category_id', sa.String(length=10), nullable=False),
    sa.Column('category_name', sa.String(length=60), nullable=False),
    sa.PrimaryKeyConstraint('category_id'),
    sa.UniqueConstraint('category_name'),
    schema='forecasting'
    )
    op.create_table('stores',
    sa.Column('store_id', sa.String(length=10), nullable=False),
    sa.Column('store_name', sa.String(length=100), nullable=False),
    sa.Column('city', sa.String(length=50), nullable=False),
    sa.Column('state', sa.String(length=50), nullable=False),
    sa.Column('region', sa.String(length=50), nullable=False),
    sa.Column('store_type', sa.String(length=20), nullable=False),
    sa.Column('latitude', sa.Numeric(precision=9, scale=6), nullable=False),
    sa.Column('longitude', sa.Numeric(precision=9, scale=6), nullable=False),
    sa.CheckConstraint("store_type IN ('Hypermarket','Supermarket','Neighbourhood')", name='ck_stores_type'),
    sa.PrimaryKeyConstraint('store_id'),
    schema='forecasting'
    )
    op.create_index(op.f('ix_forecasting_stores_city'), 'stores', ['city'], unique=False, schema='forecasting')
    op.create_table('suppliers',
    sa.Column('supplier_id', sa.String(length=10), nullable=False),
    sa.Column('supplier_name', sa.String(length=100), nullable=False),
    sa.Column('lead_days', sa.Integer(), nullable=False),
    sa.Column('delay_probability', sa.Numeric(precision=4, scale=3), nullable=False),
    sa.CheckConstraint('lead_days > 0', name='ck_suppliers_lead_days'),
    sa.PrimaryKeyConstraint('supplier_id'),
    schema='forecasting'
    )
    op.create_table('model_metadata',
    sa.Column('model_version', sa.String(length=40), nullable=False),
    sa.Column('model_type', sa.String(length=40), nullable=False),
    sa.Column('trained_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('target', sa.String(length=40), nullable=False),
    sa.Column('train_start', sa.Date(), nullable=False),
    sa.Column('train_end', sa.Date(), nullable=False),
    sa.Column('validation_end', sa.Date(), nullable=True),
    sa.Column('test_end', sa.Date(), nullable=True),
    sa.Column('training_rows', sa.Integer(), nullable=False),
    sa.Column('best_iteration', sa.Integer(), nullable=True),
    sa.Column('features', postgresql.JSONB(astext_type=sa.Text()), nullable=False),
    sa.Column('params', postgresql.JSONB(astext_type=sa.Text()), nullable=False),
    sa.Column('artifact_path', sa.String(length=300), nullable=False),
    sa.Column('artifact_sha256', sa.String(length=64), nullable=False),
    sa.Column('dataset_id', sa.Integer(), nullable=True),
    sa.Column('is_active', sa.Boolean(), server_default=sa.text('false'), nullable=False),
    sa.Column('notes', sa.Text(), nullable=True),
    sa.ForeignKeyConstraint(['dataset_id'], ['forecasting.dataset_runs.dataset_id'], ondelete='SET NULL'),
    sa.PrimaryKeyConstraint('model_version'),
    schema='forecasting'
    )
    op.create_index('ux_model_metadata_one_active', 'model_metadata', ['is_active'], unique=True, schema='forecasting', postgresql_where=sa.text('is_active'))
    op.create_table('products',
    sa.Column('product_id', sa.String(length=10), nullable=False),
    sa.Column('product_name', sa.String(length=120), nullable=False),
    sa.Column('category_id', sa.String(length=10), nullable=False),
    sa.Column('unit_price', sa.Numeric(precision=10, scale=2), nullable=False),
    sa.Column('supplier_id', sa.String(length=10), nullable=False),
    sa.Column('supplier_lead_days', sa.Integer(), nullable=False),
    sa.Column('minimum_order_quantity', sa.Integer(), nullable=False),
    sa.Column('is_perishable', sa.Boolean(), nullable=False),
    sa.Column('launched_on', sa.Date(), nullable=True),
    sa.CheckConstraint('minimum_order_quantity > 0', name='ck_products_moq'),
    sa.CheckConstraint('supplier_lead_days > 0', name='ck_products_lead_days'),
    sa.CheckConstraint('unit_price >= 0', name='ck_products_price'),
    sa.ForeignKeyConstraint(['category_id'], ['forecasting.product_categories.category_id'], ),
    sa.ForeignKeyConstraint(['supplier_id'], ['forecasting.suppliers.supplier_id'], ),
    sa.PrimaryKeyConstraint('product_id'),
    schema='forecasting'
    )
    op.create_index(op.f('ix_forecasting_products_category_id'), 'products', ['category_id'], unique=False, schema='forecasting')
    op.create_index(op.f('ix_forecasting_products_supplier_id'), 'products', ['supplier_id'], unique=False, schema='forecasting')
    op.create_table('daily_sales',
    sa.Column('store_id', sa.String(length=10), nullable=False),
    sa.Column('product_id', sa.String(length=10), nullable=False),
    sa.Column('date', sa.Date(), nullable=False),
    sa.Column('units_sold', sa.Integer(), nullable=False),
    sa.Column('latent_demand', sa.Integer(), nullable=False),
    sa.Column('lost_demand', sa.Integer(), nullable=False),
    sa.Column('unit_price', sa.Numeric(precision=10, scale=2), nullable=False),
    sa.Column('revenue', sa.Numeric(precision=14, scale=2), nullable=False),
    sa.Column('promotion_flag', sa.Boolean(), nullable=False),
    sa.Column('discount_percentage', sa.Numeric(precision=5, scale=2), nullable=False),
    sa.Column('holiday_flag', sa.Boolean(), nullable=False),
    sa.Column('holiday_name', sa.String(length=40), nullable=True),
    sa.CheckConstraint('discount_percentage >= 0 AND discount_percentage <= 100', name='ck_daily_sales_discount'),
    sa.CheckConstraint('unit_price >= 0 AND revenue >= 0', name='ck_daily_sales_money'),
    sa.CheckConstraint('units_sold >= 0 AND latent_demand >= units_sold AND lost_demand = latent_demand - units_sold', name='ck_daily_sales_censoring'),
    sa.ForeignKeyConstraint(['product_id'], ['forecasting.products.product_id'], ),
    sa.ForeignKeyConstraint(['store_id'], ['forecasting.stores.store_id'], ),
    sa.PrimaryKeyConstraint('store_id', 'product_id', 'date'),
    schema='forecasting'
    )
    op.create_index('ix_daily_sales_date', 'daily_sales', ['date'], unique=False, schema='forecasting')
    op.create_index('ix_daily_sales_product_date', 'daily_sales', ['product_id', 'date'], unique=False, schema='forecasting')
    op.create_table('forecast_runs',
    sa.Column('run_id', sa.UUID(), nullable=False),
    sa.Column('model_version', sa.String(length=40), nullable=False),
    sa.Column('as_of_date', sa.Date(), nullable=False),
    sa.Column('forecast_start', sa.Date(), nullable=False),
    sa.Column('horizon', sa.Integer(), nullable=False),
    sa.Column('generated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('series_count', sa.Integer(), nullable=False),
    sa.Column('notes', sa.Text(), nullable=True),
    sa.CheckConstraint('horizon BETWEEN 1 AND 28', name='ck_forecast_runs_horizon'),
    sa.ForeignKeyConstraint(['model_version'], ['forecasting.model_metadata.model_version'], ),
    sa.PrimaryKeyConstraint('run_id'),
    schema='forecasting'
    )
    op.create_index('ix_forecast_runs_generated_at', 'forecast_runs', ['generated_at'], unique=False, schema='forecasting')
    op.create_table('inventory_history',
    sa.Column('store_id', sa.String(length=10), nullable=False),
    sa.Column('product_id', sa.String(length=10), nullable=False),
    sa.Column('date', sa.Date(), nullable=False),
    sa.Column('opening_stock', sa.Integer(), nullable=False),
    sa.Column('stock_received', sa.Integer(), nullable=False),
    sa.Column('units_sold', sa.Integer(), nullable=False),
    sa.Column('inventory_adjustment', sa.Integer(), nullable=False),
    sa.Column('closing_stock', sa.Integer(), nullable=False),
    sa.Column('stockout_flag', sa.Boolean(), nullable=False),
    sa.CheckConstraint('closing_stock = opening_stock + stock_received - units_sold + inventory_adjustment', name='ck_inventory_ledger'),
    sa.CheckConstraint('closing_stock >= 0 AND opening_stock >= 0 AND stock_received >= 0', name='ck_inventory_non_negative'),
    sa.ForeignKeyConstraint(['product_id'], ['forecasting.products.product_id'], ),
    sa.ForeignKeyConstraint(['store_id'], ['forecasting.stores.store_id'], ),
    sa.PrimaryKeyConstraint('store_id', 'product_id', 'date'),
    schema='forecasting'
    )
    op.create_index('ix_inventory_history_date', 'inventory_history', ['date'], unique=False, schema='forecasting')
    op.create_index('ix_inventory_history_product_date', 'inventory_history', ['product_id', 'date'], unique=False, schema='forecasting')
    op.create_table('model_evaluation_metrics',
    sa.Column('metric_id', sa.Integer(), autoincrement=True, nullable=False),
    sa.Column('model_version', sa.String(length=40), nullable=False),
    sa.Column('evaluation', sa.String(length=20), nullable=False),
    sa.Column('method', sa.String(length=40), nullable=False),
    sa.Column('target', sa.String(length=20), nullable=False),
    sa.Column('scope', sa.String(length=20), nullable=False),
    sa.Column('scope_id', sa.String(length=20), server_default=sa.text("''"), nullable=False),
    sa.Column('mae', sa.Numeric(precision=14, scale=4), nullable=False),
    sa.Column('rmse', sa.Numeric(precision=14, scale=4), nullable=False),
    sa.Column('wape', sa.Numeric(precision=10, scale=4), nullable=True),
    sa.Column('bias', sa.Numeric(precision=10, scale=4), nullable=True),
    sa.Column('n_obs', sa.Integer(), nullable=False),
    sa.Column('created_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.CheckConstraint("evaluation IN ('validation','test','backtest')", name='ck_metrics_evaluation'),
    sa.CheckConstraint("scope IN ('overall','store','category','product')", name='ck_metrics_scope'),
    sa.ForeignKeyConstraint(['model_version'], ['forecasting.model_metadata.model_version'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('metric_id'),
    sa.UniqueConstraint('model_version', 'evaluation', 'method', 'target', 'scope', 'scope_id', name='uq_model_metrics'),
    schema='forecasting'
    )
    op.create_index(op.f('ix_forecasting_model_evaluation_metrics_model_version'), 'model_evaluation_metrics', ['model_version'], unique=False, schema='forecasting')
    op.create_table('purchase_orders',
    sa.Column('po_id', sa.String(length=12), nullable=False),
    sa.Column('store_id', sa.String(length=10), nullable=False),
    sa.Column('product_id', sa.String(length=10), nullable=False),
    sa.Column('supplier_id', sa.String(length=10), nullable=False),
    sa.Column('ordered_on', sa.Date(), nullable=False),
    sa.Column('expected_on', sa.Date(), nullable=False),
    sa.Column('received_on', sa.Date(), nullable=True),
    sa.Column('quantity', sa.Integer(), nullable=False),
    sa.Column('status', sa.String(length=10), nullable=False),
    sa.CheckConstraint("(status = 'received') = (received_on IS NOT NULL)", name='ck_purchase_orders_received'),
    sa.CheckConstraint("status IN ('open','received')", name='ck_purchase_orders_status'),
    sa.CheckConstraint('quantity > 0', name='ck_purchase_orders_quantity'),
    sa.ForeignKeyConstraint(['product_id'], ['forecasting.products.product_id'], ),
    sa.ForeignKeyConstraint(['store_id'], ['forecasting.stores.store_id'], ),
    sa.ForeignKeyConstraint(['supplier_id'], ['forecasting.suppliers.supplier_id'], ),
    sa.PrimaryKeyConstraint('po_id'),
    schema='forecasting'
    )
    op.create_index('ix_purchase_orders_store_product_status', 'purchase_orders', ['store_id', 'product_id', 'status'], unique=False, schema='forecasting')
    op.create_table('demand_forecasts',
    sa.Column('run_id', sa.UUID(), nullable=False),
    sa.Column('store_id', sa.String(length=10), nullable=False),
    sa.Column('product_id', sa.String(length=10), nullable=False),
    sa.Column('forecast_date', sa.Date(), nullable=False),
    sa.Column('horizon_day', sa.SmallInteger(), nullable=False),
    sa.Column('predicted_demand', sa.Numeric(precision=10, scale=2), nullable=False),
    sa.Column('method', sa.String(length=30), nullable=False),
    sa.CheckConstraint('predicted_demand >= 0', name='ck_demand_forecasts_non_negative'),
    sa.ForeignKeyConstraint(['product_id'], ['forecasting.products.product_id'], ),
    sa.ForeignKeyConstraint(['run_id'], ['forecasting.forecast_runs.run_id'], ondelete='CASCADE'),
    sa.ForeignKeyConstraint(['store_id'], ['forecasting.stores.store_id'], ),
    sa.PrimaryKeyConstraint('run_id', 'store_id', 'product_id', 'forecast_date'),
    schema='forecasting'
    )
    op.create_index('ix_demand_forecasts_product_date', 'demand_forecasts', ['product_id', 'forecast_date'], unique=False, schema='forecasting')
    op.create_index('ix_demand_forecasts_store_date', 'demand_forecasts', ['store_id', 'forecast_date'], unique=False, schema='forecasting')
    op.create_table('replenishment_recommendations',
    sa.Column('run_id', sa.UUID(), nullable=False),
    sa.Column('store_id', sa.String(length=10), nullable=False),
    sa.Column('product_id', sa.String(length=10), nullable=False),
    sa.Column('recommended_quantity', sa.Integer(), nullable=False),
    sa.Column('current_stock', sa.Integer(), nullable=False),
    sa.Column('on_order', sa.Integer(), nullable=False),
    sa.Column('safety_stock', sa.Numeric(precision=10, scale=2), nullable=False),
    sa.Column('reorder_point', sa.Numeric(precision=10, scale=2), nullable=False),
    sa.Column('order_up_to', sa.Numeric(precision=10, scale=2), nullable=False),
    sa.Column('lead_days', sa.Integer(), nullable=False),
    sa.Column('minimum_order_quantity', sa.Integer(), nullable=False),
    sa.Column('order_by_date', sa.Date(), nullable=True),
    sa.Column('reason', sa.Text(), nullable=False),
    sa.CheckConstraint('recommended_quantity >= 0', name='ck_replenishment_quantity'),
    sa.ForeignKeyConstraint(['product_id'], ['forecasting.products.product_id'], ),
    sa.ForeignKeyConstraint(['run_id'], ['forecasting.forecast_runs.run_id'], ondelete='CASCADE'),
    sa.ForeignKeyConstraint(['store_id'], ['forecasting.stores.store_id'], ),
    sa.PrimaryKeyConstraint('run_id', 'store_id', 'product_id'),
    schema='forecasting'
    )
    op.create_index('ix_replenishment_store_quantity', 'replenishment_recommendations', ['store_id', 'recommended_quantity'], unique=False, schema='forecasting')
    op.create_table('stockout_predictions',
    sa.Column('run_id', sa.UUID(), nullable=False),
    sa.Column('store_id', sa.String(length=10), nullable=False),
    sa.Column('product_id', sa.String(length=10), nullable=False),
    sa.Column('current_stock', sa.Integer(), nullable=False),
    sa.Column('inventory_known', sa.Boolean(), nullable=False),
    sa.Column('on_order_in_horizon', sa.Integer(), nullable=False),
    sa.Column('predicted_demand_horizon', sa.Numeric(precision=10, scale=2), nullable=False),
    sa.Column('days_of_cover', sa.Numeric(precision=8, scale=2), nullable=True),
    sa.Column('predicted_stockout_date', sa.Date(), nullable=True),
    sa.Column('stockout_risk', sa.String(length=6), nullable=False),
    sa.Column('lead_days', sa.Integer(), nullable=False),
    sa.Column('safety_stock', sa.Numeric(precision=10, scale=2), nullable=False),
    sa.Column('reason', sa.Text(), nullable=False),
    sa.CheckConstraint("stockout_risk IN ('HIGH','MEDIUM','LOW')", name='ck_stockout_risk'),
    sa.ForeignKeyConstraint(['product_id'], ['forecasting.products.product_id'], ),
    sa.ForeignKeyConstraint(['run_id'], ['forecasting.forecast_runs.run_id'], ondelete='CASCADE'),
    sa.ForeignKeyConstraint(['store_id'], ['forecasting.stores.store_id'], ),
    sa.PrimaryKeyConstraint('run_id', 'store_id', 'product_id'),
    schema='forecasting'
    )
    op.create_index('ix_stockout_predictions_store_risk', 'stockout_predictions', ['store_id', 'stockout_risk'], unique=False, schema='forecasting')


def downgrade() -> None:
    op.drop_index('ix_stockout_predictions_store_risk', table_name='stockout_predictions', schema='forecasting')
    op.drop_table('stockout_predictions', schema='forecasting')
    op.drop_index('ix_replenishment_store_quantity', table_name='replenishment_recommendations', schema='forecasting')
    op.drop_table('replenishment_recommendations', schema='forecasting')
    op.drop_index('ix_demand_forecasts_store_date', table_name='demand_forecasts', schema='forecasting')
    op.drop_index('ix_demand_forecasts_product_date', table_name='demand_forecasts', schema='forecasting')
    op.drop_table('demand_forecasts', schema='forecasting')
    op.drop_index('ix_purchase_orders_store_product_status', table_name='purchase_orders', schema='forecasting')
    op.drop_table('purchase_orders', schema='forecasting')
    op.drop_index(op.f('ix_forecasting_model_evaluation_metrics_model_version'), table_name='model_evaluation_metrics', schema='forecasting')
    op.drop_table('model_evaluation_metrics', schema='forecasting')
    op.drop_index('ix_inventory_history_product_date', table_name='inventory_history', schema='forecasting')
    op.drop_index('ix_inventory_history_date', table_name='inventory_history', schema='forecasting')
    op.drop_table('inventory_history', schema='forecasting')
    op.drop_index('ix_forecast_runs_generated_at', table_name='forecast_runs', schema='forecasting')
    op.drop_table('forecast_runs', schema='forecasting')
    op.drop_index('ix_daily_sales_product_date', table_name='daily_sales', schema='forecasting')
    op.drop_index('ix_daily_sales_date', table_name='daily_sales', schema='forecasting')
    op.drop_table('daily_sales', schema='forecasting')
    op.drop_index(op.f('ix_forecasting_products_supplier_id'), table_name='products', schema='forecasting')
    op.drop_index(op.f('ix_forecasting_products_category_id'), table_name='products', schema='forecasting')
    op.drop_table('products', schema='forecasting')
    op.drop_index('ux_model_metadata_one_active', table_name='model_metadata', schema='forecasting', postgresql_where=sa.text('is_active'))
    op.drop_table('model_metadata', schema='forecasting')
    op.drop_table('suppliers', schema='forecasting')
    op.drop_index(op.f('ix_forecasting_stores_city'), table_name='stores', schema='forecasting')
    op.drop_table('stores', schema='forecasting')
    op.drop_table('product_categories', schema='forecasting')
    op.drop_table('dataset_runs', schema='forecasting')
