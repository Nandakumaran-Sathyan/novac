import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { toHttpParams } from '../http/http-utils';
import {
  CreateSaleRequest,
  DailySalesReport,
  DashboardSummary,
  Insights,
  PagedResult,
  PageQuery,
  Sale,
  SaleSummary,
  TopProductRow,
} from '../models';

export interface SaleQuery extends PageQuery {
  from?: string;
  to?: string;
  search?: string;
}

@Injectable({ providedIn: 'root' })
export class SalesService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/sales`;

  create(request: CreateSaleRequest) {
    return this.http.post<Sale>(this.url, request);
  }

  list(query: SaleQuery) {
    return this.http.get<PagedResult<SaleSummary>>(this.url, { params: toHttpParams(query) });
  }

  get(id: number) {
    return this.http.get<Sale>(`${this.url}/${id}`);
  }
}

@Injectable({ providedIn: 'root' })
export class ReportsService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/reports`;

  dailySales(from: string, to: string) {
    return this.http.get<DailySalesReport>(`${this.url}/daily-sales`, { params: toHttpParams({ from, to }) });
  }

  topProducts(from: string, to: string, limit = 10) {
    return this.http.get<TopProductRow[]>(`${this.url}/top-products`, { params: toHttpParams({ from, to, limit }) });
  }
}

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  summary() {
    return this.http.get<DashboardSummary>(`${environment.apiUrl}/dashboard/summary`);
  }

  insights(days: number) {
    return this.http.get<Insights>(`${environment.apiUrl}/dashboard/insights`, { params: toHttpParams({ days }) });
  }
}
