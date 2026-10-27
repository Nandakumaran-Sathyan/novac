import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { ReportsService } from '../../core/api/sales.service';
import { addDays, errorMessage, localIsoDate } from '../../core/http/http-utils';
import { DailySalesReport, TopProductRow } from '../../core/models';
import { StatCard } from '../../shared/components/stat-card';
import { EmptyState, ErrorAlert, Loading } from '../../shared/components/states';
import { MoneyPipe } from '../../shared/pipes/money.pipe';

@Component({
  selector: 'app-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, DatePipe, MoneyPipe, StatCard, EmptyState, ErrorAlert, Loading],
  templateUrl: './reports.html',
  styleUrl: './reports.css',
})
export class Reports implements OnInit {
  private readonly reports = inject(ReportsService);

  protected readonly range = inject(NonNullableFormBuilder).group({
    from: [addDays(localIsoDate(), -6)],
    to: [localIsoDate()],
  });
  protected readonly daily = signal<DailySalesReport | null>(null);
  protected readonly topProducts = signal<TopProductRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  /** Largest daily total, for scaling the inline bars. */
  protected readonly maxDayTotal = computed(() => Math.max(0, ...(this.daily()?.days.map((d) => d.total) ?? [])));

  ngOnInit(): void {
    this.load();
  }

  protected preset(days: number): void {
    const today = localIsoDate();
    this.range.setValue({ from: addDays(today, -(days - 1)), to: today });
    this.load();
  }

  protected load(): void {
    const { from, to } = this.range.getRawValue();
    if (!from || !to || from > to) {
      this.error.set('Choose a valid date range (“From” on or before “To”).');
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    forkJoin({ daily: this.reports.dailySales(from, to), top: this.reports.topProducts(from, to, 10) }).subscribe({
      next: ({ daily, top }) => {
        this.daily.set(daily);
        this.topProducts.set(top);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(errorMessage(err, 'Could not load reports.'));
        this.loading.set(false);
      },
    });
  }

  protected barWidth(total: number): string {
    const max = this.maxDayTotal();
    return max > 0 ? `${Math.max((total / max) * 100, total > 0 ? 2 : 0)}%` : '0%';
  }
}
