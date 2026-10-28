import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export interface HBarRow {
  label: string;
  value: number;
  /** Secondary text under the label, e.g. "366 units". */
  sub?: string;
}

/** Ranked horizontal bars with the value written at the end of each bar (direct labels, so no axis is needed). */
@Component({
  selector: 'app-hbar-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display:block' },
  template: `
    <ol [attr.aria-label]="ariaLabel()">
      @for (row of rows(); track row.label; let i = $index) {
        <li [title]="row.label + ': ' + valueFormat()(row.value)">
          <div class="text">
            <span class="label">{{ row.label }}</span>
            @if (row.sub) { <span class="sub">{{ row.sub }}</span> }
          </div>
          <div class="track"><div class="fill" [style.width.%]="percent(row.value)" [style.animation-delay.ms]="i * 70"></div></div>
          <span class="value">{{ valueFormat()(row.value) }}</span>
        </li>
      }
    </ol>
  `,
  styles: `
    ol { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
    li { display: grid; grid-template-columns: minmax(80px, 30%) 1fr auto; align-items: center; gap: 12px; }
    .text { display: flex; flex-direction: column; min-width: 0; line-height: 1.25; }
    .label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 500; }
    .sub { color: var(--color-text-muted); font-size: 0.75rem; }
    .track { height: 10px; background: var(--color-surface-muted); border-radius: 0 5px 5px 0; overflow: hidden; }
    .fill { height: 100%; background: var(--chart-1); border-radius: 0 5px 5px 0; transform-origin: left; animation: chart-grow-x 0.8s cubic-bezier(0.2, 0.8, 0.2, 1) both; }
    .value { font-variant-numeric: tabular-nums; font-weight: 600; min-width: 4.5em; text-align: right; }
    @media (max-width: 520px) { li { grid-template-columns: 1fr auto; } .track { grid-column: 1 / -1; grid-row: 2; } }
  `,
})
export class HBarChart {
  readonly rows = input.required<HBarRow[]>();
  readonly valueFormat = input<(value: number) => string>((v) => String(v));
  readonly ariaLabel = input('Ranking');

  private readonly max = computed(() => Math.max(1, ...this.rows().map((r) => r.value)));
  protected percent(value: number): number {
    return (value / this.max()) * 100;
  }
}
