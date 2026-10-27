import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DashboardService } from '../../core/api/sales.service';
import { errorMessage } from '../../core/http/http-utils';
import { DashboardSummary } from '../../core/models';
import { StockBadge } from '../../shared/components/badges';
import { Icon } from '../../shared/components/icon';
import { StatCard } from '../../shared/components/stat-card';
import { EmptyState, ErrorAlert, Loading } from '../../shared/components/states';
import { MoneyPipe } from '../../shared/pipes/money.pipe';

@Component({
  selector: 'app-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, StatCard, StockBadge, EmptyState, ErrorAlert, Loading, MoneyPipe, Icon],
  templateUrl: './dashboard.html',
})
export class Dashboard implements OnInit {
  private readonly dashboard = inject(DashboardService);

  protected readonly summary = signal<DashboardSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.dashboard.summary().subscribe({
      next: (s) => {
        this.summary.set(s);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(errorMessage(err, 'Could not load the dashboard.'));
        this.loading.set(false);
      },
    });
  }
}
