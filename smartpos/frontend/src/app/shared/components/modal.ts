import { ChangeDetectionStrategy, Component, effect, ElementRef, input, output, viewChild } from '@angular/core';
import { Icon } from './icon';

/**
 * Accessible modal built on the native <dialog> element: showModal() gives focus trapping,
 * Escape-to-close and an inert background without extra code.
 */
@Component({
  selector: 'app-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <dialog #dialog class="modal" [attr.aria-labelledby]="titleId" (close)="closed.emit()" (cancel)="onCancel($event)">
      <div class="modal-header">
        <h2 [id]="titleId">{{ title() }}</h2>
        <button type="button" class="btn btn-ghost btn-icon btn-sm" (click)="closed.emit()" aria-label="Close">
          <app-icon name="close" />
        </button>
      </div>
      <ng-content />
    </dialog>
  `,
})
export class Modal {
  private static nextId = 0;
  protected readonly titleId = `modal-title-${Modal.nextId++}`;

  readonly open = input.required<boolean>();
  readonly title = input.required<string>();
  /** When true, Escape does not close the dialog (e.g. while saving). */
  readonly busy = input(false);
  readonly closed = output<void>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const el = this.dialog().nativeElement;
      if (this.open() && !el.open) {
        el.showModal();
      } else if (!this.open() && el.open) {
        el.close();
      }
    });
  }

  protected onCancel(event: Event): void {
    if (this.busy()) {
      event.preventDefault();
    }
  }
}
