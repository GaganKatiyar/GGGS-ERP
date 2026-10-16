import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { AwardItem, AwardStatus, AwardType, BandItem, CycleItem, GrantItem, OrgUnit, RevisionItem, RevisionStatus, RevisionType, TotalRewards } from '@gggs/shared/models';
import { AWARD_TYPES, REVISION_TYPES, humanize } from '@gggs/shared/models';
import { CompensationApi, MoneyPipe, TotalRewardsView } from '@gggs/shared/money';
import { OrgStructureApi } from '@gggs/shared/org-reference';
import { Drawer, EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const today = () => new Date().toISOString().slice(0, 10);

/** Salary bands (spec §23): minimum, midpoint and maximum by grade, with who sits where. */
@Component({
  selector: 'gggs-salary-bands-page',
  imports: [DatePipe, DecimalPipe, MoneyPipe, PageHeader, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Salary bands" subtitle="The pay range for each grade, and how people sit against it.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="edit(null)"><i class="pi pi-plus" aria-hidden="true"></i> New band</button>
      }
    </gggs-page-header>
    <section class="panel comp__scroll">
      <table class="comp__table">
        <thead>
          <tr>
            <th scope="col">Grade</th><th scope="col" class="comp__num">Minimum</th><th scope="col" class="comp__num">Midpoint</th><th scope="col" class="comp__num">Maximum</th>
            <th scope="col">From</th><th scope="col" class="comp__num">People</th><th scope="col" class="comp__num">Avg compa-ratio</th><th scope="col">Outside</th><th scope="col"><span class="muted">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          @for (band of bands(); track band.id) {
            <tr [class.muted]="!band.current">
              <th scope="row">{{ band.gradeName ?? '?' }} @if (!band.current) {<span class="muted">(history or future)</span>}</th>
              <td class="comp__num">{{ band.minimum | gggsMoney: band.currency }}</td>
              <td class="comp__num">{{ band.midpoint | gggsMoney: band.currency }}</td>
              <td class="comp__num">{{ band.maximum | gggsMoney: band.currency }}</td>
              <td>{{ band.effectiveFrom | date: 'd MMM y' }}</td>
              <td class="comp__num">{{ band.employees }}</td>
              <td class="comp__num">{{ band.averageCompaRatio !== undefined ? (band.averageCompaRatio | number: '1.2-2') : '—' }}</td>
              <td>{{ band.belowRange ? band.belowRange + ' below' : '' }}{{ band.belowRange && band.aboveRange ? ', ' : '' }}{{ band.aboveRange ? band.aboveRange + ' above' : '' }}</td>
              <td>
                @if (canManage) {
                  <button type="button" class="btn btn--link btn--sm" (click)="edit(band)">Edit</button>
                  <button type="button" class="btn btn--link btn--sm" (click)="remove(band)">Remove</button>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="9"><gggs-empty-state icon="pi pi-sliders-h" title="No bands yet" /></td></tr>
          }
        </tbody>
      </table>
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="dialogOpen" [title]="editing() ? 'Edit band' : 'New band'" [busy]="busy()" [error]="formError()" [saveDisabled]="!gradeId() || minimum() <= 0 || midpoint() < minimum() || maximum() < midpoint()" (save)="save()">
      <div class="comp__grid">
        <label class="field">
          <span class="field__label">Grade</span>
          <select [value]="gradeId()" [disabled]="!!editing()" (change)="gradeId.set($any($event.target).value)">
            <option value="">Choose a grade</option>
            @for (grade of grades(); track grade.id) {
              <option [value]="grade.id">{{ grade.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">From</span>
          <input type="date" [value]="effectiveFrom()" (change)="effectiveFrom.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Minimum (annual)</span>
          <input type="number" min="0" step="10000" [value]="minimum() || ''" (input)="minimum.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Midpoint</span>
          <input type="number" min="0" step="10000" [value]="midpoint() || ''" (input)="midpoint.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Maximum</span>
          <input type="number" min="0" step="10000" [value]="maximum() || ''" (input)="maximum.set(+$any($event.target).value)" />
        </label>
      </div>
      <label class="field">
        <span class="field__label">Notes</span>
        <input type="text" maxlength="500" [value]="notes()" (input)="notes.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './compensation.css',
})
export class SalaryBandsPage {
  private readonly api = inject(CompensationApi);
  private readonly org = inject(OrgStructureApi);

  protected readonly canManage = inject(PermissionStore).has('compensation.band.manage');
  protected readonly bands = signal<readonly BandItem[]>([]);
  protected readonly grades = signal<readonly OrgUnit[]>([]);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<BandItem | null>(null);
  protected readonly gradeId = signal('');
  protected readonly minimum = signal(0);
  protected readonly midpoint = signal(0);
  protected readonly maximum = signal(0);
  protected readonly effectiveFrom = signal(today());
  protected readonly notes = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async edit(band: BandItem | null): Promise<void> {
    if (!this.grades().length) {
      this.grades.set(await this.org.active('grades'));
    }

    this.editing.set(band);
    this.gradeId.set(band?.gradeId ?? '');
    this.minimum.set(band?.minimum ?? 0);
    this.midpoint.set(band?.midpoint ?? 0);
    this.maximum.set(band?.maximum ?? 0);
    this.effectiveFrom.set(band?.effectiveFrom ?? today());
    this.notes.set(band?.notes ?? '');
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.saveBand(this.editing()?.id ?? null, {
        gradeId: this.gradeId(),
        minimum: this.minimum(),
        midpoint: this.midpoint(),
        maximum: this.maximum(),
        effectiveFrom: this.effectiveFrom(),
        notes: this.notes().trim() || null,
      });
      this.dialogOpen.set(false);
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'The band could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(band: BandItem): Promise<void> {
    try {
      await this.api.deleteBand(band.id);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'The band could not be removed.'));
    }
  }

  private async load(): Promise<void> {
    try {
      this.bands.set(await this.api.bands());
    } catch (error) {
      this.error.set(message(error, 'Bands could not be loaded.'));
    }
  }
}

/** Review cycles and salary revisions (spec §23): budgets, proposals, approval and application to payroll. */
@Component({
  selector: 'gggs-revisions-page',
  imports: [DatePipe, DecimalPipe, FormsModule, MoneyPipe, PageHeader, StatusBadge, EmptyState, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Salary revisions" subtitle="Merit increases, promotions and corrections - approved, then applied to payroll.">
      @if (canManage) {
        <button type="button" class="btn" (click)="editCycle(null)">New cycle</button>
        <button type="button" class="btn btn--primary" (click)="openPropose()"><i class="pi pi-plus" aria-hidden="true"></i> Propose a revision</button>
      }
    </gggs-page-header>

    @if (cycles().length) {
      <section class="panel stack">
        <h2 class="comp__heading">Review cycles</h2>
        @for (cycle of cycles(); track cycle.id) {
          <div class="comp__row">
            <span><strong>{{ cycle.name }}</strong><br /><span class="muted">effective {{ cycle.effectiveDate | date: 'd MMM y' }} · budget {{ cycle.budgetPercent }}%</span></span>
            <span>{{ cycle.committed | gggsMoney }} of {{ cycle.budget | gggsMoney }} committed</span>
            <span class="muted">{{ cycle.revisions }} revision(s) · avg {{ cycle.averageIncreasePercent | number: '1.0-1' }}%{{ cycle.proposed > cycle.budget ? ' · proposals over budget' : '' }}</span>
            <span class="toolbar">
              <gggs-status-badge [status]="cycle.status" />
              @if (canManage && cycle.status === 'Planning') {
                <button type="button" class="btn btn--link btn--sm" (click)="setCycle(cycle, 'open')">Open</button>
              }
              @if (canManage && cycle.status === 'Open') {
                <button type="button" class="btn btn--link btn--sm" (click)="setCycle(cycle, 'close')">Close</button>
              }
              <button type="button" class="btn btn--link btn--sm" (click)="cycleFilter.set(cycle.id); load()">Show</button>
            </span>
          </div>
        }
      </section>
    }

    <section class="panel toolbar comp__filters">
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">Any</option>
          <option value="Draft">Draft</option>
          <option value="PendingApproval">Pending approval</option>
          <option value="Approved">Approved (not applied)</option>
          <option value="Applied">Applied</option>
          <option value="Rejected">Rejected</option>
        </select>
      </label>
      @if (cycleFilter()) {
        <button type="button" class="btn btn--sm" (click)="cycleFilter.set(''); load()">All cycles</button>
      }
    </section>
    <section class="panel stack">
      @for (revision of revisions(); track revision.id) {
        <div class="comp__row">
          <span><strong>{{ revision.employeeName ?? revision.employeeCode }}</strong><br /><span class="muted">{{ humanize(revision.type) }}{{ revision.cycleName ? ' · ' + revision.cycleName : '' }}{{ revision.newGradeName ? ' · to ' + revision.newGradeName : '' }}</span></span>
          <span>{{ revision.currentCtc | gggsMoney: revision.currency }} → <strong>{{ revision.proposedCtc | gggsMoney: revision.currency }}</strong> ({{ revision.increasePercent > 0 ? '+' : '' }}{{ revision.increasePercent }}%)</span>
          <span class="muted">
            from {{ revision.effectiveDate | date: 'd MMM y' }}
            @if (revision.compaRatioAfter !== undefined) {
              · compa {{ revision.compaRatioBefore ?? 0 | number: '1.2-2' }} → {{ revision.compaRatioAfter | number: '1.2-2' }}
            }
            @if (revision.applyError) {
              <br /><span class="comp__danger">{{ revision.applyError }}</span>
            }
          </span>
          <span class="toolbar">
            <gggs-status-badge [status]="revision.status" />
            @if (canManage && revision.status === 'Draft') {
              <button type="button" class="btn btn--link btn--sm" (click)="action(revision, 'submit')">Submit</button>
            }
            @if (canManage && revision.status === 'Approved' && revision.applyError) {
              <button type="button" class="btn btn--link btn--sm" (click)="openReschedule(revision)">Move date</button>
            }
            @if (canManage && (revision.status === 'Draft' || revision.status === 'PendingApproval' || revision.status === 'Approved')) {
              <button type="button" class="btn btn--link btn--sm" (click)="action(revision, 'cancel')">Cancel</button>
            }
          </span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-arrow-up-right" title="No revisions here" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="proposeOpen" title="Propose a revision" saveLabel="Submit for approval" [busy]="busy()" [error]="formError()" [saveDisabled]="!employeeId() || proposedCtc() <= 0 || !justification().trim()" (save)="propose()">
      <label class="field">
        <span class="field__label">Employee</span>
        <gggs-employee-select ariaLabel="Employee" [ngModel]="employeeId()" (ngModelChange)="employeeId.set($event)" />
      </label>
      <div class="comp__grid">
        <label class="field">
          <span class="field__label">Type</span>
          <select [value]="type()" (change)="type.set($any($event.target).value)">
            @for (option of types; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">New annual CTC</span>
          <input type="number" min="0" step="10000" [value]="proposedCtc() || ''" (input)="proposedCtc.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Effective from</span>
          <input type="date" [value]="effectiveDate()" (change)="effectiveDate.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Review cycle</span>
          <select [value]="cycleId()" (change)="cycleId.set($any($event.target).value)">
            <option value="">None</option>
            @for (cycle of openCycles(); track cycle.id) {
              <option [value]="cycle.id">{{ cycle.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Performance rating</span>
          <input type="text" maxlength="50" [value]="rating()" (input)="rating.set($any($event.target).value)" />
        </label>
        @if (type() === 'Promotion') {
          <label class="field">
            <span class="field__label">New designation</span>
            <input type="text" maxlength="200" [value]="designation()" (input)="designation.set($any($event.target).value)" />
          </label>
        }
      </div>
      <label class="field">
        <span class="field__label">Why</span>
        <textarea rows="3" maxlength="2000" [value]="justification()" (input)="justification.set($any($event.target).value)"></textarea>
      </label>
      <p class="muted">Approved revisions become the person's new salary in payroll from the effective date.</p>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="cycleOpen" title="Review cycle" [busy]="busy()" [error]="formError()" [saveDisabled]="!cycleName().trim() || !cycleDate()" (save)="saveCycle()">
      <div class="comp__grid">
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="200" placeholder="Annual review 2027" [value]="cycleName()" (input)="cycleName.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Effective date</span>
          <input type="date" [value]="cycleDate()" (change)="cycleDate.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Budget (% of current pay)</span>
          <input type="number" min="0" max="100" step="0.5" [value]="budget()" (input)="budget.set(+$any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="rescheduleOpen" title="Move the effective date" saveLabel="Apply again" [busy]="busy()" [error]="formError()" [saveDisabled]="!effectiveDate()" (save)="reschedule()">
      <p class="muted">Payroll has already been finalized for the original date. Choose a later date and pay the difference as arrears.</p>
      <label class="field">
        <span class="field__label">Effective from</span>
        <input type="date" [value]="effectiveDate()" (change)="effectiveDate.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './compensation.css',
})
export class RevisionsPage {
  private readonly api = inject(CompensationApi);

  protected readonly canManage = inject(PermissionStore).has('compensation.revision.manage');
  protected readonly types = REVISION_TYPES;
  protected readonly humanize = humanize;
  protected readonly cycles = signal<readonly CycleItem[]>([]);
  protected readonly revisions = signal<readonly RevisionItem[]>([]);
  protected readonly status = signal<RevisionStatus | ''>('');
  protected readonly cycleFilter = signal('');
  protected readonly proposeOpen = signal(false);
  protected readonly cycleOpen = signal(false);
  protected readonly rescheduleOpen = signal(false);
  protected readonly target = signal<RevisionItem | null>(null);
  protected readonly employeeId = signal<string | null>(null);
  protected readonly type = signal<RevisionType>('Merit');
  protected readonly proposedCtc = signal(0);
  protected readonly effectiveDate = signal(today());
  protected readonly cycleId = signal('');
  protected readonly rating = signal('');
  protected readonly designation = signal('');
  protected readonly justification = signal('');
  protected readonly cycleName = signal('');
  protected readonly cycleDate = signal(today());
  protected readonly budget = signal(8);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly openCycles = computed(() => this.cycles().filter((cycle) => cycle.status !== 'Closed'));

  constructor() {
    void this.load();
  }

  protected openPropose(): void {
    this.employeeId.set(null);
    this.type.set('Merit');
    this.proposedCtc.set(0);
    this.effectiveDate.set(this.openCycles()[0]?.effectiveDate ?? today());
    this.cycleId.set(this.openCycles()[0]?.id ?? '');
    this.rating.set('');
    this.designation.set('');
    this.justification.set('');
    this.formError.set(null);
    this.proposeOpen.set(true);
  }

  protected async propose(): Promise<void> {
    const employeeId = this.employeeId();
    if (!employeeId) {
      return;
    }

    await this.dialog(async () => {
      await this.api.propose(
        employeeId,
        {
          cycleId: this.cycleId() || null,
          type: this.type(),
          proposedCtc: this.proposedCtc(),
          effectiveDate: this.effectiveDate(),
          newDesignation: this.designation().trim() || null,
          justification: this.justification().trim(),
          performanceRating: this.rating().trim() || null,
        },
        true,
      );
      this.proposeOpen.set(false);
    });
  }

  protected editCycle(cycle: CycleItem | null): void {
    this.cycleName.set(cycle?.name ?? '');
    this.cycleDate.set(cycle?.effectiveDate ?? today());
    this.budget.set(cycle?.budgetPercent ?? 8);
    this.formError.set(null);
    this.cycleOpen.set(true);
  }

  protected async saveCycle(): Promise<void> {
    await this.dialog(async () => {
      await this.api.saveCycle(null, { name: this.cycleName().trim(), effectiveDate: this.cycleDate(), budgetPercent: this.budget() });
      this.cycleOpen.set(false);
    });
  }

  protected async setCycle(cycle: CycleItem, action: 'open' | 'close'): Promise<void> {
    await this.act(() => this.api.setCycle(cycle.id, action));
  }

  protected async action(revision: RevisionItem, action: 'submit' | 'cancel'): Promise<void> {
    await this.act(() => this.api.revisionAction(revision.id, action));
  }

  protected openReschedule(revision: RevisionItem): void {
    this.target.set(revision);
    this.effectiveDate.set(today());
    this.formError.set(null);
    this.rescheduleOpen.set(true);
  }

  protected async reschedule(): Promise<void> {
    const revision = this.target();
    if (revision) {
      await this.dialog(async () => {
        await this.api.reschedule(revision.id, this.effectiveDate());
        this.rescheduleOpen.set(false);
      });
    }
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      const [cycles, revisions] = await Promise.all([this.api.cycles(), this.api.revisions({ status: this.status(), cycleId: this.cycleFilter() || undefined })]);
      this.cycles.set(cycles);
      this.revisions.set(revisions);
    } catch (error) {
      this.error.set(message(error, 'Revisions could not be loaded.'));
    }
  }

  private async dialog(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await action();
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'That could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async act(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Variable pay (spec §23, §94): bonuses, commission and incentives, approved and paid through payroll. */
@Component({
  selector: 'gggs-variable-pay-page',
  imports: [DatePipe, FormsModule, MoneyPipe, PageHeader, StatusBadge, EmptyState, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Variable pay" subtitle="Bonuses, commission and incentives. Approved awards are paid in the chosen month's payroll.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="openNew()"><i class="pi pi-plus" aria-hidden="true"></i> New award</button>
      }
    </gggs-page-header>
    <section class="panel toolbar comp__filters">
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">Any</option>
          <option value="PendingApproval">Pending approval</option>
          <option value="Approved">Approved (with payroll)</option>
          <option value="Paid">Paid</option>
          <option value="Rejected">Rejected</option>
        </select>
      </label>
      <span class="comp__spacer"></span>
      <strong>{{ total() | gggsMoney }}</strong>
    </section>
    <section class="panel stack">
      @for (award of awards(); track award.id) {
        <div class="comp__row">
          <span><strong>{{ award.employeeName ?? award.employeeCode }}</strong> <span class="muted">{{ award.employeeCode }}</span></span>
          <span><strong>{{ award.amount | gggsMoney: award.currency }}</strong> · {{ humanize(award.type) }}</span>
          <span class="muted">{{ award.payoutMonth | date: 'MMM y' }} · {{ award.reason }}{{ award.proposedByName ? ' · by ' + award.proposedByName : '' }}</span>
          <span class="toolbar">
            <gggs-status-badge [status]="award.status" />
            @if (canManage && (award.status === 'PendingApproval' || award.status === 'Approved')) {
              <button type="button" class="btn btn--link btn--sm" (click)="cancel(award)">Cancel</button>
            }
          </span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-gift" title="No awards here" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="dialogOpen" title="New award" saveLabel="Submit" [busy]="busy()" [error]="formError()" [saveDisabled]="!employeeId() || amount() <= 0 || !reason().trim()" (save)="save()">
      <label class="field">
        <span class="field__label">Employee</span>
        <gggs-employee-select ariaLabel="Employee" [ngModel]="employeeId()" (ngModelChange)="employeeId.set($event)" />
      </label>
      <div class="comp__grid">
        <label class="field">
          <span class="field__label">Type</span>
          <select [value]="type()" (change)="type.set($any($event.target).value)">
            @for (option of types; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Amount</span>
          <input type="number" min="0" step="1000" [value]="amount() || ''" (input)="amount.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Pay in</span>
          <input type="month" [value]="month()" (change)="month.set($any($event.target).value)" />
        </label>
      </div>
      <label class="field">
        <span class="field__label">Reason</span>
        <input type="text" maxlength="500" [value]="reason()" (input)="reason.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './compensation.css',
})
export class VariablePayPage {
  private readonly api = inject(CompensationApi);

  protected readonly canManage = inject(PermissionStore).has('compensation.variable-pay.manage');
  protected readonly types = AWARD_TYPES;
  protected readonly humanize = humanize;
  protected readonly status = signal<AwardStatus | ''>('');
  protected readonly awards = signal<readonly AwardItem[]>([]);
  protected readonly dialogOpen = signal(false);
  protected readonly employeeId = signal<string | null>(null);
  protected readonly type = signal<AwardType>('Bonus');
  protected readonly amount = signal(0);
  protected readonly month = signal(today().slice(0, 7));
  protected readonly reason = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly total = computed(() => this.awards().filter((award) => award.status !== 'Rejected' && award.status !== 'Cancelled').reduce((sum, award) => sum + award.amount, 0));

  constructor() {
    void this.load();
  }

  protected openNew(): void {
    this.employeeId.set(null);
    this.type.set('Bonus');
    this.amount.set(0);
    this.month.set(today().slice(0, 7));
    this.reason.set('');
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected async save(): Promise<void> {
    const employeeId = this.employeeId();
    if (!employeeId) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.proposeAward({ employeeId, type: this.type(), amount: this.amount(), payoutMonth: `${this.month()}-01`, reason: this.reason().trim() });
      this.dialogOpen.set(false);
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'The award could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(award: AwardItem): Promise<void> {
    try {
      await this.api.cancelAward(award.id);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'The award could not be cancelled.'));
    }
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.awards.set(await this.api.awards(this.status()));
    } catch (error) {
      this.error.set(message(error, 'Awards could not be loaded.'));
    }
  }
}

/** Equity grants (spec §23 "ESOP metadata"): units, strike price and vesting with a cliff. */
@Component({
  selector: 'gggs-equity-page',
  imports: [DatePipe, DecimalPipe, FormsModule, MoneyPipe, PageHeader, StatusBadge, EmptyState, FormDialog, Drawer, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Equity" subtitle="Stock options and units granted, and how they vest.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="openNew()"><i class="pi pi-plus" aria-hidden="true"></i> New grant</button>
      }
    </gggs-page-header>
    <section class="panel stack">
      @for (grant of grants(); track grant.id) {
        <button type="button" class="comp__row comp__row--button" (click)="selected.set(grant); drawerOpen.set(true)">
          <span><strong>{{ grant.employeeName }}</strong><br /><span class="muted">{{ grant.planName }} · granted {{ grant.grantDate | date: 'd MMM y' }}</span></span>
          <span>{{ grant.vestedUnits | number: '1.0-2' }} / {{ grant.units | number: '1.0-2' }} vested{{ grant.exercisedUnits ? ' · ' + (grant.exercisedUnits | number: '1.0-2') + ' exercised' : '' }}</span>
          <span class="muted">{{ grant.vestedValue !== undefined ? (grant.vestedValue | gggsMoney: grant.currency) + ' vested value' : '' }}{{ grant.nextVestingDate ? ' · next ' + (grant.nextVestingDate | date: 'd MMM y') : '' }}</span>
          <gggs-status-badge [status]="grant.status" />
        </button>
      } @empty {
        <gggs-empty-state icon="pi pi-chart-line" title="No grants yet" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-drawer [(visible)]="drawerOpen" [title]="selected()?.planName ?? 'Grant'">
      @if (selected(); as g) {
        <div class="stack">
          <p>{{ g.employeeName }} · {{ g.units | number: '1.0-2' }} units at {{ g.strikePrice | gggsMoney: g.currency: 2 }}{{ g.fairValuePerUnit ? ', valued at ' + (g.fairValuePerUnit | gggsMoney: g.currency: 2) : '' }}</p>
          <p class="muted">Vests over {{ g.vestingMonths }} months from {{ g.vestingStart | date: 'd MMM y' }}, {{ g.cliffMonths }}-month cliff, every {{ g.vestingFrequencyMonths }} month(s).</p>
          <table class="comp__table">
            <thead><tr><th scope="col">Vests on</th><th scope="col" class="comp__num">Units</th><th scope="col" class="comp__num">Total vested</th></tr></thead>
            <tbody>
              @for (vest of g.schedule; track vest.date) {
                <tr [class.muted]="vest.date > todayIso"><td>{{ vest.date | date: 'd MMM y' }}</td><td class="comp__num">{{ vest.units | number: '1.0-2' }}</td><td class="comp__num">{{ vest.cumulative | number: '1.0-2' }}</td></tr>
              }
            </tbody>
          </table>
          @if (canManage && g.status === 'Active') {
            <div class="toolbar">
              <label class="field">
                <span class="field__label">Exercise units</span>
                <input type="number" min="0" [value]="exerciseUnits() || ''" (input)="exerciseUnits.set(+$any($event.target).value)" />
              </label>
              <button type="button" class="btn" [disabled]="busy() || exerciseUnits() <= 0" (click)="exercise(g)">Record exercise</button>
              <button type="button" class="btn btn--link" [disabled]="busy()" (click)="cancel(g)">Cancel unvested</button>
            </div>
          }
        </div>
      }
    </gggs-drawer>

    <gggs-form-dialog [(visible)]="dialogOpen" title="New grant" [busy]="busy()" [error]="formError()" [saveDisabled]="!employeeId() || !planName().trim() || units() <= 0" (save)="save()">
      <label class="field">
        <span class="field__label">Employee</span>
        <gggs-employee-select ariaLabel="Employee" [ngModel]="employeeId()" (ngModelChange)="employeeId.set($event)" />
      </label>
      <div class="comp__grid">
        <label class="field">
          <span class="field__label">Plan</span>
          <input type="text" maxlength="200" placeholder="ESOP 2026" [value]="planName()" (input)="planName.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Granted on</span>
          <input type="date" [value]="grantDate()" (change)="grantDate.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Units</span>
          <input type="number" min="0" [value]="units() || ''" (input)="units.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Strike price</span>
          <input type="number" min="0" step="0.01" [value]="strike()" (input)="strike.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Fair value a unit</span>
          <input type="number" min="0" step="0.01" [value]="fairValue() ?? ''" (input)="fairValue.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Vesting months</span>
          <input type="number" min="1" max="120" [value]="vestingMonths()" (input)="vestingMonths.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Cliff months</span>
          <input type="number" min="0" max="120" [value]="cliffMonths()" (input)="cliffMonths.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Vests every</span>
          <select [value]="frequency()" (change)="frequency.set(+$any($event.target).value)">
            <option value="1">Month</option>
            <option value="3">Quarter</option>
            <option value="6">Half year</option>
            <option value="12">Year</option>
          </select>
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styleUrl: './compensation.css',
})
export class EquityPage {
  private readonly api = inject(CompensationApi);

  protected readonly canManage = inject(PermissionStore).has('compensation.equity.manage');
  protected readonly todayIso = today();
  protected readonly grants = signal<readonly GrantItem[]>([]);
  protected readonly selected = signal<GrantItem | null>(null);
  protected readonly drawerOpen = signal(false);
  protected readonly dialogOpen = signal(false);
  protected readonly employeeId = signal<string | null>(null);
  protected readonly planName = signal('');
  protected readonly grantDate = signal(today());
  protected readonly units = signal(0);
  protected readonly strike = signal(0);
  protected readonly fairValue = signal<number | null>(null);
  protected readonly vestingMonths = signal(48);
  protected readonly cliffMonths = signal(12);
  protected readonly frequency = signal(1);
  protected readonly exerciseUnits = signal(0);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected openNew(): void {
    this.employeeId.set(null);
    this.planName.set('');
    this.grantDate.set(today());
    this.units.set(0);
    this.strike.set(0);
    this.fairValue.set(null);
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected async save(): Promise<void> {
    const employeeId = this.employeeId();
    if (!employeeId) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.saveGrant(null, employeeId, {
        planName: this.planName().trim(),
        grantDate: this.grantDate(),
        units: this.units(),
        strikePrice: this.strike(),
        fairValuePerUnit: this.fairValue(),
        vestingMonths: this.vestingMonths(),
        cliffMonths: this.cliffMonths(),
        vestingFrequencyMonths: this.frequency(),
      });
      this.dialogOpen.set(false);
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'The grant could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async exercise(grant: GrantItem): Promise<void> {
    await this.act(async () => this.selected.set(await this.api.exercise(grant.id, this.exerciseUnits())));
  }

  protected async cancel(grant: GrantItem): Promise<void> {
    await this.act(async () => this.selected.set(await this.api.cancelGrant(grant.id)));
  }

  private async act(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      this.exerciseUnits.set(0);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.grants.set(await this.api.grants());
    } catch (error) {
      this.error.set(message(error, 'Grants could not be loaded.'));
    }
  }
}

/** My total rewards (spec §23): what the company pays me, all of it. */
@Component({
  selector: 'gggs-my-compensation-page',
  imports: [PageHeader, Skeleton, TotalRewardsView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My compensation" subtitle="Your salary, where it sits in your band, variable pay, benefits and equity." />
    @if (rewards(); as r) {
      <section class="panel">
        <gggs-total-rewards [rewards]="r" />
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
})
export class MyCompensationPage {
  protected readonly rewards = signal<TotalRewards | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    inject(CompensationApi)
      .myRewards()
      .then(
        (rewards) => this.rewards.set(rewards),
        (error: unknown) => this.error.set(message(error, 'Your compensation could not be loaded.')),
      );
  }
}
