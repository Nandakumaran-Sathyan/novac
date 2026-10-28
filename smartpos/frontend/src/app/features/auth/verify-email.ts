import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http/http-utils';
import { Icon } from '../../shared/components/icon';
import { Loading } from '../../shared/components/states';

/** Landing page for the link in the verification email. Works whether or not the user is signed in on this device. */
@Component({
  selector: 'app-verify-email',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Icon, Loading],
  styleUrl: './login.css',
  template: `
    <div class="login-page">
      <div class="login-card card" aria-live="polite">
        <div class="brand">
          <span class="brand-mark" aria-hidden="true">S</span>
          <div><h1>Email verification</h1></div>
        </div>
        @if (state() === 'working') {
          <app-loading label="Confirming your email address…" />
        } @else if (state() === 'done') {
          <div class="alert alert-info" role="status"><app-icon name="check" /><span>Thanks, your email address is confirmed.</span></div>
          <a [routerLink]="auth.isAuthenticated() ? '/dashboard' : '/login'" class="btn btn-primary btn-lg btn-block">
            {{ auth.isAuthenticated() ? 'Go to the dashboard' : 'Sign in' }}
          </a>
        } @else {
          <div class="alert alert-error" role="alert"><app-icon name="alert" /><span>{{ message() }}</span></div>
          <a [routerLink]="auth.isAuthenticated() ? '/dashboard' : '/login'" class="btn btn-secondary btn-block">
            {{ auth.isAuthenticated() ? 'Back to the dashboard (you can request a new link there)' : 'Sign in to request a new link' }}
          </a>
        }
      </div>
    </div>
  `,
})
export class VerifyEmail implements OnInit {
  /** Bound from ?token= by withComponentInputBinding. */
  readonly token = input<string>();

  protected readonly auth = inject(AuthService);
  protected readonly state = signal<'working' | 'done' | 'failed'>('working');
  protected readonly message = signal('');

  ngOnInit(): void {
    const token = this.token();
    if (!token) {
      this.fail('This link is incomplete. Open the whole link from the email, or request a new one.');
      return;
    }
    this.auth.verifyEmail(token).subscribe({
      next: () => {
        this.state.set('done');
        if (this.auth.isAuthenticated()) {
          this.auth.refreshUser().subscribe({ error: () => undefined }); // the banner disappears once the profile updates
        }
      },
      error: (err: unknown) => this.fail(errorMessage(err, 'We could not verify this link.')),
    });
  }

  private fail(message: string): void {
    this.message.set(message);
    this.state.set('failed');
  }
}
