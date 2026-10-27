import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SalesService } from '../../core/api/sales.service';
import { SettingsService } from '../../core/api/settings.service';
import { errorMessage } from '../../core/http/http-utils';
import { Sale } from '../../core/models';
import { Icon } from '../../shared/components/icon';
import { ErrorAlert, Loading } from '../../shared/components/states';
import { MoneyPipe } from '../../shared/pipes/money.pipe';

/** Itemised invoice. The print stylesheet hides the app chrome so only the invoice prints. */
@Component({
  selector: 'app-sale-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, MoneyPipe, Icon, ErrorAlert, Loading],
  templateUrl: './sale-detail.html',
  styleUrl: './sale-detail.css',
})
export class SaleDetail implements OnInit {
  readonly id = input.required<string>();
  /** ?print=1 opens the print dialog once the invoice has rendered (used after checkout). */
  readonly print = input<string>();

  private readonly sales = inject(SalesService);
  protected readonly settings = inject(SettingsService);
  protected readonly sale = signal<Sale | null>(null);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.error.set(null);
    this.sales.get(Number(this.id())).subscribe({
      next: (sale) => {
        this.sale.set(sale);
        if (this.print() === '1') {
          setTimeout(() => window.print(), 100);
        }
      },
      error: (err: unknown) => this.error.set(errorMessage(err, 'Could not load this sale.')),
    });
  }

  protected printInvoice(): void {
    window.print();
  }
}
