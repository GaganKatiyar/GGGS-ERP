import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { PermissionStore } from '@gggs/core/permissions';
import type { UserSession } from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

export type SessionState = 'unknown' | 'authenticated' | 'anonymous';

/**
 * The signed-in user, loaded from the BFF.
 *
 * The browser never holds a token. `/bff/user` answers from the httpOnly cookie, and the BFF adds
 * the access token when it proxies API calls. That means nothing in this application - or in any
 * script that reaches the page - can read or exfiltrate a credential.
 */
@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly http = inject(HttpClient);
  private readonly permissions = inject(PermissionStore);

  private readonly session = signal<UserSession | null>(null);
  private readonly state = signal<SessionState>('unknown');

  readonly user = this.session.asReadonly();
  readonly status = this.state.asReadonly();
  readonly isAuthenticated = computed(() => this.state() === 'authenticated');
  readonly displayName = computed(() => this.session()?.displayName ?? '');
  readonly canSwitchTenant = computed(
    () => (this.session()?.availableTenants.length ?? 0) > 1,
  );

  /**
   * Loads the session once at startup. A 401 here is not an error: it is how an anonymous visitor
   * looks, and the router sends them to sign in.
   */
  async load(): Promise<UserSession | null> {
    try {
      const session = await firstValueFrom(
        this.http.get<UserSession>('/bff/user'),
      );

      this.session.set(session);
      this.state.set('authenticated');
      this.permissions.setPermissions(session.permissions, session.features);
      return session;
    } catch {
      this.clear();
      return null;
    }
  }

  /** Sends the user to the BFF's login endpoint, which starts the OIDC flow. */
  signIn(returnUrl: string = window.location.pathname): void {
    window.location.href = `/bff/login?returnUrl=${encodeURIComponent(returnUrl)}`;
  }

  /** Full-page navigation so the BFF can clear its cookie and sign out of the upstream provider. */
  signOut(): void {
    this.clear();
    window.location.href = '/bff/logout';
  }

  /**
   * Switches tenant by exchanging the session for one in another tenant. The page is reloaded
   * rather than patched: every store, cache and open screen holds the previous tenant's data, and
   * reloading is the only way to be certain none of it survives the switch.
   */
  switchTenant(tenantId: string): void {
    window.location.href = `/bff/switch-tenant?tenantId=${encodeURIComponent(tenantId)}`;
  }

  private clear(): void {
    this.session.set(null);
    this.state.set('anonymous');
    this.permissions.clear();
  }
}
