import { describe, expect, it } from 'vitest';
import { PHONE_PATTERN } from './customer-list';

// Same cases as PhoneRulesTests on the server, so the form and the API agree.
describe('PHONE_PATTERN (Indian mobile numbers)', () => {
  it.each(['9876543210', '6000000000', '98765 43210', '98765-43210', '+919876543210', '+91 98765 43210', '+91-98765-43210',
    '919876543210', '09876543210', '9198765432'])('accepts %s', (phone) => {
    expect(PHONE_PATTERN.test(phone)).toBe(true);
  });

  it.each(['1234567890', '5876543210', '0876543210', '987654321', '98765432101', '+1 415 555 0101', '+92 98765 43210',
    '044 2434 5678', '98765  43210', '(98765) 43210', '९८७६५४३२१०', ''])('refuses %s', (phone) => {
    expect(PHONE_PATTERN.test(phone)).toBe(false);
  });
});
