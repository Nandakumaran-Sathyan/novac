import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService } from '../../core/ui/toast.service';
import { Icon } from './icon';

@Component({
  selector: 'app-toast-container',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <div class="toasts no-print" aria-live="polite" aria-atomic="false">
      @for (toast of toasts.toasts(); track toast.id) {
        <div class="toast" [class]="'toast toast-' + toast.kind" role="status">
          <app-icon [name]="toast.kind === 'error' ? 'alert' : 'check'" />
          <span>{{ toast.message }}</span>
          <button type="button" class="btn btn-ghost btn-icon btn-sm" (click)="toasts.dismiss(toast.id)" aria-label="Dismiss">
            <app-icon name="close" [size]="16" />
          </button>
        </div>
      }
    </div>
  `,
  styles: `
    .toasts { position: fixed; right: 16px; bottom: 16px; z-index: 1000; display: flex; flex-direction: column; gap: 8px; max-width: min(420px, calc(100vw - 32px)); }
    .toast { display: flex; align-items: center; gap: 10px; padding: 10px 10px 10px 14px; border-radius: var(--radius); background: var(--color-sidebar); color: #fff; box-shadow: var(--shadow-md); }
    .toast span { flex: 1; }
    .toast-success app-icon { color: #4ade80; }
    .toast-error app-icon { color: #f87171; }
    .toast .btn-ghost { color: #c3c9d1; }
    .toast .btn-ghost:hover { background: rgba(255,255,255,0.08); color: #fff; }
  `,
})
export class ToastContainer {
  protected readonly toasts = inject(ToastService);
}
