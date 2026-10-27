import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { StockMovementType, StockStatus } from '../../core/models';

@Component({
  selector: 'app-stock-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="badge" [class]="cssClass()">{{ label() }}</span>`,
})
export class StockBadge {
  readonly status = input.required<StockStatus>();
  protected readonly label = computed(
    () => ({ InStock: 'In stock', Low: 'Low stock', OutOfStock: 'Out of stock' })[this.status()],
  );
  protected readonly cssClass = computed(
    () => ({ InStock: 'badge-success', Low: 'badge-warning', OutOfStock: 'badge-danger' })[this.status()],
  );
}

@Component({
  selector: 'app-movement-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="badge" [class]="cssClass()">{{ label() }}</span>`,
})
export class MovementBadge {
  readonly type = input.required<StockMovementType>();
  protected readonly label = computed(
    () => ({ InitialStock: 'Opening stock', Sale: 'Sale', Adjustment: 'Adjustment' })[this.type()],
  );
  protected readonly cssClass = computed(
    () => ({ InitialStock: 'badge-neutral', Sale: 'badge-info', Adjustment: 'badge-warning' })[this.type()],
  );
}
