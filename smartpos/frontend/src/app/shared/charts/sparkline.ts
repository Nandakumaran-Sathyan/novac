import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { elementWidth, uniqueId } from './chart-utils';

/** A tiny trend line with no axes, drawn in once. Decorative: the figure beside it carries the data. */
@Component({
  selector: 'app-sparkline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true', style: 'display:block;width:100%' },
  template: `
    @if (geometry(); as g) {
      <svg [attr.width]="g.width" [attr.height]="height()">
        <defs>
          <linearGradient [attr.id]="gradientId" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stop-color="var(--chart-1)" stop-opacity="0.22" />
            <stop offset="1" stop-color="var(--chart-1)" stop-opacity="0" />
          </linearGradient>
        </defs>
        <path class="fill" [attr.d]="g.area" [attr.fill]="'url(#' + gradientId + ')'" />
        <path class="line" [attr.d]="g.line" pathLength="1" />
        <circle class="end" [attr.cx]="g.endX" [attr.cy]="g.endY" r="3" />
      </svg>
    }
  `,
  styles: `
    svg { display: block; overflow: visible; }
    .line { fill: none; stroke: var(--chart-1); stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; stroke-dasharray: 1; animation: chart-draw 1s ease-out both; }
    .fill { animation: chart-fade 0.9s ease-out 0.2s both; }
    .end { fill: var(--chart-1); stroke: var(--color-surface); stroke-width: 2; transform-box: fill-box; transform-origin: center; animation: chart-pop 0.3s ease-out 0.9s both; }
  `,
})
export class Sparkline {
  readonly values = input.required<number[]>();
  readonly height = input(36);

  protected readonly gradientId = uniqueId('spark');
  private readonly width = elementWidth();

  protected readonly geometry = computed(() => {
    const width = this.width();
    const values = this.values();
    const height = this.height();
    if (width <= 0 || values.length < 2) {
      return null;
    }
    const pad = 4;
    const max = Math.max(...values, 1);
    const min = Math.min(...values, 0);
    const span = max - min || 1;
    const x = (i: number) => pad + (i * (width - 2 * pad)) / (values.length - 1);
    const y = (v: number) => pad + (height - 2 * pad) * (1 - (v - min) / span);
    const line = values.map((v, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join('');
    const area = `${line}L${x(values.length - 1).toFixed(1)},${height}L${x(0).toFixed(1)},${height}Z`;
    return { width, line, area, endX: x(values.length - 1), endY: y(values[values.length - 1]) };
  });
}
