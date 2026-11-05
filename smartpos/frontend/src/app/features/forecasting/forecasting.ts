import { DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { catchError, forkJoin, of } from 'rxjs';
import { ForecastingService } from '../../core/api/forecasting.service';
import { errorMessage } from '../../core/http/http-utils';
import { ForecastModelMetrics, ForecastStatus, ReorderRecommendation, ShopForecast, StockoutAlert, StockoutRisk } from '../../core/models';
import { ToastService } from '../../core/ui/toast.service';
import { AreaChart, AreaPoint } from '../../shared/charts/area-chart';
import { HBarChart, HBarRow } from '../../shared/charts/hbar-chart';
import { Icon } from '../../shared/components/icon';
import { StatCard } from '../../shared/components/stat-card';
import { EmptyState, ErrorAlert, Loading } from '../../shared/components/states';
import { categoriesOf, categoryTotals, dailyTotals, demandOn, filterProducts, matches, RISK_BADGE, riskCounts, units } from './forecast-view';

export type ForecastTab = 'demand' | 'products' | 'alerts' | 'orders';
const TABS: readonly ForecastTab[] = ['demand', 'products', 'alerts', 'orders'];

const dayLabel = new Intl.DateTimeFormat(undefined, { weekday: 'short', day: 'numeric' });
const dayTitle = new Intl.DateTimeFormat(undefined, { weekday: 'long', day: 'numeric', month: 'long' });
const parse = (iso: string) => new Date(`${iso}T00:00:00`);

/** Forecasting (beta) for the signed-in owner's shop: learned from the shop's own sales and stock. */
@Component({
  selector: 'app-forecasting',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe, PercentPipe, Icon, StatCard, EmptyState, ErrorAlert, Loading, AreaChart, HBarChart],
  templateUrl: './forecasting.html',
  styleUrl: './forecasting.css',
})
export class Forecasting implements OnInit {
  private readonly api = inject(ForecastingService);
  private readonly toast = inject(ToastService);

  protected readonly units = units;
  protected readonly riskBadge = RISK_BADGE;
  protected readonly unitFormat = (v: number) => `${units(v)} units`;

  // ---- view: one tabbed card instead of a long page; the filters apply to every tab
  protected readonly tabs = TABS;
  protected readonly tab = signal<ForecastTab>('demand');
  protected readonly category = signal<string | null>(null);
  protected readonly productSearch = signal('');
  protected readonly day = signal<string | null>(null);
  protected readonly alertRisk = signal<StockoutRisk | 'ALL'>('ALL');

  // ---- data
  protected readonly status = signal<ForecastStatus | null>(null);
  protected readonly forecast = signal<ShopForecast | null>(null);
  protected readonly alerts = signal<StockoutAlert[]>([]);
  protected readonly riskNote = signal('');
  protected readonly reorders = signal<ReorderRecommendation[]>([]);
  protected readonly metrics = signal<ForecastModelMetrics | null>(null);
  protected readonly loading = signal(true);
  protected readonly generating = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly run = computed(() => this.forecast()?.run ?? null);
  protected readonly hasForecast = computed(() => this.status()?.latest_run != null);
  protected readonly allItems = computed(() => this.forecast()?.items ?? []);
  protected readonly categories = computed(() => categoriesOf(this.allItems()));
  protected readonly days = computed(() => (this.forecast()?.daily_totals ?? []).map((d) => d.forecast_date));

  protected readonly items = computed(() => filterProducts(this.allItems(), this.productSearch(), this.category(), this.day()));
  protected readonly risk = computed(() => riskCounts(this.allItems()));
  protected readonly demand = computed(() => this.items().reduce((sum, i) => sum + demandOn(i, this.day()), 0));
  protected readonly toReorder = computed(() => this.allItems().filter((i) => i.recommended_reorder_quantity > 0).length);

  protected readonly demandPoints = computed<AreaPoint[]>(() => dailyTotals(this.items()).map((d) => ({
    label: dayLabel.format(parse(d.forecast_date)), title: dayTitle.format(parse(d.forecast_date)), value: d.predicted_demand,
    detail: this.day() === d.forecast_date ? 'selected day' : undefined,
  })));
  protected readonly categoryRows = computed<HBarRow[]>(() => categoryTotals(this.items(), this.day())
    .map((c) => ({ label: c.category, value: c.demand, sub: `${c.products} products` })));

  protected readonly matchingAlerts = computed(() => this.alerts().filter((a) => matches(a, this.productSearch(), this.category())));
  protected readonly visibleAlerts = computed(() => this.matchingAlerts()
    .filter((a) => (this.alertRisk() === 'ALL' ? a.stockout_risk !== 'LOW' : a.stockout_risk === this.alertRisk())));
  protected readonly visibleReorders = computed(() => this.reorders().filter((r) => matches(r, this.productSearch(), this.category())));
  protected readonly tabCounts = computed<Record<ForecastTab, number | null>>(() => ({
    demand: null,
    products: this.items().length,
    alerts: this.matchingAlerts().filter((a) => a.stockout_risk !== 'LOW').length,
    orders: this.visibleReorders().length,
  }));
  protected readonly filtered = computed(() => this.category() !== null || this.productSearch().trim() !== '' || this.day() !== null);

  /** Test-period error of the model and the moving average, overall and on this shop only. */
  protected readonly accuracy = computed(() => {
    const rows = (this.metrics()?.metrics ?? []).filter((m) => m.target === 'units_sold' && m.evaluation === 'test');
    const of = (scope: 'overall' | 'shop', method: string) => rows.find((m) => m.scope === scope && m.method === method) ?? null;
    return {
      shop: of('shop', 'lightgbm'), shopBaseline: of('shop', 'moving_average_7'),
      overall: of('overall', 'lightgbm'), overallBaseline: of('overall', 'moving_average_7'),
    };
  });

  /** 1 - WAPE on this shop's test days (clamped at 0), the usual retail "forecast accuracy" figure. */
  protected readonly accuracyScore = computed(() => {
    const score = (wape: number | null | undefined) => (wape == null ? null : Math.max(0, 1 - wape));
    return { model: score(this.accuracy().shop?.wape), baseline: score(this.accuracy().shopBaseline?.wape) };
  });

  ngOnInit(): void {
    this.loadAll();
  }

  protected loadAll(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.status().subscribe({
      next: (status) => {
        this.status.set(status);
        if (status.latest_run) {
          this.loadForecast();
        } else {
          this.forecast.set(null);
          this.loading.set(false);
        }
      },
      error: (err: unknown) => {
        this.error.set(errorMessage(err, 'Could not load the forecasts.'));
        this.loading.set(false);
      },
    });
  }

  protected selectDay(value: string): void {
    this.day.set(value || null);
  }

  protected clearFilters(): void {
    this.category.set(null);
    this.productSearch.set('');
    this.day.set(null);
  }

  /** Arrow keys, Home and End move between tabs (WAI-ARIA tabs pattern). */
  protected onTabKey(event: KeyboardEvent): void {
    const i = TABS.indexOf(this.tab());
    const next = { ArrowRight: i + 1, ArrowLeft: i - 1, Home: 0, End: TABS.length - 1 }[event.key];
    if (next === undefined) {
      return;
    }
    event.preventDefault();
    const tab = TABS[(next + TABS.length) % TABS.length];
    this.tab.set(tab);
    (event.currentTarget as HTMLElement).querySelector<HTMLElement>(`#tab-${tab}`)?.focus();
  }

  protected generate(): void {
    this.generating.set(true);
    this.api.generate().subscribe({
      next: () => {
        this.generating.set(false);
        this.toast.success('Forecast updated from your latest sales.');
        this.loadAll();
      },
      error: (err: unknown) => {
        this.generating.set(false);
        this.toast.error(errorMessage(err, 'Could not update the forecast.'));
      },
    });
  }

  private loadForecast(): void {
    forkJoin({
      forecast: this.api.forecast({ horizon: 7 }),
      alerts: this.api.stockoutAlerts(),
      reorders: this.api.replenishment(),
      // Accuracy is extra information: the page works without it.
      metrics: this.api.metrics().pipe(catchError(() => of(null))),
    }).subscribe({
      next: ({ forecast, alerts, reorders, metrics }) => {
        this.forecast.set(forecast);
        this.alerts.set(alerts.items);
        this.riskNote.set(alerts.risk_note);
        this.reorders.set(reorders.items);
        this.metrics.set(metrics);
        if (this.category() !== null && !categoriesOf(forecast.items).includes(this.category()!)) {
          this.category.set(null);
        }
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(errorMessage(err, 'Could not load the forecasts.'));
        this.loading.set(false);
      },
    });
  }

  protected dayValue(item: ShopForecast['items'][number], date: string): number {
    return demandOn(item, date);
  }
}
