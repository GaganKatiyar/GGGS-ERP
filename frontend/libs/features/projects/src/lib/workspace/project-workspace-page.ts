import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { SessionStore } from '@gggs/core/auth';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { ProjectsApi } from '@gggs/shared/delivery';
import { RecordDocuments } from '@gggs/shared/documents';
import type { ProjectDetail, ProjectStage, ProjectStatus } from '@gggs/shared/models';
import { PROJECT_STAGES, PROJECT_TYPE_LABELS, STAGE_LABELS } from '@gggs/shared/models';
import { FormDialog, PageHeader, Skeleton, StatusBadge, Tabs, type TabItem } from '@gggs/shared/ui';
import { ProjectForm } from '../project-form';
import { CalendarTab } from './calendar-tab';
import { FinancialsTab } from './financials-tab';
import { ChangesTab } from './changes-tab';
import { MeetingsTab } from './meetings-tab';
import { OverviewTab } from './overview-tab';
import { PlanTab } from './plan-tab';
import { ProjectBoard } from './project-board';
import { RaidTab } from './raid-tab';
import { RequirementsTab } from './requirements-tab';
import { SettingsTab } from './settings-tab';
import { SprintsTab } from './sprints-tab';
import { TasksTab } from './tasks-tab';
import { TeamTab } from './team-tab';

const TABS: readonly TabItem[] = [
  { key: 'overview', label: 'Overview' },
  { key: 'tasks', label: 'Tasks' },
  { key: 'board', label: 'Board' },
  { key: 'sprints', label: 'Sprints' },
  { key: 'plan', label: 'Phases & milestones' },
  { key: 'team', label: 'Team' },
  { key: 'requirements', label: 'Requirements' },
  { key: 'changes', label: 'Change requests' },
  { key: 'raid', label: 'RAID' },
  { key: 'meetings', label: 'Meetings' },
  { key: 'calendar', label: 'Calendar' },
  { key: 'documents', label: 'Documents' },
  { key: 'financials', label: 'Financials' },
  { key: 'settings', label: 'Board & history' },
];

/** One project's workspace (spec §39-56): everything about it, one tab each. */
@Component({
  selector: 'gggs-project-workspace-page',
  imports: [
    DatePipe,
    RouterLink,
    PageHeader,
    Tabs,
    StatusBadge,
    Skeleton,
    FormDialog,
    RecordDocuments,
    FinancialsTab,
    ProjectForm,
    OverviewTab,
    TasksTab,
    ProjectBoard,
    SprintsTab,
    PlanTab,
    TeamTab,
    RequirementsTab,
    ChangesTab,
    RaidTab,
    MeetingsTab,
    CalendarTab,
    SettingsTab,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (detail(); as d) {
      <gggs-page-header [title]="d.project.code + ' · ' + d.project.name" [subtitle]="subtitle()">
        <div class="toolbar">
          <gggs-status-badge [status]="d.project.healthStatus" />
          <gggs-status-badge [status]="d.project.status" />
          <a class="btn" routerLink="/projects">All projects</a>
          @if (d.can.manage) {
            <button type="button" class="btn" (click)="formOpen.set(true)">Edit</button>
          }
        </div>
      </gggs-page-header>

      <section class="panel workspace__lifecycle">
        <ol class="workspace__stages" aria-label="Lifecycle">
          @for (stage of stages; track stage; let index = $index) {
            <li class="workspace__stage" [class.workspace__stage--done]="index < stageIndex()" [class.workspace__stage--current]="index === stageIndex()">
              @if (d.can.manage && isOpen()) {
                <button type="button" (click)="openStage(stage)" [attr.aria-current]="index === stageIndex() ? 'step' : null">{{ stageLabels[stage] }}</button>
              } @else {
                <span [attr.aria-current]="index === stageIndex() ? 'step' : null">{{ stageLabels[stage] }}</span>
              }
            </li>
          }
        </ol>
        <div class="workspace__facts">
          <span>{{ d.project.startDate | date: 'd MMM y' }} – {{ d.project.endDate | date: 'd MMM y' }}{{ d.project.plannedEndDate ? ' (forecast ' + (d.project.plannedEndDate | date: 'd MMM y') + ')' : '' }}</span>
          <span>PM: {{ d.project.projectManagerName ?? '—' }}</span>
          @if (d.project.deliveryManagerName) {
            <span>DM: {{ d.project.deliveryManagerName }}</span>
          }
          @if (d.project.contractNumber) {
            <a [routerLink]="['/contracts']" [queryParams]="{ open: d.project.contractId }">{{ d.project.contractNumber }}</a>
          }
          <span>Client satisfaction: {{ d.project.clientSatisfaction ? d.project.clientSatisfaction + '/5' : 'not rated' }}</span>
          @if (d.can.manage) {
            <span class="workspace__actions">
              <button type="button" class="btn btn--link btn--sm" (click)="satisfactionOpen.set(true)">Rate satisfaction</button>
              @if (d.project.status === 'Active') {
                <button type="button" class="btn btn--link btn--sm" (click)="openStatus('OnHold')">Put on hold</button>
                <button type="button" class="btn btn--link btn--sm" (click)="openStatus('Completed')">Complete</button>
                <button type="button" class="btn btn--link btn--sm" (click)="openStatus('Cancelled')">Cancel</button>
              } @else {
                <button type="button" class="btn btn--link btn--sm" (click)="openStatus('Active')">Reopen</button>
              }
            </span>
          }
        </div>
      </section>

      <gggs-tabs [tabs]="tabs" [selected]="currentTab()" (selectedChange)="selectTab($event)" />

      <div class="workspace__body">
        @switch (currentTab()) {
          @case ('overview') {
            <gggs-overview-tab [projectId]="d.project.id" [can]="d.can" (changed)="reload()" />
          }
          @case ('tasks') {
            <gggs-tasks-tab [projectId]="d.project.id" [canEdit]="d.can.manageTasks" [openTaskId]="task()" />
          }
          @case ('board') {
            <gggs-project-board [projectId]="d.project.id" [canEdit]="d.can.manageTasks" />
          }
          @case ('sprints') {
            <gggs-sprints-tab [projectId]="d.project.id" [canEdit]="d.can.manageSprints" />
          }
          @case ('plan') {
            <gggs-plan-tab [projectId]="d.project.id" [canManage]="d.can.manage" [canManageMilestones]="d.can.manageMilestones" />
          }
          @case ('team') {
            <gggs-team-tab [projectId]="d.project.id" [canManage]="d.can.manage" [seeRates]="d.can.seeRates" />
          }
          @case ('requirements') {
            <gggs-requirements-tab [projectId]="d.project.id" [canEdit]="d.can.manageTasks" />
          }
          @case ('changes') {
            <gggs-changes-tab [projectId]="d.project.id" [canEdit]="d.can.manageChangeRequests" />
          }
          @case ('raid') {
            <gggs-raid-tab [projectId]="d.project.id" [canEdit]="d.can.manageRaid" />
          }
          @case ('meetings') {
            <gggs-meetings-tab [projectId]="d.project.id" [canManage]="d.can.manage" [me]="me()" />
          }
          @case ('calendar') {
            <gggs-calendar-tab [projectId]="d.project.id" />
          }
          @case ('documents') {
            <gggs-record-documents ownerType="Project" [ownerId]="d.project.id" [canUpload]="d.can.manage || d.can.manageTasks" />
          }
          @case ('financials') {
            <gggs-financials-tab [projectId]="d.project.id" />
          }
          @case ('settings') {
            <gggs-settings-tab [projectId]="d.project.id" [canManage]="d.can.manage" />
          }
        }
      </div>

      <gggs-project-form [(visible)]="formOpen" [project]="d.project" (saved)="detail.set($event)" />
    } @else {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }

    <gggs-form-dialog [(visible)]="stageOpen" [title]="'Move to ' + stageLabels[targetStage()]" saveLabel="Move" [busy]="busy()" [error]="dialogError()" (save)="moveStage()">
      <label class="field">
        <span class="field__label">Comment</span>
        <textarea rows="3" maxlength="1000" [value]="comment()" (input)="comment.set($any($event.target).value)"></textarea>
      </label>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="statusOpen" [title]="statusTitle()" saveLabel="Confirm" [busy]="busy()" [error]="dialogError()" (save)="setStatus()">
      <label class="field">
        <span class="field__label">Comment</span>
        <textarea rows="3" maxlength="1000" [value]="comment()" (input)="comment.set($any($event.target).value)"></textarea>
      </label>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="satisfactionOpen" title="Client satisfaction" saveLabel="Record" [busy]="busy()" [error]="dialogError()" (save)="rate()">
      <div class="stack">
        <div class="workspace__stars" role="radiogroup" aria-label="Score from 1 to 5">
          @for (score of [1, 2, 3, 4, 5]; track score) {
            <label><input type="radio" name="satisfaction" [checked]="satisfaction() === score" (change)="satisfaction.set(score)" /> {{ score }}</label>
          }
        </div>
        <label class="field">
          <span class="field__label">What the client said</span>
          <textarea rows="3" maxlength="1000" [value]="comment()" (input)="comment.set($any($event.target).value)"></textarea>
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .workspace__lifecycle {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-2);
      margin-block-end: var(--gggs-space-3);
    }

    .workspace__stages {
      display: flex;
      flex-wrap: wrap;
      gap: 2px;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .workspace__stage button,
    .workspace__stage span {
      display: block;
      padding: var(--gggs-space-1) var(--gggs-space-2);
      border: 0;
      border-radius: var(--gggs-radius-sm, 4px);
      background: var(--gggs-color-border);
      color: inherit;
      font: inherit;
      font-size: var(--gggs-font-size-xs);
    }

    .workspace__stage button {
      cursor: pointer;
    }

    .workspace__stage--done button,
    .workspace__stage--done span {
      background: var(--gggs-color-primary-subtle);
    }

    .workspace__stage--current button,
    .workspace__stage--current span {
      background: var(--gggs-color-primary);
      color: var(--gggs-color-on-primary, #fff);
      font-weight: var(--gggs-font-weight-semibold);
    }

    .workspace__facts {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-3);
      align-items: center;
      font-size: var(--gggs-font-size-sm);
    }

    .workspace__actions {
      margin-inline-start: auto;
    }

    .workspace__body {
      margin-block-start: var(--gggs-space-3);
    }

    .workspace__stars {
      display: flex;
      gap: var(--gggs-space-3);
    }
  `,
})
export class ProjectWorkspacePage {
  private readonly api = inject(ProjectsApi);
  private readonly router = inject(Router);
  private readonly session = inject(SessionStore);
  private readonly permissions = inject(PermissionStore);

  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  /** Bound from the `tab` query parameter, so a link can open a section. */
  readonly tab = input<string | undefined>(undefined);

  /** Bound from the `task` query parameter: opens that task in the tasks tab. */
  readonly task = input<string | undefined>(undefined);

  protected readonly tabs = this.permissions.has('project.financials.view') ? TABS : TABS.filter((tab) => tab.key !== 'financials');
  protected readonly stages = PROJECT_STAGES;
  protected readonly stageLabels = STAGE_LABELS;
  protected readonly detail = signal<ProjectDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly selected = signal<string | null>(null);
  protected readonly formOpen = signal(false);
  protected readonly stageOpen = signal(false);
  protected readonly statusOpen = signal(false);
  protected readonly satisfactionOpen = signal(false);
  protected readonly targetStage = signal<ProjectStage>('Planning');
  protected readonly targetStatus = signal<ProjectStatus>('Active');
  protected readonly satisfaction = signal(4);
  protected readonly comment = signal('');
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  protected readonly me = computed(() => this.session.user()?.employeeId);
  protected readonly currentTab = computed(() => this.selected() ?? (this.tab() && TABS.some((item) => item.key === this.tab()) ? (this.tab() as string) : 'overview'));
  protected readonly stageIndex = computed(() => {
    const project = this.detail()?.project;
    return project ? PROJECT_STAGES.indexOf(project.stage) : -1;
  });
  protected readonly isOpen = computed(() => this.detail()?.project.status === 'Active' || this.detail()?.project.status === 'OnHold');
  protected readonly subtitle = computed(() => {
    const project = this.detail()?.project;
    return project ? [project.clientName ?? 'Internal', PROJECT_TYPE_LABELS[project.type], project.methodology].join(' · ') : '';
  });
  protected readonly statusTitle = computed(() => {
    switch (this.targetStatus()) {
      case 'OnHold':
        return 'Put the project on hold';
      case 'Completed':
        return 'Complete the project';
      case 'Cancelled':
        return 'Cancel the project';
      default:
        return 'Reopen the project';
    }
  });

  constructor() {
    effect(() => {
      void this.load(this.id());
    });
  }

  protected selectTab(key: string): void {
    this.selected.set(key);
    void this.router.navigate([], { queryParams: { tab: key }, replaceUrl: true });
  }

  protected openStage(stage: ProjectStage): void {
    this.targetStage.set(stage);
    this.comment.set('');
    this.dialogError.set(null);
    this.stageOpen.set(true);
  }

  protected openStatus(status: ProjectStatus): void {
    this.targetStatus.set(status);
    this.comment.set('');
    this.dialogError.set(null);
    this.statusOpen.set(true);
  }

  protected async moveStage(): Promise<void> {
    await this.dialog(async () => {
      this.detail.set(await this.api.moveStage(this.id(), this.targetStage(), this.comment() || null));
      this.stageOpen.set(false);
    });
  }

  protected async setStatus(): Promise<void> {
    await this.dialog(async () => {
      this.detail.set(await this.api.setStatus(this.id(), this.targetStatus(), this.comment() || null));
      this.statusOpen.set(false);
    });
  }

  protected async rate(): Promise<void> {
    await this.dialog(async () => {
      await this.api.rateSatisfaction(this.id(), this.satisfaction(), this.comment() || null);
      this.satisfactionOpen.set(false);
      await this.load(this.id());
    });
  }

  protected async reload(): Promise<void> {
    await this.load(this.id());
  }

  private async dialog(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await action();
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    this.error.set(null);
    try {
      this.detail.set(await this.api.get(id));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The project could not be loaded.');
    }
  }
}
