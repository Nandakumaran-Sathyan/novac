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
  /** Value of the loyalty points used, taken off after the discount. */
  pointsAmount: number;
  total: number;
}

/**
 * Preview of SaleCalculator on the server (SmartPOS.Application/Sales/SaleCalculator.cs).
 * Works in integer cents and basis points so results match the server's decimal arithmetic
 * exactly, including half-away-from-zero rounding. The server's figures are still authoritative.
 */
export function calculateTotals(lines: readonly CartLine[], discountPercent: number, pointsAmount = 0): CartTotals {
  const subtotalCents = lines.reduce((sum, line) => sum + toCents(line.unitPrice) * line.quantity, 0);
  const discountCents = roundDiv(subtotalCents * toBasisPoints(discountPercent), 10_000);
  const pointsCents = Math.min(Math.max(toCents(pointsAmount), 0), subtotalCents - discountCents);
  return {
    subtotal: subtotalCents / 100,
    discountAmount: discountCents / 100,
    pointsAmount: pointsCents / 100,
    total: (subtotalCents - discountCents - pointsCents) / 100,
  };
}

/** Same as the server's LoyaltyRules.RedemptionValue: points × point value, in whole cents. */
export function pointsValue(points: number, pointValue: number): number {
  return (Math.trunc(points) * toCents(pointValue)) / 100;
}

/** Same as LoyaltyRules.MaxRedeemable: limited by the balance and by the amount still due, in whole points. */
export function maxRedeemablePoints(balance: number, amountDue: number, pointValue: number): number {
  const valueCents = toCents(pointValue);
  if (balance <= 0 || valueCents <= 0) {
    return 0;
  }
  return Math.max(0, Math.min(Math.trunc(balance), Math.floor(toCents(amountDue) / valueCents)));
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
      unitPrice: product.price,
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
