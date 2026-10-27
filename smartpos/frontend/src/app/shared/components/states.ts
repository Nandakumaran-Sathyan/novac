import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Icon, IconName } from './icon';

/** Centered message for "nothing here yet" situations. Projected content becomes the call to action. */
@Component({
  selector: 'app-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <div class="empty">
      <span class="empty-icon"><app-icon [name]="icon()" [size]="22" /></span>
      <p class="empty-title">{{ title() }}</p>
      @if (message()) {
        <p class="muted">{{ message() }}</p>
      }
      <ng-content />
    </div>
  `,
  styles: `
    .empty { display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 40px 20px; text-align: center; }
    .empty-icon { display: inline-flex; padding: 12px; border-radius: 999px; background: var(--color-surface-muted); color: var(--color-text-muted); margin-bottom: 4px; }
    .empty-title { font-weight: 600; }
    .empty p.muted { max-width: 420px; }
    .empty ::ng-deep .btn { margin-top: 8px; }
  `,
})
export class EmptyState {
  readonly title = input.required<string>();
  readonly message = input<string>('');
  readonly icon = input<IconName>('package');
}

@Component({
  selector: 'app-loading',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="loading" role="status">
      <span class="spinner" aria-hidden="true"></span>
      <span>{{ label() }}</span>
    </div>
  `,
  styles: `
    .loading { display: flex; align-items: center; justify-content: center; gap: 10px; padding: 36px 16px; color: var(--color-text-muted); }
    .spinner { width: 18px; height: 18px; border: 2px solid var(--color-border-strong); border-top-color: var(--color-primary); border-radius: 50%; animation: spin 0.8s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }
    @media (prefers-reduced-motion: reduce) { .spinner { animation-duration: 2s; } }
  `,
})
export class Loading {
  readonly label = input('Loading…');
}

@Component({
  selector: 'app-error-alert',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <div class="alert alert-error" role="alert">
      <app-icon name="alert" />
      <span class="grow">{{ message() }}</span>
      @if (retryable()) {
        <button type="button" class="btn btn-secondary btn-sm" (click)="retry.emit()">Try again</button>
      }
    </div>
  `,
  styles: `:host { display: block; }`,
})
export class ErrorAlert {
  readonly message = input.required<string>();
  readonly retryable = input(true);
  readonly retry = output<void>();
}
