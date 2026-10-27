import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

/** Adds the bearer token to API calls and signs the user out if the API rejects it. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const token = auth.accessToken;
  const isApiCall = request.url.startsWith(environment.apiUrl);
  const isLogin = request.url.endsWith('/auth/login');

  const outgoing =
    token && isApiCall && !isLogin
      ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : request;

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      // 401 on an authenticated call = token expired, revoked, or user deactivated.
      if (error instanceof HttpErrorResponse && error.status === 401 && token && isApiCall && !isLogin) {
        auth.logout('expired');
      }
      return throwError(() => error);
    }),
  );
};
