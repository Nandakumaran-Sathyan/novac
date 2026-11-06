import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AuthService } from '../../core/auth/auth.service';
import { User } from '../../core/models';
import { CartLine } from './cart';
import { PosCart } from './pos-cart';

const KEY = 'smartpos.posCart';
const user = (id: number) => ({ id, emailVerified: true }) as User;
const line: CartLine = { productId: 7, sku: 'RICE-5KG', name: 'Rice 5 kg', unitPrice: 320, quantity: 2, available: 40 };

describe('PosCart', () => {
  let current: ReturnType<typeof signal<User | null>>;

  function start(): PosCart {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [{ provide: AuthService, useValue: { user: current } }] });
    const cart = TestBed.inject(PosCart);
    TestBed.tick();
    return cart;
  }

  beforeEach(() => {
    localStorage.clear();
    current = signal<User | null>(user(1));
  });
  afterEach(() => localStorage.clear());

  it('survives a reload for the same user', () => {
    const cart = start();
    cart.lines.set([line]);
    cart.discountPercent.set(5);
    cart.paymentMethod.set('UPI');
    TestBed.tick();

    const reloaded = start();
    expect(reloaded.lines()).toEqual([line]);
    expect(reloaded.discountPercent()).toBe(5);
    expect(reloaded.paymentMethod()).toBe('UPI');
  });

  it('is never shown to a different user', () => {
    const cart = start();
    cart.lines.set([line]);
    TestBed.tick();

    current.set(user(2));
    const other = start();
    expect(other.lines()).toEqual([]);
  });

  it('clearing removes the saved copy', () => {
    const cart = start();
    cart.lines.set([line]);
    TestBed.tick();
    expect(localStorage.getItem(KEY)).not.toBeNull();

    cart.clear();
    TestBed.tick();
    expect(localStorage.getItem(KEY)).toBeNull();
    expect(start().lines()).toEqual([]);
  });

  it('signing out empties the cart and the saved copy', () => {
    const cart = start();
    cart.lines.set([line]);
    TestBed.tick();

    current.set(null);
    TestBed.tick();
    expect(cart.lines()).toEqual([]);
    expect(localStorage.getItem(KEY)).toBeNull();
  });

  it('ignores a damaged saved copy', () => {
    localStorage.setItem(KEY, '{not json');
    expect(start().lines()).toEqual([]);
    localStorage.setItem(KEY, JSON.stringify({ userId: 1, lines: [{ productId: 'x', quantity: -1 }] }));
    expect(start().lines()).toEqual([]);
  });
});
