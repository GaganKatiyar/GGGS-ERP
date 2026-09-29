import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ConfirmDialogHost, ToastHost } from '@gggs/shared/ui';

/**
 * Root component. Everything else hangs off the router: the shell is a route, not a wrapper.
 *
 * The toast/confirm-dialog hosts live here rather than in the shell so sign-in and
 * complete-invitation (both outside the shell) can use them too.
 */
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
