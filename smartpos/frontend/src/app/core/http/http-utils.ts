import { HttpErrorResponse, HttpParams } from '@angular/common/http';

/** Builds query parameters, skipping null/undefined/empty values. */
export function toHttpParams(query: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== null && value !== undefined && value !== '') {
      params = params.set(key, String(value));
    }
  }
  return params;
}

/**
 * Turns an API error into a sentence for the user. The API returns RFC 7807 problem details:
 * `errors` for validation failures, `detail` for business-rule failures.
 */
export function errorMessage(error: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (!(error instanceof HttpErrorResponse)) {
    return fallback;
  }
  if (error.status === 0) {
    return 'Cannot reach the SmartPOS server. Check that the API is running.';
  }
  const body = error.error;
  if (body && typeof body === 'object') {
    if (body.errors && typeof body.errors === 'object') {
      const messages = Object.values(body.errors as Record<string, string[]>).flat();
      if (messages.length > 0) {
        return messages.join(' ');
      }
    }
    if (typeof body.detail === 'string' && body.detail) {
      return body.detail;
    }
  }
  switch (error.status) {
    case 401:
      return 'Your session has ended. Please sign in again.';
    case 403:
      return 'You do not have permission to do that.';
    case 404:
      return 'The requested record was not found.';
    case 429:
      return 'Too many attempts. Wait a minute and try again.';
  }
  return error.status >= 500 ? 'The server hit an unexpected error. Please try again.' : fallback;
}

/** Today's date as yyyy-MM-dd in the browser's time zone. */
export function localIsoDate(date = new Date()): string {
  const offsetMs = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offsetMs).toISOString().slice(0, 10);
}

export function addDays(isoDate: string, days: number): string {
  const d = new Date(`${isoDate}T00:00:00`);
  d.setDate(d.getDate() + days);
  return localIsoDate(d);
}
