import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { toHttpParams } from '../http/http-utils';
import { ForecastModelMetrics, ForecastStatus, ReorderRecommendations, ShopForecast, StockoutAlerts } from '../models';

/**
 * Forecasts (beta) for the signed-in owner's own shop, via the SmartPOS API, which takes the shop from the token
 * and forwards to the forecasting service. No call names a shop.
 */
@Injectable({ providedIn: 'root' })
export class ForecastingService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/forecasting`;

  status() {
    return this.http.get<ForecastStatus>(`${this.url}/status`);
  }

  forecast(query: { horizon?: number; category?: string | null } = {}) {
    return this.http.get<ShopForecast>(`${this.url}/forecast`, { params: toHttpParams(query) });
  }

  stockoutAlerts() {
    const params = toHttpParams({}).appendAll({ risk: ['HIGH', 'MEDIUM', 'LOW'] });
    return this.http.get<StockoutAlerts>(`${this.url}/stockout-alerts`, { params });
  }

  replenishment() {
    return this.http.get<ReorderRecommendations>(`${this.url}/replenishment`);
  }

  metrics() {
    return this.http.get<ForecastModelMetrics>(`${this.url}/metrics`, { params: toHttpParams({ evaluation: 'test' }) });
  }

  generate() {
    return this.http.post<{ run_id: string; forecast_rows: number }>(`${this.url}/generate`, {});
  }
}
