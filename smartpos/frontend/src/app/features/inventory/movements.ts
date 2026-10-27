import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { InventoryService } from '../../core/api/inventory.service';
import { errorMessage } from '../../core/http/http-utils';
import { PagedResult, StockMovement, StockMovementType } from '../../core/models';
import { MovementBadge } from '../../shared/components/badges';
import { Icon } from '../../shared/components/icon';
import { Pagination } from '../../shared/components/pagination';
import { EmptyState, ErrorAlert, Loading } from '../../shared/components/states';

@Component({
  selector: 'app-movements',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, DatePipe, Icon, MovementBadge, Pagination, EmptyState, ErrorAlert, Loading],
  templateUrl: './movements.html',
})
export class Movements implements OnInit {
  /** Optional ?productId= from the inventory "History" link. */
  readonly productId = input<string>();

  private readonly inventory = inject(InventoryService);
  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly filters = this.fb.group({
    type: this.fb.control<StockMovementType | ''>(''),
    from: [''],
    to: [''],
  });
  protected readonly result = signal<PagedResult<StockMovement> | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly productName = signal<string | null>(null);
  private page = 1;

  ngOnInit(): void {
    this.load();
  }

  protected apply(): void {
    this.page = 1;
    this.load();
  }

  protected clear(): void {
    this.filters.reset({ type: '', from: '', to: '' });
    this.apply();
  }

  protected goToPage(page: number): void {
    this.page = page;
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    const { type, from, to } = this.filters.getRawValue();
    const productId = this.productId() ? Number(this.productId()) : null;
    this.inventory.movements({ productId, type, from, to, page: this.page, pageSize: 25 }).subscribe({
      next: (r) => {
        this.result.set(r);
        if (productId && r.items.length > 0) {
          this.productName.set(`${r.items[0]!.productName} (${r.items[0]!.sku})`);
        }
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(errorMessage(err, 'Could not load stock movements.'));
        this.loading.set(false);
      },
    });
  }
}
