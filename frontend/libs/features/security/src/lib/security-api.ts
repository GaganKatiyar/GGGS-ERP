import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface MfaStatus {
  readonly enabled: boolean;
  readonly enrolledDate: string | null;
  readonly recoveryCodesLeft: number;
  readonly required: boolean;
}

export interface MfaSetup {
  readonly secret: string;
  readonly provisioningUri: string;
}

export interface MfaCodes {
  readonly recoveryCodes: readonly string[];
}

export interface SsoProvider {
  readonly id: string;
  readonly key: string;
  readonly displayName: string;
  readonly authority: string;
  readonly clientId: string;
  readonly hasClientSecret: boolean;
  readonly scopes: string;
  readonly allowedDomains: string | null;
  readonly enabled: boolean;
}

export interface SsoProviderRequest {
  readonly key: string;
  readonly displayName: string;
  readonly authority: string;
  readonly clientId: string;
  /** Null leaves the stored secret alone; an empty string removes it. */
  readonly clientSecret: string | null;
  readonly scopes: string | null;
  readonly allowedDomains: string | null;
  readonly enabled: boolean;
}

/** The signed-in user's multi-factor sign-in, and the organisation's single sign-on providers. */
@Injectable({ providedIn: 'root' })
export class SecurityApi {
  private readonly http = inject(HttpClient);

  mfa(): Promise<MfaStatus> {
    return firstValueFrom(this.http.get<MfaStatus>('/api/v1/me/security/mfa'));
  }

  setup(): Promise<MfaSetup> {
    return firstValueFrom(this.http.post<MfaSetup>('/api/v1/me/security/mfa/setup', null));
  }

  confirm(code: string): Promise<MfaCodes> {
    return firstValueFrom(this.http.post<MfaCodes>('/api/v1/me/security/mfa/confirm', { code }));
  }

  regenerate(code: string): Promise<MfaCodes> {
    return firstValueFrom(this.http.post<MfaCodes>('/api/v1/me/security/mfa/recovery-codes', { code }));
  }

  disable(code: string): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/me/security/mfa/disable', { code }));
  }

  providers(): Promise<readonly SsoProvider[]> {
    return firstValueFrom(this.http.get<SsoProvider[]>('/api/v1/admin/identity-providers'));
  }

  createProvider(body: SsoProviderRequest): Promise<SsoProvider> {
    return firstValueFrom(this.http.post<SsoProvider>('/api/v1/admin/identity-providers', body));
  }

  updateProvider(id: string, body: SsoProviderRequest): Promise<SsoProvider> {
    return firstValueFrom(this.http.put<SsoProvider>(`/api/v1/admin/identity-providers/${id}`, body));
  }

  deleteProvider(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/v1/admin/identity-providers/${id}`));
  }
}
