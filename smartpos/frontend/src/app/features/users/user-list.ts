import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { UsersService } from '../../core/api/users.service';
import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http/http-utils';
import { PagedResult, User, UserRole } from '../../core/models';
import { ToastService } from '../../core/ui/toast.service';
import { Icon } from '../../shared/components/icon';
import { Modal } from '../../shared/components/modal';
import { Pagination } from '../../shared/components/pagination';
import { EmptyState, ErrorAlert, Loading } from '../../shared/components/states';

/** Same rule as the API's PasswordRules. */
const PASSWORD_PATTERN = /^(?=.*[A-Za-z])(?=.*\d).{8,128}$/;

@Component({
  selector: 'app-user-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, DatePipe, Icon, Modal, Pagination, EmptyState, ErrorAlert, Loading],
  templateUrl: './user-list.html',
})
export class UserList implements OnInit {
  private readonly users = inject(UsersService);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly auth = inject(AuthService);

  protected readonly result = signal<PagedResult<User> | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  private page = 1;

  /** null = closed, 'new' = create, User = edit. */
  protected readonly editing = signal<User | 'new' | null>(null);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.fb.group({
    fullName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    password: ['', [Validators.pattern(PASSWORD_PATTERN)]],
    role: this.fb.control<UserRole>('Cashier'),
    isActive: [true],
  });

  ngOnInit(): void {
    this.load();
  }

  protected goToPage(page: number): void {
    this.page = page;
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.users.list({ page: this.page, pageSize: 20 }).subscribe({
      next: (r) => {
        this.result.set(r);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(errorMessage(err, 'Could not load users.'));
        this.loading.set(false);
      },
    });
  }

  protected isSelf(user: User): boolean {
    return user.id === this.auth.user()?.id;
  }

  protected isNew(): boolean {
    return this.editing() === 'new';
  }

  protected editingSelf(): boolean {
    const e = this.editing();
    return e !== null && e !== 'new' && this.isSelf(e);
  }

  protected openCreate(): void {
    this.form.reset({ fullName: '', email: '', password: '', role: 'Cashier', isActive: true });
    this.form.controls.email.enable();
    this.form.controls.role.enable();
    this.form.controls.isActive.enable();
    this.form.controls.password.setValidators([Validators.required, Validators.pattern(PASSWORD_PATTERN)]);
    this.form.controls.password.updateValueAndValidity();
    this.formError.set(null);
    this.editing.set('new');
  }

  protected openEdit(user: User): void {
    this.form.reset({ fullName: user.fullName, email: user.email, password: '', role: user.role, isActive: user.isActive });
    this.form.controls.email.disable();
    // The API rejects self-demotion/deactivation; lock the controls so the UI says so up front.
    for (const control of [this.form.controls.role, this.form.controls.isActive]) {
      if (this.isSelf(user)) {
        control.disable();
      } else {
        control.enable();
      }
    }
    this.form.controls.password.setValidators([Validators.pattern(PASSWORD_PATTERN)]);
    this.form.controls.password.updateValueAndValidity();
    this.formError.set(null);
    this.editing.set(user);
  }

  protected close(): void {
    if (!this.saving()) {
      this.editing.set(null);
    }
  }

  protected invalid(name: 'fullName' | 'email' | 'password'): boolean {
    const c = this.form.controls[name];
    return c.invalid && c.touched;
  }

  protected save(): void {
    const target = this.editing();
    if (target === null) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const request =
      target === 'new'
        ? this.users.create({ fullName: v.fullName.trim(), email: v.email.trim(), password: v.password, role: v.role })
        : this.users.update(target.id, {
            fullName: v.fullName.trim(),
            role: v.role,
            isActive: v.isActive,
            newPassword: v.password || null,
          });

    this.saving.set(true);
    this.formError.set(null);
    request.subscribe({
      next: (user) => {
        this.saving.set(false);
        this.editing.set(null);
        this.toast.success(target === 'new' ? `Created ${user.fullName}.` : `Saved ${user.fullName}.`);
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.formError.set(errorMessage(err, 'Could not save the user.'));
      },
    });
  }
}
