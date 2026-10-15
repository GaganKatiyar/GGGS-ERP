import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  AllocationOutcome,
  BenchItem,
  CalendarRow,
  CapacityState,
  CertificationInput,
  EmployeeSkillInput,
  ForecastReport,
  MatchItem,
  MatrixRow,
  MissingTimesheet,
  PagedResult,
  RequestStatus,
  ResourceDashboard,
  ResourceProfileView,
  ResourceRequestDetail,
  ResourceRequestInput,
  ResourceRequestItem,
  ResourceRow,
  SkillItem,
  SkillProfile,
  SkilledPerson,
  TimesheetEntryInput,
  TimesheetIssue,
  TimesheetRow,
  TimesheetRules,
  TimesheetStatus,
  TimesheetView,
  TransferItem,
  TransferStatus,
  UtilizationGrouping,
  UtilizationReport,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

type Query = Record<string, string | number | boolean | null | undefined>;

function params(query: Query): Record<string, string | number | boolean> {
  const result: Record<string, string | number | boolean> = {};
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') {
      result[key] = value;
    }
  }
  return result;
}

/** Skills (`/api/v1/skills`, `/api/v1/me/skills`). */
@Injectable({ providedIn: 'root' })
export class SkillsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/skills';

  catalog(query: { search?: string; category?: string; includeInactive?: boolean } = {}): Promise<readonly SkillItem[]> {
    return firstValueFrom(this.http.get<readonly SkillItem[]>(this.base, { params: params({ ...query }) }));
  }

  saveSkill(id: string | null, body: { code: string; name: string; category: string; description?: string | null }): Promise<SkillItem> {
    return firstValueFrom(id ? this.http.put<SkillItem>(`${this.base}/${id}`, body) : this.http.post<SkillItem>(this.base, body));
  }

  async setSkillActive(id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }

  matrix(category?: string): Promise<readonly MatrixRow[]> {
    return firstValueFrom(this.http.get<readonly MatrixRow[]>(`${this.base}/matrix`, { params: params({ category }) }));
  }

  peopleWith(skillId: string, minLevel = 1): Promise<readonly SkilledPerson[]> {
    return firstValueFrom(this.http.get<readonly SkilledPerson[]>(`${this.base}/${skillId}/people`, { params: { minLevel } }));
  }

  profile(employeeId: string): Promise<SkillProfile> {
    return firstValueFrom(this.http.get<SkillProfile>(`${this.base}/employees/${employeeId}`));
  }

  assess(employeeId: string, body: EmployeeSkillInput): Promise<SkillProfile> {
    return firstValueFrom(this.http.put<SkillProfile>(`${this.base}/employees/${employeeId}`, body));
  }

  removeSkill(employeeId: string, skillId: string): Promise<SkillProfile> {
    return firstValueFrom(this.http.delete<SkillProfile>(`${this.base}/employees/${employeeId}/${skillId}`));
  }

  addCertification(employeeId: string, body: CertificationInput): Promise<SkillProfile> {
    return firstValueFrom(this.http.post<SkillProfile>(`${this.base}/employees/${employeeId}/certifications`, body));
  }

  removeCertification(employeeId: string, certificationId: string): Promise<SkillProfile> {
    return firstValueFrom(this.http.delete<SkillProfile>(`${this.base}/employees/${employeeId}/certifications/${certificationId}`));
  }

  mine(): Promise<SkillProfile> {
    return firstValueFrom(this.http.get<SkillProfile>('/api/v1/me/skills'));
  }

  myCatalog(): Promise<readonly SkillItem[]> {
    return firstValueFrom(this.http.get<readonly SkillItem[]>('/api/v1/me/skills/catalog'));
  }

  assessMine(body: EmployeeSkillInput): Promise<SkillProfile> {
    return firstValueFrom(this.http.put<SkillProfile>('/api/v1/me/skills', body));
  }

  removeMine(skillId: string): Promise<SkillProfile> {
    return firstValueFrom(this.http.delete<SkillProfile>(`/api/v1/me/skills/${skillId}`));
  }

  addMyCertification(body: CertificationInput): Promise<SkillProfile> {
    return firstValueFrom(this.http.post<SkillProfile>('/api/v1/me/skills/certifications', body));
  }

  removeMyCertification(certificationId: string): Promise<SkillProfile> {
    return firstValueFrom(this.http.delete<SkillProfile>(`/api/v1/me/skills/certifications/${certificationId}`));
  }
}

/** Timesheets (`/api/v1/me/timesheets`, `/api/v1/timesheets`). */
@Injectable({ providedIn: 'root' })
export class TimesheetsApi {
  private readonly http = inject(HttpClient);
  private readonly mine = '/api/v1/me/timesheets';
  private readonly base = '/api/v1/timesheets';

  week(week?: string): Promise<TimesheetView> {
    return firstValueFrom(this.http.get<TimesheetView>(this.mine, { params: params({ week }) }));
  }

  saveWeek(week: string, entries: readonly TimesheetEntryInput[], comment: string | null): Promise<TimesheetView> {
    return firstValueFrom(this.http.put<TimesheetView>(`${this.mine}/${week}`, { entries, comment }));
  }

  copyPrevious(week: string): Promise<TimesheetView> {
    return firstValueFrom(this.http.post<TimesheetView>(`${this.mine}/${week}/copy-previous`, {}));
  }

  submit(week: string): Promise<{ status: TimesheetStatus; workflowInstanceId?: string; issues: readonly TimesheetIssue[] }> {
    return firstValueFrom(this.http.post<{ status: TimesheetStatus; workflowInstanceId?: string; issues: readonly TimesheetIssue[] }>(`${this.mine}/${week}/submit`, {}));
  }

  async withdraw(week: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.mine}/${week}/withdraw`, {}));
  }

  list(query: { weekStart?: string; status?: TimesheetStatus | ''; employeeId?: string; page?: number; pageSize?: number } = {}): Promise<PagedResult<TimesheetRow>> {
    return firstValueFrom(this.http.get<PagedResult<TimesheetRow>>(this.base, { params: params({ ...query }) }));
  }

  get(id: string): Promise<TimesheetView> {
    return firstValueFrom(this.http.get<TimesheetView>(`${this.base}/${id}`));
  }

  missing(week: string): Promise<readonly MissingTimesheet[]> {
    return firstValueFrom(this.http.get<readonly MissingTimesheet[]>(`${this.base}/missing`, { params: { week } }));
  }

  async reopen(id: string, reason: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/reopen`, { reason }));
  }

  rules(): Promise<TimesheetRules> {
    return firstValueFrom(this.http.get<TimesheetRules>(`${this.base}/settings`));
  }

  saveRules(body: TimesheetRules): Promise<TimesheetRules> {
    return firstValueFrom(this.http.put<TimesheetRules>(`${this.base}/settings`, body));
  }

  lock(through: string): Promise<TimesheetRules> {
    return firstValueFrom(this.http.post<TimesheetRules>(`${this.base}/settings/lock`, { through }));
  }
}

/** Resource management (`/api/v1/resources`, `/api/v1/resource-requests`, `/api/v1/resource-allocations`). */
@Injectable({ providedIn: 'root' })
export class ResourcesApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/resources';

  list(query: { search?: string; skillId?: string; minLevel?: number; departmentId?: string; state?: CapacityState | ''; minAvailable?: number; page?: number; pageSize?: number } = {}): Promise<PagedResult<ResourceRow>> {
    return firstValueFrom(this.http.get<PagedResult<ResourceRow>>(this.base, { params: params({ ...query }) }));
  }

  dashboard(): Promise<ResourceDashboard> {
    return firstValueFrom(this.http.get<ResourceDashboard>(`${this.base}/dashboard`));
  }

  profile(employeeId: string): Promise<ResourceProfileView> {
    return firstValueFrom(this.http.get<ResourceProfileView>(`${this.base}/${employeeId}`));
  }

  saveProfile(employeeId: string, body: { costRate?: number | null; billingRate?: number | null; currency?: string | null; capacityHoursPerWeek?: number | null; noticePeriodDays?: number | null; isBillable: boolean; notes?: string | null }): Promise<ResourceProfileView> {
    return firstValueFrom(this.http.put<ResourceProfileView>(`${this.base}/${employeeId}/profile`, body));
  }

  calendar(query: { from?: string; months?: number; departmentId?: string; skillId?: string; projectId?: string; search?: string } = {}): Promise<readonly CalendarRow[]> {
    return firstValueFrom(this.http.get<readonly CalendarRow[]>(`${this.base}/calendar`, { params: params({ ...query }) }));
  }

  bench(): Promise<readonly BenchItem[]> {
    return firstValueFrom(this.http.get<readonly BenchItem[]>(`${this.base}/bench`));
  }

  forecast(months = 6): Promise<ForecastReport> {
    return firstValueFrom(this.http.get<ForecastReport>(`${this.base}/forecast`, { params: { months } }));
  }

  utilization(query: { from?: string; to?: string; groupBy?: UtilizationGrouping; departmentId?: string } = {}): Promise<UtilizationReport> {
    return firstValueFrom(this.http.get<UtilizationReport>(`${this.base}/utilization`, { params: params({ ...query }) }));
  }

  allocate(body: { projectId: string; employeeId: string; projectRoleId?: string | null; startDate: string; endDate?: string | null; allocationPercent: number; billable: boolean }): Promise<AllocationOutcome> {
    return firstValueFrom(this.http.post<AllocationOutcome>('/api/v1/resource-allocations', body));
  }

  updateAllocation(memberId: string, body: { allocationPercent: number; startDate: string; endDate?: string | null; billable: boolean }): Promise<AllocationOutcome> {
    return firstValueFrom(this.http.put<AllocationOutcome>(`/api/v1/resource-allocations/${memberId}`, body));
  }

  async release(memberId: string, on: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/resource-allocations/${memberId}/release`, { on }));
  }

  transfers(status?: TransferStatus | ''): Promise<readonly TransferItem[]> {
    return firstValueFrom(this.http.get<readonly TransferItem[]>(`${this.base}/transfers`, { params: params({ status }) }));
  }

  requestTransfer(body: { fromMemberId: string; toProjectId: string; projectRoleId?: string | null; allocationPercent: number; effectiveDate: string; endDate?: string | null; billable: boolean; reason: string }): Promise<TransferItem> {
    return firstValueFrom(this.http.post<TransferItem>(`${this.base}/transfers`, body));
  }

  async cancelTransfer(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/transfers/${id}/cancel`, {}));
  }

  requests(status?: RequestStatus | '', projectId?: string): Promise<readonly ResourceRequestItem[]> {
    return firstValueFrom(this.http.get<readonly ResourceRequestItem[]>('/api/v1/resource-requests', { params: params({ status, projectId }) }));
  }

  request(id: string): Promise<ResourceRequestDetail> {
    return firstValueFrom(this.http.get<ResourceRequestDetail>(`/api/v1/resource-requests/${id}`));
  }

  saveRequest(id: string | null, body: ResourceRequestInput): Promise<ResourceRequestDetail> {
    return firstValueFrom(id ? this.http.put<ResourceRequestDetail>(`/api/v1/resource-requests/${id}`, body) : this.http.post<ResourceRequestDetail>('/api/v1/resource-requests', body));
  }

  async cancelRequest(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/resource-requests/${id}/cancel`, {}));
  }

  matches(id: string, take = 20): Promise<readonly MatchItem[]> {
    return firstValueFrom(this.http.get<readonly MatchItem[]>(`/api/v1/resource-requests/${id}/matches`, { params: { take } }));
  }

  shortlist(id: string, employeeId: string, note: string | null): Promise<ResourceRequestDetail> {
    return firstValueFrom(this.http.post<ResourceRequestDetail>(`/api/v1/resource-requests/${id}/candidates`, { employeeId, note }));
  }

  rejectCandidate(id: string, candidateId: string, note: string | null): Promise<ResourceRequestDetail> {
    return firstValueFrom(this.http.post<ResourceRequestDetail>(`/api/v1/resource-requests/${id}/candidates/${candidateId}/reject`, { note }));
  }

  allocateCandidate(id: string, candidateId: string): Promise<ResourceRequestDetail> {
    return firstValueFrom(this.http.post<ResourceRequestDetail>(`/api/v1/resource-requests/${id}/candidates/${candidateId}/allocate`, {}));
  }
}
