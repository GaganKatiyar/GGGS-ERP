import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PageHeader } from '@gggs/shared/ui';

@Component({
  selector: 'gggs-console-forbidden-page',
  imports: [PageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Not allowed" subtitle="Your platform roles do not include this page." />
    <p class="muted">Ask a platform staff administrator to give you a role that does (Platform staff › Roles).</p>
  `,
})
export class ConsoleForbiddenPage {}
