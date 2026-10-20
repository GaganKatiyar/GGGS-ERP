import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { PermissionStore } from '@gggs/core/permissions';
import type { UserSession } from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

export type SessionState = 'unknown' | 'authenticated' | 'anonymous';

/** Credentials as the sign-in page collects them. */
export interface PasswordSignInRequest {
  readonly email: string;
  readonly password: string;
  /** Usually taken from the subdomain; optional for customers with a verified email domain. */
  readonly tenantCode?: string;
}

/**
 * What the API says about an attempt.
 *
 * `errorCode` is what the page switches on. There is deliberately only one code for a wrong address
 * and a wrong password, because telling them apart would let anyone find out who has an account.
 */
export interface PasswordSignInResult {
  readonly succeeded: boolean;
  readonly errorCode?: string;
  readonly mustChangePassword?: boolean;
  readonly lockoutEndDate?: string;
  /** With `MFA_REQUIRED`: the token for the second step, posted back with the authenticator code. */
  readonly mfaChallenge?: string;
  /** Signed in, but the organisation requires an authenticator app that is not set up yet. */
  readonly mfaEnrollmentRequired?: boolean;
}

/** A "Sign in with …" button the organisation has configured. */
export interface SsoOption {
  readonly key: string;
  readonly displayName: string;
}

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

  /**
   * Sends the user to the sign-in page.
   *
   * An in-app navigation, not a redirect to the API: with local accounts the credentials are
   * collected by our own page and posted to `/bff/login`. Single sign-on is the second path, which
   * does redirect (see {@link startSingleSignOn}); both end at the same place, a session cookie
   * set by the API.
   */
  signIn(returnUrl: string = window.location.pathname): void {
    const target =
      returnUrl && returnUrl !== '/sign-in'
        ? `/sign-in?returnUrl=${encodeURIComponent(returnUrl)}`
        : '/sign-in';

    window.location.href = target;
  }

  /**
   * Signs in with a password.
   *
   * The response carries no credential: the API sets an httpOnly cookie and the session is then
   * read from `/bff/user`, so there is one description of a session rather than two that can drift.
   */
  async signInWithPassword(
    request: PasswordSignInRequest,
  ): Promise<PasswordSignInResult> {
    const result = await firstValueFrom(
      this.http.post<PasswordSignInResult>('/bff/login', request),
    );

    if (result.succeeded) {
      await this.load();
    }

    return result;
  }

  /** The second step of sign-in: a code from the authenticator app, or a recovery code. */
  async completeMfa(challenge: string, code: string): Promise<PasswordSignInResult> {
    const result = await firstValueFrom(
      this.http.post<PasswordSignInResult>('/bff/login/mfa', { challenge, code }),
    );

    if (result.succeeded) {
      await this.load();
    }

    return result;
  }

  /** The single sign-on providers an organisation offers; none for an unknown code. */
  singleSignOnOptions(tenantCode: string): Promise<readonly SsoOption[]> {
    return firstValueFrom(
      this.http.get<SsoOption[]>('/bff/sso/providers', { params: { tenant: tenantCode } }),
    );
  }

  /**
   * Leaves for the organisation's identity provider. A full navigation: the API redirects to the
   * provider, which sends the browser back to the API, which sets the session and returns here.
   */
  startSingleSignOn(tenantCode: string, provider: string, returnUrl: string): void {
    const query = new URLSearchParams({ tenant: tenantCode, provider, returnUrl });
    window.location.href = `/bff/sso/start?${query.toString()}`;
  }

  /** Confirms it is still the same person before a sensitive action: an authenticator code or the password. */
  async stepUp(proof: { readonly code?: string; readonly password?: string }): Promise<void> {
    await firstValueFrom(this.http.post<void>('/bff/step-up', proof));
  }

  /**
   * Ends the session, then reloads.
   *
   * A POST rather than a navigation, because signing out changes state and the API refuses
   * unsafe requests without the CSRF header - which a plain navigation cannot send. The reload is
   * what guarantees no store, cache or open screen keeps the previous user's data.
   */
  async signOut(): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>('/bff/logout', {}));
    } finally {
      this.clear();
      window.location.href = '/sign-in';
    }
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
