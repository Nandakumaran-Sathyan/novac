import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { prefersReducedMotion } from './chart-utils';

/** Shows a number that counts up (or down) to its value, easing out. Falls back to an instant change when motion is reduced. */
@Component({
  selector: 'app-count-up',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `{{ text() }}`,
})
export class CountUp {
  readonly value = input.required<number>();
  readonly format = input<(value: number) => string>((v) => String(Math.round(v)));
  readonly duration = input(900);

  private readonly shown = signal(0);
  protected readonly text = () => this.format()(this.shown());
  private frame = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => cancelAnimationFrame(this.frame));
    effect(() => {
      const target = this.value();
      untracked(() => this.animateTo(target));
    });
  }

  private animateTo(target: number): void {
    cancelAnimationFrame(this.frame);
    const start = this.shown();
    if (prefersReducedMotion() || start === target) {
      this.shown.set(target);
      return;
    }
    const duration = this.duration();
    const startedAt = performance.now();
    const step = (now: number) => {
      const t = Math.min((now - startedAt) / duration, 1);
      const eased = 1 - Math.pow(1 - t, 3);
      this.shown.set(start + (target - start) * eased);
      if (t < 1) {
        this.frame = requestAnimationFrame(step);
      }
    };
    this.frame = requestAnimationFrame(step);
  }
}
