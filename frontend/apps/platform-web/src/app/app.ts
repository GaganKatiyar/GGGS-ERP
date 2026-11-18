import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ConfirmDialogHost, ToastHost } from '@gggs/shared/ui';

/** The platform console's root. Everything hangs off the router (libs/platform/console). */
@Component({
  selector: 'gggs-root',
  imports: [RouterOutlet, ToastHost, ConfirmDialogHost],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <router-outlet />
    <gggs-toast-host />
    <gggs-confirm-dialog-host />
  `,
})
export class App {}
