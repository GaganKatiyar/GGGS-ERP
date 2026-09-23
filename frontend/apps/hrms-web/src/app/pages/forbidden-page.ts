import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmptyState } from '@gggs/shared/ui';

/** Shown when a route guard refuses: the user is signed in but lacks the permission. */
@Component({
  selector: 'gggs-forbidden-page',
  imports: [EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-empty-state
      icon="pi pi-lock"
      title="You do not have access to this page"
      description="Ask your HR or system administrator to grant the permission you need."
    />
  `,
})
export class ForbiddenPage {}
