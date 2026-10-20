import { DatePipe, DecimalPipe, KeyValuePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { SeparationApi } from '@gggs/shared/experience';
import type {
  AlumniItem,
  ClearanceArea,
  ClearanceItem,
  ClearanceStatus,
  ExitInterviewInput,
  SeparationCaseItem,
  SeparationCaseView,
  SeparationDashboard,
  SeparationStatus,
  SeparationType,
} from '@gggs/shared/models';
import { CLEARANCE_AREAS, EXIT_REASONS, SEPARATION_TYPES, humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const today = () => new Date().toISOString().slice(0, 10);
const inDays = (days: number) => new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10);
const blankInterview = (): ExitInterviewInput => ({ primaryReason: EXIT_REASONS[0], overallRating: 4, wouldRecommend: true, wouldReturn: true, whatWentWell: null, whatToImprove: null });

/** The exit interview form, used by HR and by the leaver. */
@Component({
  selector: 'gggs-exit-interview-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="xp__grid">
      <label class="field">
        <span class="field__label">Main reason for leaving</span>
        <select [value]="value().primaryReason" (change)="patch({ primaryReason: $any($event.target).value })">
          @for (reason of reasons; track reason) {
            <option [value]="reason">{{ reason }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Overall experience (1-5)</span>
        <input type="number" min="1" max="5" [value]="value().overallRating" (input)="patch({ overallRating: +$any($event.target).value })" />
      </label>
      <label class="xp__flag"><input type="checkbox" [checked]="value().wouldRecommend" (change)="patch({ wouldRecommend: $any($event.target).checked })" /> Would recommend us</label>
      <label class="xp__flag"><input type="checkbox" [checked]="value().wouldReturn" (change)="patch({ wouldReturn: $any($event.target).checked })" /> Would come back</label>
      <label class="field xp__wide">
        <span class="field__label">What went well</span>
        <textarea rows="2" maxlength="2000" [value]="value().whatWentWell ?? ''" (input)="patch({ whatWentWell: $any($event.target).value || null })"></textarea>
      </label>
      <label class="field xp__wide">
        <span class="field__label">What we should improve</span>
        <textarea rows="2" maxlength="2000" [value]="value().whatToImprove ?? ''" (input)="patch({ whatToImprove: $any($event.target).value || null })"></textarea>
      </label>
    </div>
  `,
})
export class ExitInterviewForm {
  /** Edited in place; the parent sends it when saving. */
  readonly value = input.required<ExitInterviewInput>();

  protected readonly reasons = EXIT_REASONS;

  protected patch(change: Partial<ExitInterviewInput>): void {
    Object.assign(this.value(), change);
  }
}

/** Separations in progress, those leaving soon, attrition and the alumni network. */
@Component({
  selector: 'gggs-separation-page',
  imports: [FormsModule, RouterLink, DatePipe, KeyValuePipe, PageHeader, KpiCard, StatusBadge, EmptyState, FormDialog, Skeleton, Tabs, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Separation" subtitle="Resignations and exits through notice, clearance, settlement and letters.">
      <a class="btn" routerLink="/separation/clearance">My clearance</a>
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="openStart()">Start a separation</button>
      }
    </gggs-page-header>
    @if (dashboard(); as d) {
      <div class="xp__kpis">
        <gggs-kpi-card label="In progress" [value]="d.inProgress" />
        <gggs-kpi-card label="Awaiting approval" [value]="d.pendingApproval" />
        <gggs-kpi-card label="Leaving this month" [value]="d.leavingThisMonth" />
        <gggs-kpi-card label="Clearance items open" [value]="d.clearancePending" />
        <gggs-kpi-card label="Settlements to pay" [value]="d.settlementsDue" />
        <gggs-kpi-card label="Attrition (12 months)" [value]="d.attritionLast12Months + '%'" />
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="3" />
    }
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />
    <section class="panel stack">
      @if (tab() === 'cases') {
        <div class="toolbar xp__filters">
          <label class="field">
            <span class="field__label">Status</span>
            <select [ngModel]="status()" (ngModelChange)="status.set($event); load()">
              <option value="">Any</option>
              @for (option of statuses; track option) {
                <option [value]="option">{{ humanize(option) }}</option>
              }
            </select>
          </label>
        </div>
        @for (item of cases(); track item.id) {
          <a class="xp__row" [routerLink]="['/separation/cases', item.id]">
            <span><strong>{{ item.employeeName }}</strong> <span class="muted">{{ item.employeeCode }} · {{ item.designation ?? '' }}</span></span>
            <span>{{ label(item.type) }}</span>
            <span>last day {{ item.lastWorkingDay | date: 'd MMM y' }}{{ item.shortfallDays && !item.shortfallWaived ? ' · ' + item.shortfallDays + ' days short' : '' }}</span>
            <span class="muted">clearance {{ item.clearanceTotal - item.clearancePending }}/{{ item.clearanceTotal }}{{ item.settlementStatus ? ' · settlement ' + item.settlementStatus.toLowerCase() : '' }}</span>
            <gggs-status-badge [status]="item.status" />
          </a>
        } @empty {
          <gggs-empty-state icon="pi pi-sign-out" title="No separations" />
        }
        @if (dashboard(); as d) {
          <div class="xp__grid">
            <div class="stack">
              <h2 class="xp__heading">By type (12 months)</h2>
              @for (entry of d.byType | keyvalue; track entry.key) {
                <span><strong>{{ entry.value }}</strong> {{ entry.key }}</span>
              }
            </div>
            <div class="stack">
              <h2 class="xp__heading">Top exit reasons</h2>
              @for (entry of d.topExitReasons | keyvalue; track entry.key) {
                <span><strong>{{ entry.value }}</strong> {{ entry.key }}</span>
              } @empty {
                <span class="muted">No exit interviews yet.</span>
              }
            </div>
          </div>
        }
      } @else {
        <div class="toolbar xp__filters">
          <label class="field">
            <span class="field__label">Search</span>
            <input type="search" [ngModel]="search()" (ngModelChange)="search.set($event)" (keyup.enter)="loadAlumni()" />
          </label>
        </div>
        @for (person of alumni(); track person.id) {
          <div class="xp__row">
            <span><strong>{{ person.name }}</strong> <span class="muted">{{ person.lastDesignation ?? '' }}</span></span>
            <span>{{ label(person.separationType) }} · left {{ person.exitDate | date: 'MMM y' }}</span>
            <span class="muted">{{ person.email ?? '' }}</span>
            <span>{{ person.optedIn ? 'In the network' : 'Opted out' }}</span>
            <gggs-status-badge [status]="person.eligibleForRehire ? 'Eligible' : 'Not eligible'" />
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-users" title="No alumni yet" />
        }
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>

    <gggs-form-dialog [(visible)]="startOpen" title="Start a separation" saveLabel="Start" [busy]="busy()" [error]="formError()" [saveDisabled]="!employeeId() || !reason().trim()" (save)="start()">
      <div class="xp__grid">
        <label class="field xp__wide"><span class="field__label">Employee</span><gggs-employee-select [ngModel]="employeeId()" (ngModelChange)="employeeId.set($event)" /></label>
        <label class="field">
          <span class="field__label">Type</span>
          <select [value]="type()" (change)="type.set($any($event.target).value)">
            @for (option of types; track option) {
              <option [value]="option">{{ label(option) }}</option>
            }
          </select>
        </label>
        <label class="field"><span class="field__label">Notice given on</span><input type="date" [value]="noticeDate()" (change)="noticeDate.set($any($event.target).value)" /></label>
        <label class="field"><span class="field__label">Last working day</span><input type="date" [value]="lastDay()" (change)="lastDay.set($any($event.target).value)" /></label>
        <label class="field xp__wide"><span class="field__label">Reason</span><textarea rows="2" maxlength="1000" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea></label>
      </div>
    </gggs-form-dialog>
  `,
})
export class SeparationPage {
  private readonly api = inject(SeparationApi);
  private readonly permissions = inject(PermissionStore);

  protected readonly canManage = this.permissions.has('separation.case.manage');
  protected readonly humanize = humanize;
  protected readonly statuses: readonly SeparationStatus[] = ['PendingApproval', 'Approved', 'Completed', 'Withdrawn', 'Rejected'];
  protected readonly types = SEPARATION_TYPES.filter((type) => type !== 'Resignation');
  protected readonly tabs: readonly TabItem[] = [
    { key: 'cases', label: 'Cases' },
    { key: 'alumni', label: 'Alumni', disabled: !this.permissions.has('separation.alumni.view') },
  ];
  protected readonly tab = signal('cases');
  protected readonly dashboard = signal<SeparationDashboard | null>(null);
  protected readonly cases = signal<readonly SeparationCaseItem[]>([]);
  protected readonly alumni = signal<readonly AlumniItem[]>([]);
  protected readonly status = signal<SeparationStatus | ''>('');
  protected readonly search = signal('');
  protected readonly startOpen = signal(false);
  protected readonly employeeId = signal<string | null>(null);
  protected readonly type = signal<SeparationType>('Termination');
  protected readonly noticeDate = signal(today());
  protected readonly lastDay = signal(inDays(30));
  protected readonly reason = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.dashboard().then((d) => this.dashboard.set(d), (error: unknown) => this.error.set(message(error, 'The overview could not be loaded.')));
    this.load();
    if (this.permissions.has('separation.alumni.view')) {
      this.loadAlumni();
    }
  }

  protected label(type: SeparationType): string {
    return type === 'ContractEnd' ? 'End of contract' : type === 'Mutual' ? 'Mutual separation' : type;
  }

  protected load(): void {
    this.api.cases(this.status() || null).then((cases) => this.cases.set(cases), (error: unknown) => this.error.set(message(error, 'Cases could not be loaded.')));
  }

  protected loadAlumni(): void {
    this.api.alumni(this.search() || null).then((alumni) => this.alumni.set(alumni), () => undefined);
  }

  protected openStart(): void {
    this.employeeId.set(null);
    this.type.set('Termination');
    this.noticeDate.set(today());
    this.lastDay.set(inDays(30));
    this.reason.set('');
    this.formError.set(null);
    this.startOpen.set(true);
  }

  protected async start(): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.initiate({ employeeId: this.employeeId() ?? '', type: this.type(), reason: this.reason(), noticeDate: this.noticeDate(), lastWorkingDay: this.lastDay(), noticePeriodDays: null });
      this.startOpen.set(false);
      this.load();
    } catch (error) {
      this.formError.set(message(error, 'The separation could not be started.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** One separation: terms, clearance, exit interview, the full and final settlement and letters. */
@Component({
  selector: 'gggs-separation-case-page',
  imports: [FormsModule, DatePipe, DecimalPipe, RouterLink, PageHeader, StatusBadge, FormDialog, Skeleton, EmployeeSelect, ExitInterviewForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="v.case.employeeName ?? 'Separation'" [subtitle]="label(v.case.type) + ' · last day ' + (v.case.lastWorkingDay | date: 'd MMM y')">
        <a class="btn" routerLink="/separation">All separations</a>
        @if (canManage && (v.case.status === 'Approved' || v.case.status === 'PendingApproval')) {
          <button type="button" class="btn" (click)="openTerms(v)">Terms</button>
          <button type="button" class="btn btn--link" (click)="withdraw()">Withdraw</button>
        }
        @if (canManage && (v.case.status === 'Approved' || v.case.status === 'Completed')) {
          <button type="button" class="btn" (click)="letters()">{{ v.case.relievingLetterId ? 'Reissue letters' : 'Issue letters' }}</button>
        }
      </gggs-page-header>
      <section class="panel xp__grid">
        <div><span class="muted">Status</span><br /><gggs-status-badge [status]="v.case.status" /></div>
        <div><span class="muted">Notice given</span><br />{{ v.case.noticeDate | date: 'd MMM y' }} ({{ v.case.noticePeriodDays }} days due)</div>
        <div><span class="muted">Asked for</span><br />{{ v.case.requestedLastDay | date: 'd MMM y' }}</div>
        <div><span class="muted">Shortfall</span><br />{{ v.case.shortfallDays }} days{{ v.case.shortfallWaived ? ' (waived)' : '' }}</div>
        <div><span class="muted">Rehire</span><br />{{ v.case.eligibleForRehire ? 'Eligible' : 'Not eligible' }}</div>
        <div><span class="muted">Letters</span><br />{{ v.case.relievingLetterId ? 'Relieving' + (v.case.experienceLetterId ? ' and experience' : '') + ' issued' : 'Not issued' }}</div>
        <div class="xp__wide"><span class="muted">Reason</span><br />{{ v.case.reason }}</div>
      </section>

      <section class="panel stack">
        <div class="toolbar">
          <h2 class="xp__heading">Clearance</h2>
          @if (canManage && v.case.status === 'Approved') {
            <button type="button" class="btn btn--sm" (click)="clearanceOpen.set(true)">Add item</button>
          }
        </div>
        @for (item of v.clearance; track item.id) {
          <div class="xp__row">
            <span><strong>{{ item.title }}</strong> <span class="muted">{{ areaLabel(item.area) }}</span></span>
            <span>{{ item.ownerName ?? 'Clearance team' }}</span>
            <span>{{ item.recoveryAmount ? 'recover ' + (item.recoveryAmount | number: '1.0-0') : '' }}{{ item.note ? ' · ' + item.note : '' }}</span>
            <span class="toolbar">
              @if (item.canResolve && v.case.status !== 'Withdrawn') {
                @if (item.status === 'Pending') {
                  <button type="button" class="btn btn--sm" (click)="resolve(item, 'Cleared')">Clear</button>
                  <button type="button" class="btn btn--link btn--sm" (click)="resolve(item, 'Waived')">Waive</button>
                } @else {
                  <button type="button" class="btn btn--link btn--sm" (click)="resolve(item, 'Pending')">Undo</button>
                }
              }
            </span>
            <gggs-status-badge [status]="item.status" />
          </div>
        }
      </section>

      <section class="panel stack">
        <div class="toolbar">
          <h2 class="xp__heading">Exit interview</h2>
          @if (canManage) {
            <button type="button" class="btn btn--sm" (click)="openInterview(v)">{{ v.interview ? 'Edit' : 'Record' }}</button>
          }
        </div>
        @if (v.interview; as i) {
          <p><strong>{{ i.primaryReason }}</strong> · {{ i.overallRating }}/5 · {{ i.wouldRecommend ? 'would recommend' : 'would not recommend' }} · {{ i.wouldReturn ? 'would return' : 'would not return' }}</p>
          @if (i.whatWentWell) {
            <p>Went well: {{ i.whatWentWell }}</p>
          }
          @if (i.whatToImprove) {
            <p>Improve: {{ i.whatToImprove }}</p>
          }
        } @else {
          <p class="muted">Not done yet.</p>
        }
      </section>

      @if (canSettle && (v.case.status === 'Approved' || v.case.status === 'Completed')) {
        <section class="panel stack">
          <div class="toolbar">
            <h2 class="xp__heading">Full and final settlement</h2>
            @if (!v.settlement || v.settlement.status === 'Draft') {
              <button type="button" class="btn btn--sm" (click)="settlement('calculate')">{{ v.settlement ? 'Recalculate' : 'Calculate' }}</button>
            }
            @if (v.settlement?.status === 'Draft') {
              <button type="button" class="btn btn--sm" (click)="lineOpen.set(true)">Add line</button>
              <button type="button" class="btn btn--sm btn--primary" (click)="settlement('approve')">Approve</button>
            }
            @if (v.settlement?.status === 'Approved') {
              <button type="button" class="btn btn--sm" (click)="settlement('reopen')">Reopen</button>
              <button type="button" class="btn btn--sm btn--primary" (click)="payOpen.set(true)">Mark paid</button>
            }
          </div>
          @if (v.settlement; as s) {
            @for (line of s.lines; track line.id) {
              <div class="xp__row">
                <span><strong>{{ line.label }}</strong> <span class="muted">{{ line.code }}</span></span>
                <span class="muted">{{ line.basis ?? '' }}</span>
                <span [class.xp__danger]="line.isDeduction">{{ line.isDeduction ? '-' : '' }}{{ line.amount | number: '1.2-2' }}</span>
                <span>
                  @if (s.status === 'Draft' && line.isManual) {
                    <button type="button" class="btn btn--link btn--sm" (click)="removeLine(line.id)">Remove</button>
                  }
                </span>
              </div>
            }
            <p>Earnings {{ s.earnings | number: '1.2-2' }} · deductions {{ s.deductions | number: '1.2-2' }} · <strong>net {{ s.net | number: '1.2-2' }}</strong></p>
            <p><gggs-status-badge [status]="s.status" />{{ s.paidOn ? ' on ' + (s.paidOn | date: 'd MMM y') : '' }}{{ s.paymentReference ? ' · ' + s.paymentReference : '' }}</p>
          } @else {
            <p class="muted">Calculate to work out salary, leave encashment, gratuity and recoveries.</p>
          }
        </section>
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="termsOpen" title="Separation terms" [busy]="busy()" [error]="formError()" (save)="saveTerms()">
      <div class="stack">
        <label class="field"><span class="field__label">Last working day</span><input type="date" [value]="termsDay()" (change)="termsDay.set($any($event.target).value)" /></label>
        <label class="xp__flag"><input type="checkbox" [checked]="termsWaive()" (change)="termsWaive.set($any($event.target).checked)" /> Waive the notice shortfall</label>
        <label class="xp__flag"><input type="checkbox" [checked]="termsRehire()" (change)="termsRehire.set($any($event.target).checked)" /> Eligible for rehire</label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="clearanceOpen" title="Add clearance item" [busy]="busy()" [error]="formError()" [saveDisabled]="!clearanceTitle().trim()" (save)="addClearance()">
      <div class="xp__grid">
        <label class="field">
          <span class="field__label">Area</span>
          <select [value]="clearanceArea()" (change)="clearanceArea.set($any($event.target).value)">
            @for (area of areas; track area) {
              <option [value]="area">{{ areaLabel(area) }}</option>
            }
          </select>
        </label>
        <label class="field"><span class="field__label">Item</span><input type="text" maxlength="200" [value]="clearanceTitle()" (input)="clearanceTitle.set($any($event.target).value)" /></label>
        <label class="field"><span class="field__label">Owner (optional)</span><gggs-employee-select [ngModel]="clearanceOwner()" (ngModelChange)="clearanceOwner.set($event)" /></label>
        <label class="field"><span class="field__label">Amount to recover</span><input type="number" min="0" [value]="clearanceAmount()" (input)="clearanceAmount.set(+$any($event.target).value)" /></label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="interviewOpen" title="Exit interview" [busy]="busy()" [error]="formError()" (save)="saveInterview()" size="lg">
      <gggs-exit-interview-form [value]="interview" />
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="lineOpen" title="Add settlement line" [busy]="busy()" [error]="formError()" [saveDisabled]="!lineLabel().trim() || lineAmount() <= 0" (save)="addLine()">
      <div class="xp__grid">
        <label class="field"><span class="field__label">Code</span><input type="text" maxlength="30" [value]="lineCode()" (input)="lineCode.set($any($event.target).value.toUpperCase())" /></label>
        <label class="field"><span class="field__label">Label</span><input type="text" maxlength="200" [value]="lineLabel()" (input)="lineLabel.set($any($event.target).value)" /></label>
        <label class="field"><span class="field__label">Amount</span><input type="number" min="0" [value]="lineAmount()" (input)="lineAmount.set(+$any($event.target).value)" /></label>
        <label class="xp__flag"><input type="checkbox" [checked]="lineDeduction()" (change)="lineDeduction.set($any($event.target).checked)" /> A deduction</label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="payOpen" title="Mark settlement paid" saveLabel="Paid" [busy]="busy()" [error]="formError()" (save)="settlement('pay')">
      <label class="field"><span class="field__label">Payment reference</span><input type="text" maxlength="100" [value]="payReference()" (input)="payReference.set($any($event.target).value)" /></label>
    </gggs-form-dialog>
  `,
})
export class SeparationCasePage {
  private readonly api = inject(SeparationApi);
  private readonly permissions = inject(PermissionStore);

  readonly id = input.required<string>();

  protected readonly canManage = this.permissions.has('separation.case.manage');
  protected readonly canSettle = this.permissions.has('separation.settlement.manage');
  protected readonly areas = CLEARANCE_AREAS;
  protected readonly view = signal<SeparationCaseView | null>(null);
  protected readonly termsOpen = signal(false);
  protected readonly termsDay = signal(today());
  protected readonly termsWaive = signal(false);
  protected readonly termsRehire = signal(true);
  protected readonly clearanceOpen = signal(false);
  protected readonly clearanceArea = signal<ClearanceArea>('Admin');
  protected readonly clearanceTitle = signal('');
  protected readonly clearanceOwner = signal<string | null>(null);
  protected readonly clearanceAmount = signal(0);
  protected readonly interviewOpen = signal(false);
  protected interview: ExitInterviewInput = blankInterview();
  protected readonly lineOpen = signal(false);
  protected readonly lineCode = signal('BONUS');
  protected readonly lineLabel = signal('');
  protected readonly lineAmount = signal(0);
  protected readonly lineDeduction = signal(false);
  protected readonly payOpen = signal(false);
  protected readonly payReference = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.case(this.id()).then(
        (view) => this.view.set(view),
        (error: unknown) => this.error.set(message(error, 'The separation could not be loaded.')),
      );
    });
  }

  protected label(type: SeparationType): string {
    return type === 'ContractEnd' ? 'End of contract' : type === 'Mutual' ? 'Mutual separation' : type;
  }

  protected areaLabel(area: ClearanceArea): string {
    return area === 'It' ? 'IT' : area === 'Hr' ? 'HR' : area;
  }

  protected openTerms(view: SeparationCaseView): void {
    this.termsDay.set(view.case.lastWorkingDay);
    this.termsWaive.set(view.case.shortfallWaived);
    this.termsRehire.set(view.case.eligibleForRehire);
    this.formError.set(null);
    this.termsOpen.set(true);
  }

  protected saveTerms(): Promise<void> {
    return this.run(async () => {
      this.view.set(await this.api.terms(this.id(), { lastWorkingDay: this.termsDay(), shortfallWaived: this.termsWaive(), eligibleForRehire: this.termsRehire() }));
      this.termsOpen.set(false);
    });
  }

  protected withdraw(): Promise<void> {
    return confirm('Withdraw this separation? Notice is cancelled.') ? this.run(async () => this.view.set(await this.api.withdraw(this.id()))) : Promise.resolve();
  }

  protected letters(): Promise<void> {
    return this.run(async () => this.view.set(await this.api.letters(this.id())));
  }

  protected addClearance(): Promise<void> {
    return this.run(async () => {
      this.view.set(await this.api.addClearance(this.id(), { area: this.clearanceArea(), title: this.clearanceTitle(), ownerEmployeeId: this.clearanceOwner(), recoveryAmount: this.clearanceAmount() }));
      this.clearanceOpen.set(false);
      this.clearanceTitle.set('');
    });
  }

  protected resolve(item: ClearanceItem, status: ClearanceStatus): Promise<void> {
    return this.run(async () => {
      await this.api.resolve(item.id, status, null, null);
      this.view.set(await this.api.case(this.id()));
    });
  }

  protected openInterview(view: SeparationCaseView): void {
    const i = view.interview;
    this.interview = i ? { primaryReason: i.primaryReason, overallRating: i.overallRating, wouldRecommend: i.wouldRecommend, wouldReturn: i.wouldReturn, whatWentWell: i.whatWentWell ?? null, whatToImprove: i.whatToImprove ?? null } : blankInterview();
    this.formError.set(null);
    this.interviewOpen.set(true);
  }

  protected saveInterview(): Promise<void> {
    return this.run(async () => {
      this.view.set(await this.api.interview(this.id(), { ...this.interview }));
      this.interviewOpen.set(false);
    });
  }

  protected settlement(action: 'calculate' | 'approve' | 'reopen' | 'pay'): Promise<void> {
    return this.run(async () => {
      this.view.set(action === 'calculate' ? await this.api.calculate(this.id()) : await this.api.settlement(this.id(), action, action === 'pay' ? this.payReference() || null : null));
      this.payOpen.set(false);
    });
  }

  protected addLine(): Promise<void> {
    return this.run(async () => {
      this.view.set(await this.api.addLine(this.id(), { code: this.lineCode(), label: this.lineLabel(), isDeduction: this.lineDeduction(), amount: this.lineAmount() }));
      this.lineOpen.set(false);
      this.lineLabel.set('');
      this.lineAmount.set(0);
    });
  }

  protected removeLine(lineId: string): Promise<void> {
    return this.run(async () => this.view.set(await this.api.removeLine(this.id(), lineId)));
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    this.error.set(null);
    try {
      await work();
    } catch (error) {
      const text = message(error, 'That did not work.');
      this.formError.set(text);
      this.error.set(text);
    } finally {
      this.busy.set(false);
    }
  }
}

/** Clearance items waiting on me - handovers, returns and no-dues for people leaving. */
@Component({
  selector: 'gggs-clearance-page',
  imports: [DecimalPipe, RouterLink, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My clearance" subtitle="Sign off what you own for people who are leaving." />
    <section class="panel stack">
      @for (item of items(); track item.id) {
        <div class="xp__row">
          <span><strong>{{ item.title }}</strong> <span class="muted">{{ item.area === 'It' ? 'IT' : item.area === 'Hr' ? 'HR' : item.area }}</span></span>
          <span>{{ item.recoveryAmount ? 'recover ' + (item.recoveryAmount | number: '1.0-0') + ' if not returned' : '' }}</span>
          <span class="muted">{{ item.note ?? '' }}</span>
          <span class="toolbar">
            <button type="button" class="btn btn--sm btn--primary" (click)="resolve(item, 'Cleared')">Clear</button>
            <button type="button" class="btn btn--link btn--sm" (click)="resolve(item, 'Waived')">Waive</button>
          </span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-check-circle" title="Nothing to clear" />
      }
      <a routerLink="/separation" class="btn btn--link">All separations</a>
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
  `,
})
export class ClearancePage {
  private readonly api = inject(SeparationApi);

  protected readonly items = signal<readonly ClearanceItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected async resolve(item: ClearanceItem, status: ClearanceStatus): Promise<void> {
    const note = prompt('Note (optional):') ?? null;
    try {
      await this.api.resolve(item.id, status, null, note);
      this.load();
    } catch (error) {
      this.error.set(message(error, 'The item could not be signed off.'));
    }
  }

  private load(): void {
    this.api.myClearance().then((items) => this.items.set(items), (error: unknown) => this.error.set(message(error, 'Clearance could not be loaded.')));
  }
}

/** My resignation: submit it, follow notice and clearance, give an exit interview and see the settlement. */
@Component({
  selector: 'gggs-my-separation-page',
  imports: [DatePipe, DecimalPipe, PageHeader, StatusBadge, Skeleton, ExitInterviewForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My resignation" subtitle="Resign, follow your notice and clearance, and see your full and final settlement." />
    @if (loaded()) {
      @if (view(); as v) {
        <section class="panel xp__grid">
          <div><span class="muted">Status</span><br /><gggs-status-badge [status]="v.case.status" /></div>
          <div><span class="muted">Last working day</span><br /><strong>{{ v.case.lastWorkingDay | date: 'd MMM y' }}</strong></div>
          <div><span class="muted">Notice given</span><br />{{ v.case.noticeDate | date: 'd MMM y' }}</div>
          <div><span class="muted">Notice shortfall</span><br />{{ v.case.shortfallDays }} days{{ v.case.shortfallWaived ? ' (waived)' : '' }}</div>
          <div><span class="muted">Clearance</span><br />{{ v.case.clearanceTotal - v.case.clearancePending }} of {{ v.case.clearanceTotal }} done</div>
          @if (v.case.status === 'PendingApproval' || v.case.status === 'Approved') {
            <div><button type="button" class="btn" (click)="withdraw()">Withdraw resignation</button></div>
          }
        </section>
        @if (v.clearance.length) {
          <section class="panel stack">
            <h2 class="xp__heading">Clearance</h2>
            @for (item of v.clearance; track item.id) {
              <div class="xp__row">
                <span><strong>{{ item.title }}</strong></span>
                <span>{{ item.ownerName ?? 'Clearance team' }}</span>
                <span class="muted">{{ item.note ?? '' }}</span>
                <span></span>
                <gggs-status-badge [status]="item.status" />
              </div>
            }
          </section>
        }
        @if (v.case.status === 'Approved') {
          <section class="panel stack">
            <h2 class="xp__heading">Exit interview</h2>
            @if (v.interview && !editingInterview()) {
              <p>Thank you - your feedback is recorded ({{ v.interview.primaryReason }}, {{ v.interview.overallRating }}/5).</p>
              <div><button type="button" class="btn btn--sm" (click)="editingInterview.set(true)">Change</button></div>
            } @else {
              <gggs-exit-interview-form [value]="interview" />
              <div><button type="button" class="btn btn--primary" [disabled]="busy()" (click)="sendInterview()">Send feedback</button></div>
            }
          </section>
        }
        @if (v.settlement; as s) {
          <section class="panel stack">
            <h2 class="xp__heading">Full and final settlement</h2>
            @for (line of s.lines; track line.id) {
              <div class="xp__row">
                <span>{{ line.label }}</span>
                <span class="muted">{{ line.basis ?? '' }}</span>
                <span [class.xp__danger]="line.isDeduction">{{ line.isDeduction ? '-' : '' }}{{ line.amount | number: '1.2-2' }}</span>
              </div>
            }
            <p><strong>Net {{ s.net | number: '1.2-2' }}</strong> · <gggs-status-badge [status]="s.status" />{{ s.paidOn ? ' on ' + (s.paidOn | date: 'd MMM y') : '' }}</p>
          </section>
        }
      } @else {
        <section class="panel stack">
          <p>Resigning starts your notice period once it is accepted. Your notice period is normally {{ noticeDays }} days; ask for an earlier last day if you need one.</p>
          <label class="field"><span class="field__label">Last working day you are asking for</span><input type="date" [value]="lastDay()" (change)="lastDay.set($any($event.target).value)" /></label>
          <label class="field"><span class="field__label">Reason</span><textarea rows="3" maxlength="1000" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea></label>
          <div><button type="button" class="btn btn--primary" [disabled]="busy() || !reason().trim()" (click)="resign()">Submit resignation</button></div>
        </section>
      }
    } @else {
      <gggs-skeleton [lines]="6" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
})
export class MySeparationPage {
  private readonly api = inject(SeparationApi);

  protected readonly noticeDays = 60;
  protected readonly loaded = signal(false);
  protected readonly view = signal<SeparationCaseView | null>(null);
  protected readonly lastDay = signal(inDays(60));
  protected readonly reason = signal('');
  protected readonly editingInterview = signal(false);
  protected interview: ExitInterviewInput = blankInterview();
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected resign(): Promise<void> {
    return confirm('Submit your resignation?') ? this.run(async () => this.view.set(await this.api.resign(this.reason(), this.lastDay()))) : Promise.resolve();
  }

  protected withdraw(): Promise<void> {
    return confirm('Withdraw your resignation?') ? this.run(async () => {
          await this.api.withdrawMine();
          this.view.set(null);
        }) : Promise.resolve();
  }

  protected sendInterview(): Promise<void> {
    return this.run(async () => {
      this.view.set(await this.api.myInterview({ ...this.interview }));
      this.editingInterview.set(false);
    });
  }

  private load(): void {
    this.api.mine().then(
      (view) => {
        const active = view && view.case.status !== 'Withdrawn' && view.case.status !== 'Rejected' ? view : null;
        this.view.set(active);
        this.loaded.set(true);
      },
      (error: unknown) => {
        this.error.set(message(error, 'Your resignation could not be loaded.'));
        this.loaded.set(true);
      },
    );
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await work();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}
