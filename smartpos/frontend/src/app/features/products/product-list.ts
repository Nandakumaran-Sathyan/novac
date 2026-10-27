import { ChangeDetectionStrategy, Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { CategoriesService, ProductsService } from '../../core/api/products.service';
import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http/http-utils';
import { Category, PagedResult, Product } from '../../core/models';
import { ConfirmService } from '../../core/ui/confirm.service';
import { ToastService } from '../../core/ui/toast.service';
import { Icon } from '../../shared/components/icon';
import { Pagination } from '../../shared/components/pagination';
import { EmptyState, ErrorAlert, Loading } from '../../shared/components/states';
import { MoneyPipe } from '../../shared/pipes/money.pipe';

@Component({
  selector: 'app-product-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, Icon, Pagination, EmptyState, ErrorAlert, Loading, MoneyPipe],
  templateUrl: './product-list.html',
})
export class ProductList implements OnInit {
  private readonly products = inject(ProductsService);
  private readonly categoriesApi = inject(CategoriesService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthService);

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly categoryId = new FormControl<number | null>(null);
  protected readonly categories = signal<Category[]>([]);
  protected readonly result = signal<PagedResult<Product> | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  private page = 1;
  private readonly pageSize = 20;

  ngOnInit(): void {
    this.categoriesApi.list().subscribe({ next: (c) => this.categories.set(c) });
    this.search.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.reload());
    this.categoryId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.reload());
    this.load();
  }

  protected get hasFilters(): boolean {
    return this.search.value.trim() !== '' || this.categoryId.value !== null;
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
    this.products
      .list({ search: this.search.value.trim(), categoryId: this.categoryId.value, page: this.page, pageSize: this.pageSize })
      .subscribe({
        next: (r) => {
          this.result.set(r);
          this.loading.set(false);
        },
        error: (err: unknown) => {
          this.error.set(errorMessage(err, 'Could not load products.'));
          this.loading.set(false);
        },
      });
  }

  protected async remove(product: Product): Promise<void> {
    const confirmed = await this.confirm.ask({
      title: 'Delete product?',
      message: `"${product.name}" (${product.sku}) and its stock history will be permanently deleted. Products that have been sold cannot be deleted; deactivate them instead.`,
      confirmText: 'Delete product',
      danger: true,
    });
    if (!confirmed) {
      return;
    }
    this.products.delete(product.id).subscribe({
      next: () => {
        this.toast.success(`Deleted ${product.name}.`);
        this.load();
      },
      error: (err: unknown) => this.toast.error(errorMessage(err)),
    });
  }
}
