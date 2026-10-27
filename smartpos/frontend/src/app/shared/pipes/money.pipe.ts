import { inject, Pipe, PipeTransform } from '@angular/core';
import { SettingsService } from '../../core/api/settings.service';

const formatters = new Map<string, Intl.NumberFormat>();

/** Formats an amount in the store currency from /api/settings/pos, e.g. 1234.5 → "₹1,234.50". */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  private readonly settings = inject(SettingsService);

  transform(value: number | null | undefined): string {
    if (value === null || value === undefined) {
      return '';
    }
    const currency = this.settings.currencyCode();
    let formatter = formatters.get(currency);
    if (!formatter) {
      formatter = new Intl.NumberFormat(undefined, { style: 'currency', currency });
      formatters.set(currency, formatter);
    }
    return formatter.format(value);
  }
}
