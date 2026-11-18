import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { PermissionStore } from '@gggs/core/permissions';
import { RecruitmentApi, WorkforceApi } from '@gggs/shared/hiring';
import type { ApplicationStage, CandidateProfile, CandidateSource, CandidateView, JobApplicationItem, ManpowerRequestItem, OrgUnit, RequisitionItem, RequisitionStatus } from '@gggs/shared/models';
import { CANDIDATE_SOURCES, PIPELINE_STAGES, humanize } from '@gggs/shared/models';
import { OrgStructureApi } from '@gggs/shared/org-reference';
import { Drawer, EmptyState, localIsoDate, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { CandidateForm, RequisitionForm, errorText } from './recruitment-forms';

/** Stage colours, shared by the board columns, the cards' stage bars and the list. */
export const STAGE_COLORS: Readonly<Record<string, string>> = {
  Applied: '#7c8796',
  Screening: '#1e6fb8',
  Shortlisted: '#6b4fd1',
  Interview: '#d81324',
  Offer: '#c27a00',
  Hired: '#0e6b30',
};

const today = () => localIsoDate(new Date());
const dayDiff = (from: string, to: string) => Math.round((Date.parse(to) - Date.parse(from)) / 86_400_000);

/** How urgent a next step is: late (due before today), soon (within two days) or on track. */
export function dueState(due: string | undefined, on = today()): 'late' | 'soon' | '' {
  if (!due) {
    return '';
  }
  const days = dayDiff(on, due);
  return days < 0 ? 'late' : days <= 2 ? 'soon' : '';
}

/** "3 days late", "due today", "due tomorrow", or the date. */
export function dueLabel(due: string, on = today()): string {
  const days = dayDiff(on, due);
  if (days < 0) {
    return `${-days} day${days === -1 ? '' : 's'} late`;
  }
  if (days === 0) {
    return 'due today';
  }
  if (days === 1) {
    return 'due tomorrow';
  }
  return `due ${new Date(due + 'T00:00:00').toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })}`;
}

const AVATAR_COLORS = ['#d81324', '#1e6fb8', '#6b4fd1', '#0e6b30', '#c27a00', '#0f766e', '#9d174d'];
export const avatarColor = (name: string) => AVATAR_COLORS[[...name].reduce((sum, c) => sum + c.charCodeAt(0), 0) % AVATAR_COLORS.length];
export const initials = (name: string) => name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part.charAt(0).toUpperCase()).join('');
const scoreClass = (score: number) => (score >= 80 ? 'pl__score--high' : score >= 60 ? 'pl__score--mid' : 'pl__score--low');
const lakh = (amount: number | undefined) => (amount === undefined ? '' : `₹${(amount / 100_000).toLocaleString('en-IN', { maximumFractionDigits: 1 })} L`);

type StatusTab = 'Open' | 'OnHold' | 'Filled' | 'Closed' | 'All';
const STATUS_TABS: readonly { key: StatusTab; label: string }[] = [
  { key: 'Open', label: 'Open' },
  { key: 'OnHold', label: 'On hold' },
  { key: 'Filled', label: 'Filled' },
  { key: 'Closed', label: 'Closed' },
  { key: 'All', label: 'All' },
];
const inTab = (status: RequisitionStatus, tab: StatusTab) => tab === 'All' || status === tab || (tab === 'Closed' && status === 'Cancelled');

interface RequirementFilters {
  readonly departmentId: string;
  readonly priority: string;
  readonly recruiter: string;
  readonly skills: readonly string[];
  readonly dueSoon: boolean;
}
const noRequirementFilters = (): RequirementFilters => ({ departmentId: '', priority: '', recruiter: '', skills: [], dueSoon: false });

/**
 * Requirements (requisitions): every opening as a card - hired against openings, candidates by
 * stage, days to target - beside the selected one's pipeline board. Approved manpower requests
 * arrive here as requirements; ones still waiting for approval are shown above the list.
 */
@Component({
  selector: 'gggs-requirements-page',
  imports: [DatePipe, RouterLink, PageHeader, EmptyState, Skeleton, StatusBadge, Drawer, RequisitionForm, CandidateForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Requirements" subtitle="Each opening and its pipeline in one place. Pick a requirement to see its candidates by stage.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="formOpen.set(true)"><i class="pi pi-plus" aria-hidden="true"></i> New requirement</button>
      }
    </gggs-page-header>

    @for (request of awaiting(); track request.id) {
      <div class="pl__upstream">
        <span class="pl__upstream-icon"><i class="pi pi-clock" aria-hidden="true"></i></span>
        <div>
          <strong>{{ request.title }}</strong> <span class="pl__number">{{ request.number }}</span>
          <div class="pl__muted">
            Manpower request from {{ request.requesterName ?? 'a manager' }} for {{ request.positions }} {{ request.positions === 1 ? 'position' : 'positions' }}{{ request.departmentName ? ' in ' + request.departmentName : '' }}{{ request.requiredBy ? ', needed by ' + (request.requiredBy | date: 'd MMM') : '' }}.
            It becomes a requirement here once approved.
          </div>
        </div>
        <a class="btn btn--sm" [routerLink]="['/workforce/requests']">Open request</a>
      </div>
    }

    <div class="pl__toolbar">
      <div class="pl__seg" role="group" aria-label="Status">
        @for (tab of statusTabs; track tab.key) {
          <button type="button" [attr.aria-pressed]="status() === tab.key" (click)="status.set(tab.key)">{{ tab.label }} <span class="pl__seg-n">{{ countIn(tab.key) }}</span></button>
        }
      </div>
      <label class="pl__search">
        <i class="pi pi-search" aria-hidden="true"></i>
        <input type="search" placeholder="Search title, number, skill or manager" aria-label="Search requirements" [value]="search()" (input)="search.set($any($event.target).value)" />
      </label>
      <button type="button" class="btn" (click)="openFilters()">
        <i class="pi pi-sliders-h" aria-hidden="true"></i> Filters
        @if (chips().length) {
          <span class="pl__count">{{ chips().length }}</span>
        }
      </button>
    </div>
    @if (chips().length) {
      <div class="pl__chips">
        @for (chip of chips(); track chip.label) {
          <span class="pl__chip">{{ chip.label }}<button type="button" [attr.aria-label]="'Remove ' + chip.label" (click)="chip.clear()">×</button></span>
        }
        <button type="button" class="btn btn--link btn--sm" (click)="filters.set(empty())">Clear filters</button>
      </div>
    }

    @if (loaded()) {
      <div class="pl__split">
        <div class="pl__list">
          @for (r of shown(); track r.id) {
            <button type="button" class="pl__rcard" [attr.aria-current]="r.id === selected()?.id" (click)="select(r.id)">
              <span class="pl__rcard-top">
                <span>
                  <h3>{{ r.title }}</h3>
                  <span class="pl__number">{{ r.number }}</span>
                </span>
                <span class="pl__ring" [title]="r.hired + ' of ' + r.openings + ' hired'">
                  <svg width="44" height="44" viewBox="0 0 44 44" aria-hidden="true">
                    <circle cx="22" cy="22" r="17" fill="none" stroke="var(--gggs-color-surface-sunken)" stroke-width="5" />
                    <circle cx="22" cy="22" r="17" fill="none" stroke="var(--gggs-color-success)" stroke-width="5" stroke-linecap="round" [attr.stroke-dasharray]="ring(r)" />
                  </svg>
                  <span>{{ r.hired }}/{{ r.openings }}</span>
                </span>
              </span>
              <span class="pl__meta">
                <span>{{ r.departmentName ?? 'No department' }}</span>
                @if (r.locationName) {
                  <span>{{ r.locationName }}</span>
                }
                <span>{{ humanize(r.workModeCode) }}</span>
              </span>
              <span class="pl__bar" role="img" [attr.aria-label]="r.applications + ' candidates by stage'">
                @for (part of stageParts(r); track part.stage) {
                  <i [style.width.%]="part.share" [style.background]="part.color"></i>
                }
              </span>
              <span class="pl__meta">
                <strong>{{ r.applications }} {{ r.applications === 1 ? 'candidate' : 'candidates' }}</strong>
                @if (r.priority) {
                  <span class="pl__pill" [class]="priorityClass(r.priority)">{{ r.priority }}</span>
                }
                @if (r.targetDate && r.status === 'Open') {
                  <span [class]="targetState(r.targetDate)">{{ targetLabel(r.targetDate) }}</span>
                } @else {
                  <gggs-status-badge [status]="r.status" />
                }
              </span>
            </button>
          } @empty {
            <gggs-empty-state icon="pi pi-file-edit" title="No requirements here" [description]="search() || chips().length ? 'Remove a filter or search for something else.' : 'Approved manpower requests appear here, or open one directly.'" />
          }
        </div>

        @if (selected(); as r) {
          <section class="pl__pane" [attr.aria-label]="r.title">
            <div class="pl__pane-head">
              <div class="pl__pane-title">
                <div>
                  <span class="pl__number">{{ r.number }}</span>
                  <h2>{{ r.title }}</h2>
                  <span class="pl__muted">{{ r.departmentName ?? 'No department' }}{{ r.roleName ? ' · ' + r.roleName : '' }}{{ r.hiringManagerName ? ' · hiring manager ' + r.hiringManagerName : '' }}</span>
                </div>
                <div class="pl__actions">
                  @if (canManage && (r.status === 'Open' || r.status === 'OnHold')) {
                    <button type="button" class="btn btn--sm" (click)="editOpen.set(true)">Edit</button>
                  }
                  <a class="btn btn--sm" [routerLink]="['/recruitment/requirements', r.id]">Postings and more</a>
                  @if (canApply && r.status === 'Open') {
                    <button type="button" class="btn btn--sm btn--primary" (click)="candidateOpen.set(true)"><i class="pi pi-user-plus" aria-hidden="true"></i> Add candidate</button>
                  }
                </div>
              </div>
              <div class="pl__facts">
                <div><span class="pl__fact-label">Hired</span><strong>{{ r.hired }} of {{ r.openings }}</strong></div>
                <div>
                  <span class="pl__fact-label">Target</span>
                  @if (r.targetDate) {
                    <strong>{{ r.targetDate | date: 'd MMM y' }}</strong>
                  } @else {
                    <span class="pl__muted">Not set</span>
                  }
                </div>
                <div><span class="pl__fact-label">Budget</span><strong>{{ budget(r) }}</strong></div>
                <div><span class="pl__fact-label">Experience</span><strong>{{ experience(r) }}</strong></div>
                <div><span class="pl__fact-label">Recruiter</span><strong>{{ r.recruiterName ?? 'Not assigned' }}</strong></div>
              </div>
              @if (retired(r)) {
                <p class="pl__warn"><i class="pi pi-exclamation-triangle" aria-hidden="true"></i><span>{{ r.departmentName }} is no longer an active department. Edit this requirement and choose a current department or function.</span></p>
              }
              @if (!r.roleId) {
                <p class="pl__warn"><i class="pi pi-info-circle" aria-hidden="true"></i><span>No role is set. Choose one of the department's roles so the offer can carry its designation.</span></p>
              }
            </div>
            <div class="pl__pane-tabs" role="tablist">
              <button type="button" role="tab" [attr.aria-selected]="paneTab() === 'pipeline'" (click)="paneTab.set('pipeline')">Pipeline ({{ active().length }})</button>
              <button type="button" role="tab" [attr.aria-selected]="paneTab() === 'details'" (click)="paneTab.set('details')">Job details</button>
              @if (closed().length) {
                <button type="button" role="tab" [attr.aria-selected]="paneTab() === 'closed'" (click)="paneTab.set('closed')">Closed ({{ closed().length }})</button>
              }
            </div>
            <div class="pl__pane-body">
              @switch (paneTab()) {
                @case ('pipeline') {
                  @if (pipelineLoaded()) {
                    <div class="pl__board">
                      @for (stage of boardStages; track stage) {
                        <section class="pl__col" [attr.aria-label]="humanize(stage)">
                          <div class="pl__col-head"><i [style.background]="color(stage)"></i>{{ humanize(stage) }}<span>{{ inStage(stage).length }}</span></div>
                          @for (a of inStage(stage); track a.id) {
                            <a class="pl__ccard" [routerLink]="['/recruitment/applications', a.id]">
                              <span class="pl__ccard-top">
                                <span class="pl__avatar" [style.background]="avatar(a.candidateName)" aria-hidden="true">{{ initials(a.candidateName) }}</span>
                                <span><strong>{{ a.candidateName }}</strong><small>{{ a.currentTitle ?? '' }}{{ a.currentCompany ? ' · ' + a.currentCompany : '' }}</small></span>
                                <span class="pl__score" [class]="score(a.matchScore)" title="Match score">{{ a.matchScore }}%</span>
                              </span>
                              <span class="pl__meta">
                                @if (a.experienceYears !== undefined) {
                                  <span>{{ a.experienceYears }} yrs</span>
                                }
                                @if (a.noticePeriodDays !== undefined) {
                                  <span>{{ a.noticePeriodDays }} days notice</span>
                                }
                                @if (a.source === 'Referral') {
                                  <span class="pl__pill pl__pill--info">Referral</span>
                                }
                              </span>
                              @if (a.nextStep && a.nextStepDue) {
                                <span class="pl__next" [class.pl__next--late]="due(a.nextStepDue) === 'late'" [class.pl__next--soon]="due(a.nextStepDue) === 'soon'">
                                  <i class="pi pi-clock" aria-hidden="true"></i><span>{{ a.nextStep }} · {{ dueText(a.nextStepDue) }}</span>
                                </span>
                              }
                            </a>
                          } @empty {
                            <p class="pl__col-empty">Nobody yet</p>
                          }
                        </section>
                      }
                    </div>
                  } @else {
                    <gggs-skeleton [lines]="4" />
                  }
                }
                @case ('details') {
                  <dl class="pl__kv">
                    <dt>Employment</dt><dd>{{ humanize(r.employmentTypeCode) }} · {{ humanize(r.workModeCode) }}</dd>
                    <dt>Role</dt><dd>{{ r.roleName ?? 'Not set' }}</dd>
                    <dt>Location</dt><dd>{{ r.locationName ?? 'Any' }}</dd>
                    <dt>Experience</dt><dd>{{ experience(r) }}</dd>
                    <dt>Salary budget</dt><dd>{{ budget(r) }} a year</dd>
                    <dt>Skills</dt>
                    <dd>
                      <span class="pl__tags">
                        @for (skill of r.skills; track skill) {
                          <span class="pl__tag">{{ skill }}</span>
                        } @empty {
                          <span class="pl__muted">None listed</span>
                        }
                      </span>
                    </dd>
                    <dt>Opened</dt><dd>{{ r.openedOn | date: 'd MMM y' }}{{ r.manpowerRequestId ? ' · from a manpower request' : ' · directly' }}</dd>
                  </dl>
                  @if (r.description) {
                    <p class="rec__pre">{{ r.description }}</p>
                  }
                }
                @case ('closed') {
                  <div class="pl__table-wrap">
                    <table class="pl__table">
                      <thead><tr><th>Candidate</th><th>Outcome</th><th>Reason</th><th>On</th></tr></thead>
                      <tbody>
                        @for (a of closed(); track a.id) {
                          <tr>
                            <td><a [routerLink]="['/recruitment/applications', a.id]"><strong>{{ a.candidateName }}</strong></a></td>
                            <td><gggs-status-badge [status]="a.stage" /></td>
                            <td class="pl__muted">{{ a.rejectionReason ?? '' }}</td>
                            <td class="pl__muted">{{ a.stageChangedDate | date: 'd MMM y' }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                }
              }
            </div>
          </section>
        }
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-drawer [(visible)]="filtersOpen" title="Filter requirements">
      <div class="pl__drawer">
        <label class="field">
          <span class="field__label">Department or function</span>
          <select [value]="draft().departmentId" (change)="patchDraft({ departmentId: $any($event.target).value })">
            <option value="">All</option>
            @for (option of departmentOptions(); track option.id) {
              <option [value]="option.id">{{ option.label }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Priority</span>
          <select [value]="draft().priority" (change)="patchDraft({ priority: $any($event.target).value })">
            <option value="">Any</option>
            @for (option of priorities; track option) {
              <option [value]="option">{{ option }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Recruiter</span>
          <select [value]="draft().recruiter" (change)="patchDraft({ recruiter: $any($event.target).value })">
            <option value="">Anyone</option>
            @for (option of recruiters(); track option) {
              <option [value]="option">{{ option }}</option>
            }
          </select>
        </label>
        @if (skillOptions().length) {
          <p class="pl__group">Skills (must have all)</p>
          <div class="pl__checks">
            @for (skill of skillOptions(); track skill) {
              <label><input type="checkbox" [checked]="draft().skills.includes(skill)" (change)="toggleSkill(skill, $any($event.target).checked)" />{{ skill }}</label>
            }
          </div>
        }
        <div class="pl__checks">
          <label><input type="checkbox" [checked]="draft().dueSoon" (change)="patchDraft({ dueSoon: $any($event.target).checked })" />Target date within 7 days</label>
        </div>
        <div class="pl__drawer-actions">
          <button type="button" class="btn btn--link" (click)="draft.set(empty())">Reset</button>
          <button type="button" class="btn btn--primary" (click)="applyFilters()">Show results</button>
        </div>
      </div>
    </gggs-drawer>

    <gggs-requisition-form [(visible)]="formOpen" (saved)="opened($event)" />
    @if (selected(); as r) {
      <gggs-requisition-form [(visible)]="editOpen" [requisition]="r" (saved)="replaced($event)" />
      <gggs-candidate-form [(visible)]="candidateOpen" [requisitionId]="r.id" (saved)="loadPipeline(r.id)" />
    }
  `,
  styleUrls: ['./recruitment.css', './pipeline.css', './lists.css', './requirements.css'],
})
export class RequirementsPage {
  private readonly api = inject(RecruitmentApi);
  private readonly workforce = inject(WorkforceApi);
  private readonly org = inject(OrgStructureApi);
  private readonly permissions = inject(PermissionStore);

  protected readonly canManage = this.permissions.has('recruitment.requisition.manage');
  protected readonly canApply = this.permissions.has('recruitment.application.manage');
  protected readonly humanize = humanize;
  protected readonly statusTabs = STATUS_TABS;
  protected readonly priorities = ['Critical', 'High', 'Medium', 'Low'];
  protected readonly boardStages: readonly ApplicationStage[] = [...PIPELINE_STAGES, 'Hired'];
  protected readonly empty = noRequirementFilters;
  protected readonly initials = initials;
  protected readonly avatar = avatarColor;
  protected readonly score = scoreClass;
  protected readonly due = dueState;
  protected readonly dueText = dueLabel;
  protected readonly color = (stage: string) => STAGE_COLORS[stage] ?? '#7c8796';

  protected readonly requisitions = signal<readonly RequisitionItem[]>([]);
  protected readonly awaiting = signal<readonly ManpowerRequestItem[]>([]);
  private readonly departments = signal<readonly OrgUnit[]>([]);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<StatusTab>('Open');
  protected readonly search = signal('');
  protected readonly filters = signal<RequirementFilters>(noRequirementFilters());
  protected readonly draft = signal<RequirementFilters>(noRequirementFilters());
  protected readonly filtersOpen = signal(false);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly applications = signal<readonly JobApplicationItem[]>([]);
  protected readonly pipelineLoaded = signal(false);
  protected readonly paneTab = signal<'pipeline' | 'details' | 'closed'>('pipeline');
  protected readonly formOpen = signal(false);
  protected readonly editOpen = signal(false);
  protected readonly candidateOpen = signal(false);

  protected readonly shown = computed(() => {
    const term = this.search().trim().toLowerCase();
    const f = this.filters();
    const departments = this.departments();
    const within = (id: string) => id === f.departmentId || departments.find((d) => d.id === id)?.['parentDepartmentId'] === f.departmentId;
    const soon = localIsoDate(new Date(Date.now() + 7 * 86_400_000));
    return this.requisitions().filter(
      (r) =>
        inTab(r.status, this.status()) &&
        (!term || `${r.title} ${r.number} ${r.skills.join(' ')} ${r.hiringManagerName ?? ''}`.toLowerCase().includes(term)) &&
        (!f.departmentId || within(r.departmentId)) &&
        (!f.priority || r.priority === f.priority) &&
        (!f.recruiter || r.recruiterName === f.recruiter) &&
        f.skills.every((skill) => r.skills.includes(skill)) &&
        (!f.dueSoon || (!!r.targetDate && r.targetDate <= soon)),
    );
  });

  protected readonly selected = computed(() => {
    const shown = this.shown();
    return shown.find((r) => r.id === this.selectedId()) ?? (shown.length ? shown[0] : null);
  });

  protected readonly active = computed(() => this.applications().filter((a) => this.boardStages.includes(a.stage)));
  protected readonly closed = computed(() => this.applications().filter((a) => !this.boardStages.includes(a.stage)));
  protected readonly recruiters = computed(() => [...new Set(this.requisitions().map((r) => r.recruiterName).filter((name): name is string => !!name))].sort());
  protected readonly skillOptions = computed(() => [...new Set(this.requisitions().flatMap((r) => r.skills))].sort());
  protected readonly departmentOptions = computed(() => {
    const rows = this.departments();
    const name = (id: unknown) => rows.find((row) => row.id === id)?.name;
    return rows
      .map((row) => ({ id: row.id, label: row['parentDepartmentId'] ? `${name(row['parentDepartmentId']) ?? ''} › ${row.name}` : row.name }))
      .sort((a, b) => a.label.localeCompare(b.label));
  });

  protected readonly chips = computed(() => {
    const f = this.filters();
    const set = (change: Partial<RequirementFilters>) => () => this.filters.update((current) => ({ ...current, ...change }));
    const label = this.departmentOptions().find((option) => option.id === f.departmentId)?.label ?? 'Department';
    return [
      ...(f.departmentId ? [{ label, clear: set({ departmentId: '' }) }] : []),
      ...(f.priority ? [{ label: `Priority: ${f.priority}`, clear: set({ priority: '' }) }] : []),
      ...(f.recruiter ? [{ label: `Recruiter: ${f.recruiter}`, clear: set({ recruiter: '' }) }] : []),
      ...f.skills.map((skill) => ({ label: skill, clear: set({ skills: f.skills.filter((s) => s !== skill) }) })),
      ...(f.dueSoon ? [{ label: 'Target within 7 days', clear: set({ dueSoon: false }) }] : []),
    ];
  });

  constructor() {
    void this.load();
    // The board follows the selected requirement.
    effect(() => {
      const id = this.selected()?.id;
      if (id) {
        void this.loadPipeline(id);
      }
    });
  }

  protected countIn(tab: StatusTab): number {
    return this.requisitions().filter((r) => inTab(r.status, tab)).length;
  }

  protected select(id: string): void {
    this.selectedId.set(id);
  }

  protected inStage(stage: ApplicationStage): readonly JobApplicationItem[] {
    return this.active()
      .filter((a) => a.stage === stage)
      .sort((a, b) => b.matchScore - a.matchScore);
  }

  protected ring(r: RequisitionItem): string {
    const length = 2 * Math.PI * 17;
    return `${r.openings ? (length * r.hired) / r.openings : 0} ${length}`;
  }

  protected stageParts(r: RequisitionItem): readonly { stage: string; share: number; color: string }[] {
    const total = Object.entries(r.pipeline)
      .filter(([stage]) => STAGE_COLORS[stage])
      .reduce((sum, [, n]) => sum + n, 0);
    return total
      ? Object.entries(STAGE_COLORS)
          .map(([stage, color]) => ({ stage, color, count: r.pipeline[stage] ?? 0 }))
          .filter((part) => part.count > 0)
          .map((part) => ({ stage: part.stage, share: (100 * part.count) / total, color: part.color }))
      : [];
  }

  protected priorityClass(priority: string): string {
    return `pl__pill ${priority === 'Critical' || priority === 'High' ? 'pl__pill--danger' : priority === 'Medium' ? 'pl__pill--warning' : 'pl__pill--muted'}`;
  }

  protected targetState(target: string): string {
    const days = dayDiff(today(), target);
    return days < 0 ? 'pl__pill pl__pill--danger' : days <= 3 ? 'pl__pill pl__pill--warning' : '';
  }

  protected targetLabel(target: string): string {
    const days = dayDiff(today(), target);
    return days < 0 ? `${-days} day${days === -1 ? '' : 's'} past target` : days === 0 ? 'Target today' : `${days} day${days === 1 ? '' : 's'} to target`;
  }

  protected budget(r: RequisitionItem): string {
    return r.salaryMin === undefined && r.salaryMax === undefined ? 'Not set' : `${lakh(r.salaryMin) || '…'} – ${lakh(r.salaryMax) || '…'}`;
  }

  protected experience(r: RequisitionItem): string {
    return r.minExperienceYears === undefined && r.maxExperienceYears === undefined ? 'Any' : `${r.minExperienceYears ?? 0}–${r.maxExperienceYears ?? 'any'} years`;
  }

  /** The requirement's department was closed or retired (for example by a structure import). */
  protected retired(r: RequisitionItem): boolean {
    return this.departments().length > 0 && !this.departments().some((d) => d.id === r.departmentId);
  }

  protected openFilters(): void {
    this.draft.set(this.filters());
    this.filtersOpen.set(true);
  }

  protected patchDraft(change: Partial<RequirementFilters>): void {
    this.draft.update((current) => ({ ...current, ...change }));
  }

  protected toggleSkill(skill: string, on: boolean): void {
    this.draft.update((current) => ({ ...current, skills: on ? [...current.skills, skill] : current.skills.filter((s) => s !== skill) }));
  }

  protected applyFilters(): void {
    this.filters.set(this.draft());
    this.filtersOpen.set(false);
  }

  protected opened(requisition: RequisitionItem): void {
    this.requisitions.update((rows) => [requisition, ...rows]);
    this.status.set('Open');
    this.selectedId.set(requisition.id);
  }

  protected replaced(requisition: RequisitionItem): void {
    this.requisitions.update((rows) => rows.map((row) => (row.id === requisition.id ? requisition : row)));
  }

  protected async loadPipeline(id: string): Promise<void> {
    this.pipelineLoaded.set(false);
    try {
      const rows = await this.api.applications({ requisitionId: id });
      if (this.selected()?.id === id) {
        this.applications.set(rows);
        this.pipelineLoaded.set(true);
      }
    } catch (error) {
      this.error.set(errorText(error, 'The pipeline could not be loaded.'));
    }
  }

  private async load(): Promise<void> {
    // Requests still waiting for approval, for people who can see them; nobody else misses them.
    if (this.permissions.hasAny(['workforce.request.view', 'workforce.request.manage'])) {
      this.workforce.requests({ status: 'PendingApproval' }).then(
        (rows) => this.awaiting.set(rows),
        () => undefined,
      );
    }
    try {
      const [requisitions, departments] = await Promise.all([this.api.requisitions({}), this.org.active('departments')]);
      this.requisitions.set(requisitions);
      this.departments.set(departments);
      this.loaded.set(true);
    } catch (error) {
      this.error.set(errorText(error, 'Requirements could not be loaded.'));
    }
  }
}

type SmartList = 'all' | 'action' | 'week' | 'referred' | 'pool';
interface CandidateFilters {
  readonly stages: readonly ApplicationStage[];
  readonly sources: readonly CandidateSource[];
  readonly expMin: string;
  readonly expMax: string;
  readonly notice: string;
}
const noCandidateFilters = (): CandidateFilters => ({ stages: [], sources: [], expMin: '', expMax: '', notice: '' });

/**
 * Candidates: everyone in the pipeline across all requirements, as a stage board or a list, with
 * what happens next and when. Smart lists pick out what needs action; the talent pool holds the
 * people not in any open pipeline.
 */
@Component({
  selector: 'gggs-candidate-pipeline-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, EmptyState, Skeleton, StatusBadge, Drawer, CandidateForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Candidates" subtitle="Everyone in the pipeline across all requirements, with what happens next and when.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="formOpen.set(true)"><i class="pi pi-user-plus" aria-hidden="true"></i> Add candidate</button>
      }
    </gggs-page-header>

    <div class="pl__smart" role="group" aria-label="Smart lists">
      @for (list of smartLists; track list.key) {
        <button type="button" [attr.aria-pressed]="smart() === list.key" (click)="smart.set(list.key)"><strong>{{ countOf(list.key) }}</strong><span>{{ list.label }}</span></button>
      }
    </div>

    <div class="pl__toolbar">
      <label class="pl__search">
        <i class="pi pi-search" aria-hidden="true"></i>
        <input type="search" placeholder="Search name, company, skill or number" aria-label="Search candidates" [value]="search()" (input)="search.set($any($event.target).value)" />
      </label>
      @if (smart() !== 'pool') {
        <select aria-label="Requirement" [value]="requisitionId()" (change)="requisitionId.set($any($event.target).value)">
          <option value="">All requirements</option>
          @for (option of requirementOptions(); track option.id) {
            <option [value]="option.id">{{ option.title }}</option>
          }
        </select>
      }
      <button type="button" class="btn" (click)="openFilters()">
        <i class="pi pi-sliders-h" aria-hidden="true"></i> Filters
        @if (chips().length) {
          <span class="pl__count">{{ chips().length }}</span>
        }
      </button>
      @if (smart() !== 'pool') {
        <div class="pl__seg" role="group" aria-label="Layout">
          <button type="button" [attr.aria-pressed]="layout() === 'board'" (click)="layout.set('board')"><i class="pi pi-th-large" aria-hidden="true"></i> Board</button>
          <button type="button" [attr.aria-pressed]="layout() === 'list'" (click)="layout.set('list')"><i class="pi pi-list" aria-hidden="true"></i> List</button>
        </div>
      }
    </div>
    @if (chips().length) {
      <div class="pl__chips">
        @for (chip of chips(); track chip.label) {
          <span class="pl__chip">{{ chip.label }}<button type="button" [attr.aria-label]="'Remove ' + chip.label" (click)="chip.clear()">×</button></span>
        }
        <button type="button" class="btn btn--link btn--sm" (click)="filters.set(empty())">Clear filters</button>
      </div>
    }

    @if (loaded()) {
      @if (smart() === 'pool') {
        <div class="pl__table-wrap">
          <table class="pl__table">
            <thead><tr><th>Candidate</th><th>Experience</th><th>Skills</th><th>Source</th><th>Added</th></tr></thead>
            <tbody>
              @for (c of pool(); track c.id) {
                <tr>
                  <td>
                    <a class="pl__who" [routerLink]="['/recruitment/candidates', c.id]">
                      <span class="pl__avatar" [style.background]="avatar(c.fullName)" aria-hidden="true">{{ initials(c.fullName) }}</span>
                      <span><strong>{{ c.fullName }}</strong>@if (c.formerEmployee) {<span class="pl__pill pl__pill--muted"> Former employee</span>}<span class="pl__muted" style="display:block">{{ c.currentTitle ?? '' }}{{ c.currentCompany ? ' · ' + c.currentCompany : '' }}</span></span>
                    </a>
                  </td>
                  <td>{{ c.experienceYears !== undefined ? c.experienceYears + ' yrs' : '—' }}</td>
                  <td><span class="pl__tags">@for (skill of c.skills.slice(0, 4); track skill) {<span class="pl__tag">{{ skill }}</span>}</span></td>
                  <td>{{ humanize(c.source) }}{{ c.referrerName ? ' · ' + c.referrerName : '' }}</td>
                  <td class="pl__muted">{{ c.createdDate | date: 'd MMM y' }}</td>
                </tr>
              } @empty {
                <tr><td colspan="5"><gggs-empty-state icon="pi pi-users" title="Nobody in the talent pool" description="People who are in no open pipeline appear here." /></td></tr>
              }
            </tbody>
          </table>
        </div>
      } @else if (layout() === 'board') {
        <div class="pl__board">
          @for (stage of boardStages; track stage) {
            <section class="pl__col" [attr.aria-label]="humanize(stage)">
              <div class="pl__col-head"><i [style.background]="color(stage)"></i>{{ humanize(stage) }}<span>{{ inStage(stage).length }}</span></div>
              @for (a of inStage(stage); track a.id) {
                <a class="pl__ccard" [routerLink]="['/recruitment/applications', a.id]">
                  <span class="pl__ccard-top">
                    <span class="pl__avatar" [style.background]="avatar(a.candidateName)" aria-hidden="true">{{ initials(a.candidateName) }}</span>
                    <span><strong>{{ a.candidateName }}</strong><small>{{ a.requisitionTitle }}</small></span>
                    <span class="pl__score" [class]="score(a.matchScore)" title="Match score">{{ a.matchScore }}%</span>
                  </span>
                  <span class="pl__meta">
                    @if (a.experienceYears !== undefined) {
                      <span>{{ a.experienceYears }} yrs</span>
                    }
                    @if (a.noticePeriodDays !== undefined) {
                      <span>{{ a.noticePeriodDays }} days notice</span>
                    }
                    @if (a.source === 'Referral') {
                      <span class="pl__pill pl__pill--info">Referral</span>
                    }
                  </span>
                  @if (a.nextStep && a.nextStepDue) {
                    <span class="pl__next" [class.pl__next--late]="due(a.nextStepDue) === 'late'" [class.pl__next--soon]="due(a.nextStepDue) === 'soon'">
                      <i class="pi pi-clock" aria-hidden="true"></i><span>{{ a.nextStep }} · {{ dueText(a.nextStepDue) }}</span>
                    </span>
                  }
                </a>
              } @empty {
                <p class="pl__col-empty">Nobody here</p>
              }
            </section>
          }
        </div>
      } @else {
        <div class="pl__table-wrap">
          <table class="pl__table">
            <thead><tr><th>Candidate</th><th>Requirement</th><th>Stage</th><th>Match</th><th>Experience · notice</th><th>Expected</th><th>Next step</th><th>Source</th></tr></thead>
            <tbody>
              @for (a of shown(); track a.id) {
                <tr>
                  <td>
                    <a class="pl__who" [routerLink]="['/recruitment/applications', a.id]">
                      <span class="pl__avatar" [style.background]="avatar(a.candidateName)" aria-hidden="true">{{ initials(a.candidateName) }}</span>
                      <span><strong>{{ a.candidateName }}</strong><span class="pl__muted" style="display:block">{{ a.currentTitle ?? '' }}{{ a.currentCompany ? ' · ' + a.currentCompany : '' }}</span></span>
                    </a>
                  </td>
                  <td>{{ a.requisitionTitle }}<span class="pl__number" style="display:block">{{ a.requisitionNumber }}</span></td>
                  <td><gggs-status-badge [status]="a.stage" /><span class="pl__muted" style="display:block">since {{ a.stageChangedDate | date: 'd MMM' }}</span></td>
                  <td><span class="pl__score" [class]="score(a.matchScore)">{{ a.matchScore }}%</span></td>
                  <td>{{ a.experienceYears !== undefined ? a.experienceYears + ' yrs' : '—' }}<span class="pl__muted" style="display:block">{{ a.noticePeriodDays !== undefined ? a.noticePeriodDays + ' days notice' : '' }}</span></td>
                  <td>{{ a.expectedCtc !== undefined ? '₹' + (a.expectedCtc / 100000 | number: '1.0-1') + ' L' : '—' }}</td>
                  <td>
                    @if (a.nextStep && a.nextStepDue) {
                      <span [class.pl__next--late]="due(a.nextStepDue) === 'late'" [class.pl__next--soon]="due(a.nextStepDue) === 'soon'">{{ a.nextStep }}</span>
                      <span class="pl__muted" style="display:block">{{ dueText(a.nextStepDue) }}</span>
                    } @else {
                      <span class="pl__muted">—</span>
                    }
                  </td>
                  <td>{{ humanize(a.source) }}<span class="pl__muted" style="display:block">{{ a.referrerName ? 'by ' + a.referrerName : '' }}</span></td>
                </tr>
              } @empty {
                <tr><td colspan="8"><gggs-empty-state icon="pi pi-id-card" title="Nobody matches" description="Pick another smart list or remove a filter." /></td></tr>
              }
            </tbody>
          </table>
        </div>
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-drawer [(visible)]="filtersOpen" title="Filter candidates">
      <div class="pl__drawer">
        @if (smart() !== 'pool') {
          <p class="pl__group">Stage</p>
          <div class="pl__checks">
            @for (stage of boardStages; track stage) {
              <label><input type="checkbox" [checked]="draft().stages.includes(stage)" (change)="toggle('stages', stage, $any($event.target).checked)" />{{ humanize(stage) }}</label>
            }
          </div>
        }
        <p class="pl__group">Source</p>
        <div class="pl__checks">
          @for (source of sources; track source) {
            <label><input type="checkbox" [checked]="draft().sources.includes(source)" (change)="toggle('sources', source, $any($event.target).checked)" />{{ humanize(source) }}</label>
          }
        </div>
        <div class="rec__grid">
          <label class="field">
            <span class="field__label">Experience from (years)</span>
            <input type="number" min="0" [value]="draft().expMin" (input)="patchDraft({ expMin: $any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">to (years)</span>
            <input type="number" min="0" [value]="draft().expMax" (input)="patchDraft({ expMax: $any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">Notice period at most</span>
            <select [value]="draft().notice" (change)="patchDraft({ notice: $any($event.target).value })">
              <option value="">Any</option>
              @for (days of ['15', '30', '60', '90']; track days) {
                <option [value]="days">{{ days }} days</option>
              }
            </select>
          </label>
        </div>
        <div class="pl__drawer-actions">
          <button type="button" class="btn btn--link" (click)="draft.set(empty())">Reset</button>
          <button type="button" class="btn btn--primary" (click)="applyFilters()">Show results</button>
        </div>
      </div>
    </gggs-drawer>

    <gggs-candidate-form [(visible)]="formOpen" (saved)="added($event)" />
  `,
  styleUrls: ['./recruitment.css', './pipeline.css', './lists.css'],
})
export class CandidatePipelinePage {
  private readonly api = inject(RecruitmentApi);
  private readonly router = inject(Router);

  protected readonly canManage = inject(PermissionStore).has('recruitment.application.manage');
  protected readonly humanize = humanize;
  protected readonly sources = CANDIDATE_SOURCES;
  protected readonly boardStages: readonly ApplicationStage[] = [...PIPELINE_STAGES, 'Hired'];
  protected readonly empty = noCandidateFilters;
  protected readonly initials = initials;
  protected readonly avatar = avatarColor;
  protected readonly score = scoreClass;
  protected readonly due = dueState;
  protected readonly dueText = dueLabel;
  protected readonly color = (stage: string) => STAGE_COLORS[stage] ?? '#7c8796';
  protected readonly smartLists: readonly { key: SmartList; label: string }[] = [
    { key: 'all', label: 'In the pipeline' },
    { key: 'action', label: 'Needs action' },
    { key: 'week', label: 'Due this week' },
    { key: 'referred', label: 'Referred' },
    { key: 'pool', label: 'Talent pool' },
  ];

  protected readonly applications = signal<readonly JobApplicationItem[]>([]);
  protected readonly candidates = signal<readonly CandidateProfile[]>([]);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly smart = signal<SmartList>('all');
  protected readonly layout = signal<'board' | 'list'>('board');
  protected readonly search = signal('');
  protected readonly requisitionId = signal('');
  protected readonly filters = signal<CandidateFilters>(noCandidateFilters());
  protected readonly draft = signal<CandidateFilters>(noCandidateFilters());
  protected readonly filtersOpen = signal(false);
  protected readonly formOpen = signal(false);

  /** Applications still in play (and hires, shown in their own column). */
  private readonly live = computed(() => this.applications().filter((a) => this.boardStages.includes(a.stage)));

  protected readonly requirementOptions = computed(() => {
    const seen = new Map<string, string>();
    for (const a of this.live()) {
      seen.set(a.requisitionId, a.requisitionTitle);
    }
    return [...seen].map(([id, title]) => ({ id, title })).sort((a, b) => a.title.localeCompare(b.title));
  });

  protected readonly shown = computed(() => {
    const term = this.search().trim().toLowerCase();
    const f = this.filters();
    return this.live().filter(
      (a) =>
        this.inSmart(a, this.smart()) &&
        (!this.requisitionId() || a.requisitionId === this.requisitionId()) &&
        (!term || `${a.candidateName} ${a.candidateNumber} ${a.currentCompany ?? ''} ${a.currentTitle ?? ''} ${a.requisitionTitle}`.toLowerCase().includes(term)) &&
        (!f.stages.length || f.stages.includes(a.stage)) &&
        (!f.sources.length || f.sources.includes(a.source)) &&
        (f.expMin === '' || (a.experienceYears ?? 0) >= +f.expMin) &&
        (f.expMax === '' || (a.experienceYears ?? 0) <= +f.expMax) &&
        (f.notice === '' || (a.noticePeriodDays ?? 0) <= +f.notice),
    );
  });

  /** Profiles in no open pipeline: never applied, or every application closed. */
  protected readonly pool = computed(() => {
    const busy = new Set(this.live().filter((a) => a.stage !== 'Hired').map((a) => a.candidateId));
    const term = this.search().trim().toLowerCase();
    const f = this.filters();
    return this.candidates().filter(
      (c) =>
        !busy.has(c.id) &&
        !c.employeeId &&
        (!term || `${c.fullName} ${c.number} ${c.currentCompany ?? ''} ${c.currentTitle ?? ''} ${c.skills.join(' ')}`.toLowerCase().includes(term)) &&
        (!f.sources.length || f.sources.includes(c.source)) &&
        (f.expMin === '' || (c.experienceYears ?? 0) >= +f.expMin) &&
        (f.expMax === '' || (c.experienceYears ?? 0) <= +f.expMax) &&
        (f.notice === '' || (c.noticePeriodDays ?? 0) <= +f.notice),
    );
  });

  protected readonly chips = computed(() => {
    const f = this.filters();
    const set = (change: Partial<CandidateFilters>) => () => this.filters.update((current) => ({ ...current, ...change }));
    return [
      ...f.stages.map((stage) => ({ label: humanize(stage), clear: set({ stages: f.stages.filter((s) => s !== stage) }) })),
      ...f.sources.map((source) => ({ label: humanize(source), clear: set({ sources: f.sources.filter((s) => s !== source) }) })),
      ...(f.expMin !== '' || f.expMax !== '' ? [{ label: `${f.expMin || 0}–${f.expMax || 'any'} yrs`, clear: set({ expMin: '', expMax: '' }) }] : []),
      ...(f.notice !== '' ? [{ label: `Notice ≤ ${f.notice} days`, clear: set({ notice: '' }) }] : []),
    ];
  });

  constructor() {
    void this.load();
  }

  protected countOf(list: SmartList): number {
    return list === 'pool' ? this.pool().length : this.live().filter((a) => this.inSmart(a, list)).length;
  }

  protected inStage(stage: ApplicationStage): readonly JobApplicationItem[] {
    return this.shown()
      .filter((a) => a.stage === stage)
      .sort((a, b) => (a.nextStepDue ?? '9999').localeCompare(b.nextStepDue ?? '9999'));
  }

  protected openFilters(): void {
    this.draft.set(this.filters());
    this.filtersOpen.set(true);
  }

  protected patchDraft(change: Partial<CandidateFilters>): void {
    this.draft.update((current) => ({ ...current, ...change }));
  }

  protected toggle(key: 'stages' | 'sources', value: string, on: boolean): void {
    this.draft.update((current) => {
      const list = current[key] as readonly string[];
      return { ...current, [key]: on ? [...list, value] : list.filter((item) => item !== value) };
    });
  }

  protected applyFilters(): void {
    this.filters.set(this.draft());
    this.filtersOpen.set(false);
  }

  protected added(view: CandidateView): void {
    void this.router.navigate(['/recruitment/candidates', view.candidate.id]);
  }

  private inSmart(a: JobApplicationItem, list: SmartList): boolean {
    switch (list) {
      case 'action':
        return a.stage !== 'Hired' && dueState(a.nextStepDue) === 'late';
      case 'week': {
        const days = a.nextStepDue ? dayDiff(today(), a.nextStepDue) : -1;
        return a.stage !== 'Hired' && days >= 0 && days <= 7;
      }
      case 'referred':
        return a.source === 'Referral';
      case 'pool':
        return false;
      default:
        return true;
    }
  }

  private async load(): Promise<void> {
    try {
      const [applications, candidates] = await Promise.all([this.api.applications({}), this.api.candidates({})]);
      this.applications.set(applications);
      this.candidates.set(candidates);
      this.loaded.set(true);
    } catch (error) {
      this.error.set(errorText(error, 'Candidates could not be loaded.'));
    }
  }
}
