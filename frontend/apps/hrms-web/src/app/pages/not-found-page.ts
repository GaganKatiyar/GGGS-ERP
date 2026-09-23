import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmptyState } from '@gggs/shared/ui';

@Component({
  selector: 'gggs-not-found-page',
  imports: [EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-empty-state
      icon="pi pi-compass"
      title="Page not found"
      description="The page you asked for does not exist, or has moved."
    />
  `,
})
export class NotFoundPage {}
