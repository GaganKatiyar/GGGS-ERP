import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { PermissionStore } from '@gggs/core/permissions';
import { ProjectsApi, ResourcesApi, SkillsApi, addDaysIso, isoToday } from '@gggs/shared/delivery';
import type { MatchItem, ProjectItem, RequestPriority, RequestStatus, ResourceRequestDetail, ResourceRequestItem, SkillItem } from '@gggs/shared/models';
import { SKILL_LEVEL_LABELS, humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { message } from './allocation-dialogs';

interface RequestForm {
  projectId: string;
  title: string;
  skills: { skillId: string; minLevel: number }[];
  minYearsExperience: number | null;
  quantity: number;
  allocationPercent: number;
  startDate: string;
  endDate: string;
  billable: boolean;
  maxCostRate: number | null;
  priority: RequestPriority;
  notes: string;
}

const blankForm = (): RequestForm => ({
  projectId: '',
  title: '',
  skills: [],
  minYearsExperience: null,
  quantity: 1,
  allocationPercent: 100,
  startDate: isoToday(),
  endDate: addDaysIso(isoToday(), 90),
  billable: true,
  maxCostRate: null,
  priority: 'Medium',
  notes: '',
});

/** The request form, shared by the list (new) and the detail (edit). */
@Component({
  selector: 'gggs-request-form-dialog',
  imports: [FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog [(visible)]="visible" [title]="request() ? 'Edit request' : 'New resource request'" [busy]="busy()" [error]="error()" [saveDisabled]="!form().projectId || !form().title.trim()" (save)="save()" size="lg">
      <label class="field">
        <span class="field__label">Project</span>
        <select [value]="form().projectId" [disabled]="!!request()" (change)="patch({ projectId: $any($event.target).value })">
          <option value="">Choose a project</option>
          @for (project of projects(); track project.id) {
            <option [value]="project.id">{{ project.code }} · {{ project.name }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Role needed</span>
        <input type="text" maxlength="200" placeholder="e.g. Senior Angular developer" [value]="form().title" (input)="patch({ title: $any($event.target).value })" />
      </label>
      <fieldset class="req__skills">
        <legend class="field__label">Skills</legend>
        @for (requirement of form().skills; track $index; let index = $index) {
          <div class="toolbar">
            <select aria-label="Skill" [value]="requirement.skillId" (change)="setSkill(index, { skillId: $any($event.target).value })">
              @for (skill of skills(); track skill.id) {
                <option [value]="skill.id">{{ skill.name }}</option>
              }
            </select>
            <select aria-label="Minimum level" [value]="requirement.minLevel" (change)="setSkill(index, { minLevel: +$any($event.target).value })">
              @for (level of levelOptions; track level) {
                <option [value]="level">at least {{ levels[level] }}</option>
              }
            </select>
            <button type="button" class="btn btn--link btn--sm" aria-label="Remove skill" (click)="removeSkill(index)"><i class="pi pi-times" aria-hidden="true"></i></button>
          </div>
        }
        <button type="button" class="btn btn--sm" [disabled]="!skills().length" (click)="addSkill()"><i class="pi pi-plus" aria-hidden="true"></i> Add skill</button>
      </fieldset>
      <div class="res__grid">
        <label class="field">
          <span class="field__label">People</span>
          <input type="number" min="1" max="50" [value]="form().quantity" (input)="patch({ quantity: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Allocation %</span>
          <input type="number" min="5" max="100" step="5" [value]="form().allocationPercent" (input)="patch({ allocationPercent: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">From</span>
          <input type="date" [value]="form().startDate" (change)="patch({ startDate: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Until</span>
          <input type="date" [value]="form().endDate" (change)="patch({ endDate: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Min. years</span>
          <input type="number" min="0" max="40" [value]="form().minYearsExperience ?? ''" (input)="patch({ minYearsExperience: $any($event.target).value === '' ? null : +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Max cost rate</span>
          <input type="number" min="0" step="0.01" [value]="form().maxCostRate ?? ''" (input)="patch({ maxCostRate: $any($event.target).value === '' ? null : +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Priority</span>
          <select [value]="form().priority" (change)="patch({ priority: $any($event.target).value })">
            @for (priority of priorities; track priority) {
              <option [value]="priority">{{ priority }}</option>
            }
          </select>
        </label>
      </div>
      <label class="res__flag">
        <input type="checkbox" [checked]="form().billable" (change)="patch({ billable: $any($event.target).checked })" />
        <span>Billable role</span>
      </label>
      <label class="field">
        <span class="field__label">Notes</span>
        <textarea rows="2" maxlength="2000" [value]="form().notes" (input)="patch({ notes: $any($event.target).value })"></textarea>
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './resources.css',
  styles: `
    .req__skills {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-2);
      border: 0;
      padding: 0;
      margin: 0;
    }
  `,
})
export class RequestFormDialog {
  private readonly resources = inject(ResourcesApi);
  private readonly projectsApi = inject(ProjectsApi);
  private readonly skillsApi = inject(SkillsApi);

  readonly visible = signal(false);
  readonly request = signal<ResourceRequestItem | null>(null);
  readonly saved = output<ResourceRequestDetail>();

  protected readonly priorities: readonly RequestPriority[] = ['Low', 'Medium', 'High', 'Critical'];
  protected readonly levels = SKILL_LEVEL_LABELS;
  protected readonly levelOptions = [1, 2, 3, 4, 5];
  protected readonly projects = signal<readonly ProjectItem[]>([]);
  protected readonly skills = signal<readonly SkillItem[]>([]);
  protected readonly form = signal<RequestForm>(blankForm());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Opens the dialog, blank or for an existing request. */
  open(request: ResourceRequestItem | null): void {
    this.request.set(request);
    this.form.set(
      request
        ? {
            projectId: request.projectId,
            title: request.title,
            skills: request.skills.map((skill) => ({ skillId: skill.skillId, minLevel: skill.minLevel })),
            minYearsExperience: request.minYearsExperience ?? null,
            quantity: request.quantity,
            allocationPercent: request.allocationPercent,
            startDate: request.startDate,
            endDate: request.endDate,
            billable: request.billable,
            maxCostRate: request.maxCostRate ?? null,
            priority: request.priority,
            notes: request.notes ?? '',
          }
        : blankForm(),
    );
    this.error.set(null);
    this.visible.set(true);
    if (!this.projects().length) {
      Promise.all([this.projectsApi.list({ pageSize: 100 }), this.skillsApi.catalog()]).then(
        ([page, skills]) => {
          this.projects.set(page.items);
          this.skills.set(skills);
        },
        (error: unknown) => this.error.set(message(error, 'Projects and skills could not be loaded.')),
      );
    }
  }

  protected patch(change: Partial<RequestForm>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected addSkill(): void {
    const first = this.skills()[0];
    if (first) {
      this.form.update((form) => ({ ...form, skills: [...form.skills, { skillId: first.id, minLevel: 3 }] }));
    }
  }

  protected setSkill(index: number, change: Partial<{ skillId: string; minLevel: number }>): void {
    this.form.update((form) => ({ ...form, skills: form.skills.map((skill, at) => (at === index ? { ...skill, ...change } : skill)) }));
  }

  protected removeSkill(index: number): void {
    this.form.update((form) => ({ ...form, skills: form.skills.filter((_, at) => at !== index) }));
  }

  protected async save(): Promise<void> {
    const form = this.form();
    this.busy.set(true);
    this.error.set(null);
    try {
      const detail = await this.resources.saveRequest(this.request()?.id ?? null, { ...form, title: form.title.trim(), notes: form.notes.trim() || null });
      this.visible.set(false);
      this.saved.emit(detail);
    } catch (error) {
      this.error.set(message(error, 'The request could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Resource requests (spec §58): the demand projects have raised, and how much of it is filled. */
@Component({
  selector: 'gggs-resource-requests-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, EmptyState, RequestFormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Resource requests" subtitle="People projects have asked for, and who has been found.">
      @if (canRequest) {
        <button type="button" class="btn btn--primary" (click)="dialog.open(null)"><i class="pi pi-plus" aria-hidden="true"></i> New request</button>
      }
    </gggs-page-header>
    <section class="panel toolbar res__filters">
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">Open and partly filled</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ humanize(option) }}</option>
          }
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (request of shown(); track request.id) {
        <a class="res__row" [routerLink]="['/resources/requests', request.id]">
          <span><strong>{{ request.title }}</strong><br /><span class="muted">{{ request.projectCode }} · {{ request.projectName }}</span></span>
          <span class="res__skills">
            @for (skill of request.skills; track skill.skillId) {
              <span class="res__chip">{{ skill.skillName }} {{ skill.minLevel }}+</span>
            }
          </span>
          <span>{{ request.fulfilledCount }} / {{ request.quantity }} · {{ request.allocationPercent }}%</span>
          <span class="muted">{{ request.startDate | date: 'd MMM' }} – {{ request.endDate | date: 'd MMM y' }} · {{ request.priority }}</span>
          <gggs-status-badge [status]="request.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-inbox" title="No requests here" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
    <gggs-request-form-dialog #dialog (saved)="opened($event)" />
  `,
  styleUrl: './resources.css',
})
export class ResourceRequestsPage {
  private readonly api = inject(ResourcesApi);
  private readonly router = inject(Router);

  protected readonly canRequest = inject(PermissionStore).has('resource.request.manage');
  protected readonly statuses: readonly RequestStatus[] = ['Open', 'PartiallyFulfilled', 'Fulfilled', 'Cancelled'];
  protected readonly humanize = humanize;
  protected readonly status = signal<RequestStatus | ''>('');
  protected readonly requests = signal<readonly ResourceRequestItem[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly shown = computed(() =>
    this.status() ? this.requests() : this.requests().filter((request) => request.status === 'Open' || request.status === 'PartiallyFulfilled'),
  );

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.requests.set(await this.api.requests(this.status()));
    } catch (error) {
      this.error.set(message(error, 'Requests could not be loaded.'));
    }
  }

  /** A new request opens on its own page, where the matches are. */
  protected opened(detail: ResourceRequestDetail): void {
    void this.router.navigate(['/resources/requests', detail.request.id]);
  }
}

/**
 * One request (spec §58-59): the ranked matches with their score breakdown, the shortlist, and
 * allocating a shortlisted candidate - which books them on the project's team.
 */
@Component({
  selector: 'gggs-resource-request-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, StatusBadge, EmptyState, Skeleton, RequestFormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (detail(); as d) {
      <gggs-page-header [title]="d.request.title" [subtitle]="(d.request.projectCode ?? '') + ' · ' + (d.request.projectName ?? '')">
        @if (d.request.canManage && (d.request.status === 'Open' || d.request.status === 'PartiallyFulfilled')) {
          <button type="button" class="btn" (click)="dialog.open(d.request)">Edit</button>
          <button type="button" class="btn" [disabled]="busy()" (click)="cancel()">Cancel request</button>
        }
      </gggs-page-header>

      <section class="panel stack">
        <div class="res__grid">
          <span><span class="muted">Status</span><br /><gggs-status-badge [status]="d.request.status" /></span>
          <span><span class="muted">Filled</span><br />{{ d.request.fulfilledCount }} of {{ d.request.quantity }}</span>
          <span><span class="muted">Allocation</span><br />{{ d.request.allocationPercent }}% · {{ d.request.billable ? 'billable' : 'non-billable' }}</span>
          <span><span class="muted">When</span><br />{{ d.request.startDate | date: 'd MMM y' }} – {{ d.request.endDate | date: 'd MMM y' }}</span>
          <span><span class="muted">Priority</span><br />{{ d.request.priority }}</span>
          <span><span class="muted">Requested by</span><br />{{ d.request.requestedByName ?? '—' }}</span>
        </div>
        <div class="res__skills">
          @for (skill of d.request.skills; track skill.skillId) {
            <span class="res__chip">{{ skill.skillName }} · at least {{ levels[skill.minLevel] }}</span>
          }
          @if (d.request.minYearsExperience) {
            <span class="res__chip">{{ d.request.minYearsExperience }}+ years</span>
          }
        </div>
        @if (d.request.notes) {
          <p class="muted">{{ d.request.notes }}</p>
        }
      </section>

      <section class="panel stack">
        <h2 class="res__heading">Shortlist</h2>
        @for (candidate of d.candidates; track candidate.id) {
          <div class="res__row">
            <a [routerLink]="['/resources', candidate.employeeId]"><strong>{{ candidate.employeeName ?? 'Employee' }}</strong></a>
            <span>Score {{ candidate.score | number: '1.0-0' }}</span>
            <span class="muted">{{ candidate.note ?? '' }}</span>
            <gggs-status-badge [status]="candidate.status" />
            <span>
              @if (canAct(d) && candidate.status === 'Proposed') {
                <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="allocate(candidate.id)">Allocate</button>
                <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="reject(candidate.id)">Reject</button>
              }
            </span>
          </div>
        } @empty {
          <p class="muted">No one shortlisted yet. Pick from the matches below.</p>
        }
      </section>

      @if (canAct(d)) {
        <section class="panel stack">
          <h2 class="res__heading">Best matches</h2>
          <p class="muted">Scored on skills (40), availability over the dates (25), experience (15), location (10) and cost (10).</p>
          @for (match of matches(); track match.employeeId) {
            <div class="res__row">
              <span>
                <a [routerLink]="['/resources', match.employeeId]"><strong>{{ match.employeeName }}</strong></a>
                <br /><span class="muted">{{ match.designation ?? '' }}{{ match.exitDate ? ' · leaving ' + (match.exitDate | date: 'd MMM') : '' }}</span>
              </span>
              <span>
                <strong>{{ match.score.overall | number: '1.0-0' }}</strong>
                <div class="res__bar" aria-hidden="true"><div class="res__bar-fill" [style.width.%]="match.score.overall"></div></div>
              </span>
              <span class="muted">skills {{ match.score.skills | number: '1.0-0' }} · avail. {{ match.score.availability | number: '1.0-0' }} · exp. {{ match.score.experience | number: '1.0-0' }} · loc. {{ match.score.location | number: '1.0-0' }} · cost {{ match.score.cost | number: '1.0-0' }}</span>
              <span [class.res__danger]="match.peakAllocation + d.request.allocationPercent > 100">
                {{ match.peakAllocation | number: '1.0-0' }}% busy
                @if (match.score.missingSkills.length) {
                  <br /><span class="muted">missing {{ match.score.missingSkills.join(', ') }}</span>
                }
              </span>
              <span>
                @if (match.candidateStatus) {
                  <gggs-status-badge [status]="match.candidateStatus" />
                } @else {
                  <button type="button" class="btn btn--sm" [disabled]="busy()" (click)="shortlist(match)">Shortlist</button>
                }
              </span>
            </div>
          } @empty {
            <gggs-empty-state icon="pi pi-search" title="No one matches yet" />
          }
        </section>
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (notice()) {
      <p class="alert alert--success" role="status">{{ notice() }}</p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    <gggs-request-form-dialog #dialog (saved)="saved($event)" />
  `,
  styleUrl: './resources.css',
})
export class ResourceRequestPage {
  private readonly api = inject(ResourcesApi);

  readonly id = input.required<string>();

  protected readonly levels = SKILL_LEVEL_LABELS;
  protected readonly detail = signal<ResourceRequestDetail | null>(null);
  protected readonly matches = signal<readonly MatchItem[]>([]);
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      void this.load(this.id());
    });
  }

  protected canAct(detail: ResourceRequestDetail): boolean {
    return detail.request.canManage && (detail.request.status === 'Open' || detail.request.status === 'PartiallyFulfilled');
  }

  protected saved(detail: ResourceRequestDetail): void {
    this.detail.set(detail);
    this.notice.set('Request saved.');
    void this.loadMatches();
  }

  protected async shortlist(match: MatchItem): Promise<void> {
    await this.act(() => this.api.shortlist(this.id(), match.employeeId, null), `${match.employeeName} shortlisted.`);
  }

  protected async allocate(candidateId: string): Promise<void> {
    await this.act(() => this.api.allocateCandidate(this.id(), candidateId), 'Allocated to the project team.');
  }

  protected async reject(candidateId: string): Promise<void> {
    await this.act(() => this.api.rejectCandidate(this.id(), candidateId, null), 'Candidate rejected.');
  }

  protected async cancel(): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.cancelRequest(this.id());
      await this.load(this.id());
    } catch (error) {
      this.error.set(message(error, 'The request could not be cancelled.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async act(action: () => Promise<ResourceRequestDetail>, done: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      this.detail.set(await action());
      this.notice.set(done);
      await this.loadMatches();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    this.error.set(null);
    try {
      this.detail.set(await this.api.request(id));
      await this.loadMatches();
    } catch (error) {
      this.error.set(message(error, 'The request could not be loaded.'));
    }
  }

  private async loadMatches(): Promise<void> {
    const detail = this.detail();
    this.matches.set(detail && this.canAct(detail) ? await this.api.matches(detail.request.id) : []);
  }
}
