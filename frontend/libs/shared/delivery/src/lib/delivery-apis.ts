import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  BoardView,
  BurndownPoint,
  CalendarEvent,
  ChangeRequest,
  ChangeRequestInput,
  ChangeRequestStatus,
  ClientDetail,
  ClientInput,
  ClientItem,
  ClientStatus,
  ColumnInput,
  ContactInput,
  ContactItem,
  ContractDetail,
  ContractInput,
  ContractItem,
  ContractStatus,
  ContractType,
  Dependency,
  DependencyType,
  HealthRating,
  HealthView,
  HistoryItem,
  Meeting,
  MeetingInput,
  Milestone,
  MilestoneInput,
  PagedResult,
  Phase,
  PhaseInput,
  PortfolioSummary,
  ProjectDashboard,
  ProjectDetail,
  ProjectHealthThresholds,
  ProjectInput,
  ProjectItem,
  ProjectMembership,
  ProjectRole,
  ProjectStage,
  ProjectStatus,
  ProjectTask,
  RaidInput,
  RaidItem,
  RaidKind,
  RaidStatus,
  Requirement,
  RequirementInput,
  Sprint,
  SprintInput,
  SprintStatus,
  TaskInput,
  TaskType,
  TeamMember,
  TeamMemberInput,
  BoardColumn,
  VelocityPoint,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

type Query = Record<string, string | number | boolean | null | undefined>;

/** Drops empty values so the query string carries only what was asked for. */
function params(query: Query): Record<string, string | number | boolean> {
  const result: Record<string, string | number | boolean> = {};
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') {
      result[key] = value;
    }
  }
  return result;
}

/** Today in the browser's calendar, as the API's date strings are. */
export function isoToday(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

export function addDaysIso(iso: string, days: number): string {
  const [year, month, day] = iso.split('-').map(Number);
  const date = new Date(year, month - 1, day + days);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

/** Clients and contacts (`/api/v1/clients`). */
@Injectable({ providedIn: 'root' })
export class ClientsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/clients';

  list(query: { search?: string; status?: ClientStatus | ''; industryCode?: string; page?: number; pageSize?: number; sort?: string } = {}): Promise<PagedResult<ClientItem>> {
    return firstValueFrom(this.http.get<PagedResult<ClientItem>>(this.base, { params: params(query) }));
  }

  /** Every client that can take work, for pickers. Client lists are short; two hundred covers them. */
  async active(): Promise<readonly ClientItem[]> {
    const page = await this.list({ pageSize: 200, sort: 'name' });
    return page.items.filter((client) => client.status !== 'Inactive');
  }

  get(id: string): Promise<ClientDetail> {
    return firstValueFrom(this.http.get<ClientDetail>(`${this.base}/${id}`));
  }

  save(id: string | null, body: ClientInput): Promise<ClientDetail> {
    return firstValueFrom(id ? this.http.put<ClientDetail>(`${this.base}/${id}`, body) : this.http.post<ClientDetail>(this.base, body));
  }

  async setStatus(id: string, status: ClientStatus): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/status`, { status }));
  }

  contacts(query: { search?: string; clientId?: string; page?: number; pageSize?: number } = {}): Promise<PagedResult<ContactItem>> {
    return firstValueFrom(this.http.get<PagedResult<ContactItem>>(`${this.base}/contacts`, { params: params(query) }));
  }

  saveContact(clientId: string, contactId: string | null, body: ContactInput): Promise<ContactItem> {
    return firstValueFrom(
      contactId
        ? this.http.put<ContactItem>(`${this.base}/${clientId}/contacts/${contactId}`, body)
        : this.http.post<ContactItem>(`${this.base}/${clientId}/contacts`, body),
    );
  }

  async removeContact(clientId: string, contactId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${clientId}/contacts/${contactId}`));
  }
}

/** Contracts (`/api/v1/contracts`). */
@Injectable({ providedIn: 'root' })
export class ContractsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/contracts';

  list(query: { search?: string; clientId?: string; type?: ContractType | ''; status?: ContractStatus | ''; renewalWithinDays?: number; page?: number; pageSize?: number; sort?: string } = {}): Promise<PagedResult<ContractItem>> {
    return firstValueFrom(this.http.get<PagedResult<ContractItem>>(this.base, { params: params(query) }));
  }

  get(id: string): Promise<ContractDetail> {
    return firstValueFrom(this.http.get<ContractDetail>(`${this.base}/${id}`));
  }

  save(id: string | null, body: ContractInput): Promise<ContractDetail> {
    return firstValueFrom(id ? this.http.put<ContractDetail>(`${this.base}/${id}`, body) : this.http.post<ContractDetail>(this.base, body));
  }

  activate(id: string): Promise<{ status: ContractStatus; workflowInstanceId?: string }> {
    return firstValueFrom(this.http.post<{ status: ContractStatus; workflowInstanceId?: string }>(`${this.base}/${id}/activate`, {}));
  }

  async terminate(id: string, on: string, reason: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/terminate`, { on, reason }));
  }
}

export interface ProjectQuery {
  readonly search?: string;
  readonly status?: ProjectStatus | '';
  readonly stage?: ProjectStage | '';
  readonly health?: HealthRating | '';
  readonly clientId?: string;
  readonly managerEmployeeId?: string;
  readonly mine?: boolean;
  readonly includeClosed?: boolean;
  readonly page?: number;
  readonly pageSize?: number;
  readonly sort?: string;
}

export interface TaskQuery {
  readonly type?: TaskType | '';
  readonly assigneeEmployeeId?: string | null;
  readonly sprintId?: string | null;
  readonly backlog?: boolean;
  readonly milestoneId?: string | null;
  readonly requirementId?: string | null;
  readonly parentTaskId?: string | null;
  readonly includeDone?: boolean;
  readonly search?: string;
  readonly page?: number;
  readonly pageSize?: number;
  readonly sort?: string;
}

/** Projects and everything in them (`/api/v1/projects`, `/api/v1/project-*`). */
@Injectable({ providedIn: 'root' })
export class ProjectsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/projects';

  // Projects
  list(query: ProjectQuery = {}): Promise<PagedResult<ProjectItem>> {
    return firstValueFrom(this.http.get<PagedResult<ProjectItem>>(this.base, { params: params({ ...query }) }));
  }

  summary(): Promise<PortfolioSummary> {
    return firstValueFrom(this.http.get<PortfolioSummary>(`${this.base}/summary`));
  }

  get(id: string): Promise<ProjectDetail> {
    return firstValueFrom(this.http.get<ProjectDetail>(`${this.base}/${id}`));
  }

  save(id: string | null, body: ProjectInput): Promise<ProjectDetail> {
    return firstValueFrom(id ? this.http.put<ProjectDetail>(`${this.base}/${id}`, body) : this.http.post<ProjectDetail>(this.base, body));
  }

  moveStage(id: string, stage: ProjectStage, comment: string | null): Promise<ProjectDetail> {
    return firstValueFrom(this.http.post<ProjectDetail>(`${this.base}/${id}/stage`, { stage, comment }));
  }

  setStatus(id: string, status: ProjectStatus, comment: string | null): Promise<ProjectDetail> {
    return firstValueFrom(this.http.post<ProjectDetail>(`${this.base}/${id}/status`, { status, comment }));
  }

  async rateSatisfaction(id: string, score: number, comment: string | null): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/client-satisfaction`, { score, comment }));
  }

  history(id: string): Promise<readonly HistoryItem[]> {
    return firstValueFrom(this.http.get<readonly HistoryItem[]>(`${this.base}/${id}/history`));
  }

  dashboard(id: string): Promise<ProjectDashboard> {
    return firstValueFrom(this.http.get<ProjectDashboard>(`${this.base}/${id}/dashboard`));
  }

  health(id: string): Promise<HealthView> {
    return firstValueFrom(this.http.get<HealthView>(`${this.base}/${id}/health`));
  }

  assessHealth(id: string): Promise<HealthView> {
    return firstValueFrom(this.http.post<HealthView>(`${this.base}/${id}/health/assess`, {}));
  }

  calendar(id: string, from: string, to: string): Promise<readonly CalendarEvent[]> {
    return firstValueFrom(this.http.get<readonly CalendarEvent[]>(`${this.base}/${id}/calendar`, { params: { from, to } }));
  }

  byEmployee(employeeId: string): Promise<readonly ProjectMembership[]> {
    return firstValueFrom(this.http.get<readonly ProjectMembership[]>(`${this.base}/by-employee/${employeeId}`));
  }

  mine(): Promise<readonly ProjectMembership[]> {
    return firstValueFrom(this.http.get<readonly ProjectMembership[]>('/api/v1/me/projects'));
  }

  // Team and structure
  team(id: string): Promise<readonly TeamMember[]> {
    return firstValueFrom(this.http.get<readonly TeamMember[]>(`${this.base}/${id}/team`));
  }

  saveMember(id: string, memberId: string | null, body: TeamMemberInput): Promise<TeamMember> {
    return firstValueFrom(memberId ? this.http.put<TeamMember>(`${this.base}/${id}/team/${memberId}`, body) : this.http.post<TeamMember>(`${this.base}/${id}/team`, body));
  }

  async releaseMember(id: string, memberId: string, on: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/team/${memberId}/release`, { on }));
  }

  phases(id: string): Promise<readonly Phase[]> {
    return firstValueFrom(this.http.get<readonly Phase[]>(`${this.base}/${id}/phases`));
  }

  savePhase(id: string, phaseId: string | null, body: PhaseInput): Promise<readonly Phase[]> {
    return firstValueFrom(phaseId ? this.http.put<readonly Phase[]>(`${this.base}/${id}/phases/${phaseId}`, body) : this.http.post<readonly Phase[]>(`${this.base}/${id}/phases`, body));
  }

  async deletePhase(id: string, phaseId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${id}/phases/${phaseId}`));
  }

  board(id: string, sprintId?: string | null, assigneeEmployeeId?: string | null): Promise<BoardView> {
    return firstValueFrom(this.http.get<BoardView>(`${this.base}/${id}/board`, { params: params({ sprintId, assigneeEmployeeId }) }));
  }

  saveColumns(id: string, columns: readonly ColumnInput[]): Promise<readonly BoardColumn[]> {
    return firstValueFrom(this.http.put<readonly BoardColumn[]>(`${this.base}/${id}/board/columns`, columns));
  }

  // Tasks
  tasks(id: string, query: TaskQuery = {}): Promise<PagedResult<ProjectTask>> {
    return firstValueFrom(this.http.get<PagedResult<ProjectTask>>(`${this.base}/${id}/tasks`, { params: params({ ...query }) }));
  }

  task(id: string, taskId: string): Promise<ProjectTask> {
    return firstValueFrom(this.http.get<ProjectTask>(`${this.base}/${id}/tasks/${taskId}`));
  }

  saveTask(id: string, taskId: string | null, body: TaskInput): Promise<ProjectTask> {
    return firstValueFrom(taskId ? this.http.put<ProjectTask>(`${this.base}/${id}/tasks/${taskId}`, body) : this.http.post<ProjectTask>(`${this.base}/${id}/tasks`, body));
  }

  moveTask(id: string, taskId: string, columnId: string, index: number | null): Promise<ProjectTask> {
    return firstValueFrom(this.http.post<ProjectTask>(`${this.base}/${id}/tasks/${taskId}/move`, { columnId, index }));
  }

  async deleteTask(id: string, taskId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${id}/tasks/${taskId}`));
  }

  portfolioTasks(query: { assignedToMe?: boolean; projectId?: string; done?: boolean; overdue?: boolean; search?: string; page?: number; pageSize?: number } = {}): Promise<PagedResult<ProjectTask>> {
    return firstValueFrom(this.http.get<PagedResult<ProjectTask>>('/api/v1/project-tasks', { params: params({ ...query }) }));
  }

  dependencies(id: string, taskId?: string): Promise<readonly Dependency[]> {
    return firstValueFrom(this.http.get<readonly Dependency[]>(`${this.base}/${id}/dependencies`, { params: params({ taskId }) }));
  }

  addDependency(id: string, taskId: string, predecessorTaskId: string, type: DependencyType, lagDays: number): Promise<readonly Dependency[]> {
    return firstValueFrom(this.http.post<readonly Dependency[]>(`${this.base}/${id}/tasks/${taskId}/dependencies`, { predecessorTaskId, type, lagDays }));
  }

  async removeDependency(id: string, dependencyId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${id}/dependencies/${dependencyId}`));
  }

  requirements(id: string): Promise<readonly Requirement[]> {
    return firstValueFrom(this.http.get<readonly Requirement[]>(`${this.base}/${id}/requirements`));
  }

  saveRequirement(id: string, requirementId: string | null, body: RequirementInput): Promise<Requirement> {
    return firstValueFrom(
      requirementId ? this.http.put<Requirement>(`${this.base}/${id}/requirements/${requirementId}`, body) : this.http.post<Requirement>(`${this.base}/${id}/requirements`, body),
    );
  }

  async deleteRequirement(id: string, requirementId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${id}/requirements/${requirementId}`));
  }

  // Planning
  milestones(id: string): Promise<readonly Milestone[]> {
    return firstValueFrom(this.http.get<readonly Milestone[]>(`${this.base}/${id}/milestones`));
  }

  portfolioMilestones(query: { projectId?: string; withinDays?: number; overdueOnly?: boolean } = {}): Promise<readonly Milestone[]> {
    return firstValueFrom(this.http.get<readonly Milestone[]>(`${this.base}/milestones`, { params: params({ ...query }) }));
  }

  saveMilestone(id: string, milestoneId: string | null, body: MilestoneInput): Promise<Milestone> {
    return firstValueFrom(milestoneId ? this.http.put<Milestone>(`${this.base}/${id}/milestones/${milestoneId}`, body) : this.http.post<Milestone>(`${this.base}/${id}/milestones`, body));
  }

  achieveMilestone(id: string, milestoneId: string, on: string | null): Promise<Milestone> {
    return firstValueFrom(this.http.post<Milestone>(`${this.base}/${id}/milestones/${milestoneId}/achieve`, { on }));
  }

  async deleteMilestone(id: string, milestoneId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${id}/milestones/${milestoneId}`));
  }

  sprints(id: string): Promise<readonly Sprint[]> {
    return firstValueFrom(this.http.get<readonly Sprint[]>(`${this.base}/${id}/sprints`));
  }

  portfolioSprints(status: SprintStatus = 'Active'): Promise<readonly Sprint[]> {
    return firstValueFrom(this.http.get<readonly Sprint[]>(`${this.base}/sprints`, { params: { status } }));
  }

  saveSprint(id: string, sprintId: string | null, body: SprintInput): Promise<Sprint> {
    return firstValueFrom(sprintId ? this.http.put<Sprint>(`${this.base}/${id}/sprints/${sprintId}`, body) : this.http.post<Sprint>(`${this.base}/${id}/sprints`, body));
  }

  planSprint(id: string, sprintId: string, taskIds: readonly string[]): Promise<Sprint> {
    return firstValueFrom(this.http.post<Sprint>(`${this.base}/${id}/sprints/${sprintId}/tasks`, { taskIds }));
  }

  unplanTask(id: string, sprintId: string, taskId: string): Promise<Sprint> {
    return firstValueFrom(this.http.delete<Sprint>(`${this.base}/${id}/sprints/${sprintId}/tasks/${taskId}`));
  }

  startSprint(id: string, sprintId: string): Promise<Sprint> {
    return firstValueFrom(this.http.post<Sprint>(`${this.base}/${id}/sprints/${sprintId}/start`, {}));
  }

  completeSprint(id: string, sprintId: string, moveOpenTasksToSprintId: string | null): Promise<Sprint> {
    return firstValueFrom(this.http.post<Sprint>(`${this.base}/${id}/sprints/${sprintId}/complete`, { moveOpenTasksToSprintId }));
  }

  async cancelSprint(id: string, sprintId: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/sprints/${sprintId}/cancel`, {}));
  }

  burndown(id: string, sprintId: string): Promise<readonly BurndownPoint[]> {
    return firstValueFrom(this.http.get<readonly BurndownPoint[]>(`${this.base}/${id}/sprints/${sprintId}/burndown`));
  }

  velocity(id: string): Promise<readonly VelocityPoint[]> {
    return firstValueFrom(this.http.get<readonly VelocityPoint[]>(`${this.base}/${id}/velocity`));
  }

  // Governance
  changeRequests(id: string): Promise<readonly ChangeRequest[]> {
    return firstValueFrom(this.http.get<readonly ChangeRequest[]>(`${this.base}/${id}/change-requests`));
  }

  portfolioChangeRequests(status?: ChangeRequestStatus | ''): Promise<readonly ChangeRequest[]> {
    return firstValueFrom(this.http.get<readonly ChangeRequest[]>(`${this.base}/change-requests`, { params: params({ status }) }));
  }

  saveChangeRequest(id: string, crId: string | null, body: ChangeRequestInput): Promise<ChangeRequest> {
    return firstValueFrom(crId ? this.http.put<ChangeRequest>(`${this.base}/${id}/change-requests/${crId}`, body) : this.http.post<ChangeRequest>(`${this.base}/${id}/change-requests`, body));
  }

  submitChangeRequest(id: string, crId: string): Promise<{ status: ChangeRequestStatus; workflowInstanceId?: string }> {
    return firstValueFrom(this.http.post<{ status: ChangeRequestStatus; workflowInstanceId?: string }>(`${this.base}/${id}/change-requests/${crId}/submit`, {}));
  }

  recordClientApproval(id: string, crId: string, on: string, reference: string | null): Promise<ChangeRequest> {
    return firstValueFrom(this.http.post<ChangeRequest>(`${this.base}/${id}/change-requests/${crId}/client-approval`, { on, reference }));
  }

  advanceChangeRequest(id: string, crId: string, status: ChangeRequestStatus): Promise<ChangeRequest> {
    return firstValueFrom(this.http.post<ChangeRequest>(`${this.base}/${id}/change-requests/${crId}/advance`, { status }));
  }

  async withdrawChangeRequest(id: string, crId: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/change-requests/${crId}/withdraw`, {}));
  }

  raid(id: string, kind?: RaidKind | '', status?: RaidStatus | ''): Promise<readonly RaidItem[]> {
    return firstValueFrom(this.http.get<readonly RaidItem[]>(`${this.base}/${id}/raid`, { params: params({ kind, status }) }));
  }

  portfolioRaid(query: { projectId?: string; kind?: RaidKind | ''; status?: RaidStatus | ''; openOnly?: boolean } = {}): Promise<readonly RaidItem[]> {
    return firstValueFrom(this.http.get<readonly RaidItem[]>(`${this.base}/raid`, { params: params({ ...query }) }));
  }

  saveRaid(id: string, itemId: string | null, body: RaidInput): Promise<RaidItem> {
    return firstValueFrom(itemId ? this.http.put<RaidItem>(`${this.base}/${id}/raid/${itemId}`, body) : this.http.post<RaidItem>(`${this.base}/${id}/raid`, body));
  }

  meetings(id: string): Promise<readonly Meeting[]> {
    return firstValueFrom(this.http.get<readonly Meeting[]>(`${this.base}/${id}/meetings`));
  }

  saveMeeting(id: string, meetingId: string | null, body: MeetingInput): Promise<Meeting> {
    return firstValueFrom(meetingId ? this.http.put<Meeting>(`${this.base}/${id}/meetings/${meetingId}`, body) : this.http.post<Meeting>(`${this.base}/${id}/meetings`, body));
  }

  saveAction(id: string, meetingId: string, actionId: string | null, body: { description: string; ownerEmployeeId?: string | null; dueDate?: string | null; status?: string | null }): Promise<Meeting> {
    return firstValueFrom(
      actionId
        ? this.http.put<Meeting>(`${this.base}/${id}/meetings/${meetingId}/actions/${actionId}`, body)
        : this.http.post<Meeting>(`${this.base}/${id}/meetings/${meetingId}/actions`, body),
    );
  }

  // Settings
  roles(includeInactive = false): Promise<readonly ProjectRole[]> {
    return firstValueFrom(this.http.get<readonly ProjectRole[]>(`${this.base}/roles`, { params: { includeInactive } }));
  }

  saveRole(id: string | null, body: { code: string; name: string; description?: string | null; skills?: readonly string[]; sortOrder?: number | null }): Promise<ProjectRole> {
    return firstValueFrom(id ? this.http.put<ProjectRole>(`${this.base}/roles/${id}`, body) : this.http.post<ProjectRole>(`${this.base}/roles`, body));
  }

  async setRoleActive(id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/roles/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }

  thresholds(): Promise<ProjectHealthThresholds> {
    return firstValueFrom(this.http.get<ProjectHealthThresholds>(`${this.base}/settings/health`));
  }

  saveThresholds(body: ProjectHealthThresholds): Promise<ProjectHealthThresholds> {
    return firstValueFrom(this.http.put<ProjectHealthThresholds>(`${this.base}/settings/health`, body));
  }
}
