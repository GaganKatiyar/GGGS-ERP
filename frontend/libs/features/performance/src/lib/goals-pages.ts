import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { Confidence, GoalDetailsInput, GoalItem, GoalLevel, GoalView, KeyResultInput } from '@gggs/shared/models';
import { CONFIDENCES, GOAL_LEVELS, METRIC_TYPES, humanize } from '@gggs/shared/models';
import { OrgSelect } from '@gggs/shared/org-reference';
import { GoalsApi, ProgressBar } from '@gggs/shared/talent';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

export const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const iso = (date: Date) => date.toISOString().slice(0, 10);

const blankGoal = (level: GoalLevel, ownerId: string | null): GoalDetailsInput => ({
  title: '',
  description: null,
  type: 'Okr',
  level,
  ownerEmployeeId: ownerId,
  departmentId: null,
  parentGoalId: null,
  cycleId: null,
  startDate: iso(new Date()),
  dueDate: iso(new Date(Date.now() + 90 * 86_400_000)),
  weight: level === 'Individual' ? 25 : 0,
});

const blankResult = (): KeyResultInput => ({ title: '', metricType: 'Number', startValue: 0, targetValue: 100, unit: null, weight: 1 });

/** Creating or editing a goal, with its key results when new. */
@Component({
  selector: 'gggs-goal-form',
  imports: [FormsModule, FormDialog, EmployeeSelect, OrgSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog [(visible)]="visible" [title]="goal() ? 'Edit goal' : 'New goal'" [busy]="busy()" [error]="error()" [saveDisabled]="!form().title.trim()" (save)="save()" size="lg">
      <div class="perf__grid">
        <label class="field perf__wide">
          <span class="field__label">Goal</span>
          <input type="text" maxlength="300" [value]="form().title" (input)="patch({ title: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Level</span>
          <select [value]="form().level" [disabled]="lockLevel()" (change)="patch({ level: $any($event.target).value })">
            @for (level of levels; track level) {
              <option [value]="level">{{ level }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Type</span>
          <select [value]="form().type" (change)="patch({ type: $any($event.target).value })">
            <option value="Okr">Objective (OKR)</option>
            <option value="Kpi">Indicator (KPI)</option>
          </select>
        </label>
        @if (form().level === 'Individual' && !lockOwner()) {
          <label class="field">
            <span class="field__label">Owner</span>
            <gggs-employee-select ariaLabel="Owner" [ngModel]="form().ownerEmployeeId ?? null" (ngModelChange)="patch({ ownerEmployeeId: $event })" />
          </label>
        }
        @if (form().level !== 'Company') {
          <label class="field">
            <span class="field__label">Department</span>
            <gggs-org-select kind="departments" placeholder="None" ariaLabel="Department" [ngModel]="form().departmentId ?? null" (ngModelChange)="patch({ departmentId: $event })" />
          </label>
        }
        <label class="field">
          <span class="field__label">Aligns to</span>
          <select [value]="form().parentGoalId ?? ''" (change)="patch({ parentGoalId: $any($event.target).value || null })">
            <option value="">Nothing</option>
            @for (parent of parents(); track parent.id) {
              <option [value]="parent.id">{{ parent.level }} · {{ parent.title }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">From</span>
          <input type="date" [value]="form().startDate" (change)="patch({ startDate: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Due</span>
          <input type="date" [value]="form().dueDate" (change)="patch({ dueDate: $any($event.target).value })" />
        </label>
        @if (form().level === 'Individual') {
          <label class="field">
            <span class="field__label">Weight in review (%)</span>
            <input type="number" min="0" max="100" [value]="form().weight" (input)="patch({ weight: +$any($event.target).value })" />
          </label>
        }
        <label class="field perf__wide">
          <span class="field__label">Description</span>
          <textarea rows="2" maxlength="4000" [value]="form().description ?? ''" (input)="patch({ description: $any($event.target).value || null })"></textarea>
        </label>
      </div>
      @if (!goal()) {
        <h3 class="perf__heading">Key results</h3>
        @for (result of results(); track $index; let i = $index) {
          <div class="perf__kr">
            <input type="text" maxlength="300" placeholder="Key result" aria-label="Key result" [value]="result.title" (input)="patchResult(i, { title: $any($event.target).value })" />
            <select aria-label="Measure" [value]="result.metricType" (change)="patchResult(i, { metricType: $any($event.target).value })">
              @for (type of metrics; track type) {
                <option [value]="type">{{ type }}</option>
              }
            </select>
            <input type="number" aria-label="From" [value]="result.startValue" [disabled]="result.metricType === 'Milestone'" (input)="patchResult(i, { startValue: +$any($event.target).value })" />
            <input type="number" aria-label="Target" [value]="result.targetValue" [disabled]="result.metricType === 'Milestone'" (input)="patchResult(i, { targetValue: +$any($event.target).value })" />
            <input type="text" maxlength="20" aria-label="Unit" placeholder="unit" [value]="result.unit ?? ''" (input)="patchResult(i, { unit: $any($event.target).value || null })" />
            <button type="button" class="btn btn--link btn--sm" aria-label="Remove" (click)="removeResult(i)"><i class="pi pi-trash" aria-hidden="true"></i></button>
          </div>
        }
        <button type="button" class="btn btn--sm" (click)="addResult()"><i class="pi pi-plus" aria-hidden="true"></i> Add key result</button>
        <p class="muted perf__small">Without key results, progress is entered by hand at each check-in.</p>
      }
    </gggs-form-dialog>
  `,
  styleUrl: './performance.css',
})
export class GoalForm {
  private readonly api = inject(GoalsApi);

  readonly visible = model(false);
  readonly goal = input<GoalItem | null>(null);
  readonly level = input<GoalLevel>('Individual');
  readonly ownerId = input<string | null>(null);
  readonly lockOwner = input(false);
  readonly lockLevel = input(false);
  readonly saved = output<GoalView>();

  protected readonly levels = GOAL_LEVELS;
  protected readonly metrics = METRIC_TYPES;
  protected readonly form = signal<GoalDetailsInput>(blankGoal('Individual', null));
  protected readonly results = signal<readonly KeyResultInput[]>([]);
  protected readonly parents = signal<readonly GoalItem[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      if (!this.visible()) {
        return;
      }

      const goal = this.goal();
      this.form.set(
        goal
          ? {
              title: goal.title,
              description: goal.description ?? null,
              type: goal.type,
              level: goal.level,
              ownerEmployeeId: goal.ownerEmployeeId ?? null,
              departmentId: goal.departmentId ?? null,
              parentGoalId: goal.parentGoalId ?? null,
              cycleId: goal.cycleId ?? null,
              startDate: goal.startDate,
              dueDate: goal.dueDate,
              weight: goal.weight,
            }
          : blankGoal(this.level(), this.ownerId()),
      );
      this.results.set(goal ? [] : [blankResult()]);
      this.error.set(null);
      this.api.goals({ status: 'Active' }).then(
        (goals) => this.parents.set(goals.filter((row) => row.level !== 'Individual' && row.id !== goal?.id)),
        () => undefined,
      );
    });
  }

  protected patch(change: Partial<GoalDetailsInput>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected patchResult(index: number, change: Partial<KeyResultInput>): void {
    this.results.update((rows) => rows.map((row, i) => (i === index ? { ...row, ...change } : row)));
  }

  protected addResult(): void {
    this.results.update((rows) => [...rows, blankResult()]);
  }

  protected removeResult(index: number): void {
    this.results.update((rows) => rows.filter((_, i) => i !== index));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const goal = this.goal();
      const view = goal
        ? await this.api.update(goal.id, this.form())
        : await this.api.create(this.form(), this.results().filter((row) => row.title.trim()), true);
      this.visible.set(false);
      this.saved.emit(view);
    } catch (error) {
      this.error.set(errorText(error, 'The goal could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Goals across the organisation (spec: OKR and KPI, cascading): company goals at the top, aligned goals beneath. */
@Component({
  selector: 'gggs-goals-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, EmptyState, ProgressBar, GoalForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Goals" subtitle="Company and department goals, and the individual goals aligned to them.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="formOpen.set(true)"><i class="pi pi-plus" aria-hidden="true"></i> New goal</button>
      }
    </gggs-page-header>
    <section class="panel toolbar perf__filters">
      <label class="field">
        <span class="field__label">Level</span>
        <select [value]="level()" (change)="level.set($any($event.target).value); load()">
          <option value="">All</option>
          @for (option of levels; track option) {
            <option [value]="option">{{ option }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">Any</option>
          <option value="Active">Active</option>
          <option value="Draft">Draft</option>
          <option value="Completed">Completed</option>
          <option value="Cancelled">Cancelled</option>
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (goal of ordered(); track goal.id) {
        <a class="perf__row" [class.perf__row--child]="goal.parentGoalId" [routerLink]="['/goals', goal.id]">
          <span><strong>{{ goal.title }}</strong> <span class="muted">{{ goal.level }}{{ goal.ownerName ? ' · ' + goal.ownerName : goal.departmentName ? ' · ' + goal.departmentName : '' }}</span></span>
          <span class="perf__progress">{{ goal.progress }}% <gggs-progress-bar [value]="goal.progress" /></span>
          <span class="muted">due {{ goal.dueDate | date: 'd MMM y' }}{{ goal.confidence ? ' · ' + humanize(goal.confidence) : '' }}</span>
          <span class="muted">{{ goal.keyResults }} key result(s){{ goal.children ? ' · ' + goal.children + ' aligned' : '' }}</span>
          <span><gggs-status-badge [status]="goal.status" /></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-bullseye" title="No goals here" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
    <gggs-goal-form [(visible)]="formOpen" level="Company" (saved)="opened($event)" />
  `,
  styleUrl: './performance.css',
})
export class GoalsPage {
  private readonly api = inject(GoalsApi);
  private readonly router = inject(Router);

  protected readonly canManage = inject(PermissionStore).has('performance.goal.manage');
  protected readonly humanize = humanize;
  protected readonly levels = GOAL_LEVELS;
  protected readonly goals = signal<readonly GoalItem[]>([]);
  protected readonly level = signal<GoalLevel | ''>('');
  protected readonly status = signal('Active');
  protected readonly formOpen = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Parents first, each followed by the goals aligned to it. */
  protected readonly ordered = computed(() => {
    const goals = this.goals();
    const ids = new Set(goals.map((goal) => goal.id));
    const result: GoalItem[] = [];
    const visit = (goal: GoalItem) => {
      result.push(goal);
      goals.filter((child) => child.parentGoalId === goal.id).forEach(visit);
    };
    goals.filter((goal) => !goal.parentGoalId || !ids.has(goal.parentGoalId)).forEach(visit);
    return result;
  });

  constructor() {
    void this.load();
  }

  protected opened(view: GoalView): void {
    void this.router.navigate(['/goals', view.goal.id]);
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.goals.set(await this.api.goals({ level: this.level() || null, status: (this.status() || null) as GoalItem['status'] | null }));
    } catch (error) {
      this.error.set(errorText(error, 'Goals could not be loaded.'));
    }
  }
}

/** One goal: key results, check-ins, aligned goals. */
@Component({
  selector: 'gggs-goal-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, Skeleton, EmptyState, FormDialog, ProgressBar, GoalForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="v.goal.title" [subtitle]="v.goal.level + (v.goal.ownerName ? ' · ' + v.goal.ownerName : '') + (v.goal.parentTitle ? ' · aligned to ' + v.goal.parentTitle : '')">
        <gggs-status-badge [status]="v.goal.status" />
        @if (v.goal.canEdit) {
          @if (v.goal.status === 'Active' || v.goal.status === 'Draft') {
            <button type="button" class="btn btn--primary" (click)="openCheckIn(v)">Check in</button>
            <button type="button" class="btn" (click)="editOpen.set(true)">Edit</button>
            <button type="button" class="btn" [disabled]="busy()" (click)="act('complete')">Mark complete</button>
            <button type="button" class="btn btn--link" [disabled]="busy()" (click)="act('cancel')">Cancel</button>
          } @else {
            <button type="button" class="btn" [disabled]="busy()" (click)="act('reopen')">Reopen</button>
          }
        }
      </gggs-page-header>
      <p><a routerLink="/goals">← Goals</a></p>
      <section class="panel stack">
        <div class="perf__big">{{ v.goal.progress }}% <gggs-progress-bar [value]="v.goal.progress" /></div>
        <p class="muted">{{ v.goal.startDate | date: 'd MMM y' }} – {{ v.goal.dueDate | date: 'd MMM y' }}{{ v.goal.level === 'Individual' ? ' · weight ' + v.goal.weight + '%' : '' }}{{ v.goal.confidence ? ' · ' + humanize(v.goal.confidence) : '' }}</p>
        @if (v.goal.description) {
          <p>{{ v.goal.description }}</p>
        }
      </section>
      <section class="panel stack">
        <h2 class="perf__heading">Key results</h2>
        @for (result of v.keyResults; track result.id) {
          <div class="perf__row perf__row--static">
            <span><strong>{{ result.title }}</strong></span>
            <span class="perf__progress">{{ result.progress }}% <gggs-progress-bar [value]="result.progress" /></span>
            <span class="muted">{{ result.metricType === 'Milestone' ? (result.currentValue >= 1 ? 'Done' : 'Not yet') : result.currentValue + ' of ' + result.targetValue + ' ' + (result.unit ?? '') }}</span>
            <span class="muted">from {{ result.startValue }}</span>
            <span class="muted">weight {{ result.weight }}</span>
          </div>
        } @empty {
          <p class="muted">No key results - progress is entered at each check-in.</p>
        }
      </section>
      @if (v.children.length) {
        <section class="panel stack">
          <h2 class="perf__heading">Aligned goals</h2>
          @for (child of v.children; track child.id) {
            <a class="perf__row" [routerLink]="['/goals', child.id]">
              <span><strong>{{ child.title }}</strong> <span class="muted">{{ child.ownerName ?? child.departmentName ?? '' }}</span></span>
              <span class="perf__progress">{{ child.progress }}% <gggs-progress-bar [value]="child.progress" /></span>
              <span class="muted">due {{ child.dueDate | date: 'd MMM y' }}</span>
              <span></span>
              <span><gggs-status-badge [status]="child.status" /></span>
            </a>
          }
        </section>
      }
      <section class="panel stack">
        <h2 class="perf__heading">Check-ins</h2>
        @for (checkIn of v.checkIns; track checkIn.id) {
          <div class="perf__checkin">
            <strong>{{ checkIn.checkInOn | date: 'd MMM y' }}</strong>
            <span>{{ checkIn.progress }}% · {{ humanize(checkIn.confidence) }}</span>
            <span class="muted">{{ checkIn.byName ?? '' }}</span>
            @if (checkIn.note) {
              <p class="perf__wide">{{ checkIn.note }}</p>
            }
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-history" title="No check-ins yet" />
        }
      </section>

      <gggs-goal-form [(visible)]="editOpen" [goal]="v.goal" [lockLevel]="true" (saved)="view.set($event)" />
      <gggs-form-dialog [(visible)]="checkInOpen" title="Check in" [busy]="busy()" [error]="formError()" (save)="checkIn(v)">
        @for (result of v.keyResults; track result.id) {
          <label class="field">
            <span class="field__label">{{ result.title }} ({{ result.metricType === 'Milestone' ? '1 when done' : 'target ' + result.targetValue + ' ' + (result.unit ?? '') }})</span>
            <input type="number" [value]="value(result.id)" (input)="setValue(result.id, +$any($event.target).value)" />
          </label>
        } @empty {
          <label class="field">
            <span class="field__label">Progress (%)</span>
            <input type="number" min="0" max="100" [value]="progress()" (input)="progress.set(+$any($event.target).value)" />
          </label>
        }
        <label class="field">
          <span class="field__label">Confidence</span>
          <select [value]="confidence()" (change)="confidence.set($any($event.target).value)">
            @for (option of confidences; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">What happened</span>
          <textarea rows="3" maxlength="2000" [value]="note()" (input)="note.set($any($event.target).value)"></textarea>
        </label>
      </gggs-form-dialog>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styleUrl: './performance.css',
})
export class GoalPage {
  private readonly api = inject(GoalsApi);

  readonly id = input.required<string>();

  protected readonly humanize = humanize;
  protected readonly confidences = CONFIDENCES;
  protected readonly view = signal<GoalView | null>(null);
  protected readonly editOpen = signal(false);
  protected readonly checkInOpen = signal(false);
  protected readonly values = signal<Readonly<Record<string, number>>>({});
  protected readonly progress = signal(0);
  protected readonly confidence = signal<Confidence>('OnTrack');
  protected readonly note = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.goal(this.id()).then(
        (view) => this.view.set(view),
        (error: unknown) => this.error.set(errorText(error, 'The goal could not be loaded.')),
      );
    });
  }

  protected value(id: string): number {
    return this.values()[id] ?? 0;
  }

  protected setValue(id: string, value: number): void {
    this.values.update((values) => ({ ...values, [id]: value }));
  }

  protected openCheckIn(view: GoalView): void {
    this.values.set(Object.fromEntries(view.keyResults.map((result) => [result.id, result.currentValue])));
    this.progress.set(view.goal.progress);
    this.confidence.set(view.goal.confidence ?? 'OnTrack');
    this.note.set('');
    this.formError.set(null);
    this.checkInOpen.set(true);
  }

  protected async checkIn(view: GoalView): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      this.view.set(
        await this.api.checkIn(this.id(), {
          progress: view.keyResults.length ? null : this.progress(),
          confidence: this.confidence(),
          note: this.note() || null,
          values: view.keyResults.map((result) => ({ keyResultId: result.id, value: this.value(result.id) })),
        }),
      );
      this.checkInOpen.set(false);
    } catch (error) {
      this.formError.set(errorText(error, 'The check-in could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async act(action: 'complete' | 'cancel' | 'reopen'): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.view.set(await this.api.act(this.id(), action));
    } catch (error) {
      this.error.set(errorText(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** My goals: what I am working towards, how far along, and what I align to. */
@Component({
  selector: 'gggs-my-goals-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, EmptyState, ProgressBar, GoalForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My goals" subtitle="Your goals, their key results and progress - they count towards your review.">
      <button type="button" class="btn btn--primary" (click)="formOpen.set(true)"><i class="pi pi-plus" aria-hidden="true"></i> New goal</button>
    </gggs-page-header>
    <p class="muted">Total weight {{ totalWeight() }}% of 100%.</p>
    <section class="panel stack">
      @for (goal of goals(); track goal.id) {
        <a class="perf__row" [routerLink]="['/goals', goal.id]">
          <span><strong>{{ goal.title }}</strong> <span class="muted">{{ goal.parentTitle ? 'aligned to ' + goal.parentTitle : '' }}</span></span>
          <span class="perf__progress">{{ goal.progress }}% <gggs-progress-bar [value]="goal.progress" /></span>
          <span class="muted">due {{ goal.dueDate | date: 'd MMM y' }} · weight {{ goal.weight }}%</span>
          <span class="muted">{{ goal.lastCheckInOn ? 'checked in ' + (goal.lastCheckInOn | date: 'd MMM') : 'no check-in yet' }}</span>
          <span><gggs-status-badge [status]="goal.status" /></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-bullseye" title="No goals yet" description="Set your first goal, aligned to a company or department goal." />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
    <gggs-goal-form [(visible)]="formOpen" level="Individual" [lockLevel]="true" [lockOwner]="true" (saved)="load()" />
  `,
  styleUrl: './performance.css',
})
export class MyGoalsPage {
  private readonly api = inject(GoalsApi);

  protected readonly goals = signal<readonly GoalItem[]>([]);
  protected readonly formOpen = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly totalWeight = computed(() => this.goals().filter((goal) => goal.status === 'Active' || goal.status === 'Draft').reduce((sum, goal) => sum + goal.weight, 0));

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    try {
      this.goals.set(await this.api.mine());
    } catch (error) {
      this.error.set(error instanceof ApiError && error.status === 422 ? 'Your account is not linked to an employee record.' : errorText(error, 'Your goals could not be loaded.'));
    }
  }
}
