import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmptyState } from '@gggs/shared/ui';

/**
 * Shown when the module exists but the tenant's plan does not include it.
 *
 * Deliberately separate from the forbidden page: "ask your administrator for access" is the wrong
 * advice when no administrator can grant it, and the real answer is a plan change.
 */
@Component({
  selector: 'gggs-feature-unavailable-page',
  imports: [EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-empty-state
      icon="pi pi-box"
      title="This module is not part of your plan"
      description="Contact your account manager to add it to your subscription."
    />
  `,
})
export class FeatureUnavailablePage {}
