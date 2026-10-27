import { ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, Subscription } from 'rxjs';
import { ProductsService } from '../../core/api/products.service';
import { SalesService } from '../../core/api/sales.service';
import { SettingsService } from '../../core/api/settings.service';
import { errorMessage } from '../../core/http/http-utils';
import { PaymentMethod, Product, Sale } from '../../core/models';
import { ConfirmService } from '../../core/ui/confirm.service';
import { Icon } from '../../shared/components/icon';
import { EmptyState, ErrorAlert, Loading } from '../../shared/components/states';
import { MoneyPipe } from '../../shared/pipes/money.pipe';
import { addProduct, calculateTotals, CartLine, lineTotal, removeLine, setQuantity } from './cart';

@Component({
  selector: 'app-pos',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, Icon, EmptyState, ErrorAlert, Loading, MoneyPipe],
  templateUrl: './pos.html',
  styleUrl: './pos.css',
  host: { '(document:keydown)': 'onKeydown($event)' },
})
export class Pos implements OnInit {
  private readonly productsApi = inject(ProductsService);
  private readonly sales = inject(SalesService);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly settings = inject(SettingsService);

  private readonly searchInput = viewChild.required<ElementRef<HTMLInputElement>>('searchInput');
  private searchRequest?: Subscription;

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly products = signal<Product[]>([]);
  protected readonly loadingProducts = signal(true);
  protected readonly productsError = signal<string | null>(null);

  protected readonly lines = signal<CartLine[]>([]);
  protected readonly discountPercent = signal(0);
  protected readonly paymentMethod = signal<PaymentMethod>('Cash');
  protected readonly paymentMethods: PaymentMethod[] = ['Cash', 'Card', 'Other'];
  protected readonly submitting = signal(false);
  protected readonly checkoutError = signal<string | null>(null);
  protected readonly completedSale = signal<Sale | null>(null);

  protected readonly taxRate = computed(() => this.settings.settings()?.taxRatePercent ?? 0);
  protected readonly maxDiscount = computed(() => this.settings.settings()?.maxDiscountPercent ?? 0);
  protected readonly discountInvalid = computed(() => {
    const d = this.discountPercent();
    return !Number.isFinite(d) || d < 0 || d > this.maxDiscount();
  });
  protected readonly totals = computed(() =>
    calculateTotals(this.lines(), this.discountInvalid() ? 0 : this.discountPercent(), this.taxRate()),
  );
  protected readonly itemCount = computed(() => this.lines().reduce((n, l) => n + l.quantity, 0));
  protected readonly canCheckout = computed(() => this.lines().length > 0 && !this.discountInvalid() && !this.submitting());
  protected readonly lineTotal = lineTotal;

  ngOnInit(): void {
    this.search.valueChanges
      .pipe(debounceTime(250), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.loadProducts());
    this.loadProducts();
  }

  protected loadProducts(): void {
    this.searchRequest?.unsubscribe();
    this.loadingProducts.set(true);
    this.productsError.set(null);
    this.searchRequest = this.productsApi
      .list({ search: this.search.value.trim(), includeInactive: false, page: 1, pageSize: 24 })
      .subscribe({
        next: (r) => {
          this.products.set(r.items);
          this.loadingProducts.set(false);
        },
        error: (err: unknown) => {
          this.productsError.set(errorMessage(err, 'Could not load products.'));
          this.loadingProducts.set(false);
        },
      });
  }

  /** Enter in the search box: add an exact SKU match, or the only result. */
  protected addFromSearch(event: Event): void {
    event.preventDefault();
    const term = this.search.value.trim().toLowerCase();
    const results = this.products();
    const match = results.find((p) => p.sku.toLowerCase() === term) ?? (results.length === 1 ? results[0] : undefined);
    if (match && match.stockQuantity > 0) {
      this.add(match);
      this.search.setValue('');
    }
  }

  protected add(product: Product): void {
    this.completedSale.set(null);
    this.checkoutError.set(null);
    this.lines.update((lines) => addProduct(lines, product));
  }

  protected inCart(productId: number): number {
    return this.lines().find((l) => l.productId === productId)?.quantity ?? 0;
  }

  protected changeQuantity(line: CartLine, quantity: number): void {
    this.lines.update((lines) => setQuantity(lines, line.productId, quantity));
  }

  protected onQuantityInput(line: CartLine, event: Event): void {
    const input = event.target as HTMLInputElement;
    this.changeQuantity(line, input.valueAsNumber);
    // Reflect clamping (e.g. typed 99 with 5 in stock) back into the field.
    input.value = String(this.lines().find((l) => l.productId === line.productId)?.quantity ?? 1);
  }

  protected remove(line: CartLine): void {
    this.lines.update((lines) => removeLine(lines, line.productId));
  }

  protected onDiscountInput(event: Event): void {
    const value = (event.target as HTMLInputElement).valueAsNumber;
    this.discountPercent.set(Number.isNaN(value) ? 0 : value);
  }

  protected async clearCart(): Promise<void> {
    if (this.lines().length === 0) {
      return;
    }
    const ok = await this.confirm.ask({
      title: 'Clear cart?',
      message: 'All items will be removed from the current sale.',
      confirmText: 'Clear cart',
      danger: true,
    });
    if (ok) {
      this.resetSale();
    }
  }

  protected checkout(): void {
    if (!this.canCheckout()) {
      return;
    }
    this.submitting.set(true);
    this.checkoutError.set(null);
    this.sales
      .create({
        items: this.lines().map((l) => ({ productId: l.productId, quantity: l.quantity })),
        discountPercent: this.discountPercent(),
        paymentMethod: this.paymentMethod(),
      })
      .subscribe({
        next: (sale) => {
          this.submitting.set(false);
          this.completedSale.set(sale);
          this.lines.set([]);
          this.discountPercent.set(0);
          this.loadProducts(); // refresh stock figures
        },
        error: (err: unknown) => {
          this.submitting.set(false);
          this.checkoutError.set(errorMessage(err, 'The sale could not be completed.'));
          this.loadProducts(); // stock may have changed under us
        },
      });
  }

  protected newSale(): void {
    this.resetSale();
    this.searchInput().nativeElement.focus();
  }

  private resetSale(): void {
    this.lines.set([]);
    this.discountPercent.set(0);
    this.paymentMethod.set('Cash');
    this.checkoutError.set(null);
    this.completedSale.set(null);
  }

  /** "/" focuses search; Ctrl/Cmd+Enter completes the sale. */
  protected onKeydown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    const typing = !!target && ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName);
    if (event.key === '/' && !typing) {
      event.preventDefault();
      this.searchInput().nativeElement.focus();
    } else if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      this.checkout();
    }
  }
}
