import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type {
  ComplianceDashboard,
  FilingFrequency,
  FilingItem,
  FilingKind,
  FilingStatus,
  ObligationItem,
  RegistrationInput,
  RegistrationItem,
  RuleItem,
  RuleSummary,
  StatutoryRuleType,
} from '@gggs/shared/models';
import { FILING_FREQUENCIES, FILING_KINDS, REGISTRATION_KINDS, STATUTORY_RULE_TYPES, humanize } from '@gggs/shared/models';
import { ComplianceApi, MoneyPipe } from '@gggs/shared/money';
import { Drawer, EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const today = () => new Date().toISOString().slice(0, 10);

/** A starting configuration for each scheme, so a new rule is edited rather than typed from nothing. */
const TEMPLATES: Readonly<Record<StatutoryRuleType, unknown>> = {
  ProvidentFund: { employeeRate: 12, employerPensionRate: 8.33, employerProvidentRate: 3.67, wageCeiling: 15000, capEmployeeAtCeiling: true, capEmployerAtCeiling: true, edliRate: 0.5, adminChargeRate: 0.5, pensionAgeLimit: 58 },
  StateInsurance: { employeeRate: 0.75, employerRate: 3.25, wageThreshold: 21000 },
  ProfessionalTax: { slabs: [{ from: 0, amount: 0 }, { from: 15000, amount: 200, februaryAmount: 300 }] },
  IncomeTax: {
    financialYear: '2026-27',
    newRegime: { standardDeduction: 75000, slabs: [{ from: 0, rate: 0 }, { from: 400000, rate: 5 }], rebateIncomeLimit: 1200000, rebateMaximum: 60000, rebateMarginalRelief: true, allowsDeductions: false, surchargeCap: 25 },
    oldRegime: { standardDeduction: 50000, slabs: [{ from: 0, rate: 0 }, { from: 250000, rate: 5 }], rebateIncomeLimit: 500000, rebateMaximum: 12500, rebateMarginalRelief: false, allowsDeductions: true, surchargeCap: 37 },
    cessRate: 4,
    surcharge: [{ from: 5000000, rate: 10 }],
    sectionLimits: { '80C': 150000 },
  },
  Gratuity: { daysPerYear: 15, divisorDays: 26, minimumYears: 5, maximumAmount: 2000000, monthlyAccrualRate: 4.81 },
  LabourWelfareFund: { employeeAmount: 0, employerAmount: 0, months: [12] },
};

/** The compliance centre (spec §107): rules in force, what is due, and registrations nearing expiry. */
@Component({
  selector: 'gggs-compliance-dashboard-page',
  imports: [DatePipe, RouterLink, PageHeader, KpiCard, StatusBadge, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Compliance" subtitle="Statutory rules in force, the filing calendar, and licences and certificates." />
    @if (dashboard(); as d) {
      <div class="cmp__kpis">
        <gggs-kpi-card label="Rules in force" [value]="d.rulesInForce" [hint]="d.draftRules ? d.draftRules + ' draft(s)' : undefined" icon="pi pi-shield" />
        <gggs-kpi-card label="Due in 30 days" [value]="d.dueIn30Days" />
        <gggs-kpi-card label="Overdue" [value]="d.overdue" />
        <gggs-kpi-card label="Filed this month" [value]="d.filedThisMonth" />
        <gggs-kpi-card label="Expiring registrations" [value]="d.expiringRegistrations" [hint]="d.expiredRegistrations ? d.expiredRegistrations + ' expired' : undefined" />
      </div>
      <div class="cmp__two">
        <section class="panel stack">
          <div class="toolbar"><h2 class="cmp__heading">Coming up</h2><span class="cmp__spacer"></span><a class="btn btn--link btn--sm" routerLink="/compliance/filings">Calendar</a></div>
          @for (filing of d.upcoming; track filing.id) {
            <div class="cmp__row">
              <strong>{{ filing.name }}</strong>
              <span>{{ filing.periodLabel }}</span>
              <span [class.cmp__danger]="filing.isOverdue">{{ filing.dueDate | date: 'd MMM y' }}{{ filing.isOverdue ? ' · overdue' : filing.daysToDue <= 7 ? ' · ' + filing.daysToDue + ' days' : '' }}</span>
              <gggs-status-badge [status]="filing.isOverdue ? 'Overdue' : filing.status" />
            </div>
          } @empty {
            <gggs-empty-state icon="pi pi-calendar" title="Nothing pending" />
          }
        </section>
        <section class="panel stack">
          <div class="toolbar"><h2 class="cmp__heading">Licences and certificates</h2><span class="cmp__spacer"></span><a class="btn btn--link btn--sm" routerLink="/compliance/registrations">All</a></div>
          @for (registration of d.expiring; track registration.id) {
            <div class="cmp__row">
              <strong>{{ registration.name }}</strong>
              <span class="muted">{{ registration.number ?? '' }}</span>
              <span>{{ registration.expiresOn | date: 'd MMM y' }}</span>
              <gggs-status-badge [status]="registration.validity" />
            </div>
          } @empty {
            <p class="muted">Nothing expiring soon.</p>
          }
        </section>
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './compliance.css',
})
export class ComplianceDashboardPage {
  protected readonly dashboard = signal<ComplianceDashboard | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    inject(ComplianceApi)
      .dashboard()
      .then(
        (dashboard) => this.dashboard.set(dashboard),
        (error: unknown) => this.error.set(message(error, 'The compliance dashboard could not be loaded.')),
      );
  }
}

/**
 * Statutory rules (spec §22): every scheme as published versions. A version never changes once
 * published - a new one from a later date takes over, so past payroll keeps the rule it used.
 */
@Component({
  selector: 'gggs-statutory-rules-page',
  imports: [DatePipe, PageHeader, StatusBadge, EmptyState, Drawer, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Statutory rules" subtitle="EPF, ESI, professional tax, income tax, gratuity and labour welfare - versioned by the date they apply from.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="openNew()"><i class="pi pi-plus" aria-hidden="true"></i> New rule</button>
      }
    </gggs-page-header>
    <section class="panel toolbar cmp__filters">
      <label class="field">
        <span class="field__label">Scheme</span>
        <select [value]="type()" (change)="type.set($any($event.target).value); load()">
          <option value="">All</option>
          @for (option of types; track option) {
            <option [value]="option">{{ humanize(option) }}</option>
          }
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (rule of rules(); track rule.code) {
        <button type="button" class="cmp__row cmp__row--button" (click)="open(rule)">
          <span><strong>{{ rule.name }}</strong> <span class="muted">{{ rule.code }}</span></span>
          <span>{{ humanize(rule.type) }}{{ rule.stateCode ? ' · ' + rule.stateCode : '' }}</span>
          <span class="muted">
            @if (rule.current; as current) {
              v{{ current.version }} since {{ current.effectiveFrom | date: 'd MMM y' }}
            } @else {
              not in force
            }
            {{ rule.upcoming ? ' · v' + rule.upcoming.version + ' from ' + (rule.upcoming.effectiveFrom | date: 'd MMM y') : '' }}
          </span>
          <span>
            @if (rule.draft) {
              <gggs-status-badge status="Draft" />
            } @else {
              <gggs-status-badge [status]="rule.current ? 'Active' : 'Retired'" />
            }
          </span>
        </button>
      } @empty {
        <gggs-empty-state icon="pi pi-shield" title="No rules" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-drawer [(visible)]="drawerOpen" [title]="selected()?.name ?? 'Rule'" width="min(48rem, 100vw)">
      <div class="stack">
        @for (version of versions(); track version.id) {
          <article class="cmp__version stack">
            <div class="toolbar">
              <strong>Version {{ version.version }}</strong>
              <gggs-status-badge [status]="version.status" />
              @if (version.inForce) {
                <span class="cmp__good">in force</span>
              }
              <span class="muted">{{ version.effectiveFrom | date: 'd MMM y' }} – {{ version.effectiveTo ? (version.effectiveTo | date: 'd MMM y') : 'open' }}</span>
              <span class="cmp__spacer"></span>
              @if (canManage && version.status === 'Draft') {
                <button type="button" class="btn btn--sm" (click)="editDraft(version)">Edit</button>
                <button type="button" class="btn btn--sm btn--primary" [disabled]="busy()" (click)="publish(version)">Publish</button>
                <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="discard(version)">Discard</button>
              }
              @if (canManage && version.status === 'Active' && !hasDraft()) {
                <button type="button" class="btn btn--sm" (click)="newVersion(version)">New version</button>
              }
            </div>
            @if (version.eligibility) {
              <p class="muted">{{ version.eligibility }}</p>
            }
            <pre class="cmp__json">{{ pretty(version.configuration) }}</pre>
            @if (version.notes) {
              <p class="muted">{{ version.notes }}</p>
            }
          </article>
        }
      </div>
    </gggs-drawer>

    <gggs-form-dialog [(visible)]="editorOpen" [title]="editorTitle()" [saveLabel]="mode() === 'version' ? 'Create draft' : 'Save draft'" [busy]="busy()" [error]="formError()" [saveDisabled]="!effectiveFrom() || !json().trim() || (mode() === 'new' && (!code().trim() || !name().trim()))" (save)="saveEditor()" width="min(52rem, 96vw)">
      @if (mode() === 'new') {
        <div class="cmp__grid">
          <label class="field">
            <span class="field__label">Code</span>
            <input type="text" maxlength="40" placeholder="IN-TN-PT" [value]="code()" (input)="code.set($any($event.target).value.toUpperCase())" />
          </label>
          <label class="field">
            <span class="field__label">Scheme</span>
            <select [value]="newType()" (change)="pickType($any($event.target).value)">
              @for (option of types; track option) {
                <option [value]="option">{{ humanize(option) }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">State (for state levies)</span>
            <input type="text" maxlength="16" placeholder="IN-TN" [value]="stateCode()" (input)="stateCode.set($any($event.target).value.toUpperCase())" />
          </label>
        </div>
      }
      @if (mode() !== 'version') {
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="200" [value]="name()" (input)="name.set($any($event.target).value)" />
        </label>
      }
      <label class="field">
        <span class="field__label">Applies from</span>
        <input type="date" [value]="effectiveFrom()" (change)="effectiveFrom.set($any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Configuration (JSON)</span>
        <textarea rows="14" class="cmp__json-input" spellcheck="false" [value]="json()" (input)="json.set($any($event.target).value)"></textarea>
        <span class="muted">Rates are percentages; slabs start at zero. The server checks the configuration against the scheme before saving.</span>
      </label>
      @if (mode() !== 'version') {
        <label class="field">
          <span class="field__label">Who it applies to</span>
          <input type="text" maxlength="1000" [value]="eligibility()" (input)="eligibility.set($any($event.target).value)" />
        </label>
      }
      <label class="field">
        <span class="field__label">Notes</span>
        <input type="text" maxlength="2000" [value]="notes()" (input)="notes.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './compliance.css',
})
export class StatutoryRulesPage {
  private readonly api = inject(ComplianceApi);

  protected readonly canManage = inject(PermissionStore).has('compliance.rule.manage');
  protected readonly types = STATUTORY_RULE_TYPES;
  protected readonly humanize = humanize;
  protected readonly type = signal<StatutoryRuleType | ''>('');
  protected readonly rules = signal<readonly RuleSummary[]>([]);
  protected readonly selected = signal<RuleSummary | null>(null);
  protected readonly versions = signal<readonly RuleItem[]>([]);
  protected readonly drawerOpen = signal(false);
  protected readonly editorOpen = signal(false);
  protected readonly mode = signal<'new' | 'draft' | 'version'>('new');
  protected readonly editing = signal<RuleItem | null>(null);
  protected readonly code = signal('');
  protected readonly name = signal('');
  protected readonly newType = signal<StatutoryRuleType>('ProfessionalTax');
  protected readonly stateCode = signal('');
  protected readonly effectiveFrom = signal('');
  protected readonly json = signal('');
  protected readonly eligibility = signal('');
  protected readonly notes = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly hasDraft = computed(() => this.versions().some((version) => version.status === 'Draft'));
  protected readonly editorTitle = computed(() => (this.mode() === 'new' ? 'New statutory rule' : this.mode() === 'version' ? 'Draft a new version' : 'Edit draft'));

  constructor() {
    void this.load();
  }

  protected pretty(value: unknown): string {
    return JSON.stringify(value, null, 2);
  }

  protected async open(rule: RuleSummary): Promise<void> {
    this.selected.set(rule);
    this.versions.set([]);
    this.drawerOpen.set(true);
    await this.loadVersions();
  }

  protected openNew(): void {
    this.mode.set('new');
    this.editing.set(null);
    this.code.set('');
    this.name.set('');
    this.stateCode.set('');
    this.pickType('ProfessionalTax');
    this.effectiveFrom.set(today());
    this.eligibility.set('');
    this.notes.set('');
    this.formError.set(null);
    this.editorOpen.set(true);
  }

  protected pickType(type: StatutoryRuleType): void {
    this.newType.set(type);
    this.json.set(this.pretty(TEMPLATES[type]));
  }

  protected newVersion(version: RuleItem): void {
    this.mode.set('version');
    this.editing.set(version);
    this.effectiveFrom.set(today());
    this.json.set(this.pretty(version.configuration));
    this.notes.set('');
    this.formError.set(null);
    this.editorOpen.set(true);
  }

  protected editDraft(version: RuleItem): void {
    this.mode.set('draft');
    this.editing.set(version);
    this.name.set(version.name);
    this.effectiveFrom.set(version.effectiveFrom);
    this.json.set(this.pretty(version.configuration));
    this.eligibility.set(version.eligibility ?? '');
    this.notes.set(version.notes ?? '');
    this.formError.set(null);
    this.editorOpen.set(true);
  }

  protected async saveEditor(): Promise<void> {
    let configuration: unknown;
    try {
      configuration = JSON.parse(this.json());
    } catch {
      this.formError.set('The configuration is not valid JSON.');
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      const editing = this.editing();
      if (this.mode() === 'new') {
        await this.api.createRule({
          code: this.code().trim(),
          name: this.name().trim(),
          type: this.newType(),
          stateCode: this.stateCode().trim() || null,
          effectiveFrom: this.effectiveFrom(),
          configuration,
          eligibility: this.eligibility().trim() || null,
          notes: this.notes().trim() || null,
        });
      } else if (this.mode() === 'version' && editing) {
        await this.api.draftVersion(editing.id, { effectiveFrom: this.effectiveFrom(), configuration, notes: this.notes().trim() || null });
      } else if (editing) {
        await this.api.updateDraft(editing.id, { name: this.name().trim(), effectiveFrom: this.effectiveFrom(), configuration, eligibility: this.eligibility().trim() || null, notes: this.notes().trim() || null });
      }

      this.editorOpen.set(false);
      await this.load();
      await this.loadVersions();
    } catch (error) {
      this.formError.set(message(error, 'The rule could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async publish(version: RuleItem): Promise<void> {
    await this.act(() => this.api.publish(version.id));
  }

  protected async discard(version: RuleItem): Promise<void> {
    await this.act(() => this.api.discardDraft(version.id));
  }

  private async act(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      await this.load();
      await this.loadVersions();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async loadVersions(): Promise<void> {
    const code = this.selected()?.code;
    if (code) {
      try {
        this.versions.set(await this.api.versions(code));
      } catch {
        this.versions.set([]);
      }
    }
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.rules.set(await this.api.rules(this.type()));
    } catch (error) {
      this.error.set(message(error, 'Rules could not be loaded.'));
    }
  }
}

/** The filing calendar (spec §107): every obligation's periods, what is due and overdue, and the record of filing. */
@Component({
  selector: 'gggs-compliance-filings-page',
  imports: [DatePipe, FormsModule, MoneyPipe, PageHeader, Tabs, StatusBadge, EmptyState, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Filing calendar" subtitle="Returns and payments due to the authorities, and proof they were made.">
      @if (canManage && tab() === 'obligations') {
        <button type="button" class="btn btn--primary" (click)="editObligation(null)"><i class="pi pi-plus" aria-hidden="true"></i> New obligation</button>
      }
    </gggs-page-header>
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />

    @if (tab() === 'filings') {
      <section class="panel toolbar cmp__filters">
        <label class="field">
          <span class="field__label">Status</span>
          <select [value]="status()" (change)="status.set($any($event.target).value); load()">
            <option value="Pending">Pending</option>
            <option value="Filed">Filed</option>
            <option value="Waived">Waived</option>
            <option value="">Any</option>
          </select>
        </label>
        <label class="field">
          <span class="field__label">Kind</span>
          <select [value]="kind()" (change)="kind.set($any($event.target).value); load()">
            <option value="">Any</option>
            @for (option of kinds; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
        <label class="cmp__flag">
          <input type="checkbox" [checked]="overdueOnly()" (change)="overdueOnly.set($any($event.target).checked); load()" />
          <span>Overdue only</span>
        </label>
        <span class="cmp__spacer"></span>
        @if (canManage) {
          <button type="button" class="btn btn--sm" [disabled]="busy()" (click)="generate()">Generate upcoming</button>
        }
      </section>
      <section class="panel stack">
        @for (filing of filings(); track filing.id) {
          <div class="cmp__row">
            <span><strong>{{ filing.name }}</strong><br /><span class="muted">{{ filing.periodLabel }}{{ filing.ownerName ? ' · ' + filing.ownerName : '' }}</span></span>
            <span [class.cmp__danger]="filing.isOverdue">due {{ filing.dueDate | date: 'd MMM y' }}</span>
            <span class="muted">
              @if (filing.status === 'Filed') {
                filed {{ filing.filedOn | date: 'd MMM y' }}{{ filing.reference ? ' · ' + filing.reference : '' }}{{ filing.amount ? ' · ' + (filing.amount | gggsMoney) : '' }}
              } @else {
                {{ filing.notes ?? '' }}
              }
            </span>
            <span class="toolbar">
              <gggs-status-badge [status]="filing.isOverdue ? 'Overdue' : filing.status" />
              @if (canManage) {
                @if (filing.status === 'Pending') {
                  <button type="button" class="btn btn--link btn--sm" (click)="openFile(filing)">Record filing</button>
                  <button type="button" class="btn btn--link btn--sm" (click)="openOther(filing, 'waive')">Waive</button>
                  <button type="button" class="btn btn--link btn--sm" (click)="openOther(filing, 'reschedule')">Move date</button>
                } @else {
                  <button type="button" class="btn btn--link btn--sm" (click)="reopen(filing)">Reopen</button>
                }
              }
            </span>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-calendar" title="No filings match" />
        }
      </section>
    } @else {
      <section class="panel stack">
        @for (obligation of obligations(); track obligation.id) {
          <div class="cmp__row">
            <span><strong>{{ obligation.name }}</strong> <span class="muted">{{ obligation.code }}</span></span>
            <span>{{ humanize(obligation.frequency) }} · day {{ obligation.dueDay }}{{ obligation.dueMonthOffset ? ', ' + obligation.dueMonthOffset + ' month(s) after' : '' }}{{ obligation.stateCode ? ' · ' + obligation.stateCode : '' }}</span>
            <span class="muted">{{ obligation.ownerName ? 'owner ' + obligation.ownerName : 'no owner - no reminders' }}{{ obligation.nextDue ? ' · next ' + (obligation.nextDue | date: 'd MMM') : '' }}</span>
            <span class="toolbar">
              <gggs-status-badge [status]="obligation.isActive ? 'Active' : 'Inactive'" />
              @if (canManage) {
                <button type="button" class="btn btn--link btn--sm" (click)="editObligation(obligation)">Edit</button>
                <button type="button" class="btn btn--link btn--sm" (click)="toggle(obligation)">{{ obligation.isActive ? 'Stop' : 'Resume' }}</button>
              }
            </span>
          </div>
        }
      </section>
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }

    <gggs-form-dialog [(visible)]="fileOpen" [title]="'Record filing · ' + (target()?.name ?? '')" saveLabel="Record" [busy]="busy()" [error]="formError()" [saveDisabled]="!filedOn()" (save)="file()">
      <div class="cmp__grid">
        <label class="field">
          <span class="field__label">Filed on</span>
          <input type="date" [value]="filedOn()" (change)="filedOn.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Reference (challan, TRRN, acknowledgement)</span>
          <input type="text" maxlength="100" [value]="reference()" (input)="reference.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Amount paid</span>
          <input type="number" min="0" [value]="amount() ?? ''" (input)="amount.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
        </label>
      </div>
      <label class="field">
        <span class="field__label">Notes</span>
        <input type="text" maxlength="1000" [value]="notes()" (input)="notes.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="otherOpen" [title]="otherMode() === 'waive' ? 'Waive this filing' : 'Move the due date'" [busy]="busy()" [error]="formError()" [saveDisabled]="otherMode() === 'waive' ? !notes().trim() : !dueDate()" (save)="saveOther()">
      @if (otherMode() === 'waive') {
        <label class="field">
          <span class="field__label">Why it does not apply</span>
          <input type="text" maxlength="1000" [value]="notes()" (input)="notes.set($any($event.target).value)" />
        </label>
      } @else {
        <label class="field">
          <span class="field__label">Due on</span>
          <input type="date" [value]="dueDate()" (change)="dueDate.set($any($event.target).value)" />
        </label>
      }
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="obligationOpen" [title]="editingObligation() ? 'Edit obligation' : 'New obligation'" [busy]="busy()" [error]="formError()" [saveDisabled]="!obligationName().trim() || (!editingObligation() && !obligationCode().trim())" (save)="saveObligation()">
      <div class="cmp__grid">
        @if (!editingObligation()) {
          <label class="field">
            <span class="field__label">Code</span>
            <input type="text" maxlength="40" [value]="obligationCode()" (input)="obligationCode.set($any($event.target).value.toUpperCase())" />
          </label>
        }
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="200" [value]="obligationName()" (input)="obligationName.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Kind</span>
          <select [value]="obligationKind()" (change)="obligationKind.set($any($event.target).value)">
            @for (option of kinds; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Every</span>
          <select [value]="frequency()" (change)="frequency.set($any($event.target).value)">
            @for (option of frequencies; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Due day</span>
          <input type="number" min="1" max="31" [value]="dueDay()" (input)="dueDay.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Months after the period</span>
          <input type="number" min="0" max="12" [value]="offset()" (input)="offset.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">State</span>
          <input type="text" maxlength="16" [value]="obligationState()" (input)="obligationState.set($any($event.target).value.toUpperCase())" />
        </label>
      </div>
      <label class="field">
        <span class="field__label">Owner (gets reminders)</span>
        <gggs-employee-select ariaLabel="Owner" [ngModel]="owner()" (ngModelChange)="owner.set($event)" />
      </label>
      <label class="field">
        <span class="field__label">Description</span>
        <input type="text" maxlength="1000" [value]="description()" (input)="description.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './compliance.css',
})
export class ComplianceFilingsPage {
  private readonly api = inject(ComplianceApi);

  protected readonly canManage = inject(PermissionStore).has('compliance.filing.manage');
  protected readonly tabs: readonly TabItem[] = [
    { key: 'filings', label: 'Filings' },
    { key: 'obligations', label: 'Obligations' },
  ];
  protected readonly kinds = FILING_KINDS;
  protected readonly frequencies = FILING_FREQUENCIES;
  protected readonly humanize = humanize;
  protected readonly tab = signal('filings');
  protected readonly status = signal<FilingStatus | ''>('Pending');
  protected readonly kind = signal<FilingKind | ''>('');
  protected readonly overdueOnly = signal(false);
  protected readonly filings = signal<readonly FilingItem[]>([]);
  protected readonly obligations = signal<readonly ObligationItem[]>([]);
  protected readonly target = signal<FilingItem | null>(null);
  protected readonly fileOpen = signal(false);
  protected readonly otherOpen = signal(false);
  protected readonly otherMode = signal<'waive' | 'reschedule'>('waive');
  protected readonly filedOn = signal(today());
  protected readonly reference = signal('');
  protected readonly amount = signal<number | null>(null);
  protected readonly notes = signal('');
  protected readonly dueDate = signal('');
  protected readonly obligationOpen = signal(false);
  protected readonly editingObligation = signal<ObligationItem | null>(null);
  protected readonly obligationCode = signal('');
  protected readonly obligationName = signal('');
  protected readonly obligationKind = signal<FilingKind>('Other');
  protected readonly frequency = signal<FilingFrequency>('Monthly');
  protected readonly dueDay = signal(15);
  protected readonly offset = signal(1);
  protected readonly obligationState = signal('');
  protected readonly owner = signal<string | null>(null);
  protected readonly description = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected openFile(filing: FilingItem): void {
    this.target.set(filing);
    this.filedOn.set(today());
    this.reference.set('');
    this.amount.set(null);
    this.notes.set('');
    this.formError.set(null);
    this.fileOpen.set(true);
  }

  protected openOther(filing: FilingItem, mode: 'waive' | 'reschedule'): void {
    this.target.set(filing);
    this.otherMode.set(mode);
    this.notes.set('');
    this.dueDate.set(filing.dueDate);
    this.formError.set(null);
    this.otherOpen.set(true);
  }

  protected async file(): Promise<void> {
    const filing = this.target();
    if (filing) {
      await this.dialog(async () => {
        await this.api.file(filing.id, { filedOn: this.filedOn(), reference: this.reference().trim() || null, amount: this.amount(), notes: this.notes().trim() || null });
        this.fileOpen.set(false);
      });
    }
  }

  protected async saveOther(): Promise<void> {
    const filing = this.target();
    if (filing) {
      await this.dialog(async () => {
        if (this.otherMode() === 'waive') {
          await this.api.waive(filing.id, this.notes().trim());
        } else {
          await this.api.reschedule(filing.id, this.dueDate());
        }

        this.otherOpen.set(false);
      });
    }
  }

  protected async reopen(filing: FilingItem): Promise<void> {
    await this.act(() => this.api.reopen(filing.id));
  }

  protected async generate(): Promise<void> {
    await this.act(() => this.api.generate());
  }

  protected editObligation(obligation: ObligationItem | null): void {
    this.editingObligation.set(obligation);
    this.obligationCode.set(obligation?.code ?? '');
    this.obligationName.set(obligation?.name ?? '');
    this.obligationKind.set(obligation?.kind ?? 'Other');
    this.frequency.set(obligation?.frequency ?? 'Monthly');
    this.dueDay.set(obligation?.dueDay ?? 15);
    this.offset.set(obligation?.dueMonthOffset ?? 1);
    this.obligationState.set(obligation?.stateCode ?? '');
    this.owner.set(obligation?.ownerEmployeeId ?? null);
    this.description.set(obligation?.description ?? '');
    this.formError.set(null);
    this.obligationOpen.set(true);
  }

  protected async saveObligation(): Promise<void> {
    await this.dialog(async () => {
      await this.api.saveObligation(this.editingObligation()?.id ?? null, {
        code: this.obligationCode().trim() || null,
        name: this.obligationName().trim(),
        kind: this.obligationKind(),
        frequency: this.frequency(),
        dueDay: this.dueDay(),
        dueMonthOffset: this.offset(),
        stateCode: this.obligationState().trim() || null,
        ownerEmployeeId: this.owner(),
        description: this.description().trim() || null,
      });
      this.obligationOpen.set(false);
    });
  }

  protected async toggle(obligation: ObligationItem): Promise<void> {
    await this.act(() => this.api.setObligationActive(obligation.id, !obligation.isActive));
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      const [filings, obligations] = await Promise.all([
        this.api.filings({ status: this.status(), kind: this.kind(), overdueOnly: this.overdueOnly() || undefined }),
        this.api.obligations(),
      ]);
      this.filings.set(filings);
      this.obligations.set(obligations);
    } catch (error) {
      this.error.set(message(error, 'The calendar could not be loaded.'));
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

const blankRegistration = (): RegistrationInput => ({
  kind: 'License',
  name: '',
  number: null,
  authority: null,
  stateCode: null,
  issuedOn: null,
  expiresOn: null,
  renewalLeadDays: 30,
  ownerEmployeeId: null,
  notes: null,
});

/** Licences, registrations, certificates and audits (spec §107), watched for expiry. */
@Component({
  selector: 'gggs-compliance-registrations-page',
  imports: [DatePipe, FormsModule, PageHeader, StatusBadge, EmptyState, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Licences and certificates" subtitle="Registrations the company holds and audits it has had, with their renewal dates.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="edit(null)"><i class="pi pi-plus" aria-hidden="true"></i> Add</button>
      }
    </gggs-page-header>
    <section class="panel stack">
      @for (registration of registrations(); track registration.id) {
        <div class="cmp__row">
          <span><strong>{{ registration.name }}</strong><br /><span class="muted">{{ humanize(registration.kind) }}{{ registration.number ? ' · ' + registration.number : '' }}{{ registration.authority ? ' · ' + registration.authority : '' }}</span></span>
          <span>{{ registration.expiresOn ? 'expires ' + (registration.expiresOn | date: 'd MMM y') : 'no expiry' }}</span>
          <span class="muted">{{ registration.ownerName ?? '' }}{{ registration.stateCode ? ' · ' + registration.stateCode : '' }}</span>
          <span class="toolbar">
            <gggs-status-badge [status]="registration.validity" />
            @if (canManage) {
              <button type="button" class="btn btn--link btn--sm" (click)="edit(registration)">Edit</button>
              <button type="button" class="btn btn--link btn--sm" (click)="remove(registration)">Remove</button>
            }
          </span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-verified" title="Nothing recorded yet" description="Add your shops and establishments registration, PF and ESI codes, licences and certificates." />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="dialogOpen" [title]="editing() ? 'Edit' : 'Add a registration'" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().name.trim()" (save)="save()">
      <div class="cmp__grid">
        <label class="field">
          <span class="field__label">Kind</span>
          <select [value]="form().kind" (change)="patch({ kind: $any($event.target).value })">
            @for (option of kinds; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="200" [value]="form().name" (input)="patch({ name: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Number</span>
          <input type="text" maxlength="100" [value]="form().number ?? ''" (input)="patch({ number: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Authority</span>
          <input type="text" maxlength="200" [value]="form().authority ?? ''" (input)="patch({ authority: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Issued on</span>
          <input type="date" [value]="form().issuedOn ?? ''" (change)="patch({ issuedOn: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Expires on</span>
          <input type="date" [value]="form().expiresOn ?? ''" (change)="patch({ expiresOn: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Remind this many days before</span>
          <input type="number" min="0" max="365" [value]="form().renewalLeadDays" (input)="patch({ renewalLeadDays: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">State</span>
          <input type="text" maxlength="16" [value]="form().stateCode ?? ''" (input)="patch({ stateCode: $any($event.target).value.toUpperCase() })" />
        </label>
      </div>
      <label class="field">
        <span class="field__label">Owner (gets reminders)</span>
        <gggs-employee-select ariaLabel="Owner" [ngModel]="form().ownerEmployeeId ?? null" (ngModelChange)="patch({ ownerEmployeeId: $event })" />
      </label>
      <label class="field">
        <span class="field__label">Notes (an audit's findings)</span>
        <textarea rows="3" maxlength="2000" [value]="form().notes ?? ''" (input)="patch({ notes: $any($event.target).value })"></textarea>
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './compliance.css',
})
export class ComplianceRegistrationsPage {
  private readonly api = inject(ComplianceApi);

  protected readonly canManage = inject(PermissionStore).has('compliance.filing.manage');
  protected readonly kinds = REGISTRATION_KINDS;
  protected readonly humanize = humanize;
  protected readonly registrations = signal<readonly RegistrationItem[]>([]);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<RegistrationItem | null>(null);
  protected readonly form = signal<RegistrationInput>(blankRegistration());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected edit(registration: RegistrationItem | null): void {
    this.editing.set(registration);
    this.form.set(registration ? { ...registration } : blankRegistration());
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected patch(change: Partial<RegistrationInput>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.saveRegistration(this.editing()?.id ?? null, this.form());
      this.dialogOpen.set(false);
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'It could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(registration: RegistrationItem): Promise<void> {
    try {
      await this.api.deleteRegistration(registration.id);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'It could not be removed.'));
    }
  }

  private async load(): Promise<void> {
    try {
      this.registrations.set(await this.api.registrations());
    } catch (error) {
      this.error.set(message(error, 'Registrations could not be loaded.'));
    }
  }
}
