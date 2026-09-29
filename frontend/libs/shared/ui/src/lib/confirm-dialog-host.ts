import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ConfirmDialogModule } from 'primeng/confirmdialog';

/**
 * Placed once in the app root. Any feature can `inject(ConfirmationService)` (from `primeng/api`)
 * and call `.confirm({...})` for a destructive action (deactivate, delete, exit) - this is just
 * where the dialog actually renders, so every confirmation looks the same without each page
 * building its own.
 */
@Component({
  selector: 'gggs-confirm-dialog-host',
  imports: [ConfirmDialogModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p-confirmdialog />`,
})
export class ConfirmDialogHost {}
