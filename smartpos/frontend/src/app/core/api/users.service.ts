import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { toHttpParams } from '../http/http-utils';
import { PagedResult, PageQuery, User, UserRole } from '../models';

export interface CreateUserInput {
  fullName: string;
  email: string;
  password: string;
  role: UserRole;
}

export interface UpdateUserInput {
  fullName: string;
  role: UserRole;
  isActive: boolean;
  newPassword: string | null;
}

@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/users`;

  list(query: PageQuery & { search?: string }) {
    return this.http.get<PagedResult<User>>(this.url, { params: toHttpParams(query) });
  }

  create(input: CreateUserInput) {
    return this.http.post<User>(this.url, input);
  }

  update(id: number, input: UpdateUserInput) {
    return this.http.put<User>(`${this.url}/${id}`, input);
  }
}
