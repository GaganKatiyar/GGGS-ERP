import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { CodeLinkSetup } from '@gggs/shared/models';

/** Where a code host should send its webhooks, so commits and pull requests that name a task link to it. */
@Component({
  selector: 'gggs-code-link-setup',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel stack cls">
      <h2 class="cls__heading">Code links</h2>
      <p class="muted cls__note">
        Commits and pull requests that name a task code, such as APOLLO-42, show on that task. Add a GitHub or Azure DevOps connection under
        <a routerLink="/studio/connectors">Studio</a> with a shared secret, then give the other side the address below.
      </p>
      <p class="muted cls__note">
        <strong>GitHub:</strong> repository settings, Webhooks, content type application/json, push and pull request events, and the same secret.
        <strong>Azure DevOps:</strong> project settings, Service hooks, Web Hooks, for code pushed and pull request events, with the secret as the basic authentication password.
      </p>
      @for (row of rows(); track row.connectionId) {
        <div class="cls__row">
          <strong>{{ row.name }}</strong>
          @if (!row.isActive) {
            <span class="muted cls__note">Switched off</span>
          }
          <code class="cls__url">{{ origin + row.webhookPath }}</code>
          <button type="button" class="btn btn--link btn--sm" (click)="copy(row)">{{ copied() === row.connectionId ? 'Copied' : 'Copy' }}</button>
        </div>
      } @empty {
        <p class="muted cls__note">No code host is connected yet.</p>
      }
    </section>
  `,
  styles: `
    .cls {
      margin-block: var(--gggs-space-4);
    }

    .cls__heading {
      margin: 0;
      font-size: var(--gggs-font-size-lg);
    }

    .cls__note {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .cls__row {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
      align-items: center;
    }

    .cls__url {
      overflow-wrap: anywhere;
      font-size: var(--gggs-font-size-sm);
    }
  `,
})
export class CodeLinkSetupPanel {
  private readonly api = inject(ProjectsApi);

  protected readonly origin = window.location.origin;
  protected readonly rows = signal<readonly CodeLinkSetup[]>([]);
  protected readonly copied = signal<string | null>(null);

  constructor() {
    this.api.codeLinkSetup().then(
      (rows) => this.rows.set(rows),
      () => undefined,
    );
  }

  protected async copy(row: CodeLinkSetup): Promise<void> {
    await navigator.clipboard.writeText(this.origin + row.webhookPath);
    this.copied.set(row.connectionId);
  }
}
