import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { SalesService } from '../../core/api/sales.service';
import { AuthService } from '../../core/auth/auth.service';
import { addDays, errorMessage, localIsoDate } from '../../core/http/http-utils';
import { PagedResult, SaleSummary } from '../../core/models';
import { Pagination } from '../../shared/components/pagination';
import { EmptyState, ErrorAlert, Loading } from '../../shared/components/states';
import { MoneyPipe } from '../../shared/pipes/money.pipe';

@Component({
  selector: 'app-sales-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, DatePipe, MoneyPipe, Pagination, EmptyState, ErrorAlert, Loading],
  templateUrl: './sales-list.html',
})
export class SalesList implements OnInit {
  private readonly sales = inject(SalesService);
  protected readonly auth = inject(AuthService);

  protected readonly filters = inject(NonNullableFormBuilder).group({ from: [''], to: [''], search: [''] });
  protected readonly result = signal<PagedResult<SaleSummary> | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly rangeError = signal<string | null>(null);
  private page = 1;

  ngOnInit(): void {
    this.load();
  }

  protected preset(days: number): void {
    const today = localIsoDate();
    this.filters.patchValue({ from: addDays(today, -(days - 1)), to: today });
    this.apply();
  }

  protected apply(): void {
    const { from, to } = this.filters.getRawValue();
    if (from && to && from > to) {
      this.rangeError.set('“From” must be on or before “To”.');
      return;
    }
    this.rangeError.set(null);
    this.page = 1;
    this.load();
  }

  protected clear(): void {
    this.filters.reset({ from: '', to: '', search: '' });
    this.apply();
  }

  protected goToPage(page: number): void {
    this.page = page;
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    const { from, to, search } = this.filters.getRawValue();
    this.sales.list({ from, to, search: search.trim(), page: this.page, pageSize: 20 }).subscribe({
      next: (r) => {
        this.result.set(r);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(errorMessage(err, 'Could not load sales.'));
        this.loading.set(false);
      },
    });
  }
}
