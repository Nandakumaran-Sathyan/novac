import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { addDays, errorMessage, toHttpParams } from './http-utils';

const httpError = (status: number, error: unknown = null) => new HttpErrorResponse({ status, error });

describe('errorMessage', () => {
  it('joins validation messages from a ValidationProblemDetails body', () => {
    const err = httpError(400, { errors: { Sku: ['SKU is required.'], Name: ['Name is required.'] } });
    expect(errorMessage(err)).toBe('SKU is required. Name is required.');
  });

  it('uses the problem detail for business-rule failures', () => {
    expect(errorMessage(httpError(409, { detail: "Insufficient stock for 'Milk'." }))).toBe("Insufficient stock for 'Milk'.");
  });

  it('explains a network failure', () => {
    expect(errorMessage(httpError(0))).toContain('Cannot reach the SmartPOS server');
  });

  it('falls back to status-specific and generic messages', () => {
    expect(errorMessage(httpError(403))).toBe('You do not have permission to do that.');
    expect(errorMessage(httpError(500))).toContain('unexpected error');
    expect(errorMessage(new Error('boom'), 'Fallback')).toBe('Fallback');
  });
});

describe('toHttpParams', () => {
  it('skips empty values', () => {
    const params = toHttpParams({ search: '', categoryId: null, page: 2, from: undefined, includeInactive: false });
    expect(params.keys()).toEqual(['page', 'includeInactive']);
    expect(params.get('includeInactive')).toBe('false');
  });
});

describe('addDays', () => {
  it('crosses month boundaries', () => {
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
  });
});
