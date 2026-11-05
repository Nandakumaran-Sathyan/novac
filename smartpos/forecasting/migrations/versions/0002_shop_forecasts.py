"""Forecasts for the real SmartPOS shops: shop_* result tables, and one active model per data source.

Existing synthetic models are kept and marked data_source = 'synthetic'. Metric scope ids widen to 100 characters,
because a SmartPOS category name (up to 100) is a scope id.

Revision ID: 0002
Revises: 0001
Create Date: 2026-11-05 12:30:00
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa

revision: str = '0002'
down_revision: Union[str, None] = '0001'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def _schema() -> str:
    """The schema being migrated. add_column/alter_column/check constraints render the schema name literally (they
    bypass env.py's schema_translate_map), so this migration names the real target everywhere."""
    return op.get_context().version_table_schema or 'forecasting'


def upgrade() -> None:
    S = _schema()
    op.add_column('model_metadata', sa.Column('data_source', sa.String(length=10), server_default=sa.text("'synthetic'"),
                                              nullable=False), schema=S)
    op.create_check_constraint('ck_model_metadata_data_source', 'model_metadata', "data_source IN ('synthetic','smartpos')",
                               schema=S)
    op.drop_index('ux_model_metadata_one_active', table_name='model_metadata', schema=S)
    op.create_index('ux_model_metadata_one_active_per_source', 'model_metadata', ['data_source'], unique=True,
                    schema=S, postgresql_where=sa.text('is_active'))

    op.alter_column('model_evaluation_metrics', 'scope_id', type_=sa.String(length=100), existing_type=sa.String(length=20),
                    existing_nullable=False, existing_server_default=sa.text("''"), schema=S)

    op.create_table('shop_forecast_runs',
    sa.Column('run_id', sa.UUID(), nullable=False),
    sa.Column('shop_id', sa.Integer(), nullable=False),
    sa.Column('model_version', sa.String(length=40), nullable=False),
    sa.Column('as_of_date', sa.Date(), nullable=False),
    sa.Column('forecast_start', sa.Date(), nullable=False),
    sa.Column('horizon', sa.SmallInteger(), nullable=False),
    sa.Column('trading_days', sa.Integer(), nullable=False),
    sa.Column('series_count', sa.Integer(), nullable=False),
    sa.Column('generated_at', sa.DateTime(timezone=True), server_default=sa.text('now()'), nullable=False),
    sa.Column('notes', sa.Text(), nullable=True),
    sa.CheckConstraint('horizon BETWEEN 1 AND 7', name='ck_shop_forecast_runs_horizon'),
    sa.ForeignKeyConstraint(['model_version'], [f'{S}.model_metadata.model_version'], ),
    sa.PrimaryKeyConstraint('run_id'),
    schema=S
    )
    op.create_index('ix_shop_forecast_runs_shop_generated', 'shop_forecast_runs', ['shop_id', 'generated_at'], unique=False, schema=S)
    op.create_table('shop_demand_forecasts',
    sa.Column('run_id', sa.UUID(), nullable=False),
    sa.Column('product_id', sa.Integer(), nullable=False),
    sa.Column('forecast_date', sa.Date(), nullable=False),
    sa.Column('horizon_day', sa.SmallInteger(), nullable=False),
    sa.Column('predicted_demand', sa.Numeric(precision=10, scale=2), nullable=False),
    sa.Column('method', sa.String(length=30), nullable=False),
    sa.CheckConstraint('predicted_demand >= 0', name='ck_shop_demand_forecasts_non_negative'),
    sa.ForeignKeyConstraint(['run_id'], [f'{S}.shop_forecast_runs.run_id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('run_id', 'product_id', 'forecast_date'),
    schema=S
    )
    op.create_table('shop_stockout_predictions',
    sa.Column('run_id', sa.UUID(), nullable=False),
    sa.Column('product_id', sa.Integer(), nullable=False),
    sa.Column('sku', sa.String(length=50), nullable=False),
    sa.Column('product_name', sa.String(length=150), nullable=False),
    sa.Column('category', sa.String(length=100), nullable=False),
    sa.Column('current_stock', sa.Integer(), nullable=False),
    sa.Column('inventory_known', sa.Boolean(), nullable=False),
    sa.Column('predicted_demand_horizon', sa.Numeric(precision=10, scale=2), nullable=False),
    sa.Column('days_of_cover', sa.Numeric(precision=8, scale=2), nullable=True),
    sa.Column('predicted_stockout_date', sa.Date(), nullable=True),
    sa.Column('stockout_risk', sa.String(length=6), nullable=False),
    sa.Column('lead_days', sa.Integer(), nullable=False),
    sa.Column('safety_stock', sa.Numeric(precision=10, scale=2), nullable=False),
    sa.Column('reason', sa.Text(), nullable=False),
    sa.CheckConstraint("stockout_risk IN ('HIGH','MEDIUM','LOW')", name='ck_shop_stockout_risk'),
    sa.ForeignKeyConstraint(['run_id'], [f'{S}.shop_forecast_runs.run_id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('run_id', 'product_id'),
    schema=S
    )
    op.create_table('shop_replenishment_recommendations',
    sa.Column('run_id', sa.UUID(), nullable=False),
    sa.Column('product_id', sa.Integer(), nullable=False),
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
    sa.CheckConstraint('recommended_quantity >= 0', name='ck_shop_replenishment_quantity'),
    sa.ForeignKeyConstraint(['run_id'], [f'{S}.shop_forecast_runs.run_id'], ondelete='CASCADE'),
    sa.PrimaryKeyConstraint('run_id', 'product_id'),
    schema=S
    )


def downgrade() -> None:
    S = _schema()
    op.drop_table('shop_replenishment_recommendations', schema=S)
    op.drop_table('shop_stockout_predictions', schema=S)
    op.drop_table('shop_demand_forecasts', schema=S)
    op.drop_index('ix_shop_forecast_runs_shop_generated', table_name='shop_forecast_runs', schema=S)
    op.drop_table('shop_forecast_runs', schema=S)
    # Core statements (not raw SQL) so env.py's schema_translate_map applies.
    metadata = sa.table('model_metadata', sa.column('model_version'), sa.column('data_source'), schema=S)
    metrics = sa.table('model_evaluation_metrics', sa.column('model_version'), schema=S)
    shop_models = sa.select(metadata.c.model_version).where(metadata.c.data_source != 'synthetic')
    op.execute(metrics.delete().where(metrics.c.model_version.in_(shop_models)))
    op.execute(metadata.delete().where(metadata.c.data_source != 'synthetic'))
    op.alter_column('model_evaluation_metrics', 'scope_id', type_=sa.String(length=20), existing_type=sa.String(length=100),
                    existing_nullable=False, existing_server_default=sa.text("''"), schema=S)
    op.drop_index('ux_model_metadata_one_active_per_source', table_name='model_metadata', schema=S)
    op.create_index('ux_model_metadata_one_active', 'model_metadata', ['is_active'], unique=True, schema=S,
                    postgresql_where=sa.text('is_active'))
    op.drop_constraint('ck_model_metadata_data_source', 'model_metadata', schema=S, type_='check')
    op.drop_column('model_metadata', 'data_source', schema=S)
