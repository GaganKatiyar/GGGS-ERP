import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

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

/** The organisation's single sign-on providers. */
@Injectable({ providedIn: 'root' })
export class SecurityApi {
  private readonly http = inject(HttpClient);

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
