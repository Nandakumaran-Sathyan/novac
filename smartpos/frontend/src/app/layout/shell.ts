import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { errorMessage } from '../core/http/http-utils';
import { ToastService } from '../core/ui/toast.service';
import { ConfirmDialog } from '../shared/components/confirm-dialog';
import { ToastContainer } from '../shared/components/toast-container';
import { Header } from './header';
import { Sidebar } from './sidebar';

/** Authenticated application frame: sidebar, header and the routed page. */
@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, Sidebar, Header, ToastContainer, ConfirmDialog],
  host: { '(document:keydown.escape)': 'menuOpen.set(false)' },
  template: `
    <a class="skip-link" href="#main-content">Skip to content</a>
    <aside class="sidebar no-print" [class.open]="menuOpen()">
      <app-sidebar (navigate)="menuOpen.set(false)" />
    </aside>
    @if (menuOpen()) {
      <div class="scrim no-print" (click)="menuOpen.set(false)" aria-hidden="true"></div>
    }
    <div class="main">
      <app-header class="no-print" (toggleMenu)="menuOpen.set(!menuOpen())" />
      @if (auth.user(); as user) {
        @if (!user.emailVerified) {
          <div class="verify-banner no-print" role="status">
            <span>Please confirm your email address. We sent a link to <strong>{{ user.email }}</strong>.</span>
            <button type="button" class="btn btn-secondary btn-sm" (click)="resend()" [disabled]="sending()">
              {{ sending() ? 'Sending…' : 'Resend link' }}
            </button>
          </div>
        }
      }
      <main id="main-content" tabindex="-1">
        <router-outlet />
      </main>
    </div>
    <app-toast-container />
    <app-confirm-dialog />
  `,
  styles: `
    :host { display: block; min-height: 100vh; }
    .sidebar { position: fixed; inset: 0 auto 0 0; width: var(--sidebar-width); z-index: 50; }
    .main { margin-left: var(--sidebar-width); min-height: 100vh; display: flex; flex-direction: column; }
    main { flex: 1; outline: none; }
    .verify-banner { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; padding: 10px 24px; background: var(--color-warning-soft); color: var(--color-warning-text); border-bottom: 1px solid var(--color-border); font-size: 0.875rem; }
    @media (max-width: 900px) { .verify-banner { padding: 10px 16px; } }
    .scrim { position: fixed; inset: 0; background: rgba(17,24,39,0.45); z-index: 40; }
    .skip-link { position: absolute; left: -999px; top: 8px; z-index: 100; background: #fff; padding: 8px 12px; border-radius: var(--radius); }
    .skip-link:focus { left: 8px; }
    @media (max-width: 900px) {
      .sidebar { transform: translateX(-100%); transition: transform 0.2s ease; }
      .sidebar.open { transform: none; }
      .main { margin-left: 0; }
    }
    @media print { .main { margin-left: 0; } }
  `,
})
export class Shell {
  protected readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  protected readonly menuOpen = signal(false);
  protected readonly sending = signal(false);

  protected resend(): void {
    this.sending.set(true);
    this.auth.resendVerification().subscribe({
      next: () => {
        this.sending.set(false);
        this.toast.success('Verification link sent. Check your inbox.');
      },
      error: (err: unknown) => {
        this.sending.set(false);
        this.toast.error(errorMessage(err, 'Could not send the link.'));
        this.auth.refreshUser().subscribe({ error: () => undefined }); // e.g. it was verified in another tab
      },
    });
  }
}
