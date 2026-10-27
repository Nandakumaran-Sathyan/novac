import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { toHttpParams } from '../http/http-utils';
import { Category, PagedResult, PageQuery, Product, ProductInput } from '../models';

export interface ProductQuery extends PageQuery {
  search?: string;
  categoryId?: number | null;
  includeInactive?: boolean;
}

@Injectable({ providedIn: 'root' })
export class ProductsService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/products`;

  list(query: ProductQuery) {
    return this.http.get<PagedResult<Product>>(this.url, { params: toHttpParams(query) });
  }

  get(id: number) {
    return this.http.get<Product>(`${this.url}/${id}`);
  }

  create(input: ProductInput) {
    return this.http.post<Product>(this.url, input);
  }

  update(id: number, input: ProductInput) {
    return this.http.put<Product>(`${this.url}/${id}`, input);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.url}/${id}`);
  }
}

@Injectable({ providedIn: 'root' })
export class CategoriesService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/categories`;

  list() {
    return this.http.get<Category[]>(this.url);
  }

  create(name: string, description: string | null = null) {
    return this.http.post<Category>(this.url, { name, description });
  }
}
