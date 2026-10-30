import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DashboardService } from '../../core/api/sales.service';
import { SettingsService } from '../../core/api/settings.service';
import { errorMessage } from '../../core/http/http-utils';
import { DashboardSummary, Insights, PaymentMethod } from '../../core/models';
import { StockBadge } from '../../shared/components/badges';
import { Icon } from '../../shared/components/icon';
import { StatCard } from '../../shared/components/stat-card';
import { EmptyState, ErrorAlert, Loading } from '../../shared/components/states';
import { AreaChart, AreaPoint } from '../../shared/charts/area-chart';
import { Bar, BarChart } from '../../shared/charts/bar-chart';
import { compactNumber, formatMoney, formatMoneyCompact, formatMoneyWhole } from '../../shared/charts/chart-utils';
import { DonutChart, DonutSlice } from '../../shared/charts/donut-chart';
import { HBarChart, HBarRow } from '../../shared/charts/hbar-chart';
import { Heatmap } from '../../shared/charts/heatmap';
import { KpiCard } from '../../shared/charts/kpi-card';
import { MoneyPipe } from '../../shared/pipes/money.pipe';
import { AssistantChat } from '../assistant/assistant-chat';

const PERIODS = [7, 30, 90] as const;
/** Fixed colour per payment method (categorical slots 1-4), so a method keeps its colour whatever its rank. */
const PAYMENT_COLORS: Record<PaymentMethod, string> = {
  Cash: 'var(--chart-1)',
  Card: 'var(--chart-2)',
  UPI: 'var(--chart-3)',
  Other: 'var(--chart-4)',
};
const WEEKDAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

const parseDate = (iso: string): Date => {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d);
};
const shortDate = new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short' });
const longDate = new Intl.DateTimeFormat(undefined, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });
const hourName = (h: number): string => `${(h % 24) % 12 === 0 ? 12 : (h % 24) % 12} ${(h % 24) < 12 ? 'am' : 'pm'}`;
const hourShort = (h: number): string => `${h % 12 === 0 ? 12 : h % 12}${h < 12 ? 'a' : 'p'}`;

@Component({
  selector: 'app-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe, RouterLink, StatCard, StockBadge, EmptyState, ErrorAlert, Loading, MoneyPipe, Icon,
    KpiCard, AreaChart, BarChart, HBarChart, DonutChart, Heatmap, AssistantChat,
  ],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.css',
})
export class Dashboard implements OnInit {
  private readonly dashboard = inject(DashboardService);
  private readonly settings = inject(SettingsService);

  protected readonly periods = PERIODS;
  protected readonly period = signal<number>(30);
  protected readonly summary = signal<DashboardSummary | null>(null);
  protected readonly insights = signal<Insights | null>(null);
  protected readonly loading = signal(true);
  protected readonly insightsLoading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly money = (v: number) => formatMoney(this.settings.currencyCode(), v);
  protected readonly moneyWhole = (v: number) => formatMoneyWhole(this.settings.currencyCode(), v);
  protected readonly moneyCompact = (v: number) => formatMoneyCompact(this.settings.currencyCode(), v);
  protected readonly moneyAxis = (v: number) => compactNumber(v);
  protected readonly count = (v: number) => Math.round(v).toLocaleString();

  protected readonly hasSales = computed(() => (this.insights()?.kpis.bills.current ?? 0) > 0);

  protected readonly dailyPoints = computed<AreaPoint[]>(() =>
    (this.insights()?.daily ?? []).map((d) => ({
      label: shortDate.format(parseDate(d.date)),
      title: longDate.format(parseDate(d.date)),
      value: d.revenue,
      previous: d.previousRevenue,
      detail: `${d.bills} ${d.bills === 1 ? 'bill' : 'bills'}`,
    })),
  );

  protected readonly revenueTrend = computed(() => (this.insights()?.daily ?? []).map((d) => d.revenue));
  protected readonly billsTrend = computed(() => (this.insights()?.daily ?? []).map((d) => d.bills));
  protected readonly averageTrend = computed(() =>
    (this.insights()?.daily ?? []).map((d) => (d.bills ? d.revenue / d.bills : 0)),
  );

  protected readonly hourlyBars = computed<Bar[]>(() => {
    const data = this.insights();
    if (!data) {
      return [];
    }
    return Array.from({ length: 24 }, (_, hour) => {
      const bills = data.heatmap.reduce((sum, row) => sum + row[hour], 0);
      return {
        label: hourShort(hour),
        value: bills,
        title: `${hourName(hour)} – ${hourName(hour + 1)}`,
        detail: `${(bills / data.days).toFixed(1)} bills a day on average`,
      };
    });
  });

  protected readonly paymentSlices = computed<DonutSlice[]>(() =>
    (this.insights()?.payments ?? []).map((p) => ({
      label: p.method,
      value: p.amount,
      color: PAYMENT_COLORS[p.method],
      detail: `${p.bills} bills · ${this.moneyWhole(p.amount)}`,
    })),
  );
  protected readonly paymentTotal = computed(() => (this.insights()?.payments ?? []).reduce((s, p) => s + p.amount, 0));

  protected readonly categoryRows = computed<HBarRow[]>(() =>
    (this.insights()?.categories ?? []).map((c) => ({ label: c.category, value: c.revenue, sub: `${c.units.toLocaleString()} units` })),
  );
  protected readonly productRows = computed<HBarRow[]>(() =>
    (this.insights()?.topProducts ?? []).map((p) => ({
      label: p.name,
      value: p.revenue,
      sub: `${p.units.toLocaleString()} units · ${this.moneyWhole(p.grossProfit)} profit`,
    })),
  );
  protected readonly customerRows = computed<HBarRow[]>(() =>
    (this.insights()?.topCustomers ?? []).map((c) => ({ label: c.name, value: c.spent, sub: `${c.bills} bills` })),
  );

  protected readonly customerShare = computed(() => {
    const d = this.insights();
    const total = (d?.billsWithCustomer ?? 0) + (d?.billsWalkIn ?? 0);
    return total === 0 ? 0 : ((d?.billsWithCustomer ?? 0) / total) * 100;
  });

  protected readonly margin = computed(() => {
    const k = this.insights()?.kpis;
    return k && k.revenue.current > 0 ? (k.grossProfit.current / k.revenue.current) * 100 : 0;
  });

  /** Plain-language takeaways computed from the same data as the charts. */
  protected readonly highlights = computed(() => {
    const d = this.insights();
    if (!d || d.kpis.bills.current === 0) {
      return [];
    }
    const best = d.daily.reduce((a, b) => (b.revenue > a.revenue ? b : a));
    let busiest = { day: 0, hour: 0, count: -1 };
    d.heatmap.forEach((row, day) => row.forEach((count, hour) => {
      if (count > busiest.count) {
        busiest = { day, hour, count };
      }
    }));
    const weekdayTotals = d.heatmap.map((row) => row.reduce((a, b) => a + b, 0));
    const strongest = weekdayTotals.indexOf(Math.max(...weekdayTotals));
    const categoryTotal = d.categories.reduce((s, c) => s + c.revenue, 0);
    const top = d.categories[0];
    return [
      { icon: 'trend' as const, title: 'Best day', text: `${longDate.format(parseDate(best.date))}`, sub: `${this.moneyWhole(best.revenue)} from ${best.bills} bills` },
      { icon: 'history' as const, title: 'Busiest slot', text: `${WEEKDAYS[busiest.day]}s, ${hourName(busiest.hour)}`, sub: `${WEEKDAYS[strongest]} is the strongest day overall` },
      { icon: 'package' as const, title: 'Leading category', text: top?.category ?? '—', sub: top && categoryTotal > 0 ? `${((top.revenue / categoryTotal) * 100).toFixed(0)}% of net sales` : '' },
      { icon: 'user' as const, title: 'Customers', text: `${this.customerShare().toFixed(0)}% of bills`, sub: 'are linked to a customer; the rest are walk-ins' },
    ];
  });

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
    this.loadInsights();
  }

  protected setPeriod(days: number): void {
    if (days !== this.period()) {
      this.period.set(days);
      this.loadInsights();
    }
  }

  private loadInsights(): void {
    this.insightsLoading.set(true);
    this.dashboard.insights(this.period()).subscribe({
      next: (i) => {
        this.insights.set(i);
        this.insightsLoading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(errorMessage(err, 'Could not load the sales insights.'));
        this.insightsLoading.set(false);
      },
    });
  }
}
