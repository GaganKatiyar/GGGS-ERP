import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  AdministratorInvitationSent,
  AuditItem,
  CreateOrganisationBody,
  DashboardView,
  DatabaseTestResult,
  OrganisationDatabaseView,
  RevealedDatabasePassword,
  SaveDatabaseBody,
  HealthView,
  InviteStaffBody,
  InviteStaffResult,
  OrganisationAdministrator,
  OrganisationDetail,
  OrganisationRow,
  PlansView,
  PlatformRoleRow,
  PlatformStaffRow,
  SetupBody,
  TenantStatus,
  UpdateSubscriptionBody,
} from './platform.models';

const BASE = '/api/v1/platform';

/**
 * The platform console server's API (Gggs.PlatformApi). Promise-based like the HR app's feature APIs:
 * every screen loads once and reloads after a change, so there is no stream to keep open.
 */
@Injectable({ providedIn: 'root' })
export class PlatformApi {
  private readonly http = inject(HttpClient);

  dashboard(): Promise<DashboardView> {
    return firstValueFrom(this.http.get<DashboardView>(`${BASE}/dashboard`));
  }

  organisations(status?: TenantStatus | null, search?: string | null): Promise<OrganisationRow[]> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    if (search?.trim()) params = params.set('search', search.trim());
    return firstValueFrom(this.http.get<OrganisationRow[]>(`${BASE}/organisations`, { params }));
  }

  organisation(id: string): Promise<OrganisationDetail> {
    return firstValueFrom(this.http.get<OrganisationDetail>(`${BASE}/organisations/${id}`));
  }

  activity(id: string): Promise<AuditItem[]> {
    return firstValueFrom(this.http.get<AuditItem[]>(`${BASE}/organisations/${id}/activity`));
  }

  createOrganisation(body: CreateOrganisationBody): Promise<OrganisationDetail> {
    return firstValueFrom(this.http.post<OrganisationDetail>(`${BASE}/organisations`, body));
  }

  activate(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${BASE}/organisations/${id}/activate`, {}));
  }

  suspend(id: string, reason: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${BASE}/organisations/${id}/suspend`, { reason }));
  }

  reactivate(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${BASE}/organisations/${id}/reactivate`, {}));
  }

  offboard(id: string, graceDays: number): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${BASE}/organisations/${id}/offboard`, { graceDays }));
  }

  updateSubscription(id: string, body: UpdateSubscriptionBody): Promise<OrganisationDetail> {
    return firstValueFrom(this.http.put<OrganisationDetail>(`${BASE}/organisations/${id}/subscription`, body));
  }

  updateModules(id: string, enabled: readonly string[]): Promise<OrganisationDetail> {
    return firstValueFrom(this.http.put<OrganisationDetail>(`${BASE}/organisations/${id}/modules`, { enabled }));
  }

  administrators(id: string): Promise<OrganisationAdministrator[]> {
    return firstValueFrom(this.http.get<OrganisationAdministrator[]>(`${BASE}/organisations/${id}/administrators`));
  }

  resendAdministratorInvitation(id: string, email: string): Promise<AdministratorInvitationSent> {
    return firstValueFrom(
      this.http.post<AdministratorInvitationSent>(`${BASE}/organisations/${id}/administrators/resend`, { email }),
    );
  }

  addAdministrator(id: string, email: string, displayName: string | null): Promise<AdministratorInvitationSent> {
    return firstValueFrom(
      this.http.post<AdministratorInvitationSent>(`${BASE}/organisations/${id}/administrators`, { email, displayName }),
    );
  }

  database(id: string): Promise<OrganisationDatabaseView> {
    return firstValueFrom(this.http.get<OrganisationDatabaseView>(`${BASE}/organisations/${id}/database`));
  }

  saveDatabase(id: string, body: SaveDatabaseBody): Promise<OrganisationDatabaseView> {
    return firstValueFrom(this.http.put<OrganisationDatabaseView>(`${BASE}/organisations/${id}/database`, body));
  }

  removeDatabase(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${BASE}/organisations/${id}/database`));
  }

  testDatabase(id: string, body: SaveDatabaseBody): Promise<DatabaseTestResult> {
    return firstValueFrom(this.http.post<DatabaseTestResult>(`${BASE}/organisations/${id}/database/test`, body));
  }

  revealDatabasePassword(id: string, reason: string): Promise<RevealedDatabasePassword> {
    return firstValueFrom(this.http.post<RevealedDatabasePassword>(`${BASE}/organisations/${id}/database/reveal`, { reason }));
  }

  plans(): Promise<PlansView> {
    return firstValueFrom(this.http.get<PlansView>(`${BASE}/plans`));
  }

  audit(take = 200): Promise<AuditItem[]> {
    return firstValueFrom(this.http.get<AuditItem[]>(`${BASE}/audit`, { params: { take } }));
  }

  health(): Promise<HealthView> {
    return firstValueFrom(this.http.get<HealthView>(`${BASE}/health`));
  }

  retryJob(jobId: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${BASE}/health/jobs/${encodeURIComponent(jobId)}/retry`, {}));
  }

  staff(): Promise<PlatformStaffRow[]> {
    return firstValueFrom(this.http.get<PlatformStaffRow[]>(`${BASE}/staff`));
  }

  roles(): Promise<PlatformRoleRow[]> {
    return firstValueFrom(this.http.get<PlatformRoleRow[]>(`${BASE}/staff/roles`));
  }

  inviteStaff(body: InviteStaffBody): Promise<InviteStaffResult> {
    return firstValueFrom(this.http.post<InviteStaffResult>(`${BASE}/staff/invite`, body));
  }

  disableStaff(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${BASE}/staff/${id}/disable`, {}));
  }

  unlockStaff(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${BASE}/staff/${id}/unlock`, {}));
  }

  setStaffRoles(id: string, roleIds: readonly string[]): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${BASE}/staff/${id}/roles`, { roleIds }));
  }

  changeOwnPassword(currentPassword: string, newPassword: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${BASE}/staff/me/password`, { currentPassword, newPassword }));
  }

  completeInvitation(platformStaffId: string, token: string, newPassword: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`${BASE}/staff/complete-invitation`, { platformStaffId, token, newPassword }),
    );
  }

  setupOpen(): Promise<{ open: boolean }> {
    return firstValueFrom(this.http.get<{ open: boolean }>(`${BASE}/setup`));
  }

  setup(body: SetupBody): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${BASE}/setup`, body));
  }
}
