import { effect, inject, Injectable, signal, untracked } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { Customer, PaymentMethod } from '../../core/models';
import { CartLine } from './cart';

const STORAGE_KEY = 'smartpos.posCart';
const PAYMENT_METHODS: readonly PaymentMethod[] = ['Cash', 'Card', 'UPI', 'Other'];

interface SavedCart {
  userId: number;
  lines: CartLine[];
  discountPercent: number;
  paymentMethod: PaymentMethod;
  amountReceived: number | null;
  customer: Customer | null;
  pointsToRedeem: number;
}

/**
 * The bill being rung up. Kept app-wide, not in the POS page, so leaving the till (say, to add a customer) and
 * coming back keeps it, and saved in localStorage so a reload or a closed browser keeps it too. The saved copy
 * is tagged with the signed-in user and only ever restored for that user. It is emptied (and the saved copy
 * removed) when a sale completes, when the cashier clears it, or when the user signs out or changes.
 * Prices in a restored cart may be out of date; the POS page refreshes them, and the server prices every sale.
 */
@Injectable({ providedIn: 'root' })
export class PosCart {
  private readonly auth = inject(AuthService);

  readonly lines = signal<CartLine[]>([]);
  readonly discountPercent = signal(0);
  readonly paymentMethod = signal<PaymentMethod>('Cash');
  /** Cash handed over by the customer; null means "exact amount". Only used for Cash. */
  readonly amountReceived = signal<number | null>(null);
  readonly customer = signal<Customer | null>(null);
  /** Loyalty points the customer wants to spend on this bill. */
  readonly pointsToRedeem = signal(0);

  /** The user this cart belongs to. */
  private owner: number | null;

  constructor() {
    // Restore before any effect runs, so the save effect below can't overwrite the saved cart with an empty one.
    this.owner = this.auth.user()?.id ?? null;
    this.restore(this.owner);

    effect(() => {
      const userId = this.auth.user()?.id ?? null;
      untracked(() => {
        if (userId !== this.owner) {
          this.owner = userId;
          this.clear();
          this.restore(userId); // only finds something if this user's own cart was saved
        }
      });
    });

    effect(() => {
      const cart: SavedCart = {
        userId: this.owner ?? 0,
        lines: this.lines(),
        discountPercent: this.discountPercent(),
        paymentMethod: this.paymentMethod(),
        amountReceived: this.amountReceived(),
        customer: this.customer(),
        pointsToRedeem: this.pointsToRedeem(),
      };
      untracked(() => this.save(cart));
    });
  }

  clear(): void {
    this.lines.set([]);
    this.discountPercent.set(0);
    this.paymentMethod.set('Cash');
    this.amountReceived.set(null);
    this.customer.set(null);
    this.pointsToRedeem.set(0);
  }

  private save(cart: SavedCart): void {
    const empty = cart.lines.length === 0 && cart.customer === null && cart.discountPercent === 0;
    try {
      if (this.owner === null || empty) {
        localStorage.removeItem(STORAGE_KEY);
      } else {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(cart));
      }
    } catch {
      // Storage unavailable (private mode, full): the cart still works until the page is reloaded.
    }
  }

  private restore(userId: number | null): void {
    if (userId === null) {
      return;
    }
    let saved: Partial<SavedCart> | null = null;
    try {
      saved = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null') as Partial<SavedCart> | null;
    } catch {
      return;
    }
    if (!saved || saved.userId !== userId || !Array.isArray(saved.lines)) {
      return; // nothing saved, or another user's cart: never shown
    }
    this.lines.set(saved.lines.filter((l) => Number.isInteger(l?.productId) && Number.isInteger(l?.quantity) && l.quantity > 0));
    this.discountPercent.set(typeof saved.discountPercent === 'number' ? saved.discountPercent : 0);
    this.paymentMethod.set(PAYMENT_METHODS.includes(saved.paymentMethod as PaymentMethod) ? saved.paymentMethod! : 'Cash');
    this.amountReceived.set(typeof saved.amountReceived === 'number' ? saved.amountReceived : null);
    this.customer.set(saved.customer && Number.isInteger(saved.customer.id) ? saved.customer : null);
    this.pointsToRedeem.set(Number.isInteger(saved.pointsToRedeem) ? saved.pointsToRedeem! : 0);
  }
}
