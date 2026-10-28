import { describe, expect, it } from 'vitest';
import { Product } from '../../core/models';
import { addProduct, calculateTotals, CartLine, clampQuantity, removeLine, setQuantity } from './cart';

const line = (unitPrice: number, quantity: number, available = 100, productId = 1): CartLine => ({
  productId, sku: `SKU-${productId}`, name: `Item ${productId}`, unitPrice, quantity, available,
});

const product = (id: number, price: number, stockQuantity: number): Product => ({
  id, sku: `SKU-${id}`, name: `Item ${id}`, barcode: null, description: null, category: 'Test',
  price, costPrice: 0, stockQuantity, lowStockThreshold: 0, isLowStock: false, expiryDate: null, isActive: true,
  createdAt: '', updatedAt: '',
});

describe('calculateTotals (mirrors the server SaleCalculator)', () => {
  it('matches the server example: discount then tax, rounded half away from zero', () => {
    // Same figures as SaleCalculatorTests on the backend.
    const totals = calculateTotals([line(99.99, 3, 100, 1), line(40, 3, 100, 2)], 10, 5);
    expect(totals).toEqual({ subtotal: 419.97, discountAmount: 42, taxAmount: 18.9, total: 396.87 });
  });

  it('avoids floating-point drift', () => {
    // 0.1 + 0.2 style errors would give 0.30000000000000004.
    expect(calculateTotals([line(0.1, 1, 10, 1), line(0.2, 1, 10, 2)], 0, 0).total).toBe(0.3);
    // 19.99 × 3 is 59.97000000000001 in floating point.
    expect(calculateTotals([line(19.99, 3)], 0, 0).subtotal).toBe(59.97);
  });

  it('rounds a half cent up', () => {
    // 0.25 × 50% = 0.125 → 0.13
    expect(calculateTotals([line(0.25, 1)], 50, 0).discountAmount).toBe(0.13);
  });

  it('is zero for an empty cart', () => {
    expect(calculateTotals([], 10, 5)).toEqual({ subtotal: 0, discountAmount: 0, taxAmount: 0, total: 0 });
  });
});

describe('cart lines', () => {
  it('merges repeated adds of the same product', () => {
    let lines = addProduct([], product(1, 10, 5));
    lines = addProduct(lines, product(1, 10, 5));
    expect(lines).toHaveLength(1);
    expect(lines[0]!.quantity).toBe(2);
  });

  it('does not add an out-of-stock product', () => {
    expect(addProduct([], product(1, 10, 0))).toEqual([]);
  });

  it('never exceeds available stock or drops below one', () => {
    const lines = [line(10, 1, 3)];
    expect(setQuantity(lines, 1, 10)[0]!.quantity).toBe(3);
    expect(setQuantity(lines, 1, 0)[0]!.quantity).toBe(1);
    expect(setQuantity(lines, 1, -4)[0]!.quantity).toBe(1);
    expect(clampQuantity(Number.NaN, 3)).toBe(1);
    expect(clampQuantity(2.7, 3)).toBe(2);
  });

  it('removes a line', () => {
    expect(removeLine([line(1, 1, 5, 1), line(1, 1, 5, 2)], 1).map((l) => l.productId)).toEqual([2]);
  });
});
