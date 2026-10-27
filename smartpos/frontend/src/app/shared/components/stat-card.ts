import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Icon, IconName } from './icon';

@Component({
  selector: 'app-stat-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <div class="card stat" [class.stat-warning]="tone() === 'warning'">
      <div class="stat-top">
        <span class="stat-label">{{ label() }}</span>
        <span class="stat-icon"><app-icon [name]="icon()" /></span>
      </div>
      <div class="stat-value">{{ value() }}</div>
      @if (hint()) {
        <div class="stat-hint">{{ hint() }}</div>
      }
    </div>
  `,
  styles: `
    .stat { padding: 16px 18px; display: flex; flex-direction: column; gap: 6px; height: 100%; }
    .stat-top { display: flex; justify-content: space-between; align-items: center; }
    .stat-label { color: var(--color-text-muted); font-weight: 500; }
    .stat-icon { display: inline-flex; padding: 7px; border-radius: var(--radius); background: var(--color-primary-soft); color: var(--color-primary-text); }
    .stat-warning .stat-icon { background: var(--color-warning-soft); color: var(--color-warning-text); }
    .stat-value { font-size: 1.625rem; font-weight: 600; font-variant-numeric: tabular-nums; }
    .stat-hint { color: var(--color-text-muted); font-size: 0.8125rem; }
    @media (max-width: 640px) { .stat { padding: 12px; } .stat-value { font-size: 1.25rem; } .stat-icon { display: none; } }
  `,
})
export class StatCard {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly icon = input<IconName>('chart');
  readonly hint = input('');
  readonly tone = input<'default' | 'warning'>('default');
}
