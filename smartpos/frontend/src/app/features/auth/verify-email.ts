import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http/http-utils';
import { Icon } from '../../shared/components/icon';

/** Seconds between "send a new code" requests; the API enforces the same limit. */
const RESEND_SECONDS = 60;

/** Where a signed-in owner enters the 6-digit code from the verification email. The rest of the app waits for it. */
@Component({
  selector: 'app-verify-email',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, Icon],
  styleUrl: './login.css',
  template: `
    <div class="login-page">
      <div class="login-card card">
        <div class="brand">
          <span class="brand-mark" aria-hidden="true">S</span>
          <div>
            <h1>Check your email</h1>
            <p class="muted">We sent a 6-digit code to <strong>{{ auth.user()?.email }}</strong></p>
          </div>
        </div>

        @if (error()) {
          <div class="alert alert-error" role="alert"><app-icon name="alert" /><span>{{ error() }}</span></div>
        } @else if (notice()) {
          <div class="alert alert-info" role="status"><app-icon name="check" /><span>{{ notice() }}</span></div>
        }

        <form (ngSubmit)="verify()" novalidate>
          <div class="field">
            <label for="code" class="required">Verification code</label>
            <input id="code" class="input code-input" [formControl]="code" inputmode="numeric" autocomplete="one-time-code"
                   maxlength="6" placeholder="000000" autofocus (input)="onInput($event)"
                   [attr.aria-invalid]="code.invalid && code.dirty" aria-describedby="code-help">
            <span id="code-help" class="hint">The code expires 5 minutes after it is sent and can be tried 5 times.</span>
          </div>
          <button type="submit" class="btn btn-primary btn-lg btn-block" [disabled]="code.invalid || verifying()">
            {{ verifying() ? 'Checking…' : 'Verify email' }}
          </button>
        </form>

        <div class="footer-hint">
          <button type="button" class="btn btn-ghost btn-sm" (click)="resend()" [disabled]="sending() || wait() > 0">
            {{ sending() ? 'Sending…' : wait() > 0 ? 'Send a new code in ' + wait() + ' s' : 'Send a new code' }}
          </button>
          <button type="button" class="btn btn-ghost btn-sm" (click)="auth.logout()">Sign out</button>
        </div>
      </div>
    </div>
  `,
  styles: `
    .code-input { font-size: 1.5rem; letter-spacing: 0.5em; text-align: center; font-variant-numeric: tabular-nums; }
    .footer-hint { display: flex; justify-content: space-between; flex-wrap: wrap; gap: 8px; }
  `,
})
export class VerifyEmail implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly code = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\d{6}$/)] });
  protected readonly verifying = signal(false);
  protected readonly sending = signal(false);
  protected readonly wait = signal(0);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => clearInterval(this.timer));
    // Registration has just sent a code, so the countdown starts from then. An account that existed before has
    // no code yet: "Send a new code" is available straight away.
    const sentAt = this.auth.codeSentAt();
    if (sentAt !== null) {
      this.startCooldown(RESEND_SECONDS - Math.floor((Date.now() - sentAt) / 1000));
    }
  }

  /** Keeps only digits, and submits as soon as six are typed or pasted. */
  protected onInput(event: Event): void {
    const digits = (event.target as HTMLInputElement).value.replace(/\D/g, '').slice(0, 6);
    this.code.setValue(digits);
    if (digits.length === 6) {
      this.verify();
    }
  }

  protected verify(): void {
    if (this.code.invalid || this.verifying()) {
      return;
    }
    this.verifying.set(true);
    this.error.set(null);
    this.auth.verifyEmail(this.code.value).subscribe({
      next: () => {
        this.auth.refreshUser().subscribe({
          next: () => void this.router.navigateByUrl('/dashboard'),
          error: () => void this.router.navigateByUrl('/dashboard'),
        });
      },
      error: (err: unknown) => {
        this.verifying.set(false);
        this.notice.set(null);
        this.error.set(errorMessage(err, 'That code could not be checked.'));
        this.code.setValue('');
      },
    });
  }

  protected resend(): void {
    this.sending.set(true);
    this.error.set(null);
    this.auth.resendVerification().subscribe({
      next: () => {
        this.sending.set(false);
        this.notice.set(`A new code is on its way to ${this.auth.user()?.email}.`);
        this.code.setValue('');
        this.startCooldown();
      },
      error: (err: unknown) => {
        this.sending.set(false);
        this.error.set(errorMessage(err, 'Could not send a new code.'));
        if (err instanceof HttpErrorResponse && err.status === 429) {
          this.startCooldown();
        } else if (err instanceof HttpErrorResponse && err.status === 400) {
          // e.g. already verified in another tab
          this.auth.refreshUser().subscribe({ next: (u) => u.emailVerified && void this.router.navigateByUrl('/dashboard'), error: () => undefined });
        }
      },
    });
  }

  private startCooldown(seconds = RESEND_SECONDS): void {
    clearInterval(this.timer);
    if (seconds <= 0) {
      return;
    }
    this.wait.set(seconds);
    this.timer = setInterval(() => {
      this.wait.update((s) => Math.max(s - 1, 0));
      if (this.wait() === 0) {
        clearInterval(this.timer);
      }
    }, 1000);
  }
}
