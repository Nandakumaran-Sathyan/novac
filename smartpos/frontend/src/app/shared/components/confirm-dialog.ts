import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ConfirmService } from '../../core/ui/confirm.service';
import { Modal } from './modal';

/** Rendered once in the shell; driven by ConfirmService.ask(). */
@Component({
  selector: 'app-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Modal],
  template: `
    <app-modal [open]="!!request()" [title]="request()?.title ?? ''" (closed)="confirm.close(false)">
      <div class="modal-body">
        <p>{{ request()?.message }}</p>
      </div>
      <div class="modal-footer">
        <button type="button" class="btn btn-secondary" (click)="confirm.close(false)">Cancel</button>
        <button type="button" class="btn" [class.btn-danger]="request()?.danger" [class.btn-primary]="!request()?.danger"
                (click)="confirm.close(true)">
          {{ request()?.confirmText ?? 'Confirm' }}
        </button>
      </div>
    </app-modal>
  `,
})
export class ConfirmDialog {
  protected readonly confirm = inject(ConfirmService);
  protected readonly request = computed(() => this.confirm.pending());
}
