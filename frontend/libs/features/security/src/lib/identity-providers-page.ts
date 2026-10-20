import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { EmptyState, PageHeader, StatusBadge } from '@gggs/shared/ui';
import { SecurityApi, type SsoProvider } from './security-api';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

interface Draft {
  id: string | null;
  key: string;
  displayName: string;
  authority: string;
  clientId: string;
  clientSecret: string;
  scopes: string;
  allowedDomains: string;
  enabled: boolean;
  hasClientSecret: boolean;
}

const EMPTY: Draft = {
  id: null,
  key: '',
  displayName: '',
  authority: '',
  clientId: '',
  clientSecret: '',
  scopes: 'openid profile email',
  allowedDomains: '',
  enabled: true,
  hasClientSecret: false,
};

/**
 * The organisation's single sign-on providers (OpenID Connect): Entra ID, Okta, Google Workspace,
 * Keycloak. People sign in with their work account; accounts are matched by verified email and never
 * created by the provider.
 */
@Component({
  selector: 'gggs-identity-providers-page',
  imports: [PageHeader, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Single sign-on" subtitle="Let people sign in with their organisation's account">
      <button type="button" class="btn btn--primary" (click)="edit(null)"><i class="pi pi-plus" aria-hidden="true"></i> Add provider</button>
    </gggs-page-header>
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }

    @if (draft(); as d) {
      <form class="panel form-grid sso__form" (submit)="$event.preventDefault(); save()">
        <div class="field">
          <label class="field__label" for="sso-name">Button label</label>
          <input id="sso-name" required maxlength="100" placeholder="Microsoft" [value]="d.displayName" (input)="patch({ displayName: $any($event.target).value })" />
        </div>
        <div class="field">
          <label class="field__label" for="sso-key">Key</label>
          <input id="sso-key" required maxlength="40" placeholder="entra" [disabled]="!!d.id" [value]="d.key" (input)="patch({ key: $any($event.target).value })" />
          <span class="field__hint">Lower-case letters, digits and hyphens.</span>
        </div>
        <div class="field form-grid__full">
          <label class="field__label" for="sso-authority">Authority (issuer)</label>
          <input id="sso-authority" required type="url" placeholder="https://login.microsoftonline.com/your-tenant-id/v2.0" [value]="d.authority" (input)="patch({ authority: $any($event.target).value })" />
        </div>
        <div class="field">
          <label class="field__label" for="sso-client">Client ID</label>
          <input id="sso-client" required [value]="d.clientId" (input)="patch({ clientId: $any($event.target).value })" />
        </div>
        <div class="field">
          <label class="field__label" for="sso-secret">Client secret</label>
          <input id="sso-secret" type="password" autocomplete="new-password" [placeholder]="d.hasClientSecret ? 'Stored - leave blank to keep' : 'Optional with PKCE'" [value]="d.clientSecret"
            (input)="patch({ clientSecret: $any($event.target).value })" />
        </div>
        <div class="field">
          <label class="field__label" for="sso-scopes">Scopes</label>
          <input id="sso-scopes" [value]="d.scopes" (input)="patch({ scopes: $any($event.target).value })" />
        </div>
        <div class="field">
          <label class="field__label" for="sso-domains">Allowed email domains</label>
          <input id="sso-domains" placeholder="acme.com, acme.in" [value]="d.allowedDomains" (input)="patch({ allowedDomains: $any($event.target).value })" />
          <span class="field__hint">Empty allows any address that matches an account.</span>
        </div>
        <label class="form-grid__full sso__check">
          <input type="checkbox" [checked]="d.enabled" (change)="patch({ enabled: $any($event.target).checked })" /> Show on the sign-in page
        </label>
        <p class="form-grid__full muted">Register <code>{{ callback }}</code> as the redirect URI with the provider.</p>
        <div class="toolbar form-grid__full">
          <button type="submit" class="btn btn--primary" [disabled]="busy()">Save</button>
          <button type="button" class="btn" (click)="draft.set(null)">Cancel</button>
        </div>
      </form>
    }

    <div class="panel">
      @for (provider of providers(); track provider.id) {
        <div class="sso__row">
          <div class="stack sso__info">
            <strong>{{ provider.displayName }} <span class="muted">({{ provider.key }})</span></strong>
            <span class="muted">{{ provider.authority }}</span>
            @if (provider.allowedDomains) {
              <span class="muted">Domains: {{ provider.allowedDomains }}</span>
            }
          </div>
          <gggs-status-badge [status]="provider.enabled ? 'Enabled' : 'Disabled'" [variants]="{ Enabled: 'success', Disabled: 'neutral' }" />
          <button type="button" class="btn btn--sm" (click)="edit(provider)">Edit</button>
          <button type="button" class="btn btn--sm btn--danger" [attr.aria-label]="'Remove ' + provider.displayName" (click)="remove(provider)"><i class="pi pi-trash" aria-hidden="true"></i></button>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-key" title="No single sign-on yet" description="People sign in with their PeopleOS password until a provider is added." />
      }
    </div>
  `,
  styles: `
    .sso__form {
      margin-block-end: var(--gggs-space-3);
    }
    .sso__check {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
    }
    .sso__row {
      display: flex;
      gap: var(--gggs-space-3);
      align-items: center;
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }
    .sso__info {
      flex: 1;
      min-width: 0;
      gap: 2px;
    }
  `,
})
export class IdentityProvidersPage {
  private readonly api = inject(SecurityApi);
  protected readonly callback = `${location.origin}/bff/sso/callback`;
  protected readonly providers = signal<readonly SsoProvider[]>([]);
  protected readonly draft = signal<Draft | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected edit(provider: SsoProvider | null): void {
    this.error.set(null);
    this.draft.set(
      provider
        ? { ...provider, clientSecret: '', allowedDomains: provider.allowedDomains ?? '' }
        : { ...EMPTY },
    );
  }

  protected patch(change: Partial<Draft>): void {
    const current = this.draft();
    if (current) {
      this.draft.set({ ...current, ...change });
    }
  }

  protected async save(): Promise<void> {
    const d = this.draft();
    if (!d) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const body = {
      key: d.key.trim(),
      displayName: d.displayName.trim(),
      authority: d.authority.trim(),
      clientId: d.clientId.trim(),
      clientSecret: d.clientSecret ? d.clientSecret : d.id ? null : '',
      scopes: d.scopes.trim() || null,
      allowedDomains: d.allowedDomains.trim() || null,
      enabled: d.enabled,
    };
    try {
      await (d.id ? this.api.updateProvider(d.id, body) : this.api.createProvider(body));
      this.draft.set(null);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'The provider could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(provider: SsoProvider): Promise<void> {
    if (!confirm(`Remove ${provider.displayName}? People will sign in with their password instead.`)) {
      return;
    }
    try {
      await this.api.deleteProvider(provider.id);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'The provider could not be removed.'));
    }
  }

  private async load(): Promise<void> {
    try {
      this.providers.set(await this.api.providers());
    } catch (error) {
      this.error.set(message(error, 'The providers could not be loaded.'));
    }
  }
}
