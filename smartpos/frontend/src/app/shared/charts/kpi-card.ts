import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Icon, IconName } from '../components/icon';
import { percentChange } from './chart-utils';
import { CountUp } from './count-up';
import { Sparkline } from './sparkline';

/** A headline figure that counts up, with its change against the previous period and an optional trend line. */
@Component({
  selector: 'app-kpi-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CountUp, Sparkline, Icon],
  template: `
    <div class="card kpi">
      <div class="top">
        <span class="label">{{ label() }}</span>
        <span class="icon"><app-icon [name]="icon()" [size]="16" /></span>
      </div>
      <div class="value"><app-count-up [value]="current()" [format]="format()" /></div>
      <div class="delta" [class.up]="direction() === 'up'" [class.down]="direction() === 'down'">
        <span class="chip">
          @if (direction() === 'up') { <span aria-hidden="true">▲</span> }
          @else if (direction() === 'down') { <span aria-hidden="true">▼</span> }
          {{ deltaText() }}
        </span>
        <span class="vs">{{ comparison() }}</span>
      </div>
      @if (trend().length > 1) {
        <app-sparkline [values]="trend()" />
      } @else if (hint()) {
        <div class="hint">{{ hint() }}</div>
      }
    </div>
  `,
  styles: `
    .kpi { padding: 14px 16px 10px; display: flex; flex-direction: column; gap: 6px; height: 100%; animation: chart-rise 0.5s cubic-bezier(0.2, 0.8, 0.2, 1) both; }
    .top { display: flex; justify-content: space-between; align-items: center; }
    .label { color: var(--color-text-muted); font-weight: 500; }
    .icon { display: inline-flex; padding: 6px; border-radius: var(--radius); background: var(--color-primary-soft); color: var(--color-primary-text); }
    .value { font-size: 1.625rem; font-weight: 650; font-variant-numeric: tabular-nums; line-height: 1.15; }
    .delta { display: flex; align-items: center; gap: 8px; font-size: 0.8125rem; flex-wrap: wrap; }
    .chip { display: inline-flex; align-items: center; gap: 4px; padding: 1px 8px; border-radius: 999px; font-weight: 600; background: var(--color-surface-muted); color: var(--color-text-muted); }
    .up .chip { background: var(--color-primary-soft); color: var(--color-primary-text); }
    .down .chip { background: var(--color-danger-soft); color: var(--color-danger-text); }
    .chip span { font-size: 0.6rem; }
    .vs { color: var(--color-text-muted); }
    .hint { color: var(--color-text-muted); font-size: 0.8125rem; min-height: 36px; }
    app-sparkline { margin-top: 2px; }
    @media (max-width: 640px) { .kpi { padding: 12px; } .value { font-size: 1.25rem; } .icon { display: none; } .vs { display: none; } }
  `,
})
export class KpiCard {
  readonly label = input.required<string>();
  readonly icon = input<IconName>('chart');
  readonly current = input.required<number>();
  readonly previous = input.required<number>();
  readonly format = input<(value: number) => string>((v) => String(Math.round(v)));
  readonly trend = input<number[]>([]);
  readonly hint = input('');
  readonly comparison = input('vs previous period');

  private readonly change = computed(() => percentChange(this.current(), this.previous()));
  protected readonly direction = computed<'up' | 'down' | 'flat'>(() => {
    const c = this.change();
    return c === null || Math.abs(c) < 0.05 ? 'flat' : c > 0 ? 'up' : 'down';
  });
  protected readonly deltaText = computed(() => {
    const c = this.change();
    if (c === null) {
      return 'New';
    }
    return Math.abs(c) < 0.05 ? 'No change' : `${c > 0 ? '+' : '−'}${Math.abs(c).toFixed(1)}%`;
  });
}
