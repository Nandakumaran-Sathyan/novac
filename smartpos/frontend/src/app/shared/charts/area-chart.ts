import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { compactNumber, elementWidth, niceScale, uniqueId } from './chart-utils';

export interface AreaPoint {
  label: string;
  /** Longer label for the tooltip. */
  title: string;
  value: number;
  previous?: number;
  /** Extra tooltip line, e.g. "41 bills". */
  detail?: string;
}

/**
 * Revenue-over-time area chart with a dashed comparison line, crosshair tooltip and keyboard support
 * (focus it, then ← → Home End). The line draws in on load. Geometry is computed in pixels from the
 * measured width, so text stays crisp at every size.
 */
@Component({
  selector: 'app-area-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display:block;width:100%;position:relative' },
  template: `
    <div class="wrap" tabindex="0" role="group" [attr.aria-label]="ariaLabel()"
         (keydown)="onKey($event)" (focus)="focusIn()" (blur)="hover.set(null)">
      @if (g(); as g) {
        <svg [attr.width]="g.width" [attr.height]="height()" aria-hidden="true">
          <defs>
            <linearGradient [attr.id]="gradientId" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" stop-color="var(--chart-1)" stop-opacity="0.24" />
              <stop offset="1" stop-color="var(--chart-1)" stop-opacity="0.02" />
            </linearGradient>
          </defs>
          @for (t of g.yTicks; track t.value) {
            <line class="grid" [attr.x1]="g.left" [attr.x2]="g.width - g.right" [attr.y1]="t.y" [attr.y2]="t.y" />
            <text class="axis" [attr.x]="g.left - 8" [attr.y]="t.y + 4" text-anchor="end">{{ t.label }}</text>
          }
          @for (t of g.xTicks; track t.index) {
            <text class="axis" [attr.x]="t.x" [attr.y]="height() - 6" [attr.text-anchor]="t.anchor">{{ t.label }}</text>
          }
          @if (g.previousLine) {
            <path class="previous" [attr.d]="g.previousLine" />
          }
          <path class="area" [attr.d]="g.area" [attr.fill]="'url(#' + gradientId + ')'" />
          <path class="line" [attr.d]="g.line" pathLength="1" />
          @if (hover() !== null) {
            @let h = g.points[hover()!];
            <line class="crosshair" [attr.x1]="h.x" [attr.x2]="h.x" [attr.y1]="g.top" [attr.y2]="g.bottom" />
            @if (h.previousY !== null) {
              <circle class="dot-prev" [attr.cx]="h.x" [attr.cy]="h.previousY" r="4" />
            }
            <circle class="dot" [attr.cx]="h.x" [attr.cy]="h.y" r="5" />
          }
          <rect class="hit" [attr.x]="g.left" [attr.y]="g.top" [attr.width]="g.width - g.left - g.right" [attr.height]="g.bottom - g.top"
                (pointermove)="onMove($event, g)" (pointerleave)="hover.set(null)" />
        </svg>
        @if (hover() !== null) {
          @let h = g.points[hover()!];
          @let p = points()[hover()!];
          <div class="chart-tip" [style.left.px]="h.x" [style.top.px]="g.top" [class.flip]="h.x > g.width * 0.55">
            <strong>{{ p.title }}</strong>
            <div class="tip-row"><span>{{ seriesName() }}</span><span>{{ valueFormat()(p.value) }}</span></div>
            @if (p.previous !== undefined) {
              <div class="tip-row"><span>{{ previousName() }}</span><span>{{ valueFormat()(p.previous) }}</span></div>
            }
            @if (p.detail) { <div class="tip-detail">{{ p.detail }}</div> }
          </div>
        }
      }
    </div>
    <div class="chart-legend">
      <span><span class="swatch"></span>{{ seriesName() }}</span>
      @if (hasPrevious()) { <span><span class="swatch dashed"></span>{{ previousName() }}</span> }
    </div>
    <table class="sr-only">
      <caption>{{ ariaLabel() }}</caption>
      <thead><tr><th>Date</th><th>{{ seriesName() }}</th>@if (hasPrevious()) { <th>{{ previousName() }}</th> }</tr></thead>
      <tbody>
        @for (p of points(); track p.label) {
          <tr><td>{{ p.title }}</td><td>{{ valueFormat()(p.value) }}</td>@if (hasPrevious()) { <td>{{ valueFormat()(p.previous ?? 0) }}</td> }</tr>
        }
      </tbody>
    </table>
  `,
  styles: `
    .wrap { position: relative; outline: none; border-radius: var(--radius); }
    .wrap:focus-visible { box-shadow: var(--focus-ring); }
    svg { display: block; overflow: visible; }
    .grid { stroke: var(--chart-grid); stroke-width: 1; }
    .axis { fill: var(--color-text-muted); font-size: 11px; font-variant-numeric: tabular-nums; }
    .line { fill: none; stroke: var(--chart-1); stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; stroke-dasharray: 1; animation: chart-draw 1.2s ease-out both; }
    .area { animation: chart-fade 1s ease-out 0.3s both; }
    .previous { fill: none; stroke: var(--chart-previous); stroke-width: 1.5; stroke-dasharray: 4 4; animation: chart-fade 0.8s ease-out 0.6s both; }
    .crosshair { stroke: var(--color-border-strong); stroke-width: 1; }
    .dot { fill: var(--chart-1); stroke: var(--color-surface); stroke-width: 2; }
    .dot-prev { fill: var(--chart-previous); stroke: var(--color-surface); stroke-width: 2; }
    .hit { fill: transparent; cursor: crosshair; }
    .chart-tip { transform: translate(12px, 0); }
    .chart-tip.flip { transform: translate(calc(-100% - 12px), 0); }
    .tip-detail { color: var(--color-text-muted); margin-top: 2px; }
    .chart-legend { margin-top: 8px; }
  `,
})
export class AreaChart {
  readonly points = input.required<AreaPoint[]>();
  readonly valueFormat = input.required<(value: number) => string>();
  readonly axisFormat = input<(value: number) => string>(compactNumber);
  readonly seriesName = input('This period');
  readonly previousName = input('Previous period');
  readonly ariaLabel = input('Trend chart');
  readonly height = input(260);

  protected readonly hover = signal<number | null>(null);
  protected readonly gradientId = uniqueId('area');
  private readonly width = elementWidth();
  protected readonly hasPrevious = computed(() => this.points().some((p) => p.previous !== undefined));

  protected readonly g = computed(() => {
    const width = this.width();
    const points = this.points();
    const height = this.height();
    if (width <= 0 || points.length === 0) {
      return null;
    }
    const left = 48, right = 14, top = 12, bottom = height - 26;
    const innerWidth = Math.max(width - left - right, 1);
    const maxValue = Math.max(0, ...points.map((p) => Math.max(p.value, p.previous ?? 0)));
    const { max, step } = niceScale(maxValue);
    const x = (i: number) => left + (points.length === 1 ? innerWidth / 2 : (i * innerWidth) / (points.length - 1));
    const y = (v: number) => bottom - (v / max) * (bottom - top);
    const path = (values: number[]) => values.map((v, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join('');
    const line = path(points.map((p) => p.value));
    const area = `${line}L${x(points.length - 1).toFixed(1)},${bottom}L${x(0).toFixed(1)},${bottom}Z`;
    const previousLine = this.hasPrevious() ? path(points.map((p) => p.previous ?? 0)) : '';

    const labelCount = Math.max(2, Math.min(points.length, Math.floor(innerWidth / 90)));
    const indexes = [...new Set(Array.from({ length: labelCount }, (_, k) => Math.round((k * (points.length - 1)) / (labelCount - 1))))];
    return {
      width, left, right, top, bottom, line, area, previousLine,
      points: points.map((p, i) => ({ x: x(i), y: y(p.value), previousY: p.previous === undefined ? null : y(p.previous) })),
      yTicks: Array.from({ length: 5 }, (_, k) => ({ value: k * step, y: y(k * step), label: this.axisFormat()(k * step) })),
      xTicks: indexes.map((index) => ({
        index, x: x(index), label: points[index].label,
        anchor: index === 0 ? 'start' : index === points.length - 1 ? 'end' : 'middle',
      })),
    };
  });

  protected onMove(event: PointerEvent, g: { left: number; width: number; right: number }): void {
    const rect = (event.currentTarget as SVGRectElement).getBoundingClientRect();
    const count = this.points().length;
    const fraction = (event.clientX - rect.left) / Math.max(g.width - g.left - g.right, 1);
    this.hover.set(Math.min(count - 1, Math.max(0, Math.round(fraction * (count - 1)))));
  }

  protected focusIn(): void {
    if (this.hover() === null) {
      this.hover.set(this.points().length - 1);
    }
  }

  protected onKey(event: KeyboardEvent): void {
    const last = this.points().length - 1;
    const current = this.hover() ?? last;
    const next =
      event.key === 'ArrowLeft' ? current - 1 : event.key === 'ArrowRight' ? current + 1
      : event.key === 'Home' ? 0 : event.key === 'End' ? last : null;
    if (event.key === 'Escape') {
      this.hover.set(null);
    } else if (next !== null) {
      event.preventDefault();
      this.hover.set(Math.min(last, Math.max(0, next)));
    }
  }
}
