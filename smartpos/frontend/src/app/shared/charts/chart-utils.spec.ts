import { describe, expect, it } from 'vitest';
import { barPath, compactNumber, niceScale, percentChange } from './chart-utils';

describe('niceScale', () => {
  it('rounds the axis up to tidy tick values', () => {
    expect(niceScale(13_094)).toEqual({ max: 20_000, step: 5_000 });
    expect(niceScale(87)).toEqual({ max: 100, step: 25 });
    expect(niceScale(3)).toEqual({ max: 4, step: 1 });
  });

  it('always covers the largest value', () => {
    for (const value of [0.4, 1, 7, 99, 100, 101, 5_555, 123_456, 9_999_999]) {
      expect(niceScale(value).max).toBeGreaterThanOrEqual(value);
    }
  });

  it('copes with an empty series', () => {
    expect(niceScale(0)).toEqual({ max: 4, step: 1 });
  });
});

describe('percentChange', () => {
  it('compares with the previous period', () => {
    expect(percentChange(110, 100)).toBeCloseTo(10);
    expect(percentChange(75, 100)).toBeCloseTo(-25);
    expect(percentChange(100, 100)).toBe(0);
  });

  it('has no percentage when there was nothing to compare with', () => {
    expect(percentChange(50, 0)).toBeNull();
    expect(percentChange(0, 0)).toBe(0);
  });
});

describe('barPath', () => {
  it('draws a closed shape that stays inside its box', () => {
    const path = barPath(10, 20, 8, 30, 4);
    expect(path.startsWith('M10,50')).toBe(true);
    expect(path.endsWith('Z')).toBe(true);
  });

  it('shrinks the corner radius for very short or narrow bars', () => {
    expect(barPath(0, 0, 4, 1, 4)).toContain('V1');
  });
});

describe('compactNumber', () => {
  it('shortens large figures for axis labels', () => {
    expect(compactNumber(12_400)).toMatch(/12\.4\s?K/i);
    expect(compactNumber(950)).toBe('950');
  });
});
