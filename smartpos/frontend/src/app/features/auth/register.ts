import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http/http-utils';
import { Icon } from '../../shared/components/icon';

/** Same rule as the API's PasswordRules. */
const PASSWORD_PATTERN = /^(?=.*[A-Za-z])(?=.*\d).{8,128}$/;

/** Creates an account and the shop it owns, then signs in. */
@Component({
  selector: 'app-register',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, Icon],
  templateUrl: './register.html',
  styleUrl: './login.css',
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = inject(NonNullableFormBuilder).group({
    fullName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    password: ['', [Validators.required, Validators.pattern(PASSWORD_PATTERN)]],
    shopName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]],
    shopAddress: ['', [Validators.maxLength(250)]],
  });
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly showPassword = signal(false);

  protected invalid(name: keyof typeof this.form.controls): boolean {
    const control = this.form.controls[name];
    return control.invalid && control.touched;
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    const v = this.form.getRawValue();
    this.auth
      .register({
        fullName: v.fullName.trim(),
        email: v.email.trim(),
        password: v.password,
        shopName: v.shopName.trim(),
        shopAddress: v.shopAddress.trim() || null,
      })
      .subscribe({
        next: () => void this.router.navigateByUrl('/dashboard'),
        error: (err: unknown) => {
          this.error.set(errorMessage(err, 'Could not create the account. Please try again.'));
          this.submitting.set(false);
        },
      });
  }
}
