import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type {
  CalibrationView,
  FeedbackRelationship,
  PipDetailsInput,
  PipItem,
  PipObjective,
  ReviewCycleInput,
  ReviewCycleItem,
  ReviewInput,
  ReviewItem,
  ReviewView,
} from '@gggs/shared/models';
import { CYCLE_TYPES, FEEDBACK_RELATIONSHIPS, RATING_LABELS, humanize } from '@gggs/shared/models';
import { OrgSelect } from '@gggs/shared/org-reference';
import { PerformanceApi, ProgressBar } from '@gggs/shared/talent';
import { Chart, EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';
import { errorText } from './goals-pages';

const iso = (date: Date) => date.toISOString().slice(0, 10);
const days = (count: number) => iso(new Date(Date.now() + count * 86_400_000));
const split = (value: string) =>
  value
    .split(',')
    .map((part) => part.trim())
    .filter(Boolean);

const blankCycle = (): ReviewCycleInput => ({
  name: '',
  type: 'Annual',
  periodStart: `${new Date().getFullYear()}-01-01`,
  periodEnd: `${new Date().getFullYear()}-12-31`,
  selfReviewDue: days(14),
  managerReviewDue: days(28),
  competencies: ['Delivery', 'Collaboration', 'Ownership', 'Communication'],
  goalWeight: 50,
  peerFeedback: true,
  departmentIds: [],
});

/** Review cycles (spec: performance). */
@Component({
  selector: 'gggs-cycles-page',
  imports: [DatePipe, FormsModule, RouterLink, PageHeader, StatusBadge, EmptyState, FormDialog, OrgSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Performance reviews" subtitle="Review cycles: launch, self and manager reviews, calibration and release.">
      <a class="btn" routerLink="/performance/pips">Improvement plans</a>
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="open()"><i class="pi pi-plus" aria-hidden="true"></i> New cycle</button>
      }
    </gggs-page-header>
    <section class="panel stack">
      @for (cycle of cycles(); track cycle.id) {
        <a class="perf__row" [routerLink]="['/performance/cycles', cycle.id]">
          <span><strong>{{ cycle.name }}</strong> <span class="muted">{{ humanize(cycle.type) }}</span></span>
          <span>{{ cycle.periodStart | date: 'd MMM y' }} – {{ cycle.periodEnd | date: 'd MMM y' }}</span>
          <span class="muted">{{ cycle.reviews }} review(s){{ released(cycle) ? ' · ' + released(cycle) + ' released' : '' }}</span>
          <span class="muted">self by {{ cycle.selfReviewDue | date: 'd MMM' }} · manager by {{ cycle.managerReviewDue | date: 'd MMM' }}</span>
          <span><gggs-status-badge [status]="cycle.status" /></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-chart-line" title="No review cycles yet" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
    <gggs-form-dialog [(visible)]="dialogOpen" title="New review cycle" saveLabel="Create" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().name.trim()" (save)="create()" width="min(52rem, 96vw)">
      <div class="perf__grid">
        <label class="field perf__wide">
          <span class="field__label">Name</span>
          <input type="text" maxlength="200" [value]="form().name" (input)="patch({ name: $any($event.target).value })" placeholder="FY26 annual review" />
        </label>
        <label class="field">
          <span class="field__label">Type</span>
          <select [value]="form().type" (change)="patch({ type: $any($event.target).value })">
            @for (type of types; track type) {
              <option [value]="type">{{ humanize(type) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Period from</span>
          <input type="date" [value]="form().periodStart" (change)="patch({ periodStart: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">to</span>
          <input type="date" [value]="form().periodEnd" (change)="patch({ periodEnd: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Self-reviews due</span>
          <input type="date" [value]="form().selfReviewDue" (change)="patch({ selfReviewDue: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Manager reviews due</span>
          <input type="date" [value]="form().managerReviewDue" (change)="patch({ managerReviewDue: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Goals weigh (%)</span>
          <input type="number" min="0" max="100" [value]="form().goalWeight" (input)="patch({ goalWeight: +$any($event.target).value })" />
        </label>
        <label class="field perf__wide">
          <span class="field__label">Competencies (comma-separated)</span>
          <input type="text" [value]="form().competencies.join(', ')" (change)="patch({ competencies: split($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Only this department (empty for everyone)</span>
          <gggs-org-select kind="departments" placeholder="Everyone" ariaLabel="Department" [ngModel]="firstDepartment()" (ngModelChange)="patch({ departmentIds: $event ? [$event] : [] })" />
        </label>
        <label class="perf__flag"><input type="checkbox" [checked]="form().peerFeedback" (change)="patch({ peerFeedback: $any($event.target).checked })" /> <span>Collect 360 feedback</span></label>
      </div>
    </gggs-form-dialog>
  `,
  styleUrl: './performance.css',
})
export class CyclesPage {
  private readonly api = inject(PerformanceApi);
  private readonly router = inject(Router);

  protected readonly canManage = inject(PermissionStore).has('performance.cycle.manage');
  protected readonly humanize = humanize;
  protected readonly split = split;
  protected readonly types = CYCLE_TYPES;
  protected readonly cycles = signal<readonly ReviewCycleItem[]>([]);
  protected readonly dialogOpen = signal(false);
  protected readonly form = signal<ReviewCycleInput>(blankCycle());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.cycles().then(
      (cycles) => this.cycles.set(cycles),
      (error: unknown) => this.error.set(errorText(error, 'Cycles could not be loaded.')),
    );
  }

  protected firstDepartment(): string | null {
    const [first] = this.form().departmentIds;
    return first ?? null;
  }

  protected released(cycle: ReviewCycleItem): number {
    return (cycle.byStatus['Released'] ?? 0) + (cycle.byStatus['Acknowledged'] ?? 0);
  }

  protected open(): void {
    this.form.set(blankCycle());
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected patch(change: Partial<ReviewCycleInput>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected async create(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      const cycle = await this.api.saveCycle(null, this.form());
      this.dialogOpen.set(false);
      void this.router.navigate(['/performance/cycles', cycle.id]);
    } catch (error) {
      this.formError.set(errorText(error, 'The cycle could not be created.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** A cycle: its reviews by status, and calibration against the target distribution. */
@Component({
  selector: 'gggs-cycle-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, KpiCard, Skeleton, EmptyState, Tabs, Chart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (cycle(); as c) {
      <gggs-page-header [title]="c.name" [subtitle]="(c.periodStart | date: 'd MMM y') + ' – ' + (c.periodEnd | date: 'd MMM y') + ' · goals ' + c.goalWeight + '%, competencies ' + (100 - c.goalWeight) + '%'">
        <gggs-status-badge [status]="c.status" />
        @if (canManage && c.status === 'Draft') {
          <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="act('launch')">Launch</button>
        }
        @if (canManage && c.status === 'Active') {
          <button type="button" class="btn" [disabled]="busy()" (click)="act('calibrate')">Start calibration</button>
        }
        @if (canCalibrate && (c.status === 'Active' || c.status === 'Calibration' || c.status === 'Released')) {
          <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="act('release')">Release ready reviews</button>
        }
        @if (canManage && c.status === 'Released') {
          <button type="button" class="btn" [disabled]="busy()" (click)="act('close')">Close cycle</button>
        }
      </gggs-page-header>
      <p><a routerLink="/performance">← Cycles</a></p>
      <div class="perf__kpis">
        <gggs-kpi-card label="Reviews" [value]="c.reviews" />
        <gggs-kpi-card label="Waiting on self-review" [value]="count(c, 'Pending')" />
        <gggs-kpi-card label="Waiting on manager" [value]="count(c, 'SelfSubmitted')" />
        <gggs-kpi-card label="Ready to release" [value]="count(c, 'ManagerSubmitted')" />
        <gggs-kpi-card label="Released" [value]="count(c, 'Released') + count(c, 'Acknowledged')" />
      </div>
      <gggs-tabs [tabs]="tabs()" [(selected)]="tab" />
      @if (tab() === 'calibration' && calibration(); as cal) {
        <section class="panel">
          <gggs-chart title="Ratings against the target distribution (%)" [labels]="distributionLabels()" [series]="distributionSeries()" />
        </section>
      }
      <section class="panel stack">
        <div class="perf__row perf__row--head">
          <span>Employee</span><span>Manager</span><span>Self · manager · score</span><span>{{ tab() === 'calibration' ? 'Calibrated' : 'Final' }}</span><span>Status</span>
        </div>
        @for (review of rows(); track review.id) {
          <a class="perf__row" [routerLink]="['/performance/reviews', review.id]">
            <span><strong>{{ review.employeeName ?? '—' }}</strong> <span class="muted">{{ review.departmentName ?? '' }}</span></span>
            <span class="muted">{{ review.managerName ?? 'no manager' }}</span>
            <span>{{ review.selfRating ?? '–' }} · {{ review.managerRating ?? '–' }} · {{ review.score ?? '–' }}</span>
            <span>{{ review.calibratedRating ?? review.finalRating ?? '–' }}</span>
            <span><gggs-status-badge [status]="review.status" /></span>
          </a>
        } @empty {
          <gggs-empty-state icon="pi pi-users" title="No reviews" [description]="c.status === 'Draft' ? 'Launch the cycle to open a review for everyone in scope.' : undefined" />
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './performance.css',
})
export class CyclePage {
  private readonly api = inject(PerformanceApi);
  private readonly permissions = inject(PermissionStore);

  readonly id = input.required<string>();

  protected readonly canManage = this.permissions.has('performance.cycle.manage');
  protected readonly canCalibrate = this.permissions.has('performance.calibration.manage');
  protected readonly cycle = signal<ReviewCycleItem | null>(null);
  protected readonly reviews = signal<readonly ReviewItem[]>([]);
  protected readonly calibration = signal<CalibrationView | null>(null);
  protected readonly tab = signal('reviews');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly tabs = computed<readonly TabItem[]>(() => [
    { key: 'reviews', label: 'Reviews' },
    ...(this.canCalibrate ? [{ key: 'calibration', label: 'Calibration', note: this.calibration() ? `${this.calibration()?.awaitingManager ?? 0} awaiting manager` : undefined }] : []),
  ]);
  protected readonly rows = computed(() => (this.tab() === 'calibration' ? (this.calibration()?.reviews ?? []).filter((row) => row.status === 'ManagerSubmitted' || row.status === 'Released' || row.status === 'Acknowledged') : this.reviews()));
  protected readonly distributionLabels = computed(() => (this.calibration()?.distribution ?? []).map((row) => `${row.rating} ${row.label}`));
  protected readonly distributionSeries = computed(() => {
    const rows = this.calibration()?.distribution ?? [];
    return [
      { label: 'Actual', values: rows.map((row) => row.percent) },
      { label: 'Target', values: rows.map((row) => row.targetPercent) },
    ];
  });

  constructor() {
    effect(() => {
      this.id();
      void this.load();
    });
  }

  protected count(cycle: ReviewCycleItem, status: string): number {
    return cycle.byStatus[status] ?? 0;
  }

  protected async act(action: 'launch' | 'calibrate' | 'release' | 'close'): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.cycle.set(await this.api.cycleAction(this.id(), action));
      await this.load();
    } catch (error) {
      this.error.set(errorText(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const [cycle, reviews] = await Promise.all([this.api.cycle(this.id()), this.api.reviews(this.id())]);
      this.cycle.set(cycle);
      this.reviews.set(reviews);
      if (this.canCalibrate) {
        this.calibration.set(await this.api.calibration(this.id()));
      }
    } catch (error) {
      this.error.set(errorText(error, 'The cycle could not be loaded.'));
    }
  }
}

/** The ratings form for one half of a review: a 1-5 rating per competency plus comments. */
@Component({
  selector: 'gggs-review-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (competency of competencies(); track competency) {
      <div class="perf__rate">
        <span>{{ competency }}</span>
        <span class="perf__stars" role="radiogroup" [attr.aria-label]="competency">
          @for (score of scores; track score) {
            <button type="button" class="perf__star" [class.perf__star--on]="rating(competency) >= score" [attr.aria-pressed]="rating(competency) === score" [disabled]="readonly()" [title]="labels[score]" (click)="rate(competency, score)">{{ score }}</button>
          }
        </span>
      </div>
    }
    <label class="field">
      <span class="field__label">Strengths</span>
      <textarea rows="2" maxlength="4000" [value]="value().strengths ?? ''" [disabled]="readonly()" (input)="patch({ strengths: $any($event.target).value })"></textarea>
    </label>
    <label class="field">
      <span class="field__label">To work on</span>
      <textarea rows="2" maxlength="4000" [value]="value().improvements ?? ''" [disabled]="readonly()" (input)="patch({ improvements: $any($event.target).value })"></textarea>
    </label>
    <label class="field">
      <span class="field__label">Comments</span>
      <textarea rows="2" maxlength="4000" [value]="value().comments ?? ''" [disabled]="readonly()" (input)="patch({ comments: $any($event.target).value })"></textarea>
    </label>
    <label class="field">
      <span class="field__label">Overall rating</span>
      <select [value]="value().overall ?? ''" [disabled]="readonly()" (change)="patch({ overall: $any($event.target).value ? +$any($event.target).value : null })">
        <option value="">Not yet</option>
        @for (score of scores; track score) {
          <option [value]="score">{{ score }} · {{ labels[score] }}</option>
        }
      </select>
    </label>
  `,
  styleUrl: './performance.css',
})
export class ReviewForm {
  readonly competencies = input.required<readonly string[]>();
  readonly value = input.required<ReviewInput>();
  readonly readonly = input(false);
  readonly changed = input<(value: ReviewInput) => void>(() => undefined);

  protected readonly scores = [1, 2, 3, 4, 5];
  protected readonly labels = RATING_LABELS;

  protected rating(competency: string): number {
    return this.value().ratings?.[competency] ?? 0;
  }

  protected rate(competency: string, score: number): void {
    this.changed()({ ...this.value(), ratings: { ...(this.value().ratings ?? {}), [competency]: score } });
  }

  protected patch(change: Partial<ReviewInput>): void {
    this.changed()({ ...this.value(), ...change });
  }
}

/** A review: the employee's self-review, the manager's review, the evidence, 360 feedback, calibration and acknowledgement. */
@Component({
  selector: 'gggs-review-page',
  imports: [DatePipe, FormsModule, RouterLink, PageHeader, StatusBadge, Skeleton, FormDialog, EmployeeSelect, ProgressBar, ReviewForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="(v.review.employeeName ?? 'Review') + ' · ' + v.cycle.name" [subtitle]="(v.review.designation ?? '') + (v.review.managerName ? ' · manager ' + v.review.managerName : '')">
        <gggs-status-badge [status]="v.review.status" />
        @if (v.canRequestFeedback) {
          <button type="button" class="btn" (click)="feedbackOpen.set(true)">Ask for feedback</button>
        }
        @if (v.canCalibrate) {
          <button type="button" class="btn" (click)="calibrateOpen.set(true)">Calibrate</button>
          <button type="button" class="btn btn--link" [disabled]="busy()" (click)="returnReview()">Return to manager</button>
        }
        @if (v.isEmployee && v.review.status === 'Released') {
          <button type="button" class="btn btn--primary" (click)="ackOpen.set(true)">Acknowledge</button>
        }
      </gggs-page-header>
      <p><a [routerLink]="v.isEmployee ? '/me/reviews' : ['/performance/cycles', v.cycle.id]">← Back</a></p>
      @if (v.review.finalRating) {
        <section class="panel perf__final">
          <span class="perf__big">{{ v.review.finalRating }} / 5</span>
          <span>{{ v.review.finalLabel }}</span>
          @if (v.calibrationNote) {
            <span class="muted">Calibration: {{ v.calibrationNote }}</span>
          }
          @if (v.employeeComment) {
            <span class="muted">Employee: {{ v.employeeComment }}</span>
          }
        </section>
      }
      <div class="perf__split">
        <div class="stack">
          <section class="panel stack">
            <h2 class="perf__heading">Self-review {{ v.review.selfSubmittedDate ? '· submitted ' + (v.review.selfSubmittedDate | date: 'd MMM') : '' }}</h2>
            <gggs-review-form [competencies]="v.cycle.competencies" [value]="v.canEditSelf ? self() : v.self" [readonly]="!v.canEditSelf" [changed]="setSelf" />
            @if (v.canEditSelf) {
              <div class="toolbar">
                <button type="button" class="btn" [disabled]="busy()" (click)="saveSelf(false)">Save draft</button>
                <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="saveSelf(true)">Submit self-review</button>
              </div>
            }
          </section>
          @if (v.manager || v.canEditManager) {
            <section class="panel stack">
              <h2 class="perf__heading">Manager review {{ v.review.managerSubmittedDate ? '· submitted ' + (v.review.managerSubmittedDate | date: 'd MMM') : '' }}</h2>
              @if (v.canEditManager && v.suggestedScore) {
                <p class="muted">Suggested from goals and your ratings: {{ v.suggestedScore }} / 5</p>
              }
              <gggs-review-form [competencies]="v.cycle.competencies" [value]="v.canEditManager ? managerInput() : (v.manager ?? {})" [readonly]="!v.canEditManager" [changed]="setManager" />
              @if (v.canEditManager) {
                <div class="toolbar">
                  <button type="button" class="btn" [disabled]="busy()" (click)="saveManager(false)">Save draft</button>
                  <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="saveManager(true)">Submit manager review</button>
                </div>
              }
            </section>
          }
          @if (v.feedback.length) {
            <section class="panel stack">
              <h2 class="perf__heading">360 feedback</h2>
              @for (item of v.feedback; track item.id) {
                <article class="perf__feedback">
                  <div class="toolbar">
                    <strong>{{ item.reviewerName ?? humanize(item.relationship) }}</strong>
                    <span class="muted">{{ humanize(item.relationship) }}</span>
                    <span class="perf__spacer"></span>
                    <gggs-status-badge [status]="item.status" />
                  </div>
                  @if (item.status === 'Submitted') {
                    <p class="muted">{{ ratingText(item.ratings) }}</p>
                    @if (item.strengths) {
                      <p><strong>Strengths:</strong> {{ item.strengths }}</p>
                    }
                    @if (item.improvements) {
                      <p><strong>To work on:</strong> {{ item.improvements }}</p>
                    }
                  }
                </article>
              }
            </section>
          }
        </div>
        <div class="stack">
          <section class="panel stack">
            <h2 class="perf__heading">Goals {{ v.evidence.goalAchievement !== undefined ? '· ' + v.evidence.goalAchievement + '% achieved' : '' }}</h2>
            @for (goal of v.evidence.goals; track goal.id) {
              <a class="perf__evidence" [routerLink]="['/goals', goal.id]">
                <span>{{ goal.title }}</span>
                <span class="muted">{{ goal.progress }}% · weight {{ goal.weight }}%</span>
                <gggs-progress-bar [value]="goal.progress" />
              </a>
            } @empty {
              <p class="muted">No goals in this period.</p>
            }
            @if (v.evidence.goalRating) {
              <p class="muted">On the scale: {{ v.evidence.goalRating }} / 5</p>
            }
          </section>
          <section class="panel stack">
            <h2 class="perf__heading">Project work</h2>
            @for (project of v.evidence.projects; track project.project) {
              <div class="perf__evidence">
                <span>{{ project.project }}</span>
                <span class="muted">{{ project.hours }} h approved{{ project.role ? ' · ' + project.role : '' }}</span>
              </div>
            } @empty {
              <p class="muted">No approved timesheet hours in this period.</p>
            }
          </section>
          <section class="panel stack">
            <h2 class="perf__heading">Learning and skills</h2>
            @for (course of v.evidence.learning; track course.courseId + course.completedOn) {
              <div class="perf__evidence">
                <span>{{ course.courseTitle }}</span>
                <span class="muted">completed {{ course.completedOn | date: 'd MMM y' }}</span>
              </div>
            } @empty {
              <p class="muted">No courses completed in this period.</p>
            }
            @if (v.evidence.skills.length) {
              <p class="muted">{{ v.evidence.skills.join(' · ') }}</p>
            }
          </section>
        </div>
      </div>

      <gggs-form-dialog [(visible)]="feedbackOpen" title="Ask for 360 feedback" saveLabel="Ask" [busy]="busy()" [error]="formError()" [saveDisabled]="!reviewers().length" (save)="askFeedback()">
        <label class="field">
          <span class="field__label">Relationship</span>
          <select [value]="relationship()" (change)="relationship.set($any($event.target).value)">
            @for (option of relationships; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
        @for (reviewer of reviewers(); track reviewer) {
          <div class="toolbar"><span class="muted">Reviewer {{ $index + 1 }}</span> <button type="button" class="btn btn--link btn--sm" (click)="removeReviewer(reviewer)">Remove</button></div>
        }
        <gggs-employee-select ariaLabel="Add a reviewer" [ngModel]="null" (ngModelChange)="addReviewer($event)" />
      </gggs-form-dialog>

      <gggs-form-dialog [(visible)]="calibrateOpen" title="Calibrate" [busy]="busy()" [error]="formError()" [saveDisabled]="!calibrated()" (save)="calibrate()">
        <p class="muted">Manager rating {{ v.review.managerRating ?? '–' }}; score {{ v.review.score ?? '–' }}.</p>
        <label class="field">
          <span class="field__label">Calibrated rating</span>
          <select [value]="calibrated() ?? ''" (change)="calibrated.set(+$any($event.target).value || null)">
            <option value="">Choose</option>
            @for (score of scores; track score) {
              <option [value]="score">{{ score }} · {{ labels[score] }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Note</span>
          <textarea rows="3" maxlength="1000" [value]="note()" (input)="note.set($any($event.target).value)"></textarea>
        </label>
      </gggs-form-dialog>

      <gggs-form-dialog [(visible)]="ackOpen" title="Acknowledge your review" saveLabel="Acknowledge" [busy]="busy()" [error]="formError()" (save)="acknowledge()">
        <label class="field">
          <span class="field__label">Your comments (optional)</span>
          <textarea rows="3" maxlength="2000" [value]="note()" (input)="note.set($any($event.target).value)"></textarea>
        </label>
      </gggs-form-dialog>
    } @else if (!error()) {
      <gggs-skeleton [lines]="12" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './performance.css',
})
export class ReviewPage {
  private readonly api = inject(PerformanceApi);

  readonly id = input.required<string>();

  protected readonly humanize = humanize;
  protected readonly scores = [1, 2, 3, 4, 5];
  protected readonly labels = RATING_LABELS;
  protected readonly relationships = FEEDBACK_RELATIONSHIPS;
  protected readonly view = signal<ReviewView | null>(null);
  protected readonly self = signal<ReviewInput>({});
  protected readonly managerInput = signal<ReviewInput>({});
  protected readonly feedbackOpen = signal(false);
  protected readonly calibrateOpen = signal(false);
  protected readonly ackOpen = signal(false);
  protected readonly reviewers = signal<readonly string[]>([]);
  protected readonly relationship = signal<FeedbackRelationship>('Peer');
  protected readonly calibrated = signal<number | null>(null);
  protected readonly note = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly setSelf = (value: ReviewInput) => this.self.set(value);
  protected readonly setManager = (value: ReviewInput) => this.managerInput.set(value);

  constructor() {
    effect(() => {
      this.api.review(this.id()).then(
        (view) => this.show(view),
        (error: unknown) => this.error.set(errorText(error, 'The review could not be loaded.')),
      );
    });
  }

  protected ratingText(ratings: Readonly<Record<string, number>>): string {
    return Object.entries(ratings)
      .map(([name, value]) => `${name} ${value}`)
      .join(' · ');
  }

  protected async saveSelf(submit: boolean): Promise<void> {
    await this.run(() => this.api.saveSelf(this.id(), this.self(), submit));
  }

  protected async saveManager(submit: boolean): Promise<void> {
    await this.run(() => this.api.saveManager(this.id(), this.managerInput(), submit));
  }

  protected async returnReview(): Promise<void> {
    await this.run(() => this.api.returnReview(this.id()));
  }

  protected addReviewer(id: string | null): void {
    if (id && !this.reviewers().includes(id)) {
      this.reviewers.update((list) => [...list, id]);
    }
  }

  protected removeReviewer(id: string): void {
    this.reviewers.update((list) => list.filter((item) => item !== id));
  }

  protected async askFeedback(): Promise<void> {
    if (await this.run(() => this.api.askFeedback(this.id(), this.reviewers(), this.relationship()), true)) {
      this.feedbackOpen.set(false);
      this.reviewers.set([]);
    }
  }

  protected async calibrate(): Promise<void> {
    const rating = this.calibrated();
    if (rating && (await this.run(() => this.api.calibrate(this.id(), rating, this.note() || null), true))) {
      this.calibrateOpen.set(false);
    }
  }

  protected async acknowledge(): Promise<void> {
    if (await this.run(() => this.api.acknowledge(this.id(), this.note() || null), true)) {
      this.ackOpen.set(false);
    }
  }

  private show(view: ReviewView): void {
    this.view.set(view);
    this.self.set(view.self);
    this.managerInput.set(view.manager ?? {});
    this.calibrated.set(view.review.calibratedRating ?? view.review.managerRating ?? null);
  }

  private async run(action: () => Promise<ReviewView>, inDialog = false): Promise<boolean> {
    this.busy.set(true);
    (inDialog ? this.formError : this.error).set(null);
    try {
      this.show(await action());
      return true;
    } catch (error) {
      (inDialog ? this.formError : this.error).set(errorText(error, 'That did not work.'));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}

const blankPip = (): PipDetailsInput => ({ reason: '', startDate: iso(new Date()), endDate: days(60), objectives: [{ title: '', measure: null }] });

/** The reason, dates and objectives of an improvement plan. */
@Component({
  selector: 'gggs-pip-fields',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label class="field">
      <span class="field__label">Why</span>
      <textarea rows="3" maxlength="2000" [value]="value().reason" (input)="patch({ reason: $any($event.target).value })"></textarea>
    </label>
    <div class="perf__grid">
      <label class="field">
        <span class="field__label">From</span>
        <input type="date" [value]="value().startDate" (change)="patch({ startDate: $any($event.target).value })" />
      </label>
      <label class="field">
        <span class="field__label">To (up to 180 days)</span>
        <input type="date" [value]="value().endDate" (change)="patch({ endDate: $any($event.target).value })" />
      </label>
    </div>
    <h3 class="perf__heading">Objectives</h3>
    @for (objective of value().objectives; track $index; let i = $index) {
      <div class="perf__objective">
        <input type="text" maxlength="300" placeholder="What must improve" aria-label="Objective" [value]="objective.title" (input)="patchObjective(i, { title: $any($event.target).value })" />
        <input type="text" maxlength="500" placeholder="How it is measured" aria-label="Measure" [value]="objective.measure ?? ''" (input)="patchObjective(i, { measure: $any($event.target).value || null })" />
        <button type="button" class="btn btn--link btn--sm" aria-label="Remove" (click)="removeObjective(i)"><i class="pi pi-trash" aria-hidden="true"></i></button>
      </div>
    }
    <button type="button" class="btn btn--sm" (click)="addObjective()"><i class="pi pi-plus" aria-hidden="true"></i> Add objective</button>
  `,
  styleUrl: './performance.css',
})
export class PipFields {
  readonly value = input.required<PipDetailsInput>();
  readonly changed = input<(value: PipDetailsInput) => void>(() => undefined);

  protected patch(change: Partial<PipDetailsInput>): void {
    this.changed()({ ...this.value(), ...change });
  }

  protected patchObjective(index: number, change: Partial<PipObjective>): void {
    this.patch({ objectives: this.value().objectives.map((item, i) => (i === index ? { ...item, ...change } : item)) });
  }

  protected addObjective(): void {
    this.patch({ objectives: [...this.value().objectives, { title: '', measure: null }] });
  }

  protected removeObjective(index: number): void {
    this.patch({ objectives: this.value().objectives.filter((_, i) => i !== index) });
  }
}

/** Performance improvement plans. */
@Component({
  selector: 'gggs-pips-page',
  imports: [DatePipe, FormsModule, RouterLink, PageHeader, StatusBadge, EmptyState, FormDialog, EmployeeSelect, PipFields],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Improvement plans" subtitle="Performance improvement plans: objectives, check-ins and outcomes.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="open()"><i class="pi pi-plus" aria-hidden="true"></i> New plan</button>
      }
    </gggs-page-header>
    <section class="panel stack">
      @for (plan of plans(); track plan.id) {
        <a class="perf__row" [routerLink]="['/performance/pips', plan.id]">
          <span><strong>{{ plan.employeeName ?? '—' }}</strong> <span class="muted">{{ plan.employeeCode ?? '' }}</span></span>
          <span class="muted">{{ plan.managerName ?? '' }}</span>
          <span>{{ plan.startDate | date: 'd MMM' }} – {{ plan.endDate | date: 'd MMM y' }}{{ plan.extended ? ' (extended)' : '' }}</span>
          <span class="muted">{{ plan.status === 'Active' ? plan.daysLeft + ' days left' : (plan.outcome ?? '') }}</span>
          <span><gggs-status-badge [status]="plan.status" /></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-flag" title="No improvement plans" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
    <gggs-form-dialog [(visible)]="dialogOpen" title="New improvement plan" saveLabel="Create draft" [busy]="busy()" [error]="formError()" [saveDisabled]="!employeeId() || !form().reason.trim()" (save)="create()" width="min(48rem, 96vw)">
      <label class="field">
        <span class="field__label">Employee</span>
        <gggs-employee-select ariaLabel="Employee" [ngModel]="employeeId()" (ngModelChange)="employeeId.set($event)" />
      </label>
      <gggs-pip-fields [value]="form()" [changed]="setForm" />
    </gggs-form-dialog>
  `,
  styleUrl: './performance.css',
})
export class PipsPage {
  private readonly api = inject(PerformanceApi);
  private readonly router = inject(Router);

  protected readonly canManage = inject(PermissionStore).has('performance.pip.manage');
  protected readonly plans = signal<readonly PipItem[]>([]);
  protected readonly dialogOpen = signal(false);
  protected readonly employeeId = signal<string | null>(null);
  protected readonly form = signal<PipDetailsInput>(blankPip());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly setForm = (value: PipDetailsInput) => this.form.set(value);

  constructor() {
    this.api.pips().then(
      (plans) => this.plans.set(plans),
      (error: unknown) => this.error.set(errorText(error, 'Plans could not be loaded.')),
    );
  }

  protected open(): void {
    this.employeeId.set(null);
    this.form.set(blankPip());
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected async create(): Promise<void> {
    const employeeId = this.employeeId();
    if (!employeeId) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      const plan = await this.api.createPip(employeeId, { ...this.form(), objectives: this.form().objectives.filter((item) => item.title.trim()) });
      this.dialogOpen.set(false);
      void this.router.navigate(['/performance/pips', plan.id]);
    } catch (error) {
      this.formError.set(errorText(error, 'The plan could not be created.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** One improvement plan: start it, record check-ins, extend it, close it with an outcome. */
@Component({
  selector: 'gggs-pip-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, Skeleton, FormDialog, PipFields],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (plan(); as p) {
      <gggs-page-header [title]="'Improvement plan · ' + (p.employeeName ?? '')" [subtitle]="(p.startDate | date: 'd MMM y') + ' – ' + (p.endDate | date: 'd MMM y') + (p.managerName ? ' · ' + p.managerName : '')">
        <gggs-status-badge [status]="p.status" />
        @if (p.canManage) {
          @if (p.status === 'Draft') {
            <button type="button" class="btn" (click)="openEdit(p)">Edit</button>
            <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="act('start')">Start plan</button>
          }
          @if (p.status === 'Active') {
            <button type="button" class="btn" (click)="noteOpen.set(true)">Add check-in</button>
            <button type="button" class="btn" (click)="extendOpen.set(true)">Extend</button>
            <button type="button" class="btn btn--primary" (click)="openClose(p)">Close with outcome</button>
          }
          @if (p.status === 'Draft' || p.status === 'Active') {
            <button type="button" class="btn btn--link" [disabled]="busy()" (click)="act('cancel')">Cancel</button>
          }
        }
      </gggs-page-header>
      <p><a [routerLink]="p.canManage ? '/performance/pips' : '/me/reviews'">← Back</a></p>
      <section class="panel stack">
        <p>{{ p.reason }}</p>
        @if (p.outcome) {
          <p><strong>Outcome:</strong> {{ p.outcome }}</p>
        }
      </section>
      <section class="panel stack">
        <h2 class="perf__heading">Objectives</h2>
        @for (objective of p.objectives; track $index) {
          <div class="perf__evidence">
            <span>{{ objective.title }}</span>
            <span class="muted">{{ objective.measure ?? '' }}{{ objective.met === true ? ' · met' : objective.met === false ? ' · not met' : '' }}</span>
          </div>
        }
      </section>
      <section class="panel stack">
        <h2 class="perf__heading">Check-ins</h2>
        @for (note of p.notes; track $index) {
          <div class="perf__checkin">
            <strong>{{ note.on | date: 'd MMM y' }}</strong>
            <span class="muted">{{ note.byName ?? '' }}</span>
            <p class="perf__wide">{{ note.text }}</p>
          </div>
        } @empty {
          <p class="muted">No check-ins yet.</p>
        }
      </section>
      <gggs-form-dialog [(visible)]="editOpen" title="Edit plan" [busy]="busy()" [error]="formError()" (save)="saveEdit()">
        <gggs-pip-fields [value]="form()" [changed]="setForm" />
      </gggs-form-dialog>
      <gggs-form-dialog [(visible)]="noteOpen" title="Check-in" [busy]="busy()" [error]="formError()" [saveDisabled]="!text().trim()" (save)="addNote()">
        <textarea rows="4" maxlength="2000" aria-label="Check-in" [value]="text()" (input)="text.set($any($event.target).value)"></textarea>
      </gggs-form-dialog>
      <gggs-form-dialog [(visible)]="extendOpen" title="Extend to" [busy]="busy()" [error]="formError()" (save)="extend()">
        <input type="date" aria-label="New end date" [value]="endDate()" (change)="endDate.set($any($event.target).value)" />
      </gggs-form-dialog>
      <gggs-form-dialog [(visible)]="closeOpen" title="Close the plan" saveLabel="Close plan" [busy]="busy()" [error]="formError()" [saveDisabled]="!text().trim()" (save)="close()">
        @for (objective of closing(); track $index; let i = $index) {
          <label class="perf__flag"><input type="checkbox" [checked]="objective.met === true" (change)="setMet(i, $any($event.target).checked)" /> <span>{{ objective.title }} met</span></label>
        }
        <label class="perf__flag"><input type="radio" name="outcome" [checked]="succeeded()" (change)="succeeded.set(true)" /> <span>Succeeded</span></label>
        <label class="perf__flag"><input type="radio" name="outcome" [checked]="!succeeded()" (change)="succeeded.set(false)" /> <span>Did not succeed</span></label>
        <label class="field">
          <span class="field__label">Outcome</span>
          <textarea rows="3" maxlength="2000" [value]="text()" (input)="text.set($any($event.target).value)"></textarea>
        </label>
      </gggs-form-dialog>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './performance.css',
})
export class PipPage {
  private readonly api = inject(PerformanceApi);

  readonly id = input.required<string>();

  protected readonly plan = signal<PipItem | null>(null);
  protected readonly editOpen = signal(false);
  protected readonly noteOpen = signal(false);
  protected readonly extendOpen = signal(false);
  protected readonly closeOpen = signal(false);
  protected readonly form = signal<PipDetailsInput>(blankPip());
  protected readonly text = signal('');
  protected readonly endDate = signal('');
  protected readonly succeeded = signal(true);
  protected readonly closing = signal<readonly PipObjective[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly setForm = (value: PipDetailsInput) => this.form.set(value);

  constructor() {
    effect(() => {
      this.api.pip(this.id()).then(
        (plan) => {
          this.plan.set(plan);
          this.endDate.set(plan.endDate);
        },
        (error: unknown) => this.error.set(errorText(error, 'The plan could not be loaded.')),
      );
    });
  }

  protected openEdit(plan: PipItem): void {
    this.form.set({ reason: plan.reason, startDate: plan.startDate, endDate: plan.endDate, objectives: plan.objectives, reviewId: plan.reviewId ?? null });
    this.formError.set(null);
    this.editOpen.set(true);
  }

  protected openClose(plan: PipItem): void {
    this.closing.set(plan.objectives);
    this.text.set('');
    this.formError.set(null);
    this.closeOpen.set(true);
  }

  protected setMet(index: number, met: boolean): void {
    this.closing.update((items) => items.map((item, i) => (i === index ? { ...item, met } : item)));
  }

  protected async act(action: 'start' | 'cancel'): Promise<void> {
    await this.run(() => this.api.pipAction(this.id(), action));
  }

  protected async saveEdit(): Promise<void> {
    if (await this.run(() => this.api.updatePip(this.id(), this.form()), true)) {
      this.editOpen.set(false);
    }
  }

  protected async addNote(): Promise<void> {
    if (await this.run(() => this.api.pipNote(this.id(), this.text()), true)) {
      this.noteOpen.set(false);
      this.text.set('');
    }
  }

  protected async extend(): Promise<void> {
    if (await this.run(() => this.api.extendPip(this.id(), this.endDate()), true)) {
      this.extendOpen.set(false);
    }
  }

  protected async close(): Promise<void> {
    if (await this.run(() => this.api.closePip(this.id(), this.succeeded(), this.text(), this.closing()), true)) {
      this.closeOpen.set(false);
    }
  }

  private async run(action: () => Promise<PipItem>, inDialog = false): Promise<boolean> {
    this.busy.set(true);
    (inDialog ? this.formError : this.error).set(null);
    try {
      this.plan.set(await action());
      return true;
    } catch (error) {
      (inDialog ? this.formError : this.error).set(errorText(error, 'That did not work.'));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
