import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { SettingsService } from '../../core/api/settings.service';
import { CustomersService } from '../../core/api/customers.service';
import { errorMessage } from '../../core/http/http-utils';
import { Customer, PagedResult } from '../../core/models';
import { ToastService } from '../../core/ui/toast.service';
import { Icon } from '../../shared/components/icon';
import { Modal } from '../../shared/components/modal';
import { Pagination } from '../../shared/components/pagination';
import { EmptyState, ErrorAlert, Loading } from '../../shared/components/states';

/** Same rule as the API's PhoneRules. */
const PHONE_PATTERN = /^\+?[0-9][0-9 ()-]{5,18}[0-9]$/;

@Component({
  selector: 'app-customer-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, DatePipe, Icon, Modal, Pagination, EmptyState, ErrorAlert, Loading],
  templateUrl: './customer-list.html',
})
export class CustomerList implements OnInit {
  private readonly customers = inject(CustomersService);
  private readonly toast = inject(ToastService);
  private readonly settings = inject(SettingsService);
  protected readonly rateText = () => {
    const rate = this.settings.settings()?.loyaltySpendPerPoint;
    return rate ? `1 point for every ${rate} spent, rounded down` : 'points are switched off';
  };
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly result = signal<PagedResult<Customer> | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  private page = 1;

  /** null = closed, 'new' = create, Customer = edit. */
  protected readonly editing = signal<Customer | 'new' | null>(null);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(100)]],
    phone: ['', [Validators.required, Validators.pattern(PHONE_PATTERN)]],
  });

  ngOnInit(): void {
    this.search.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.page = 1;
        this.load();
      });
    this.load();
  }

  protected goToPage(page: number): void {
    this.page = page;
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.customers.list({ search: this.search.value.trim(), page: this.page, pageSize: 20 }).subscribe({
      next: (r) => {
        this.result.set(r);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(errorMessage(err, 'Could not load customers.'));
        this.loading.set(false);
      },
    });
  }

  protected isNew(): boolean {
    return this.editing() === 'new';
  }

  protected openCreate(): void {
    this.form.reset({ name: '', phone: '' });
    this.formError.set(null);
    this.editing.set('new');
  }

  protected openEdit(customer: Customer): void {
    this.form.reset({ name: customer.name, phone: customer.phone });
    this.formError.set(null);
    this.editing.set(customer);
  }

  protected close(): void {
    if (!this.saving()) {
      this.editing.set(null);
    }
  }

  protected invalid(name: 'name' | 'phone'): boolean {
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
    const input = { name: v.name.trim(), phone: v.phone.trim() };
    const request = target === 'new' ? this.customers.create(input) : this.customers.update(target.id, input);

    this.saving.set(true);
    this.formError.set(null);
    request.subscribe({
      next: (customer) => {
        this.saving.set(false);
        this.editing.set(null);
        this.toast.success(target === 'new' ? `Added ${customer.name}.` : `Saved ${customer.name}.`);
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.formError.set(errorMessage(err, 'Could not save the customer.'));
      },
    });
  }
}
