import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { PermissionStore } from '@gggs/core/permissions';
import { RecruitmentApi } from '@gggs/shared/hiring';
import type {
  AgencyInput,
  AgencyItem,
  ApplicationStage,
  CandidateProfile,
  CandidateSource,
  CandidateView,
  InterviewItem,
  JobApplicationItem,
  OfferItem,
  OfferStatus,
  PostingItem,
  RecruitmentAnalytics,
  RecruitmentRules,
  ReferralItem,
  RequisitionItem,
  RequisitionStatus,
} from '@gggs/shared/models';
import { CANDIDATE_SOURCES, PIPELINE_STAGES, humanize } from '@gggs/shared/models';
import { MoneyPipe } from '@gggs/shared/money';
import { Chart, EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';
import { CandidateForm, RequisitionForm, errorText, splitList } from './recruitment-forms';

const iso = (date: Date) => date.toISOString().slice(0, 10);

/** Requisitions (spec §12): the openings recruitment is working on. */
@Component({
  selector: 'gggs-requisitions-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, EmptyState, RequisitionForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Requisitions" subtitle="Openings being recruited for - from approved manpower requests or opened directly.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="formOpen.set(true)"><i class="pi pi-plus" aria-hidden="true"></i> Open a requisition</button>
      }
    </gggs-page-header>
    <section class="panel toolbar rec__filters">
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">Any</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ humanize(option) }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Search</span>
        <input type="search" [value]="search()" (change)="search.set($any($event.target).value); load()" placeholder="Title or number" />
      </label>
    </section>
    <section class="panel stack">
      @for (requisition of requisitions(); track requisition.id) {
        <a class="rec__row rec__row--link" [routerLink]="['/recruitment/requisitions', requisition.id]">
          <span><strong>{{ requisition.title }}</strong> <span class="muted">{{ requisition.number }}</span></span>
          <span>{{ requisition.departmentName ?? '—' }}{{ requisition.locationName ? ' · ' + requisition.locationName : '' }}</span>
          <span>{{ requisition.hired }} / {{ requisition.openings }} filled · {{ requisition.applications }} applied</span>
          <span class="muted">{{ requisition.recruiterName ?? 'no recruiter' }} · opened {{ requisition.openedOn | date: 'd MMM' }}</span>
          <span><gggs-status-badge [status]="requisition.status" /></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-file-edit" title="No requisitions here" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
    <gggs-requisition-form [(visible)]="formOpen" (saved)="opened($event)" />
  `,
  styleUrl: './recruitment.css',
})
export class RequisitionsPage {
  private readonly api = inject(RecruitmentApi);
  private readonly router = inject(Router);

  protected readonly canManage = inject(PermissionStore).has('recruitment.requisition.manage');
  protected readonly humanize = humanize;
  protected readonly statuses: readonly RequisitionStatus[] = ['Open', 'OnHold', 'Filled', 'Closed', 'Cancelled'];
  protected readonly requisitions = signal<readonly RequisitionItem[]>([]);
  protected readonly status = signal<RequisitionStatus | ''>('Open');
  protected readonly search = signal('');
  protected readonly formOpen = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected opened(requisition: RequisitionItem): void {
    void this.router.navigate(['/recruitment/requisitions', requisition.id]);
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.requisitions.set(await this.api.requisitions({ status: this.status() || null, search: this.search() || null }));
    } catch (error) {
      this.error.set(errorText(error, 'Requisitions could not be loaded.'));
    }
  }
}

/** A requisition and its pipeline board: candidates by stage, best match first. */
@Component({
  selector: 'gggs-requisition-page',
  imports: [DatePipe, RouterLink, MoneyPipe, PageHeader, StatusBadge, KpiCard, EmptyState, Skeleton, Tabs, FormDialog, RequisitionForm, CandidateForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (requisition(); as r) {
      <gggs-page-header [title]="r.title" [subtitle]="r.number + ' · ' + (r.departmentName ?? '') + (r.hiringManagerName ? ' · hiring manager ' + r.hiringManagerName : '')">
        <gggs-status-badge [status]="r.status" />
        @if (canApply && r.status === 'Open') {
          <button type="button" class="btn btn--primary" (click)="candidateOpen.set(true)"><i class="pi pi-user-plus" aria-hidden="true"></i> Add candidate</button>
        }
        @if (canManage) {
          @if (r.status === 'Open' || r.status === 'OnHold') {
            <button type="button" class="btn" (click)="editOpen.set(true)">Edit</button>
            <button type="button" class="btn" [disabled]="busy()" (click)="act(r.status === 'Open' ? 'hold' : 'resume')">{{ r.status === 'Open' ? 'Put on hold' : 'Resume' }}</button>
            <button type="button" class="btn btn--link" [disabled]="busy()" (click)="act('close')">Close</button>
          }
        }
      </gggs-page-header>
      <p><a routerLink="/recruitment/requisitions">← All requisitions</a></p>
      <div class="rec__kpis">
        <gggs-kpi-card label="Openings" [value]="r.openings" [hint]="r.remaining + ' remaining'" />
        <gggs-kpi-card label="Applications" [value]="r.applications" />
        <gggs-kpi-card label="CTC range" [value]="(r.salaryMin | gggsMoney) + ' – ' + (r.salaryMax | gggsMoney)" />
        <gggs-kpi-card label="Target" [value]="r.targetDate ? (r.targetDate | date: 'd MMM y') : '—'" [hint]="r.priority" />
      </div>
      <gggs-tabs [tabs]="tabs" [(selected)]="tab" />
      @switch (tab()) {
        @case ('pipeline') {
          <section class="rec__board">
            @for (stage of stages; track stage) {
              <div class="rec__column">
                <h3 class="rec__column-title">{{ humanize(stage) }} <span class="muted">{{ inStage(stage).length }}</span></h3>
                @for (application of inStage(stage); track application.id) {
                  <a class="rec__card" [routerLink]="['/recruitment/applications', application.id]">
                    <strong>{{ application.candidateName }}</strong>
                    <span class="muted">{{ application.currentTitle ?? '' }}{{ application.experienceYears !== undefined ? ' · ' + application.experienceYears + ' yrs' : '' }}</span>
                    <span class="rec__card-foot">
                      <span class="rec__match" [class.rec__match--high]="application.matchScore >= 70">{{ application.matchScore }}% match</span>
                      @if (application.averageRating) {
                        <span><i class="pi pi-star" aria-hidden="true"></i> {{ application.averageRating }}</span>
                      }
                      <span class="muted">{{ humanize(application.source) }}</span>
                    </span>
                  </a>
                } @empty {
                  <p class="muted rec__none">None</p>
                }
              </div>
            }
          </section>
          @if (closed().length) {
            <section class="panel stack">
              <h2 class="rec__heading">Closed</h2>
              @for (application of closed(); track application.id) {
                <a class="rec__row rec__row--link" [routerLink]="['/recruitment/applications', application.id]">
                  <span><strong>{{ application.candidateName }}</strong></span>
                  <span>{{ application.matchScore }}% match</span>
                  <span class="muted">{{ application.rejectionReason ?? '' }}</span>
                  <span class="muted">{{ application.stageChangedDate | date: 'd MMM y' }}</span>
                  <span><gggs-status-badge [status]="application.stage" /></span>
                </a>
              }
            </section>
          }
        }
        @case ('postings') {
          <section class="panel stack">
            @if (canPost && r.status === 'Open') {
              <div class="toolbar"><button type="button" class="btn btn--sm" (click)="newPosting(r)"><i class="pi pi-plus" aria-hidden="true"></i> New posting</button></div>
            }
            @for (posting of postings(); track posting.id) {
              <div class="rec__row">
                <span><strong>{{ posting.title }}</strong></span>
                <span>{{ posting.internal ? 'Internal' : '' }}{{ posting.internal && posting.external ? ' + ' : '' }}{{ posting.external ? 'Careers page' : '' }}</span>
                <span class="muted">{{ posting.publishedOn ? 'since ' + (posting.publishedOn | date: 'd MMM') : '' }}{{ posting.expiresOn ? ' · until ' + (posting.expiresOn | date: 'd MMM') : '' }}</span>
                <span class="toolbar">
                  @if (canPost) {
                    <button type="button" class="btn btn--link btn--sm" (click)="editPosting(posting)">Edit</button>
                    @if (posting.status !== 'Published') {
                      <button type="button" class="btn btn--link btn--sm" (click)="postingAction(posting, 'publish')">Publish</button>
                    } @else {
                      <button type="button" class="btn btn--link btn--sm" (click)="postingAction(posting, 'close')">Close</button>
                    }
                  }
                </span>
                <span><gggs-status-badge [status]="posting.status" /></span>
              </div>
            } @empty {
              <gggs-empty-state icon="pi pi-megaphone" title="Not posted yet" description="Post it on the internal job board so employees can apply and refer." />
            }
          </section>
        }
        @case ('details') {
          <section class="panel rec__facts">
            <div><span class="field__label">Employment</span><p>{{ humanize(r.employmentTypeCode) }} · {{ humanize(r.workModeCode) }}</p></div>
            <div><span class="field__label">Experience</span><p>{{ r.minExperienceYears ?? 0 }}–{{ r.maxExperienceYears ?? 'any' }} years</p></div>
            <div><span class="field__label">Skills</span><p>{{ r.skills.join(', ') || '—' }}</p></div>
            <div><span class="field__label">Recruiter</span><p>{{ r.recruiterName ?? '—' }}</p></div>
            <div><span class="field__label">Job · location</span><p>{{ r.jobName ?? '—' }} · {{ r.locationName ?? '—' }}</p></div>
            <div>
              <span class="field__label">From</span>
              <p>
                @if (r.manpowerRequestId) {
                  <a [routerLink]="['/workforce/requests', r.manpowerRequestId]">Manpower request</a>
                } @else {
                  Opened directly
                }
              </p>
            </div>
          </section>
          @if (r.description) {
            <section class="panel"><p class="rec__pre">{{ r.description }}</p></section>
          }
        }
      }
      <gggs-requisition-form [(visible)]="editOpen" [requisition]="r" (saved)="requisition.set($event)" />
      <gggs-candidate-form [(visible)]="candidateOpen" [requisitionId]="r.id" (saved)="loadPipeline()" />
      <gggs-form-dialog [(visible)]="postingOpen" [title]="postingId() ? 'Edit posting' : 'New posting'" [busy]="busy()" [error]="formError()" [saveDisabled]="!postingTitle().trim() || !postingText().trim() || (!postingInternal() && !postingExternal())" (save)="savePosting(r)">
        <label class="field">
          <span class="field__label">Title</span>
          <input type="text" maxlength="200" [value]="postingTitle()" (input)="postingTitle.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Description</span>
          <textarea rows="6" maxlength="8000" [value]="postingText()" (input)="postingText.set($any($event.target).value)"></textarea>
        </label>
        <div class="rec__grid">
          <label class="rec__flag"><input type="checkbox" [checked]="postingInternal()" (change)="postingInternal.set($any($event.target).checked)" /> <span>Internal job board</span></label>
          <label class="rec__flag"><input type="checkbox" [checked]="postingExternal()" (change)="postingExternal.set($any($event.target).checked)" /> <span>Careers page</span></label>
          <label class="field">
            <span class="field__label">Expires</span>
            <input type="date" [value]="postingExpires()" (change)="postingExpires.set($any($event.target).value)" />
          </label>
        </div>
      </gggs-form-dialog>
    } @else if (!error()) {
      <gggs-skeleton [lines]="10" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styleUrl: './recruitment.css',
})
export class RequisitionPage {
  private readonly api = inject(RecruitmentApi);
  private readonly permissions = inject(PermissionStore);

  readonly id = input.required<string>();

  protected readonly canManage = this.permissions.has('recruitment.requisition.manage');
  protected readonly canApply = this.permissions.has('recruitment.application.manage');
  protected readonly canPost = this.permissions.has('recruitment.posting.manage');
  protected readonly humanize = humanize;
  protected readonly stages = PIPELINE_STAGES;
  protected readonly tabs: readonly TabItem[] = [
    { key: 'pipeline', label: 'Pipeline' },
    { key: 'postings', label: 'Postings' },
    { key: 'details', label: 'Details' },
  ];
  protected readonly tab = signal('pipeline');
  protected readonly requisition = signal<RequisitionItem | null>(null);
  protected readonly applications = signal<readonly JobApplicationItem[]>([]);
  protected readonly postings = signal<readonly PostingItem[]>([]);
  protected readonly closed = computed(() => this.applications().filter((application) => !PIPELINE_STAGES.includes(application.stage)));
  protected readonly editOpen = signal(false);
  protected readonly candidateOpen = signal(false);
  protected readonly postingOpen = signal(false);
  protected readonly postingId = signal<string | null>(null);
  protected readonly postingTitle = signal('');
  protected readonly postingText = signal('');
  protected readonly postingInternal = signal(true);
  protected readonly postingExternal = signal(false);
  protected readonly postingExpires = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    effect(() => {
      const id = this.id();
      this.api.requisition(id).then(
        (requisition) => this.requisition.set(requisition),
        (error: unknown) => this.error.set(errorText(error, 'The requisition could not be loaded.')),
      );
      void this.loadPipeline();
      this.api.postings(id).then(
        (postings) => this.postings.set(postings),
        () => undefined,
      );
    });
  }

  protected inStage(stage: ApplicationStage): readonly JobApplicationItem[] {
    return this.applications().filter((application) => application.stage === stage);
  }

  protected async loadPipeline(): Promise<void> {
    try {
      this.applications.set(await this.api.applications({ requisitionId: this.id() }));
    } catch (error) {
      this.error.set(errorText(error, 'The pipeline could not be loaded.'));
    }
  }

  protected async act(action: 'hold' | 'resume' | 'close'): Promise<void> {
    this.busy.set(true);
    try {
      this.requisition.set(await this.api.requisitionAction(this.id(), action));
    } catch (error) {
      this.error.set(errorText(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected newPosting(requisition: RequisitionItem): void {
    this.postingId.set(null);
    this.postingTitle.set(requisition.title);
    this.postingText.set(requisition.description ?? '');
    this.postingInternal.set(true);
    this.postingExternal.set(false);
    this.postingExpires.set('');
    this.formError.set(null);
    this.postingOpen.set(true);
  }

  protected editPosting(posting: PostingItem): void {
    this.postingId.set(posting.id);
    this.postingTitle.set(posting.title);
    this.postingText.set(posting.description);
    this.postingInternal.set(posting.internal);
    this.postingExternal.set(posting.external);
    this.postingExpires.set(posting.expiresOn ?? '');
    this.formError.set(null);
    this.postingOpen.set(true);
  }

  protected async savePosting(requisition: RequisitionItem): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.savePosting(this.postingId(), {
        requisitionId: requisition.id,
        title: this.postingTitle(),
        description: this.postingText(),
        internal: this.postingInternal(),
        external: this.postingExternal(),
        expiresOn: this.postingExpires() || null,
      });
      this.postingOpen.set(false);
      this.postings.set(await this.api.postings(requisition.id));
    } catch (error) {
      this.formError.set(errorText(error, 'The posting could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async postingAction(posting: PostingItem, action: 'publish' | 'close'): Promise<void> {
    try {
      await this.api.postingAction(posting.id, action);
      this.postings.set(await this.api.postings(this.id()));
    } catch (error) {
      this.error.set(errorText(error, 'The posting could not be changed.'));
    }
  }
}

/** Every posting across requisitions. */
@Component({
  selector: 'gggs-postings-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Job postings" subtitle="Where each opening is advertised. Postings are created from their requisition." />
    <section class="panel stack">
      @for (posting of postings(); track posting.id) {
        <a class="rec__row rec__row--link" [routerLink]="['/recruitment/requisitions', posting.requisitionId]">
          <span><strong>{{ posting.title }}</strong> <span class="muted">{{ posting.requisitionNumber }}</span></span>
          <span>{{ posting.departmentName ?? '' }}{{ posting.locationName ? ' · ' + posting.locationName : '' }}</span>
          <span>{{ posting.internal ? 'Internal' : '' }}{{ posting.internal && posting.external ? ' + ' : '' }}{{ posting.external ? 'Careers page' : '' }}</span>
          <span class="muted">{{ posting.publishedOn ? (posting.publishedOn | date: 'd MMM y') : 'not published' }}</span>
          <span><gggs-status-badge [status]="posting.status" /></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-megaphone" title="No postings yet" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
  `,
  styleUrl: './recruitment.css',
})
export class PostingsPage {
  protected readonly postings = signal<readonly PostingItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    inject(RecruitmentApi)
      .postings()
      .then(
        (postings) => this.postings.set(postings),
        (error: unknown) => this.error.set(errorText(error, 'Postings could not be loaded.')),
      );
  }
}

/** The candidate database: one profile per person, searchable by name, email, mobile, company or skill. */
@Component({
  selector: 'gggs-candidates-page',
  imports: [DatePipe, RouterLink, PageHeader, EmptyState, CandidateForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Candidates" subtitle="Everyone who has applied, been sourced or been referred.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="formOpen.set(true)"><i class="pi pi-user-plus" aria-hidden="true"></i> Add candidate</button>
      }
    </gggs-page-header>
    <section class="panel toolbar rec__filters">
      <label class="field">
        <span class="field__label">Search</span>
        <input type="search" [value]="search()" (change)="search.set($any($event.target).value); load()" placeholder="Name, email, mobile, company" />
      </label>
      <label class="field">
        <span class="field__label">Skill</span>
        <input type="search" [value]="skill()" (change)="skill.set($any($event.target).value); load()" />
      </label>
      <label class="field">
        <span class="field__label">Source</span>
        <select [value]="source()" (change)="source.set($any($event.target).value); load()">
          <option value="">Any</option>
          @for (option of sources; track option) {
            <option [value]="option">{{ humanize(option) }}</option>
          }
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (candidate of candidates(); track candidate.id) {
        <a class="rec__row rec__row--link" [routerLink]="['/recruitment/candidates', candidate.id]">
          <span><strong>{{ candidate.fullName }}</strong> <span class="muted">{{ candidate.number }}</span></span>
          <span>{{ candidate.currentTitle ?? '' }}{{ candidate.currentCompany ? ' at ' + candidate.currentCompany : '' }}</span>
          <span class="muted">{{ candidate.skills.slice(0, 4).join(', ') }}</span>
          <span class="muted">{{ humanize(candidate.source) }}{{ candidate.agencyName ? ' · ' + candidate.agencyName : '' }}{{ candidate.referrerName ? ' · ' + candidate.referrerName : '' }}</span>
          <span>{{ candidate.employeeId ? 'Hired' : candidate.applications + ' application(s)' }} <span class="muted">{{ candidate.createdDate | date: 'd MMM' }}</span></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-id-card" title="No candidates found" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
    <gggs-candidate-form [(visible)]="formOpen" (saved)="added($event)" />
  `,
  styleUrl: './recruitment.css',
})
export class CandidatesPage {
  private readonly api = inject(RecruitmentApi);
  private readonly router = inject(Router);

  protected readonly canManage = inject(PermissionStore).has('recruitment.application.manage');
  protected readonly humanize = humanize;
  protected readonly sources = CANDIDATE_SOURCES;
  protected readonly candidates = signal<readonly CandidateProfile[]>([]);
  protected readonly search = signal('');
  protected readonly skill = signal('');
  protected readonly source = signal<CandidateSource | ''>('');
  protected readonly formOpen = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected added(view: CandidateView): void {
    void this.router.navigate(['/recruitment/candidates', view.candidate.id]);
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.candidates.set(await this.api.candidates({ search: this.search() || null, skill: this.skill() || null, source: this.source() || null }));
    } catch (error) {
      this.error.set(errorText(error, 'Candidates could not be loaded.'));
    }
  }
}

/** One candidate: profile, applications, background checks; apply them to another opening. */
@Component({
  selector: 'gggs-candidate-page',
  imports: [DatePipe, RouterLink, MoneyPipe, PageHeader, StatusBadge, EmptyState, Skeleton, FormDialog, CandidateForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="v.candidate.fullName" [subtitle]="v.candidate.number + ' · ' + v.candidate.email + (v.candidate.mobile ? ' · ' + v.candidate.mobile : '')">
        @if (v.canManage) {
          <button type="button" class="btn" (click)="editOpen.set(true)">Edit</button>
          @if (!v.candidate.employeeId) {
            <button type="button" class="btn btn--primary" (click)="openApply()">Apply to an opening</button>
          }
        }
      </gggs-page-header>
      <p><a routerLink="/recruitment/candidates">← All candidates</a></p>
      <section class="panel rec__facts">
        <div><span class="field__label">Now</span><p>{{ v.candidate.currentTitle ?? '—' }}{{ v.candidate.currentCompany ? ' at ' + v.candidate.currentCompany : '' }}</p></div>
        <div><span class="field__label">Experience · notice</span><p>{{ v.candidate.experienceYears ?? '—' }} years · {{ v.candidate.noticePeriodDays ?? '—' }} days</p></div>
        <div><span class="field__label">CTC now → expected</span><p>{{ v.candidate.currentCtc | gggsMoney }} → {{ v.candidate.expectedCtc | gggsMoney }}</p></div>
        <div><span class="field__label">Location</span><p>{{ v.candidate.location ?? '—' }}</p></div>
        <div><span class="field__label">Source</span><p>{{ humanize(v.candidate.source) }}{{ v.candidate.agencyName ? ' · ' + v.candidate.agencyName : '' }}{{ v.candidate.referrerName ? ' · referred by ' + v.candidate.referrerName : '' }}</p></div>
        <div><span class="field__label">Skills</span><p>{{ v.candidate.skills.join(', ') || '—' }}</p></div>
        @if (v.candidate.linkedInUrl) {
          <div><span class="field__label">LinkedIn</span><p><a [href]="v.candidate.linkedInUrl" target="_blank" rel="noopener">Profile</a></p></div>
        }
        @if (v.candidate.employeeId) {
          <div><span class="field__label">Employee</span><p><a [routerLink]="['/employees', v.candidate.employeeId]">View employee record</a></p></div>
        }
      </section>
      @if (v.candidate.notes) {
        <section class="panel"><p class="rec__pre">{{ v.candidate.notes }}</p></section>
      }
      <section class="panel stack">
        <h2 class="rec__heading">Applications</h2>
        @for (application of v.applications; track application.id) {
          <a class="rec__row rec__row--link" [routerLink]="['/recruitment/applications', application.id]">
            <span><strong>{{ application.requisitionTitle }}</strong> <span class="muted">{{ application.requisitionNumber }}</span></span>
            <span>{{ application.matchScore }}% match</span>
            <span class="muted">applied {{ application.appliedDate | date: 'd MMM y' }}</span>
            <span>{{ application.averageRating ? application.averageRating + ' ★' : '' }}</span>
            <span><gggs-status-badge [status]="application.stage" /></span>
          </a>
        } @empty {
          <gggs-empty-state icon="pi pi-inbox" title="No applications" />
        }
      </section>
      @if (v.backgroundChecks.length) {
        <section class="panel stack">
          <h2 class="rec__heading">Background verification</h2>
          @for (check of v.backgroundChecks; track check.id) {
            <div class="rec__row">
              <span>{{ check.checks.join(', ') }}</span>
              <span class="muted">{{ check.vendor ?? '' }}</span>
              <span class="muted">{{ check.initiatedOn | date: 'd MMM y' }}{{ check.completedOn ? ' – ' + (check.completedOn | date: 'd MMM y') : '' }}</span>
              <span class="muted">{{ check.remarks ?? '' }}</span>
              <span><gggs-status-badge [status]="check.status" /></span>
            </div>
          }
        </section>
      }
      <gggs-candidate-form [(visible)]="editOpen" [candidate]="v.candidate" (saved)="view.set($event)" />
      <gggs-form-dialog [(visible)]="applyOpen" title="Apply to an opening" saveLabel="Apply" [busy]="busy()" [error]="formError()" [saveDisabled]="!requisitionId()" (save)="apply(v)">
        <label class="field">
          <span class="field__label">Requisition</span>
          <select [value]="requisitionId()" (change)="requisitionId.set($any($event.target).value)">
            <option value="">Choose</option>
            @for (requisition of open(); track requisition.id) {
              <option [value]="requisition.id">{{ requisition.number }} · {{ requisition.title }}</option>
            }
          </select>
        </label>
      </gggs-form-dialog>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styleUrl: './recruitment.css',
})
export class CandidatePage {
  private readonly api = inject(RecruitmentApi);
  private readonly router = inject(Router);

  readonly id = input.required<string>();

  protected readonly humanize = humanize;
  protected readonly view = signal<CandidateView | null>(null);
  protected readonly open = signal<readonly RequisitionItem[]>([]);
  protected readonly editOpen = signal(false);
  protected readonly applyOpen = signal(false);
  protected readonly requisitionId = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.candidate(this.id()).then(
        (view) => this.view.set(view),
        (error: unknown) => this.error.set(errorText(error, 'The candidate could not be loaded.')),
      );
    });
  }

  protected async openApply(): Promise<void> {
    this.requisitionId.set('');
    this.formError.set(null);
    this.applyOpen.set(true);
    try {
      this.open.set(await this.api.requisitions({ status: 'Open' }));
    } catch {
      this.open.set([]);
    }
  }

  protected async apply(view: CandidateView): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      const application = await this.api.apply(view.candidate.id, this.requisitionId());
      this.applyOpen.set(false);
      void this.router.navigate(['/recruitment/applications', application.application.id]);
    } catch (error) {
      this.formError.set(errorText(error, 'The application could not be made.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Interviews over the coming weeks (and the past week), for the recruiting team. */
@Component({
  selector: 'gggs-interviews-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Interviews" subtitle="Scheduled interviews and whether the panel has given feedback." />
    <section class="panel toolbar rec__filters">
      <label class="field">
        <span class="field__label">From</span>
        <input type="date" [value]="from()" (change)="from.set($any($event.target).value); load()" />
      </label>
      <label class="field">
        <span class="field__label">To</span>
        <input type="date" [value]="to()" (change)="to.set($any($event.target).value); load()" />
      </label>
    </section>
    <section class="panel stack">
      @for (interview of interviews(); track interview.id) {
        <a class="rec__row rec__row--link" [routerLink]="['/recruitment/applications', interview.applicationId]">
          <span><strong>{{ interview.scheduledAt | date: 'EEE d MMM, HH:mm' }}</strong> <span class="muted">{{ interview.durationMinutes }} min</span></span>
          <span>{{ interview.candidateName }} · {{ interview.round }}</span>
          <span class="muted">{{ interview.requisitionTitle }}</span>
          <span class="muted">{{ interview.panelNames.join(', ') }} · {{ interview.scorecards }}/{{ interview.panel.length }} feedback</span>
          <span><gggs-status-badge [status]="interview.status" /></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-comments" title="No interviews in this range" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
  `,
  styleUrl: './recruitment.css',
})
export class InterviewsPage {
  private readonly api = inject(RecruitmentApi);

  protected readonly interviews = signal<readonly InterviewItem[]>([]);
  protected readonly from = signal(iso(new Date(Date.now() - 7 * 86_400_000)));
  protected readonly to = signal(iso(new Date(Date.now() + 30 * 86_400_000)));
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.interviews.set(await this.api.interviews(this.from(), this.to()));
    } catch (error) {
      this.error.set(errorText(error, 'Interviews could not be loaded.'));
    }
  }
}

/** Offers across requisitions. */
@Component({
  selector: 'gggs-offers-page',
  imports: [DatePipe, RouterLink, MoneyPipe, PageHeader, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Offers" subtitle="Offers in approval, out with candidates, accepted and declined." />
    <section class="panel toolbar rec__filters">
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">Any</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ humanize(option) }}</option>
          }
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (offer of offers(); track offer.id) {
        <a class="rec__row rec__row--link" [routerLink]="['/recruitment/applications', offer.applicationId]">
          <span><strong>{{ offer.candidateName }}</strong> <span class="muted">{{ offer.number }}</span></span>
          <span>{{ offer.designation }} · {{ offer.requisitionNumber }}</span>
          <span>{{ offer.annualCtc | gggsMoney: offer.currency }}{{ offer.withinBudget ? '' : ' (over budget)' }}</span>
          <span class="muted">joining {{ offer.joiningDate | date: 'd MMM y' }}</span>
          <span><gggs-status-badge [status]="offer.status" /></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-envelope" title="No offers here" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
  `,
  styleUrl: './recruitment.css',
})
export class OffersPage {
  private readonly api = inject(RecruitmentApi);

  protected readonly humanize = humanize;
  protected readonly statuses: readonly OfferStatus[] = ['Draft', 'PendingApproval', 'Approved', 'Sent', 'Accepted', 'Declined', 'Rejected', 'Withdrawn'];
  protected readonly offers = signal<readonly OfferItem[]>([]);
  protected readonly status = signal<OfferStatus | ''>('');
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.offers.set(await this.api.offers(this.status() || null));
    } catch (error) {
      this.error.set(errorText(error, 'Offers could not be loaded.'));
    }
  }
}

/** An offer on its own - where an approver lands from their workflow task. */
@Component({
  selector: 'gggs-offer-page',
  imports: [DatePipe, RouterLink, MoneyPipe, PageHeader, StatusBadge, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (offer(); as o) {
      <gggs-page-header [title]="o.candidateName + ' · ' + o.designation" [subtitle]="o.number + ' · ' + o.requisitionNumber + ' ' + o.requisitionTitle">
        <gggs-status-badge [status]="o.status" />
        @if (canView) {
          <a class="btn" [routerLink]="['/recruitment/applications', o.applicationId]">Open application</a>
        }
      </gggs-page-header>
      <section class="panel rec__facts">
        <div><span class="field__label">Annual CTC</span><p>{{ o.annualCtc | gggsMoney: o.currency }}</p></div>
        <div><span class="field__label">Within the requisition's range</span><p>{{ o.withinBudget ? 'Yes' : 'No - above the maximum' }}</p></div>
        <div><span class="field__label">Joining</span><p>{{ o.joiningDate | date: 'd MMM y' }}</p></div>
        <div><span class="field__label">Valid until</span><p>{{ o.expiresOn ? (o.expiresOn | date: 'd MMM y') : '—' }}</p></div>
      </section>
      @if (o.notes) {
        <section class="panel"><p class="rec__pre">{{ o.notes }}</p></section>
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styleUrl: './recruitment.css',
})
export class OfferPage {
  private readonly api = inject(RecruitmentApi);

  readonly id = input.required<string>();

  protected readonly canView = inject(PermissionStore).has('recruitment.application.view');
  protected readonly offer = signal<OfferItem | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.offer(this.id()).then(
        (offer) => this.offer.set(offer),
        (error: unknown) => this.error.set(errorText(error, 'The offer could not be loaded.')),
      );
    });
  }
}

const blankAgency = (): AgencyInput => ({ name: '', contactName: null, email: null, phone: null, feePercent: 8.33, isActive: true });

/** Recruitment settings: the referral bonus, offer validity, scorecard criteria, agencies and referrals. */
@Component({
  selector: 'gggs-recruitment-settings-page',
  imports: [DatePipe, MoneyPipe, PageHeader, StatusBadge, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Recruitment settings" subtitle="Referral bonus, offer validity, interview criteria and the agencies you work with." />
    @if (rules(); as r) {
      <section class="panel stack">
        <h2 class="rec__heading">Rules</h2>
        <div class="rec__grid">
          <label class="field">
            <span class="field__label">Referral bonus</span>
            <input type="number" min="0" [value]="r.referralBonus" [disabled]="!canManage" (input)="patch({ referralBonus: +$any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">Paid after (months in the job)</span>
            <input type="number" min="0" max="24" [value]="r.referralBonusAfterMonths" [disabled]="!canManage" (input)="patch({ referralBonusAfterMonths: +$any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">Offers valid for (days)</span>
            <input type="number" min="1" max="90" [value]="r.offerValidDays" [disabled]="!canManage" (input)="patch({ offerValidDays: +$any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">Currency</span>
            <input type="text" maxlength="3" [value]="r.currency" [disabled]="!canManage" (input)="patch({ currency: $any($event.target).value.toUpperCase() })" />
          </label>
          <label class="field rec__wide">
            <span class="field__label">Scorecard criteria (comma-separated)</span>
            <input type="text" [value]="r.scorecardCriteria.join(', ')" [disabled]="!canManage" (change)="patch({ scorecardCriteria: split($any($event.target).value) })" />
          </label>
        </div>
        @if (canManage) {
          <div class="toolbar">
            <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="saveRules(r)">Save rules</button>
            @if (notice()) {
              <span class="muted" role="status">{{ notice() }}</span>
            }
          </div>
        }
      </section>
    }
    <section class="panel stack">
      <div class="toolbar">
        <h2 class="rec__heading">Agencies</h2>
        <span class="rec__spacer"></span>
        @if (canManage) {
          <button type="button" class="btn btn--sm" (click)="editAgency(null)"><i class="pi pi-plus" aria-hidden="true"></i> Add agency</button>
        }
      </div>
      @for (agency of agencies(); track agency.id) {
        <div class="rec__row">
          <span><strong>{{ agency.name }}</strong></span>
          <span class="muted">{{ agency.contactName ?? '' }} {{ agency.email ?? '' }} {{ agency.phone ?? '' }}</span>
          <span>{{ agency.feePercent }}% of CTC</span>
          <span>{{ agency.candidates }} candidates · {{ agency.hires }} hired</span>
          <span class="toolbar">
            <gggs-status-badge [status]="agency.isActive ? 'Active' : 'Inactive'" />
            @if (canManage) {
              <button type="button" class="btn btn--link btn--sm" (click)="editAgency(agency)">Edit</button>
            }
          </span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-building" title="No agencies" />
      }
    </section>
    <section class="panel stack">
      <h2 class="rec__heading">Referrals</h2>
      @for (referral of referrals(); track referral.id) {
        <div class="rec__row">
          <span><strong>{{ referral.candidateName }}</strong> <span class="muted">by {{ referral.referrerName ?? '—' }}</span></span>
          <span>{{ referral.requisitionTitle ?? 'General referral' }}</span>
          <span class="muted">{{ referral.createdDate | date: 'd MMM y' }}{{ referral.stage ? ' · ' + referral.stage : '' }}</span>
          <span>{{ referral.bonusAmount ? (referral.bonusAmount | gggsMoney) + (referral.bonusDueOn ? ' due ' + (referral.bonusDueOn | date: 'd MMM y') : '') : '' }}</span>
          <span><gggs-status-badge [status]="referral.status" /></span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-share-alt" title="No referrals yet" />
      }
    </section>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="agencyOpen" [title]="agencyId() ? 'Edit agency' : 'Add agency'" [busy]="busy()" [error]="formError()" [saveDisabled]="!agency().name.trim()" (save)="saveAgency()">
      <div class="rec__grid">
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="200" [value]="agency().name" (input)="patchAgency({ name: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Fee (% of CTC)</span>
          <input type="number" min="0" max="50" step="0.01" [value]="agency().feePercent" (input)="patchAgency({ feePercent: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Contact</span>
          <input type="text" maxlength="200" [value]="agency().contactName ?? ''" (input)="patchAgency({ contactName: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Email</span>
          <input type="email" maxlength="256" [value]="agency().email ?? ''" (input)="patchAgency({ email: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Phone</span>
          <input type="tel" maxlength="30" [value]="agency().phone ?? ''" (input)="patchAgency({ phone: $any($event.target).value || null })" />
        </label>
        <label class="rec__flag"><input type="checkbox" [checked]="agency().isActive" (change)="patchAgency({ isActive: $any($event.target).checked })" /> <span>Active</span></label>
      </div>
    </gggs-form-dialog>
  `,
  styleUrl: './recruitment.css',
})
export class RecruitmentSettingsPage {
  private readonly api = inject(RecruitmentApi);

  protected readonly canManage = inject(PermissionStore).has('recruitment.settings.manage');
  protected readonly split = splitList;
  protected readonly rules = signal<RecruitmentRules | null>(null);
  protected readonly agencies = signal<readonly AgencyItem[]>([]);
  protected readonly referrals = signal<readonly ReferralItem[]>([]);
  protected readonly agencyOpen = signal(false);
  protected readonly agencyId = signal<string | null>(null);
  protected readonly agency = signal<AgencyInput>(blankAgency());
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    Promise.all([this.api.rules(), this.api.agencies(), this.api.referrals()]).then(
      ([rules, agencies, referrals]) => {
        this.rules.set(rules);
        this.agencies.set(agencies);
        this.referrals.set(referrals);
      },
      (error: unknown) => this.error.set(errorText(error, 'Settings could not be loaded.')),
    );
  }

  protected patch(change: Partial<RecruitmentRules>): void {
    this.rules.update((rules) => (rules ? { ...rules, ...change } : rules));
  }

  protected async saveRules(rules: RecruitmentRules): Promise<void> {
    this.busy.set(true);
    this.notice.set(null);
    this.error.set(null);
    try {
      this.rules.set(await this.api.saveRules(rules));
      this.notice.set('Saved.');
    } catch (error) {
      this.error.set(errorText(error, 'The rules could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected editAgency(agency: AgencyItem | null): void {
    this.agencyId.set(agency?.id ?? null);
    this.agency.set(agency ? { name: agency.name, contactName: agency.contactName ?? null, email: agency.email ?? null, phone: agency.phone ?? null, feePercent: agency.feePercent, isActive: agency.isActive } : blankAgency());
    this.formError.set(null);
    this.agencyOpen.set(true);
  }

  protected patchAgency(change: Partial<AgencyInput>): void {
    this.agency.update((agency) => ({ ...agency, ...change }));
  }

  protected async saveAgency(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      this.agencies.set(await this.api.saveAgency(this.agencyId(), this.agency()));
      this.agencyOpen.set(false);
    } catch (error) {
      this.formError.set(errorText(error, 'The agency could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Hiring analytics (spec §12): time to hire, cost per hire, offer acceptance, sources, recruiters and the pipeline. */
@Component({
  selector: 'gggs-recruitment-analytics-page',
  imports: [DecimalPipe, MoneyPipe, PageHeader, KpiCard, Chart, Skeleton, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Recruitment analytics" subtitle="How fast, how costly and how well hiring is going." />
    <section class="panel toolbar rec__filters">
      <label class="field">
        <span class="field__label">From</span>
        <input type="date" [value]="from()" (change)="from.set($any($event.target).value); load()" />
      </label>
      <label class="field">
        <span class="field__label">To</span>
        <input type="date" [value]="to()" (change)="to.set($any($event.target).value); load()" />
      </label>
    </section>
    @if (data(); as d) {
      <div class="rec__kpis">
        <gggs-kpi-card label="Open requisitions" [value]="d.openRequisitions" [hint]="d.openPositions + ' positions to fill'" />
        <gggs-kpi-card label="Applications" [value]="d.applications" />
        <gggs-kpi-card label="Hires" [value]="d.hires" />
        <gggs-kpi-card label="Time to hire" [value]="d.averageDaysToHire !== undefined ? (d.averageDaysToHire | number: '1.0-1') + ' days' : '—'" hint="Applied to hired" />
        <gggs-kpi-card label="Cost per hire" [value]="d.costPerHire !== undefined ? (d.costPerHire | gggsMoney) : '—'" hint="Agency fees and referral bonuses" />
        <gggs-kpi-card label="Offer acceptance" [value]="d.offerAcceptancePercent !== undefined ? d.offerAcceptancePercent + '%' : '—'" [hint]="d.offersAccepted + ' accepted, ' + d.offersDeclined + ' declined'" />
      </div>
      <div class="rec__charts">
        <section class="panel">
          <gggs-chart title="Live pipeline" [labels]="pipelineLabels()" [series]="pipelineSeries()" />
        </section>
        <section class="panel">
          <gggs-chart title="Hires by source" kind="doughnut" [labels]="sourceLabels()" [series]="sourceSeries()" />
        </section>
      </div>
      <section class="panel stack">
        <h2 class="rec__heading">Source effectiveness</h2>
        <div class="rec__row rec__row--head"><span>Source</span><span>Applications</span><span>Interviewed</span><span>Offered</span><span>Hired (conversion)</span></div>
        @for (row of d.sources; track row.source) {
          <div class="rec__row">
            <span>{{ humanize(row.source) }}</span><span>{{ row.applications }}</span><span>{{ row.interviewed }}</span><span>{{ row.offered }}</span><span>{{ row.hired }} ({{ row.conversionPercent }}%)</span>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-chart-bar" title="No applications in this range" />
        }
      </section>
      <section class="panel stack">
        <h2 class="rec__heading">Recruiter performance</h2>
        <div class="rec__row rec__row--head"><span>Recruiter</span><span>Requisitions</span><span>Applications</span><span>Hires</span><span>Days to hire</span></div>
        @for (row of d.recruiters; track row.recruiterEmployeeId ?? 'none') {
          <div class="rec__row">
            <span>{{ row.recruiterName ?? 'Unassigned' }}</span><span>{{ row.requisitions }}</span><span>{{ row.applications }}</span><span>{{ row.hires }}</span><span>{{ row.averageDaysToHire ?? '—' }}</span>
          </div>
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="10" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styleUrl: './recruitment.css',
})
export class RecruitmentAnalyticsPage {
  private readonly api = inject(RecruitmentApi);

  protected readonly humanize = humanize;
  protected readonly data = signal<RecruitmentAnalytics | null>(null);
  protected readonly from = signal(iso(new Date(Date.now() - 365 * 86_400_000)));
  protected readonly to = signal(iso(new Date()));
  protected readonly error = signal<string | null>(null);
  protected readonly pipelineLabels = computed(() => (this.data()?.pipeline ?? []).map((row) => humanize(row.label)));
  protected readonly pipelineSeries = computed(() => [{ label: 'Candidates', values: (this.data()?.pipeline ?? []).map((row) => row.count) }]);
  protected readonly sourceLabels = computed(() => (this.data()?.sources ?? []).map((row) => humanize(row.source)));
  protected readonly sourceSeries = computed(() => [{ label: 'Hires', values: (this.data()?.sources ?? []).map((row) => row.hired) }]);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.data.set(await this.api.analytics(this.from(), this.to()));
    } catch (error) {
      this.error.set(errorText(error, 'Analytics could not be loaded.'));
    }
  }
}
