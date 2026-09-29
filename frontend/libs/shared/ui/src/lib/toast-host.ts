import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ToastModule } from 'primeng/toast';

/**
 * Placed once in the app root. Any feature can `inject(MessageService)` (from `primeng/api`) and
 * call `.add(...)` - this is just where the message actually renders, kept out of feature code per
 * ADR-10 so PrimeNG's toast is the only thing that would change if the kit ever did.
 */
@Component({
  selector: 'gggs-toast-host',
  imports: [ToastModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p-toast />`,
})
export class ToastHost {}
