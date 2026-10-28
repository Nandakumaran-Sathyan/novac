import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { toHttpParams } from '../http/http-utils';
import { Customer, CustomerInput, PagedResult, PageQuery } from '../models';

export interface CustomerQuery extends PageQuery {
  search?: string;
}

@Injectable({ providedIn: 'root' })
export class CustomersService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/customers`;

  list(query: CustomerQuery) {
    return this.http.get<PagedResult<Customer>>(this.url, { params: toHttpParams(query) });
  }

  create(input: CustomerInput) {
    return this.http.post<Customer>(this.url, input);
  }

  update(id: number, input: CustomerInput) {
    return this.http.put<Customer>(`${this.url}/${id}`, input);
  }
}
