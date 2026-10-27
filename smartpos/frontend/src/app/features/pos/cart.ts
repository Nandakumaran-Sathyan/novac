import { Product } from '../../core/models';

export interface CartLine {
  productId: number;
  sku: string;
  name: string;
  unitPrice: number;
  quantity: number;
  /** Stock on hand when the product was last loaded; the server re-checks at checkout. */
  available: number;
}

export interface CartTotals {
  subtotal: number;
  discountAmount: number;
  taxAmount: number;
  total: number;
}

/**
 * Preview of SaleCalculator on the server (SmartPOS.Application/Sales/SaleCalculator.cs).
 * Works in integer cents and basis points so results match the server's decimal arithmetic
 * exactly, including half-away-from-zero rounding. The server's figures are still authoritative.
 */
export function calculateTotals(lines: readonly CartLine[], discountPercent: number, taxRatePercent: number): CartTotals {
  const subtotalCents = lines.reduce((sum, line) => sum + toCents(line.unitPrice) * line.quantity, 0);
  const discountCents = roundDiv(subtotalCents * toBasisPoints(discountPercent), 10_000);
  const taxableCents = subtotalCents - discountCents;
  const taxCents = roundDiv(taxableCents * toBasisPoints(taxRatePercent), 10_000);
  return {
    subtotal: subtotalCents / 100,
    discountAmount: discountCents / 100,
    taxAmount: taxCents / 100,
    total: (taxableCents + taxCents) / 100,
  };
}

export function lineTotal(line: CartLine): number {
  return (toCents(line.unitPrice) * line.quantity) / 100;
}

/** Adds one unit of a product, merging with an existing line. Returns the same array if no stock is left. */
export function addProduct(lines: readonly CartLine[], product: Product): CartLine[] {
  const existing = lines.find((l) => l.productId === product.id);
  if (existing) {
    return setQuantity(lines, product.id, existing.quantity + 1);
  }
  if (product.stockQuantity < 1) {
    return [...lines];
  }
  return [
    ...lines,
    {
      productId: product.id,
      sku: product.sku,
      name: product.name,
      unitPrice: product.sellingPrice,
      quantity: 1,
      available: product.stockQuantity,
    },
  ];
}

/** Sets a line's quantity, clamped to 1..available. Use removeLine to delete. */
export function setQuantity(lines: readonly CartLine[], productId: number, quantity: number): CartLine[] {
  return lines.map((l) =>
    l.productId === productId ? { ...l, quantity: clampQuantity(quantity, l.available) } : l,
  );
}

export function removeLine(lines: readonly CartLine[], productId: number): CartLine[] {
  return lines.filter((l) => l.productId !== productId);
}

export function clampQuantity(quantity: number, available: number): number {
  if (!Number.isFinite(quantity)) {
    return 1;
  }
  return Math.min(Math.max(Math.trunc(quantity), 1), Math.max(available, 1));
}

function toCents(amount: number): number {
  return Math.round(amount * 100);
}

function toBasisPoints(percent: number): number {
  return Math.round(percent * 100);
}

/** Integer division rounded half away from zero (inputs are non-negative here). */
function roundDiv(numerator: number, denominator: number): number {
  return Math.floor((2 * numerator + denominator) / (2 * denominator));
}
