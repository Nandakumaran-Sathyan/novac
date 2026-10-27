import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { InventoryService } from '../../core/api/inventory.service';
import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http/http-utils';
import { InventoryFilter, InventoryItem, PagedResult } from '../../core/models';
import { ToastService } from '../../core/ui/toast.service';
import { StockBadge } from '../../shared/components/badges';
import { Icon } from '../../shared/components/icon';
import { Modal } from '../../shared/components/modal';
import { Pagination } from '../../shared/components/pagination';
import { EmptyState, ErrorAlert, Loading } from '../../shared/components/states';
import { DatePipe } from '@angular/common';

const FILTERS: { value: InventoryFilter; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'Low', label: 'Low stock' },
  { value: 'OutOfStock', label: 'Out of stock' },
];

export const ADJUSTMENT_REASONS = ['Delivery received', 'Stock count correction', 'Damaged', 'Expired', 'Returned to supplier', 'Customer return'];

@Component({
  selector: 'app-inventory',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, DatePipe, Icon, Modal, Pagination, StockBadge, EmptyState, ErrorAlert, Loading],
  templateUrl: './inventory.html',
})
export class Inventory implements OnInit {
  /** Optional ?filter=Low from the dashboard link. */
  readonly filterParam = input<string | undefined>(undefined, { alias: 'filter' });

  private readonly inventory = inject(InventoryService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly auth = inject(AuthService);

  protected readonly filters = FILTERS;
  protected readonly reasons = ADJUSTMENT_REASONS;
  protected readonly filter = signal<InventoryFilter>('All');
  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly result = signal<PagedResult<InventoryItem> | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  private page = 1;

  // Adjustment dialog state
  protected readonly adjusting = signal<InventoryItem | null>(null);
  protected readonly saving = signal(false);
  protected readonly adjustError = signal<string | null>(null);
  protected readonly adjustForm = this.fb.group({
    direction: this.fb.control<'add' | 'remove'>('add'),
    quantity: this.fb.control<number | null>(null, [Validators.required, Validators.min(1), Validators.max(100_000), Validators.pattern(/^\d+$/)]),
    reason: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(250)]],
  });
  private readonly adjustValue = signal(this.adjustForm.getRawValue());
  protected readonly newQuantity = computed(() => {
    const item = this.adjusting();
    const { direction, quantity } = this.adjustValue();
    if (!item || !quantity || quantity < 1) {
      return null;
    }
    return item.stockQuantity + (direction === 'add' ? quantity : -quantity);
  });

  ngOnInit(): void {
    const initial = this.filterParam();
    if (initial === 'Low' || initial === 'OutOfStock') {
      this.filter.set(initial);
    }
    this.search.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.reload());
    this.adjustForm.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.adjustValue.set(this.adjustForm.getRawValue()));
    this.load();
  }

  protected setFilter(filter: InventoryFilter): void {
    this.filter.set(filter);
    this.reload();
  }

  protected reload(): void {
    this.page = 1;
    this.load();
  }

  protected goToPage(page: number): void {
    this.page = page;
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.inventory.list({ search: this.search.value.trim(), filter: this.filter(), page: this.page, pageSize: 20 }).subscribe({
      next: (r) => {
        this.result.set(r);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(errorMessage(err, 'Could not load inventory.'));
        this.loading.set(false);
      },
    });
  }

  protected openAdjust(item: InventoryItem): void {
    this.adjustForm.reset({ direction: 'add', quantity: null, reason: '' });
    this.adjustError.set(null);
    this.adjusting.set(item);
  }

  protected closeAdjust(): void {
    if (!this.saving()) {
      this.adjusting.set(null);
    }
  }

  protected submitAdjust(): void {
    const item = this.adjusting();
    if (!item) {
      return;
    }
    if (this.adjustForm.invalid) {
      this.adjustForm.markAllAsTouched();
      return;
    }
    const { direction, quantity, reason } = this.adjustForm.getRawValue();
    const change = direction === 'add' ? quantity! : -quantity!;
    if (item.stockQuantity + change < 0) {
      this.adjustError.set(`Only ${item.stockQuantity} on hand; you cannot remove ${quantity}.`);
      return;
    }
    this.saving.set(true);
    this.adjustError.set(null);
    this.inventory.adjust(item.productId, change, reason.trim()).subscribe({
      next: (movement) => {
        this.saving.set(false);
        this.adjusting.set(null);
        this.toast.success(`${item.name}: stock is now ${movement.quantityAfter}.`);
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.adjustError.set(errorMessage(err, 'Could not record the adjustment.'));
      },
    });
  }

  protected adjustInvalid(name: 'quantity' | 'reason'): boolean {
    const c = this.adjustForm.controls[name];
    return c.invalid && c.touched;
  }
}
