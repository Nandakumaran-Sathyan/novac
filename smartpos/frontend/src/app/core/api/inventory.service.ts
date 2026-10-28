import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { toHttpParams } from '../http/http-utils';
import { InventoryFilter, InventoryItem, PagedResult, PageQuery, StockMovement, StockMovementType } from '../models';

export interface InventoryQuery extends PageQuery {
  search?: string;
  category?: string | null;
  filter?: InventoryFilter;
}

export interface MovementQuery extends PageQuery {
  productId?: number | null;
  type?: StockMovementType | '';
  from?: string;
  to?: string;
}

@Injectable({ providedIn: 'root' })
export class InventoryService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/inventory`;

  list(query: InventoryQuery) {
    return this.http.get<PagedResult<InventoryItem>>(this.url, { params: toHttpParams(query) });
  }

  adjust(productId: number, quantityChange: number, reason: string) {
    return this.http.post<StockMovement>(`${this.url}/adjustments`, { productId, quantityChange, reason });
  }

  movements(query: MovementQuery) {
    return this.http.get<PagedResult<StockMovement>>(`${this.url}/movements`, { params: toHttpParams(query) });
  }
}
