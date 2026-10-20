import { ChangeDetectionStrategy, Component, input, model, output } from '@angular/core';
import { DialogModule } from 'primeng/dialog';
import { DrawerModule } from 'primeng/drawer';

/**
 * A side panel for detail and quick edits that should not take the user away from the list.
 * Content is projected; the header text comes from `title`.
 */
@Component({
  selector: 'gggs-drawer',
  imports: [DrawerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-drawer
      [(visible)]="visible"
      [header]="title()"
      [position]="position()"
      [modal]="true"
      [style]="{ width: width() }"
      [blockScroll]="true"
    >
      <ng-content />
    </p-drawer>
  `,
})
export class Drawer {
  readonly visible = model(false);
  readonly title = input('');
  readonly position = input<'right' | 'left'>('right');
  readonly width = input('min(36rem, 100vw)');
}

const DIALOG_WIDTHS = { sm: 'min(28rem, 96vw)', md: 'min(40rem, 96vw)', lg: 'min(56rem, 96vw)' } as const;

let nextFormId = 0;

/**
 * A modal form: title, projected fields, and Save / Cancel. The caller owns the form and the save;
 * this owns the frame, the busy state and the error line, so every dialog behaves the same way.
 * The actions sit in a fixed footer, so a long form scrolls without taking Save out of reach.
 */
@Component({
  selector: 'gggs-form-dialog',
  imports: [DialogModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-dialog
      [(visible)]="visible"
      [header]="title()"
      [modal]="true"
      [draggable]="false"
      [dismissableMask]="!busy()"
      [closable]="!busy()"
      [style]="{ width: width() ?? widths[size()] }"
      styleClass="gggs-dialog"
    >
      <form [id]="formId" class="stack" (submit)="$event.preventDefault(); save.emit()" novalidate>
        @if (description()) {
          <p class="form-dialog__description">{{ description() }}</p>
        }

        <ng-content />

        @if (error()) {
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i>{{ error() }}</p>
        }
      </form>

      <ng-template #footer>
        <div class="form-dialog__actions">
          <button type="button" class="btn" [disabled]="busy()" (click)="visible.set(false)">Cancel</button>
          <button type="submit" [attr.form]="formId" class="btn" [class.btn--primary]="!danger()" [class.btn--danger-solid]="danger()" [disabled]="busy() || saveDisabled()">
            {{ busy() ? 'Saving…' : saveLabel() }}
          </button>
        </div>
      </ng-template>
    </p-dialog>
  `,
  styles: `
    .form-dialog__description {
      margin: 0;
      color: var(--gggs-color-text-muted);
    }

    .form-dialog__actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--gggs-space-2);
      width: 100%;
    }

    .alert {
      margin: 0;
    }
  `,
})
export class FormDialog {
  protected readonly widths = DIALOG_WIDTHS;
  protected readonly formId = `gggs-form-dialog-${++nextFormId}`;
  readonly visible = model(false);
  readonly title = input('');
  /** A line under the title saying what the form is for. */
  readonly description = input<string | undefined>(undefined);
  readonly saveLabel = input('Save');
  readonly busy = input(false);
  readonly saveDisabled = input(false);
  /** The save destroys or removes something: the button is red. */
  readonly danger = input(false);
  readonly error = input<string | null>(null);
  readonly size = input<keyof typeof DIALOG_WIDTHS>('md');
  /** An exact width, overriding `size`. */
  readonly width = input<string | undefined>(undefined);
  readonly save = output<void>();
}
