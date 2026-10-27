import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CategoriesService, ProductsService } from '../../core/api/products.service';
import { errorMessage } from '../../core/http/http-utils';
import { Category, ProductInput } from '../../core/models';
import { ToastService } from '../../core/ui/toast.service';
import { ErrorAlert, Loading } from '../../shared/components/states';

/** Mirrors the API: up to 2 decimal places. */
function money(control: AbstractControl<number | null>): ValidationErrors | null {
  const v = control.value;
  return v === null || Math.abs(v * 100 - Math.round(v * 100)) < 1e-6 ? null : { money: true };
}

@Component({
  selector: 'app-product-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, ErrorAlert, Loading],
  templateUrl: './product-form.html',
})
export class ProductForm implements OnInit {
  /** Bound from the :id route parameter; absent when creating. */
  readonly id = input<string>();

  private readonly products = inject(ProductsService);
  private readonly categoriesApi = inject(CategoriesService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly isEdit = computed(() => this.id() !== undefined);
  protected readonly categories = signal<Category[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saveError = signal<string | null>(null);
  protected readonly currentStock = signal<number | null>(null);
  protected readonly addingCategory = signal(false);
  protected readonly newCategoryName = this.fb.control('', [Validators.required, Validators.maxLength(100)]);

  protected readonly form = this.fb.group({
    sku: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(50), Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9._-]*$/)]],
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]],
    description: ['', [Validators.maxLength(1000)]],
    categoryId: this.fb.control<number | null>(null, [Validators.required]),
    sellingPrice: this.fb.control<number | null>(null, [Validators.required, Validators.min(0), Validators.max(9999999.99), money]),
    costPrice: this.fb.control<number | null>(null, [Validators.required, Validators.min(0), Validators.max(9999999.99), money]),
    initialStock: this.fb.control<number | null>(0, [Validators.required, Validators.min(0), Validators.max(1_000_000), Validators.pattern(/^\d+$/)]),
    reorderLevel: this.fb.control<number | null>(5, [Validators.required, Validators.min(0), Validators.max(1_000_000), Validators.pattern(/^\d+$/)]),
    isActive: [true],
  });

  ngOnInit(): void {
    this.categoriesApi.list().subscribe({ next: (c) => this.categories.set(c) });
    if (this.isEdit()) {
      this.form.controls.initialStock.disable();
      this.loadProduct();
    }
  }

  protected loadProduct(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.products.get(Number(this.id())).subscribe({
      next: (p) => {
        this.form.patchValue({
          sku: p.sku,
          name: p.name,
          description: p.description ?? '',
          categoryId: p.categoryId,
          sellingPrice: p.sellingPrice,
          costPrice: p.costPrice,
          reorderLevel: p.reorderLevel,
          isActive: p.isActive,
        });
        this.currentStock.set(p.stockQuantity);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.loadError.set(errorMessage(err, 'Could not load this product.'));
        this.loading.set(false);
      },
    });
  }

  protected invalid(name: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[name];
    return c.invalid && (c.touched || c.dirty);
  }

  protected addCategory(): void {
    if (this.newCategoryName.invalid) {
      this.newCategoryName.markAsTouched();
      return;
    }
    this.categoriesApi.create(this.newCategoryName.value.trim()).subscribe({
      next: (category) => {
        this.categories.update((list) => [...list, category].sort((a, b) => a.name.localeCompare(b.name)));
        this.form.controls.categoryId.setValue(category.id);
        this.newCategoryName.reset('');
        this.addingCategory.set(false);
        this.toast.success(`Category "${category.name}" created.`);
      },
      error: (err: unknown) => this.toast.error(errorMessage(err)),
    });
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const input: ProductInput = {
      sku: v.sku.trim(),
      name: v.name.trim(),
      description: v.description.trim() || null,
      categoryId: v.categoryId!,
      sellingPrice: v.sellingPrice!,
      costPrice: v.costPrice!,
      reorderLevel: v.reorderLevel!,
      ...(this.isEdit() ? { isActive: v.isActive } : { initialStock: v.initialStock! }),
    };

    this.saving.set(true);
    this.saveError.set(null);
    const request = this.isEdit() ? this.products.update(Number(this.id()), input) : this.products.create(input);
    request.subscribe({
      next: (p) => {
        this.toast.success(this.isEdit() ? `Saved ${p.name}.` : `Created ${p.name}.`);
        void this.router.navigate(['/products']);
      },
      error: (err: unknown) => {
        this.saveError.set(errorMessage(err, 'Could not save the product.'));
        this.saving.set(false);
      },
    });
  }
}
