import { describe, expect, it } from 'vitest';
import { ProductForecast } from '../../core/models';
import { categoriesOf, categoryTotals, dailyTotals, demandOn, filterProducts, matches, riskCounts } from './forecast-view';

const item = (id: number, sku: string, name: string, category: string, daily: number[], risk: ProductForecast['stockout_risk'] = 'LOW'): ProductForecast => ({
  product_id: id, sku, product_name: name, category,
  method: 'lightgbm', current_stock: 10, cumulative_predicted_demand: daily.reduce((a, b) => a + b, 0), stockout_risk: risk,
  predicted_stockout_date: null, recommended_reorder_quantity: 0, model_version: 'v',
  daily: daily.map((v, i) => ({ forecast_date: `2026-11-0${5 + i}`, predicted_demand: v })),
});

const items = [
  item(1, 'RICE-5KG', 'Ponni Raw Rice 5 kg', 'Grocery', [1, 2, 3], 'HIGH'),
  item(16, 'MILK-500', 'Toned Milk 500 ml', 'Dairy', [10, 11, 12], 'MEDIUM'),
  item(3, 'DAL-1KG', 'Toor Dal 1 kg', 'Grocery', [4, 0.5, 0.25]),
];

describe('forecast view helpers', () => {
  it('reads one day or the whole horizon', () => {
    expect(demandOn(items[0], '2026-11-06')).toBe(2);
    expect(demandOn(items[0], null)).toBe(6);
    expect(demandOn(items[0], '2026-12-01')).toBe(0);
  });

  it('filters by name, SKU and category and sorts by demand', () => {
    expect(filterProducts(items, '', null, null).map((i) => i.product_id)).toEqual([16, 1, 3]);
    expect(filterProducts(items, 'dal', null, null).map((i) => i.product_id)).toEqual([3]);
    expect(filterProducts(items, 'rice-5', null, null)).toHaveLength(1);
    expect(filterProducts(items, '', 'Grocery', '2026-11-05').map((i) => i.product_id)).toEqual([3, 1]);
  });

  it('matches alerts and orders by the same rules', () => {
    const alert = { product_name: 'Toned Milk 500 ml', sku: 'MILK-500', category: 'Dairy' };
    expect(matches(alert, ' milk ', null)).toBe(true);
    expect(matches(alert, 'MILK-5', 'Dairy')).toBe(true);
    expect(matches(alert, '', 'Grocery')).toBe(false);
    expect(matches(alert, 'rice', null)).toBe(false);
  });

  it('lists the shop categories once each', () => {
    expect(categoriesOf(items)).toEqual(['Dairy', 'Grocery']);
  });

  it('category totals are exactly the sum of their products', () => {
    expect(categoryTotals(items, null)).toEqual([
      { category: 'Dairy', demand: 33, products: 1 },
      { category: 'Grocery', demand: 10.75, products: 2 },
    ]);
    expect(categoryTotals(items, '2026-11-05').find((c) => c.category === 'Grocery')?.demand).toBe(5);
  });

  it('adds products up day by day and counts risks', () => {
    expect(dailyTotals(items)).toEqual([
      { forecast_date: '2026-11-05', predicted_demand: 15 },
      { forecast_date: '2026-11-06', predicted_demand: 13.5 },
      { forecast_date: '2026-11-07', predicted_demand: 15.25 },
    ]);
    expect(riskCounts(items)).toEqual({ HIGH: 1, MEDIUM: 1, LOW: 1 });
  });
});
