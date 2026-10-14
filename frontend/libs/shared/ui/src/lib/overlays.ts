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

/**
 * A modal form: title, projected fields, and Save / Cancel. The caller owns the form and the save;
 * this owns the frame, the busy state and the error line, so every dialog behaves the same way.
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
      [style]="{ width: width() }"
    >
      <form class="stack" (submit)="$event.preventDefault(); save.emit()" novalidate>
        <ng-content />

        @if (error()) {
          <p class="field__error" role="alert">{{ error() }}</p>
        }

        <div class="toolbar form-dialog__actions">
          <button type="button" class="btn" [disabled]="busy()" (click)="visible.set(false)">Cancel</button>
          <button type="submit" class="btn btn--primary" [disabled]="busy() || saveDisabled()">
            {{ busy() ? 'Saving…' : saveLabel() }}
          </button>
        </div>
      </form>
    </p-dialog>
  `,
  styles: `
    .form-dialog__actions {
      justify-content: flex-end;
    }
  `,
})
export class FormDialog {
  readonly visible = model(false);
  readonly title = input('');
  readonly saveLabel = input('Save');
  readonly busy = input(false);
  readonly saveDisabled = input(false);
  readonly error = input<string | null>(null);
  readonly width = input('min(40rem, 96vw)');
  readonly save = output<void>();
}
