import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { Icon } from './icon';

@Component({
  selector: 'app-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    @if (totalCount() > 0) {
      <nav class="pager" aria-label="Pagination">
        <span class="muted">{{ rangeLabel() }}</span>
        <div class="pager-buttons">
          <button type="button" class="btn btn-secondary btn-sm" [disabled]="page() <= 1"
                  (click)="pageChange.emit(page() - 1)" aria-label="Previous page">
            <app-icon name="chevronLeft" [size]="16" /> Prev
          </button>
          <span class="pager-current">Page {{ page() }} of {{ totalPages() }}</span>
          <button type="button" class="btn btn-secondary btn-sm" [disabled]="page() >= totalPages()"
                  (click)="pageChange.emit(page() + 1)" aria-label="Next page">
            Next <app-icon name="chevronRight" [size]="16" />
          </button>
        </div>
      </nav>
    }
  `,
  styles: `
    .pager { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 10px; padding: 12px 18px; border-top: 1px solid var(--color-border); }
    .pager-buttons { display: flex; align-items: center; gap: 10px; }
    .pager-current { font-size: 0.8125rem; color: var(--color-text-muted); }
  `,
})
export class Pagination {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly totalCount = input.required<number>();
  readonly totalPages = input.required<number>();
  readonly pageChange = output<number>();

  protected readonly rangeLabel = computed(() => {
    const start = (this.page() - 1) * this.pageSize() + 1;
    const end = Math.min(this.page() * this.pageSize(), this.totalCount());
    return `${start}–${end} of ${this.totalCount()}`;
  });
}
