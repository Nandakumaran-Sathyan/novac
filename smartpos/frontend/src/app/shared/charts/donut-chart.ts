import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

export interface DonutSlice {
  label: string;
  value: number;
  color: string;
  /** Secondary text for the legend, e.g. "384 bills". */
  detail?: string;
}

/** Share-of-total donut. Slices sweep in clockwise; hovering a slice or legend row shows its share in the centre. */
@Component({
  selector: 'app-donut-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display:block' },
  template: `
    <div class="layout">
      <svg viewBox="0 0 120 120" role="img" [attr.aria-label]="ariaLabel()">
        <g transform="rotate(-90 60 60)">
          <circle class="ring" cx="60" cy="60" [attr.r]="r" />
          @for (s of arcs(); track s.label) {
            <circle class="slice" cx="60" cy="60" [attr.r]="r" [attr.stroke]="s.color" [class.dim]="active() !== null && active() !== s.label"
                    [style.--len]="s.len" [style.--rest]="circumference - s.len" [style.--circ]="circumference"
                    [style.stroke-dashoffset]="-s.offset" [style.animation-delay.ms]="s.delay"
                    (pointerenter)="active.set(s.label)" (pointerleave)="active.set(null)" />
          }
        </g>
        <text x="60" y="56" text-anchor="middle" class="center-big">{{ centerBig() }}</text>
        <text x="60" y="71" text-anchor="middle" class="center-small">{{ centerSmall() }}</text>
      </svg>
      <ul>
        @for (s of arcs(); track s.label) {
          <li [class.dim]="active() !== null && active() !== s.label" (pointerenter)="active.set(s.label)" (pointerleave)="active.set(null)">
            <span class="dot" [style.background]="s.color"></span>
            <span class="name">{{ s.label }}</span>
            <span class="pct">{{ s.percentText }}</span>
            @if (s.detail) { <span class="detail">{{ s.detail }}</span> }
          </li>
        }
      </ul>
    </div>
  `,
  styles: `
    .layout { display: flex; align-items: center; gap: 20px; flex-wrap: wrap; }
    svg { width: 150px; height: 150px; flex: none; overflow: visible; }
    .ring { fill: none; stroke: var(--color-surface-muted); stroke-width: 16; }
    .slice { fill: none; stroke-width: 16; animation: chart-sweep 0.9s cubic-bezier(0.2, 0.8, 0.2, 1) both; transition: opacity 0.15s; stroke-dasharray: var(--len) var(--rest); }
    .slice.dim { opacity: 0.35; }
    .center-big { font-size: 14px; font-weight: 700; fill: var(--color-text); font-variant-numeric: tabular-nums; }
    .center-small { font-size: 7.5px; fill: var(--color-text-muted); }
    ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; flex: 1; min-width: 160px; }
    li { display: grid; grid-template-columns: 12px 1fr auto; column-gap: 8px; align-items: baseline; transition: opacity 0.15s; }
    li.dim { opacity: 0.45; }
    .dot { width: 10px; height: 10px; border-radius: 3px; align-self: center; }
    .name { font-weight: 500; }
    .pct { font-weight: 600; font-variant-numeric: tabular-nums; }
    .detail { grid-column: 2 / -1; color: var(--color-text-muted); font-size: 0.75rem; }
  `,
})
export class DonutChart {
  readonly slices = input.required<DonutSlice[]>();
  readonly totalLabel = input('total');
  readonly totalFormat = input<(value: number) => string>((v) => String(v));
  readonly ariaLabel = input('Share of total');

  protected readonly r = 44;
  protected readonly circumference = 2 * Math.PI * this.r;
  protected readonly active = signal<string | null>(null);

  private readonly total = computed(() => this.slices().reduce((sum, s) => sum + s.value, 0));

  protected readonly arcs = computed(() => {
    const total = this.total();
    const gap = this.slices().length > 1 ? 2 : 0; // 2px surface gap between slices
    let offset = 0;
    return this.slices().map((s, i) => {
      const share = total > 0 ? s.value / total : 0;
      const full = share * this.circumference;
      const arc = { ...s, len: Math.max(full - gap, 0), offset, delay: i * 140, share, percentText: `${(share * 100).toFixed(share < 0.1 ? 1 : 0)}%` };
      offset += full;
      return arc;
    });
  });

  protected readonly centerBig = computed(() => {
    const slice = this.arcs().find((s) => s.label === this.active());
    return slice ? slice.percentText : this.totalFormat()(this.total());
  });
  protected readonly centerSmall = computed(() => this.active() ?? this.totalLabel());
}
