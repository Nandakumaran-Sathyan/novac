import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { barPath, compactNumber, elementWidth, niceScale } from './chart-utils';

export interface Bar {
  label: string;
  value: number;
  title: string;
  detail?: string;
}

/**
 * Column chart for ordered buckets (hours of the day). The tallest bar is emphasised and labelled
 * directly; the rest use a lighter step of the same hue. Bars grow from the baseline with a stagger.
 */
@Component({
  selector: 'app-bar-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display:block;width:100%;position:relative' },
  template: `
    @if (g(); as g) {
      <svg [attr.width]="g.width" [attr.height]="height()" role="img" [attr.aria-label]="ariaLabel()">
        @for (t of g.yTicks; track t.value) {
          <line class="grid" [attr.x1]="g.left" [attr.x2]="g.width - 8" [attr.y1]="t.y" [attr.y2]="t.y" />
          <text class="axis" [attr.x]="g.left - 8" [attr.y]="t.y + 4" text-anchor="end">{{ t.label }}</text>
        }
        @for (b of g.bars; track b.index) {
          <path class="bar" [class.peak]="b.peak" [class.dim]="hover() !== null && hover() !== b.index" [attr.d]="b.path"
                [style.animation-delay.ms]="b.index * 28" />
          @if (b.showLabel) {
            <text class="axis" [attr.x]="b.cx" [attr.y]="height() - 6" text-anchor="middle">{{ b.label }}</text>
          }
          <rect class="hit" [attr.x]="b.x0" [attr.y]="g.top" [attr.width]="g.band" [attr.height]="g.bottom - g.top"
                (pointerenter)="hover.set(b.index)" (pointerleave)="hover.set(null)" />
        }
        @if (g.peak; as peak) {
          <text class="peak-label" [attr.x]="peak.cx" [attr.y]="peak.top - 6" text-anchor="middle">{{ peak.text }}</text>
        }
      </svg>
      @if (hover() !== null) {
        @let b = g.bars[hover()!];
        <div class="chart-tip" [style.left.px]="b.cx" [style.top.px]="g.top" [class.flip]="b.cx > g.width * 0.6">
          <strong>{{ bars()[hover()!].title }}</strong>
          <div class="tip-row"><span>{{ valueName() }}</span><span>{{ valueFormat()(bars()[hover()!].value) }}</span></div>
          @if (bars()[hover()!].detail) { <div class="tip-detail">{{ bars()[hover()!].detail }}</div> }
        </div>
      }
    }
  `,
  styles: `
    svg { display: block; overflow: visible; }
    .grid { stroke: var(--chart-grid); stroke-width: 1; }
    .axis { fill: var(--color-text-muted); font-size: 11px; font-variant-numeric: tabular-nums; }
    .bar { fill: var(--chart-ramp-200); transform-box: fill-box; transform-origin: bottom; animation: chart-grow-y 0.6s cubic-bezier(0.2, 0.8, 0.2, 1) both; transition: opacity 0.15s; }
    .bar.peak { fill: var(--chart-1); }
    .bar.dim { opacity: 0.45; }
    .peak-label { fill: var(--color-text); font-size: 11px; font-weight: 600; animation: chart-fade 0.4s ease-out 0.9s both; }
    .hit { fill: transparent; }
    .chart-tip { transform: translate(10px, 0); }
    .chart-tip.flip { transform: translate(calc(-100% - 10px), 0); }
    .tip-detail { color: var(--color-text-muted); margin-top: 2px; }
  `,
})
export class BarChart {
  readonly bars = input.required<Bar[]>();
  readonly valueFormat = input<(value: number) => string>((v) => String(v));
  readonly valueName = input('Value');
  readonly ariaLabel = input('Bar chart');
  readonly height = input(220);
  /** Label every n-th bar on the x axis. */
  readonly labelEvery = input(3);

  protected readonly hover = signal<number | null>(null);
  private readonly width = elementWidth();

  protected readonly g = computed(() => {
    const width = this.width();
    const bars = this.bars();
    const height = this.height();
    if (width <= 0 || bars.length === 0) {
      return null;
    }
    const left = 36, right = 8, top = 22, bottom = height - 24;
    const band = (width - left - right) / bars.length;
    const barWidth = Math.max(4, band * 0.64);
    const maxValue = Math.max(0, ...bars.map((b) => b.value));
    const { max, step } = niceScale(maxValue, 4);
    const y = (v: number) => bottom - (v / max) * (bottom - top);
    const peakIndex = maxValue > 0 ? bars.findIndex((b) => b.value === maxValue) : -1;
    const every = this.labelEvery();
    const built = bars.map((b, i) => {
      const cx = left + band * i + band / 2;
      const barTop = y(b.value);
      return {
        index: i, cx, x0: left + band * i, label: b.label, peak: i === peakIndex, showLabel: i % every === 0,
        path: b.value > 0 ? barPath(cx - barWidth / 2, barTop, barWidth, bottom - barTop, 4) : '',
        top: barTop,
      };
    });
    return {
      width, left, top, bottom, band, bars: built,
      yTicks: Array.from({ length: 5 }, (_, k) => ({ value: k * step, y: y(k * step), label: compactNumber(k * step) })),
      peak: peakIndex >= 0 ? { cx: built[peakIndex].cx, top: built[peakIndex].top, text: `${bars[peakIndex].label} · ${this.valueFormat()(maxValue)}` } : null,
    };
  });
}
