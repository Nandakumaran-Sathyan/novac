import { afterNextRender, DestroyRef, ElementRef, inject, Signal, signal } from '@angular/core';

/** Width of the host element in px, kept up to date with a ResizeObserver (0 until first render). */
export function elementWidth(): Signal<number> {
  const element = inject(ElementRef).nativeElement as HTMLElement;
  const destroyRef = inject(DestroyRef);
  const width = signal(0);
  afterNextRender(() => {
    const update = (value: number) => {
      // A transient 0 (layout in flux, element briefly hidden) must not tear the chart down:
      // that would rebuild the SVG and replay its entrance animation.
      if (value > 0) {
        width.set(Math.round(value));
      }
    };
    update(element.clientWidth);
    const observer = new ResizeObserver((entries) => update(entries[0].contentRect.width));
    observer.observe(element);
    destroyRef.onDestroy(() => observer.disconnect());
  });
  return width.asReadonly();
}

let nextId = 0;
/** A unique id for SVG defs (gradients) so several charts on a page don't collide. */
export function uniqueId(prefix: string): string {
  return `${prefix}-${++nextId}`;
}

export function prefersReducedMotion(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/** A round axis maximum and step so `ticks` gridlines land on tidy numbers (0, 5k, 10k…). */
export function niceScale(maxValue: number, ticks = 4): { max: number; step: number } {
  if (!(maxValue > 0)) {
    return { max: ticks, step: 1 };
  }
  const rough = maxValue / ticks;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rough)));
  const step = [1, 2, 2.5, 5, 10].map((m) => m * magnitude).find((s) => s >= rough) ?? 10 * magnitude;
  return { max: step * ticks, step };
}

const compact = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 });
/** 12400 → "12.4K" (for axis labels). */
export const compactNumber = (value: number): string => compact.format(value);

const formatters = new Map<string, Intl.NumberFormat>();
/** Full money, e.g. "₹12,400.50". */
export function formatMoney(currency: string, value: number): string {
  const key = `${currency}:full`;
  let f = formatters.get(key);
  if (!f) {
    f = new Intl.NumberFormat(undefined, { style: 'currency', currency });
    formatters.set(key, f);
  }
  return f.format(value);
}

/** Short money for tight spaces, e.g. "₹2.7L" or "$265K". */
export function formatMoneyCompact(currency: string, value: number): string {
  const key = `${currency}:compact`;
  let f = formatters.get(key);
  if (!f) {
    f = new Intl.NumberFormat(undefined, { style: 'currency', currency, notation: 'compact', maximumFractionDigits: 1 });
    formatters.set(key, f);
  }
  return f.format(value);
}

/** Whole-unit money for headline figures, e.g. "₹2,65,117". */
export function formatMoneyWhole(currency: string, value: number): string {
  const key = `${currency}:whole`;
  let f = formatters.get(key);
  if (!f) {
    f = new Intl.NumberFormat(undefined, { style: 'currency', currency, maximumFractionDigits: 0 });
    formatters.set(key, f);
  }
  return f.format(value);
}

/** Percentage change from `previous` to `current`, or null when there is no baseline. */
export function percentChange(current: number, previous: number): number | null {
  if (previous === 0) {
    return current === 0 ? 0 : null;
  }
  return ((current - previous) / previous) * 100;
}

/** Path for a vertical bar with a rounded top and a square baseline. */
export function barPath(x: number, y: number, width: number, height: number, radius: number): string {
  const r = Math.min(radius, width / 2, height);
  return `M${x},${y + height}V${y + r}Q${x},${y} ${x + r},${y}H${x + width - r}Q${x + width},${y} ${x + width},${y + r}V${y + height}Z`;
}
