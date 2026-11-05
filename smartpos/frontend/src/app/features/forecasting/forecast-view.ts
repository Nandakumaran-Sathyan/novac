import { DailyDemand, ProductForecast, StockoutRisk } from '../../core/models';

/** Pure helpers behind the forecasting page (unit-tested in forecast-view.spec.ts). */

export const RISK_ORDER: Record<StockoutRisk, number> = { HIGH: 0, MEDIUM: 1, LOW: 2 };
export const RISK_BADGE: Record<StockoutRisk, string> = { HIGH: 'badge-danger', MEDIUM: 'badge-warning', LOW: 'badge-success' };

/** Demand of one product on one day, or its total over the horizon when no day is chosen. */
export function demandOn(item: ProductForecast, day: string | null): number {
  if (day === null) {
    return item.cumulative_predicted_demand;
  }
  return item.daily.find((d) => d.forecast_date === day)?.predicted_demand ?? 0;
}

/** Whether a product row matches the category and the search text (name or SKU). */
export function matches(item: { product_name: string; sku: string; category: string }, search: string, category: string | null): boolean {
  const term = search.trim().toLowerCase();
  return (category === null || item.category === category)
    && (term === '' || item.product_name.toLowerCase().includes(term) || item.sku.toLowerCase().includes(term));
}

/** Products matching the search text (name or SKU) and category, sorted by demand (largest first). */
export function filterProducts(items: readonly ProductForecast[], search: string, category: string | null, day: string | null): ProductForecast[] {
  return items
    .filter((i) => matches(i, search, category))
    .sort((a, b) => demandOn(b, day) - demandOn(a, day) || a.product_name.localeCompare(b.product_name));
}

/** The shop's categories, alphabetically. */
export function categoriesOf(items: readonly ProductForecast[]): string[] {
  return [...new Set(items.map((i) => i.category))].sort((a, b) => a.localeCompare(b));
}

/** Category totals from product forecasts: always exactly the sum of the products shown. */
export function categoryTotals(items: readonly ProductForecast[], day: string | null): { category: string; demand: number; products: number }[] {
  const totals = new Map<string, { category: string; demand: number; products: number }>();
  for (const item of items) {
    const row = totals.get(item.category) ?? { category: item.category, demand: 0, products: 0 };
    row.demand += demandOn(item, day);
    row.products += 1;
    totals.set(item.category, row);
  }
  return [...totals.values()].map((r) => ({ ...r, demand: round2(r.demand) })).sort((a, b) => b.demand - a.demand);
}

/** Daily totals of a set of products (for the 7-day chart). */
export function dailyTotals(items: readonly ProductForecast[]): DailyDemand[] {
  const byDay = new Map<string, number>();
  for (const item of items) {
    for (const d of item.daily) {
      byDay.set(d.forecast_date, (byDay.get(d.forecast_date) ?? 0) + d.predicted_demand);
    }
  }
  return [...byDay.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([forecast_date, v]) => ({ forecast_date, predicted_demand: round2(v) }));
}

export function riskCounts(items: readonly { stockout_risk: StockoutRisk }[]): Record<StockoutRisk, number> {
  const counts: Record<StockoutRisk, number> = { HIGH: 0, MEDIUM: 0, LOW: 0 };
  for (const i of items) {
    counts[i.stockout_risk] += 1;
  }
  return counts;
}

export function units(value: number): string {
  return Math.round(value).toLocaleString();
}

function round2(v: number): number {
  return Math.round(v * 100) / 100;
}
