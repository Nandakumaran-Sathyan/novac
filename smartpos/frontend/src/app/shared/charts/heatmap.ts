import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

const DAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const RAMP = ['var(--chart-ramp-100)', 'var(--chart-ramp-200)', 'var(--chart-ramp-300)', 'var(--chart-ramp-400)', 'var(--chart-ramp-500)', 'var(--chart-ramp-600)', 'var(--chart-ramp-700)'];

/** Weekday × hour grid, one hue from light (quiet) to dark (busy). Cells fade in on a diagonal wave. */
@Component({
  selector: 'app-heatmap',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display:block' },
  template: `
    <div class="grid" role="img" [attr.aria-label]="ariaLabel()">
      <span></span>
      @for (h of hours; track h) {
        <span class="hour">{{ h % 3 === 0 ? hourLabel(h) : '' }}</span>
      }
      @for (row of cells(); track row.day) {
        <span class="day">{{ row.day }}</span>
        @for (c of row.cells; track c.hour) {
          <span class="cell" [class.empty]="c.count === 0" [style.background]="c.color" [style.animation-delay.ms]="(c.hour + row.index * 2) * 14"
                [class.active]="active() === row.index * 24 + c.hour"
                (pointerenter)="active.set(row.index * 24 + c.hour)" (pointerleave)="active.set(null)"></span>
        }
      }
    </div>
    <div class="footer">
      <span class="readout" aria-live="polite">{{ readout() }}</span>
      <span class="scale"><span>Quiet</span>
        @for (c of ramp; track c) { <i [style.background]="c"></i> }
        <span>Busy</span></span>
    </div>
  `,
  styles: `
    .grid { display: grid; grid-template-columns: 34px repeat(24, minmax(0, 1fr)); gap: 3px; align-items: center; }
    .hour { font-size: 10.5px; color: var(--color-text-muted); text-align: left; white-space: nowrap; overflow: visible; }
    .day { font-size: 11px; color: var(--color-text-muted); }
    .cell { aspect-ratio: 1; min-height: 12px; border-radius: 3px; animation: chart-pop 0.35s ease-out both; transition: transform 0.12s; }
    .cell.empty { background: var(--color-surface-muted) !important; box-shadow: inset 0 0 0 1px var(--chart-grid); }
    .cell.active { transform: scale(1.35); box-shadow: 0 0 0 2px var(--color-surface), 0 0 0 3px var(--color-text); position: relative; z-index: 1; }
    .footer { display: flex; justify-content: space-between; align-items: center; gap: 12px; margin-top: 10px; flex-wrap: wrap; min-height: 22px; }
    .readout { font-size: 0.8125rem; font-weight: 500; }
    .scale { display: inline-flex; align-items: center; gap: 3px; font-size: 0.75rem; color: var(--color-text-muted); }
    .scale i { width: 16px; height: 10px; border-radius: 2px; }
    .scale span:first-child { margin-right: 4px; } .scale span:last-child { margin-left: 4px; }
  `,
})
export class Heatmap {
  /** 7 rows (Monday first) × 24 hourly counts. */
  readonly data = input.required<number[][]>();
  readonly unit = input('bills');
  readonly ariaLabel = input('Busiest times by weekday and hour');

  protected readonly hours = Array.from({ length: 24 }, (_, i) => i);
  protected readonly ramp = RAMP;
  protected readonly active = signal<number | null>(null);

  private readonly max = computed(() => Math.max(1, ...this.data().flat()));

  protected readonly cells = computed(() =>
    this.data().map((counts, index) => ({
      day: DAYS[index], index,
      cells: counts.map((count, hour) => ({
        hour, count,
        color: RAMP[Math.min(RAMP.length - 1, Math.floor((count / this.max()) * RAMP.length))],
      })),
    })),
  );

  protected readonly readout = computed(() => {
    const a = this.active();
    if (a === null) {
      return 'Hover a cell for the count';
    }
    const day = Math.floor(a / 24), hour = a % 24;
    return `${DAYS[day]} ${this.hourLabel(hour)}–${this.hourLabel(hour + 1)} · ${this.data()[day][hour]} ${this.unit()}`;
  });

  protected hourLabel(hour: number): string {
    const h = hour % 24;
    return `${h % 12 === 0 ? 12 : h % 12}${h < 12 ? 'am' : 'pm'}`;
  }
}
