import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { RewardsApi } from '@gggs/shared/experience';
import type {
  CatalogItem,
  CatalogItemInput,
  OrgUnit,
  PointLedgerItem,
  RecognitionItem,
  RecognitionTypeInput,
  RecognitionTypeItem,
  RedemptionItem,
  RewardBudgetItem,
  RewardDashboard,
  RewardRuleInput,
  RewardRuleItem,
  RewardSettings,
  RewardWallet,
} from '@gggs/shared/models';
import { RECOGNITION_KINDS, RULE_TRIGGERS, humanize } from '@gggs/shared/models';
import { OrgStructureApi } from '@gggs/shared/org-reference';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const num = (value: string): number | null => (value === '' ? null : +value);

/** The recognition feed: who was recognised for what, with cheers. */
@Component({
  selector: 'gggs-recognition-feed',
  imports: [DatePipe, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      @for (item of items(); track item.id) {
        <div class="xp__card">
          <span><strong>{{ item.recipientName }}</strong> was recognised {{ item.giverName ? 'by ' + item.giverName : '' }} · <span class="muted">{{ item.typeName ?? item.ruleName }}</span></span>
          <span>“{{ item.message }}”</span>
          <span class="toolbar">
            <span class="muted">{{ item.givenOn | date: 'd MMM y' }} · +{{ item.points }} points</span>
            <button type="button" class="btn btn--sm" [class.btn--primary]="cheered(item)" [disabled]="cheered(item)" (click)="cheer(item)">👏 {{ cheers(item) }}</button>
          </span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-star" title="No recognition yet" description="Be the first to thank someone." />
      }
    </div>
  `,
  styleUrl: './xp.css',
})
export class RecognitionFeed {
  private readonly api = inject(RewardsApi);

  readonly items = input.required<readonly RecognitionItem[]>();

  /** Cheer counts updated here since the feed loaded. */
  private readonly updated = signal<Readonly<Record<string, number>>>({});

  protected cheered(item: RecognitionItem): boolean {
    return item.cheeredByMe || item.id in this.updated();
  }

  protected cheers(item: RecognitionItem): number {
    return this.updated()[item.id] ?? item.cheers;
  }

  protected async cheer(item: RecognitionItem): Promise<void> {
    const result = await this.api.cheer(item.id);
    this.updated.update((counts) => ({ ...counts, [item.id]: result.cheers }));
  }
}

/** The rewards programme: recognition at a glance, approvals, redemptions, catalogue, types, rules and budgets. */
@Component({
  selector: 'gggs-rewards-page',
  imports: [FormsModule, DatePipe, DecimalPipe, PageHeader, KpiCard, StatusBadge, EmptyState, FormDialog, Skeleton, Tabs, EmployeeSelect, RecognitionFeed],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Rewards" subtitle="Recognition, points, the catalogue and budgets for the rewards programme.">
      @if (canProgram) {
        <button type="button" class="btn" (click)="adjustOpen.set(true)">Adjust points</button>
      }
    </gggs-page-header>
    @if (dashboard(); as d) {
      <div class="xp__kpis">
        <gggs-kpi-card label="Recognitions this month" [value]="d.recognitionsThisMonth" />
        <gggs-kpi-card label="Points awarded this year" [value]="d.pointsAwardedThisYear" [hint]="'worth ' + (d.valueAwardedThisYear | number: '1.0-0')" />
        <gggs-kpi-card label="Awaiting approval" [value]="d.pendingApprovals" />
        <gggs-kpi-card label="Redemptions to fulfil" [value]="d.pendingRedemptions" />
        <gggs-kpi-card label="Points outstanding" [value]="d.pointsOutstanding" />
      </div>
      <section class="panel xp__grid">
        <div class="stack">
          <h2 class="xp__heading">Most recognised this quarter</h2>
          @for (leader of d.topRecipients; track leader.employeeId) {
            <span>{{ leader.name }} · {{ leader.points }} points ({{ leader.count }})</span>
          } @empty {
            <span class="muted">No one yet.</span>
          }
        </div>
        <div class="stack">
          <h2 class="xp__heading">Most generous this quarter</h2>
          @for (leader of d.topGivers; track leader.employeeId) {
            <span>{{ leader.name }} · {{ leader.count }} recognitions</span>
          } @empty {
            <span class="muted">No one yet.</span>
          }
        </div>
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="3" />
    }
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />
    <section class="panel stack">
      @switch (tab()) {
        @case ('feed') {
          <gggs-recognition-feed [items]="recognitions()" />
        }
        @case ('redemptions') {
          @for (redemption of redemptions(); track redemption.id) {
            <div class="xp__row">
              <span><strong>{{ redemption.itemName }}</strong> <span class="muted">{{ redemption.points }} points</span></span>
              <span>{{ redemption.employeeName }}</span>
              <span class="muted">{{ redemption.requestedOn | date: 'd MMM y' }}{{ redemption.note ? ' · ' + redemption.note : '' }}</span>
              <span class="toolbar">
                @if (canRedeem && redemption.status === 'Requested') {
                  <button type="button" class="btn btn--sm" (click)="redemptionAction(redemption, 'fulfil')">Fulfil</button>
                  <button type="button" class="btn btn--link btn--sm" (click)="redemptionAction(redemption, 'cancel')">Cancel</button>
                }
              </span>
              <gggs-status-badge [status]="redemption.status" />
            </div>
          } @empty {
            <gggs-empty-state icon="pi pi-gift" title="No redemptions" />
          }
        }
        @case ('catalogue') {
          @if (canCatalog) {
            <div class="toolbar"><button type="button" class="btn" (click)="editItem(null)">New item</button></div>
          }
          @for (item of catalog(); track item.id) {
            <div class="xp__row">
              <span><strong>{{ item.name }}</strong> <span class="muted">{{ item.category }}</span></span>
              <span>{{ item.pointsCost }} points</span>
              <span class="muted">{{ item.stock !== undefined ? item.stock + ' in stock' : 'unlimited' }}</span>
              <span>
                @if (canCatalog) {
                  <button type="button" class="btn btn--link btn--sm" (click)="editItem(item)">Edit</button>
                }
              </span>
              <gggs-status-badge [status]="item.isActive ? 'Active' : 'Inactive'" />
            </div>
          }
        }
        @case ('programme') {
          <h2 class="xp__heading">Settings</h2>
          @if (settings(); as s) {
            <div class="xp__grid">
              <label class="field">
                <span class="field__label">One point is worth</span>
                <input type="number" min="0.01" step="0.01" [value]="s.pointValue" (input)="patchSettings({ pointValue: +$any($event.target).value })" />
              </label>
              <label class="field">
                <span class="field__label">Currency</span>
                <input type="text" maxlength="3" [value]="s.currency" (input)="patchSettings({ currency: $any($event.target).value.toUpperCase() })" />
              </label>
              <label class="field">
                <span class="field__label">Points lapse after (months, 0 never)</span>
                <input type="number" min="0" max="60" [value]="s.expiryMonths" (input)="patchSettings({ expiryMonths: +$any($event.target).value })" />
              </label>
              <label class="field">
                <span class="field__label">Monthly peer allowance</span>
                <input type="number" min="0" [value]="s.monthlyPeerAllowance" (input)="patchSettings({ monthlyPeerAllowance: +$any($event.target).value })" />
              </label>
              <div><button type="button" class="btn" (click)="saveSettings()">Save settings</button></div>
            </div>
          }
          <h2 class="xp__heading">Recognition types</h2>
          <div class="toolbar"><button type="button" class="btn btn--sm" (click)="editType(null)">New type</button></div>
          @for (type of types(); track type.id) {
            <div class="xp__row">
              <span><strong>{{ type.name }}</strong> <span class="muted">{{ type.code }}</span></span>
              <span>{{ type.kind }} · {{ type.points }} points{{ type.cashAmount ? ' + ' + (type.cashAmount | number: '1.0-0') + ' cash' : '' }}</span>
              <span class="muted">{{ type.requiresApproval ? 'needs approval' : 'instant' }}</span>
              <button type="button" class="btn btn--link btn--sm" (click)="editType(type)">Edit</button>
              <gggs-status-badge [status]="type.isActive ? 'Active' : 'Inactive'" />
            </div>
          }
          <h2 class="xp__heading">Automatic rules</h2>
          <div class="toolbar"><button type="button" class="btn btn--sm" (click)="editRule(null)">New rule</button></div>
          @for (rule of rules(); track rule.id) {
            <div class="xp__row">
              <span><strong>{{ rule.name }}</strong></span>
              <span>{{ humanize(rule.trigger) }}{{ rule.trigger !== 'CourseCompleted' ? ' ≥ ' + rule.threshold : '' }}</span>
              <span>{{ rule.points }} points{{ rule.perYear ? ' per year' : '' }}</span>
              <button type="button" class="btn btn--link btn--sm" (click)="editRule(rule)">Edit</button>
              <gggs-status-badge [status]="rule.isActive ? 'Active' : 'Inactive'" />
            </div>
          }
          <h2 class="xp__heading">Budgets {{ year }}</h2>
          <div class="toolbar"><button type="button" class="btn btn--sm" (click)="openBudget()">Set a budget</button></div>
          @for (budget of budgets(); track budget.id) {
            <div class="xp__row">
              <span><strong>{{ budget.department }}</strong></span>
              <span>{{ budget.used }} of {{ budget.points }} points used</span>
              <span class="xp__bar"><span [style.width.%]="budget.usedPercent > 100 ? 100 : budget.usedPercent"></span></span>
              <span>{{ budget.usedPercent }}%</span>
            </div>
          } @empty {
            <p class="muted">No budgets - manager awards are unlimited.</p>
          }
        }
      }
      @if (notice()) {
        <p class="xp__notice" role="status">{{ notice() }}</p>
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="itemOpen" [title]="itemEditing() ? 'Edit item' : 'New item'" [busy]="busy()" [error]="formError()" [saveDisabled]="!itemForm().name.trim() || !itemForm().category.trim()" (save)="saveItem()">
      <div class="xp__grid">
        <label class="field"><span class="field__label">Name</span><input type="text" maxlength="150" [value]="itemForm().name" (input)="patchItem({ name: $any($event.target).value })" /></label>
        <label class="field"><span class="field__label">Category</span><input type="text" maxlength="60" [value]="itemForm().category" (input)="patchItem({ category: $any($event.target).value })" /></label>
        <label class="field"><span class="field__label">Points</span><input type="number" min="1" [value]="itemForm().pointsCost" (input)="patchItem({ pointsCost: +$any($event.target).value })" /></label>
        <label class="field"><span class="field__label">Stock (empty for unlimited)</span><input type="number" min="0" [value]="itemForm().stock ?? ''" (input)="patchItem({ stock: num($any($event.target).value) })" /></label>
        <label class="field xp__wide"><span class="field__label">Description</span><input type="text" maxlength="1000" [value]="itemForm().description ?? ''" (input)="patchItem({ description: $any($event.target).value || null })" /></label>
        <label class="xp__flag"><input type="checkbox" [checked]="itemForm().isActive" (change)="patchItem({ isActive: $any($event.target).checked })" /> Available</label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="typeOpen" [title]="typeEditing() ? 'Edit recognition type' : 'New recognition type'" [busy]="busy()" [error]="formError()" [saveDisabled]="!typeForm().code.trim() || !typeForm().name.trim()" (save)="saveType()">
      <div class="xp__grid">
        <label class="field"><span class="field__label">Code</span><input type="text" maxlength="30" [value]="typeForm().code" (input)="patchType({ code: $any($event.target).value.toUpperCase() })" /></label>
        <label class="field"><span class="field__label">Name</span><input type="text" maxlength="100" [value]="typeForm().name" (input)="patchType({ name: $any($event.target).value })" /></label>
        <label class="field">
          <span class="field__label">Given by</span>
          <select [value]="typeForm().kind" (change)="patchType({ kind: $any($event.target).value })">
            @for (kind of kinds; track kind) {
              <option [value]="kind">{{ kind === 'Peer' ? 'Anyone (peer)' : kind === 'Manager' ? 'Managers to their team' : 'The programme team (award)' }}</option>
            }
          </select>
        </label>
        <label class="field"><span class="field__label">Points</span><input type="number" min="0" [value]="typeForm().points" (input)="patchType({ points: +$any($event.target).value })" /></label>
        <label class="field"><span class="field__label">Cash through payroll</span><input type="number" min="0" [value]="typeForm().cashAmount ?? ''" (input)="patchType({ cashAmount: num($any($event.target).value) })" /></label>
        <label class="field xp__wide"><span class="field__label">Description</span><input type="text" maxlength="500" [value]="typeForm().description ?? ''" (input)="patchType({ description: $any($event.target).value || null })" /></label>
        <label class="xp__flag"><input type="checkbox" [checked]="typeForm().requiresApproval" (change)="patchType({ requiresApproval: $any($event.target).checked })" /> Needs approval</label>
        <label class="xp__flag"><input type="checkbox" [checked]="typeForm().isActive" (change)="patchType({ isActive: $any($event.target).checked })" /> Active</label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="ruleOpen" [title]="ruleEditing() ? 'Edit rule' : 'New rule'" [busy]="busy()" [error]="formError()" [saveDisabled]="!ruleForm().name.trim() || !ruleForm().message.trim()" (save)="saveRule()">
      <div class="xp__grid">
        <label class="field"><span class="field__label">Name</span><input type="text" maxlength="100" [value]="ruleForm().name" (input)="patchRule({ name: $any($event.target).value })" /></label>
        <label class="field">
          <span class="field__label">When</span>
          <select [value]="ruleForm().trigger" (change)="patchRule({ trigger: $any($event.target).value })">
            @for (trigger of triggers; track trigger) {
              <option [value]="trigger">{{ humanize(trigger) }}</option>
            }
          </select>
        </label>
        <label class="field"><span class="field__label">Points</span><input type="number" min="1" [value]="ruleForm().points" (input)="patchRule({ points: +$any($event.target).value })" /></label>
        <label class="field"><span class="field__label">Minimum (years or rating)</span><input type="number" min="0" [value]="ruleForm().threshold" (input)="patchRule({ threshold: +$any($event.target).value })" /></label>
        <label class="field xp__wide"><span class="field__label">Message ({{ '{' }}years{{ '}' }}, {{ '{' }}rating{{ '}' }}, {{ '{' }}course{{ '}' }} fill in)</span><input type="text" maxlength="300" [value]="ruleForm().message" (input)="patchRule({ message: $any($event.target).value })" /></label>
        <label class="xp__flag"><input type="checkbox" [checked]="ruleForm().perYear" (change)="patchRule({ perYear: $any($event.target).checked })" /> Multiply by years (anniversaries)</label>
        <label class="xp__flag"><input type="checkbox" [checked]="ruleForm().isActive" (change)="patchRule({ isActive: $any($event.target).checked })" /> Active</label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="budgetOpen" title="Set a budget" [busy]="busy()" [error]="formError()" (save)="saveBudget()">
      <div class="xp__grid">
        <label class="field">
          <span class="field__label">Department</span>
          <select [value]="budgetDepartment()" (change)="budgetDepartment.set($any($event.target).value)">
            <option value="">Company-wide</option>
            @for (department of departments(); track department.id) {
              <option [value]="department.id">{{ department.name }}</option>
            }
          </select>
        </label>
        <label class="field"><span class="field__label">Points for {{ year }}</span><input type="number" min="0" [value]="budgetPoints()" (input)="budgetPoints.set(+$any($event.target).value)" /></label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="adjustOpen" title="Adjust points" [busy]="busy()" [error]="formError()" [saveDisabled]="!adjustEmployee() || !adjustPoints() || !adjustNote().trim()" (save)="adjust()">
      <div class="stack">
        <label class="field"><span class="field__label">Employee</span><gggs-employee-select [ngModel]="adjustEmployee()" (ngModelChange)="adjustEmployee.set($event)" /></label>
        <label class="field"><span class="field__label">Points (negative to take away)</span><input type="number" [value]="adjustPoints()" (input)="adjustPoints.set(+$any($event.target).value)" /></label>
        <label class="field"><span class="field__label">Why</span><input type="text" maxlength="250" [value]="adjustNote()" (input)="adjustNote.set($any($event.target).value)" /></label>
      </div>
    </gggs-form-dialog>
  `,
  styleUrl: './xp.css',
})
export class RewardsPage {
  private readonly api = inject(RewardsApi);
  private readonly permissions = inject(PermissionStore);

  protected readonly canProgram = this.permissions.has('reward.program.manage');
  protected readonly canCatalog = this.permissions.has('reward.catalog.manage');
  protected readonly canRedeem = this.permissions.has('reward.redemption.manage');
  private readonly canView = this.permissions.has('reward.recognition.view');
  protected readonly humanize = humanize;
  protected readonly num = num;
  protected readonly kinds = RECOGNITION_KINDS;
  protected readonly triggers = RULE_TRIGGERS;
  protected readonly year = new Date().getFullYear();
  protected readonly tabs: readonly TabItem[] = [
    { key: 'feed', label: 'Recognition', disabled: !this.canView },
    { key: 'redemptions', label: 'Redemptions', disabled: !this.canRedeem },
    { key: 'catalogue', label: 'Catalogue' },
    { key: 'programme', label: 'Programme', disabled: !this.canProgram },
  ];
  protected readonly tab = signal(this.canView ? 'feed' : 'catalogue');
  protected readonly dashboard = signal<RewardDashboard | null>(null);
  protected readonly recognitions = signal<readonly RecognitionItem[]>([]);
  protected readonly redemptions = signal<readonly RedemptionItem[]>([]);
  protected readonly catalog = signal<readonly CatalogItem[]>([]);
  protected readonly types = signal<readonly RecognitionTypeItem[]>([]);
  protected readonly rules = signal<readonly RewardRuleItem[]>([]);
  protected readonly budgets = signal<readonly RewardBudgetItem[]>([]);
  protected readonly settings = signal<RewardSettings | null>(null);
  protected readonly departments = signal<readonly OrgUnit[]>([]);
  protected readonly itemOpen = signal(false);
  protected readonly itemEditing = signal<CatalogItem | null>(null);
  protected readonly itemForm = signal<CatalogItemInput>({ name: '', description: null, category: 'Vouchers', pointsCost: 500, stock: null, isActive: true });
  protected readonly typeOpen = signal(false);
  protected readonly typeEditing = signal<RecognitionTypeItem | null>(null);
  protected readonly typeForm = signal<RecognitionTypeInput>({ code: '', name: '', description: null, kind: 'Peer', points: 50, requiresApproval: false, cashAmount: null, isActive: true });
  protected readonly ruleOpen = signal(false);
  protected readonly ruleEditing = signal<RewardRuleItem | null>(null);
  protected readonly ruleForm = signal<RewardRuleInput>({ name: '', trigger: 'WorkAnniversary', points: 100, threshold: 1, perYear: false, message: '', isActive: true });
  protected readonly budgetOpen = signal(false);
  protected readonly budgetDepartment = signal('');
  protected readonly budgetPoints = signal(10000);
  protected readonly adjustOpen = signal(false);
  protected readonly adjustEmployee = signal<string | null>(null);
  protected readonly adjustPoints = signal(0);
  protected readonly adjustNote = signal('');
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    if (this.canView || this.canProgram) {
      this.api.dashboard().then((d) => this.dashboard.set(d), (error: unknown) => this.error.set(message(error, 'The overview could not be loaded.')));
    }

    if (this.canView) {
      this.api.recognitions().then((items) => this.recognitions.set(items), () => undefined);
    }

    if (this.canRedeem) {
      this.api.redemptions().then((items) => this.redemptions.set(items), () => undefined);
    }

    this.api.catalog(this.canCatalog).then((items) => this.catalog.set(items), () => undefined);
    if (this.canProgram) {
      this.api.types().then((types) => this.types.set(types), () => undefined);
      this.api.rules().then((rules) => this.rules.set(rules), () => undefined);
      this.api.budgets().then((budgets) => this.budgets.set(budgets), () => undefined);
      this.api.settings().then((settings) => this.settings.set(settings), () => undefined);
      inject(OrgStructureApi)
        .active('departments')
        .then((departments) => this.departments.set(departments), () => undefined);
    }
  }

  protected async redemptionAction(redemption: RedemptionItem, action: 'fulfil' | 'cancel'): Promise<void> {
    const note = prompt(action === 'fulfil' ? 'Note for the employee (voucher code, delivery date):' : 'Why is it cancelled? The points are refunded.') ?? null;
    try {
      await this.api.redemptionAction(redemption.id, action, note);
      this.redemptions.set(await this.api.redemptions());
    } catch (error) {
      this.error.set(message(error, 'The redemption could not be changed.'));
    }
  }

  protected patchSettings(change: Partial<RewardSettings>): void {
    this.settings.update((settings) => (settings ? { ...settings, ...change } : settings));
  }

  protected async saveSettings(): Promise<void> {
    const settings = this.settings();
    if (settings) {
      try {
        this.settings.set(await this.api.saveSettings(settings));
        this.notice.set('Settings saved.');
      } catch (error) {
        this.error.set(message(error, 'The settings could not be saved.'));
      }
    }
  }

  protected patchItem(change: Partial<CatalogItemInput>): void {
    this.itemForm.update((form) => ({ ...form, ...change }));
  }

  protected editItem(item: CatalogItem | null): void {
    this.itemEditing.set(item);
    this.itemForm.set(item ? { name: item.name, description: item.description ?? null, category: item.category, pointsCost: item.pointsCost, stock: item.stock ?? null, isActive: item.isActive } : { name: '', description: null, category: 'Vouchers', pointsCost: 500, stock: null, isActive: true });
    this.formError.set(null);
    this.itemOpen.set(true);
  }

  protected saveItem(): Promise<void> {
    return this.run(async () => {
      this.catalog.set(await this.api.saveItem(this.itemEditing()?.id ?? null, this.itemForm()));
      this.itemOpen.set(false);
    });
  }

  protected patchType(change: Partial<RecognitionTypeInput>): void {
    this.typeForm.update((form) => ({ ...form, ...change }));
  }

  protected editType(type: RecognitionTypeItem | null): void {
    this.typeEditing.set(type);
    this.typeForm.set(
      type
        ? { code: type.code, name: type.name, description: type.description ?? null, kind: type.kind, points: type.points, requiresApproval: type.requiresApproval, cashAmount: type.cashAmount ?? null, isActive: type.isActive }
        : { code: '', name: '', description: null, kind: 'Peer', points: 50, requiresApproval: false, cashAmount: null, isActive: true },
    );
    this.formError.set(null);
    this.typeOpen.set(true);
  }

  protected saveType(): Promise<void> {
    return this.run(async () => {
      this.types.set(await this.api.saveType(this.typeEditing()?.id ?? null, this.typeForm()));
      this.typeOpen.set(false);
    });
  }

  protected patchRule(change: Partial<RewardRuleInput>): void {
    this.ruleForm.update((form) => ({ ...form, ...change }));
  }

  protected editRule(rule: RewardRuleItem | null): void {
    this.ruleEditing.set(rule);
    this.ruleForm.set(rule ? { name: rule.name, trigger: rule.trigger, points: rule.points, threshold: rule.threshold, perYear: rule.perYear, message: rule.message, isActive: rule.isActive } : { name: '', trigger: 'WorkAnniversary', points: 100, threshold: 1, perYear: false, message: '', isActive: true });
    this.formError.set(null);
    this.ruleOpen.set(true);
  }

  protected saveRule(): Promise<void> {
    return this.run(async () => {
      this.rules.set(await this.api.saveRule(this.ruleEditing()?.id ?? null, this.ruleForm()));
      this.ruleOpen.set(false);
    });
  }

  protected openBudget(): void {
    this.budgetDepartment.set('');
    this.budgetPoints.set(10000);
    this.formError.set(null);
    this.budgetOpen.set(true);
  }

  protected saveBudget(): Promise<void> {
    return this.run(async () => {
      this.budgets.set(await this.api.saveBudget({ year: this.year, departmentId: this.budgetDepartment() || null, points: this.budgetPoints() }));
      this.budgetOpen.set(false);
    });
  }

  protected adjust(): Promise<void> {
    return this.run(async () => {
      await this.api.adjust(this.adjustEmployee() ?? '', this.adjustPoints(), this.adjustNote());
      this.adjustOpen.set(false);
      this.notice.set('Points adjusted.');
    });
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await work();
    } catch (error) {
      this.formError.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** My rewards: my points, recognising colleagues, the feed, and the catalogue to spend points in. */
@Component({
  selector: 'gggs-my-rewards-page',
  imports: [FormsModule, DatePipe, PageHeader, KpiCard, StatusBadge, EmptyState, FormDialog, Tabs, EmployeeSelect, RecognitionFeed],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My rewards" subtitle="Thank colleagues, collect points and spend them on rewards.">
      @if (canGive) {
        <button type="button" class="btn btn--primary" (click)="openGive()"><i class="pi pi-star" aria-hidden="true"></i> Recognise someone</button>
      }
    </gggs-page-header>
    @if (wallet(); as w) {
      <div class="xp__kpis">
        <gggs-kpi-card label="My points" [value]="w.balance" [hint]="'worth ' + w.balance * w.pointValue + ' ' + w.currency" />
        <gggs-kpi-card label="Expiring in 30 days" [value]="w.expiringSoon" />
        <gggs-kpi-card label="Earned this year" [value]="w.earnedThisYear" />
        <gggs-kpi-card label="Left to give this month" [value]="w.peerAllowanceLeft" [hint]="'of ' + w.peerAllowance" />
      </div>
    }
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />
    <section class="panel stack">
      @switch (tab()) {
        @case ('feed') {
          <gggs-recognition-feed [items]="feed()" />
        }
        @case ('catalogue') {
          <div class="xp__cards">
            @for (item of catalog(); track item.id) {
              <div class="xp__card">
                <strong>{{ item.name }}</strong>
                <span class="muted">{{ item.category }}{{ item.stock !== undefined ? ' · ' + item.stock + ' left' : '' }}</span>
                <span>{{ item.description ?? '' }}</span>
                <span class="toolbar">
                  <strong>{{ item.pointsCost }} points</strong>
                  <button type="button" class="btn btn--sm btn--primary" [disabled]="balance() < item.pointsCost || item.stock === 0" (click)="redeem(item)">Redeem</button>
                </span>
              </div>
            } @empty {
              <gggs-empty-state icon="pi pi-gift" title="The catalogue is empty" />
            }
          </div>
        }
        @case ('redemptions') {
          @for (redemption of redemptions(); track redemption.id) {
            <div class="xp__row">
              <span><strong>{{ redemption.itemName }}</strong></span>
              <span>{{ redemption.points }} points</span>
              <span class="muted">{{ redemption.requestedOn | date: 'd MMM y' }}{{ redemption.note ? ' · ' + redemption.note : '' }}</span>
              <span>
                @if (redemption.status === 'Requested') {
                  <button type="button" class="btn btn--link btn--sm" (click)="cancel(redemption.id)">Cancel</button>
                }
              </span>
              <gggs-status-badge [status]="redemption.status" />
            </div>
          } @empty {
            <gggs-empty-state icon="pi pi-gift" title="No redemptions yet" />
          }
        }
        @default {
          @for (entry of ledger(); track entry.id) {
            <div class="xp__row">
              <span><strong>{{ entry.note }}</strong></span>
              <span>{{ entry.kind }}</span>
              <span class="muted">{{ entry.occurredOn | date: 'd MMM y' }}{{ entry.expiresOn && entry.remainingPoints ? ' · ' + entry.remainingPoints + ' lapse ' + (entry.expiresOn | date: 'd MMM y') : '' }}</span>
              <strong [class.xp__danger]="entry.points < 0">{{ entry.points > 0 ? '+' : '' }}{{ entry.points }}</strong>
            </div>
          } @empty {
            <gggs-empty-state icon="pi pi-list" title="No points yet" />
          }
        }
      }
      @if (notice()) {
        <p class="xp__notice" role="status">{{ notice() }}</p>
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="giveOpen" title="Recognise someone" saveLabel="Send" [busy]="busy()" [error]="formError()" [saveDisabled]="!recipient() || !typeId() || !note().trim()" (save)="give()">
      <div class="stack">
        <label class="field"><span class="field__label">Who</span><gggs-employee-select [ngModel]="recipient()" (ngModelChange)="recipient.set($event)" /></label>
        <label class="field">
          <span class="field__label">For</span>
          <select [value]="typeId()" (change)="typeId.set($any($event.target).value)">
            <option value="">Choose</option>
            @for (type of types(); track type.id) {
              <option [value]="type.id">{{ type.name }} · {{ type.points }} points{{ type.kind !== 'Peer' ? ' (' + type.kind.toLowerCase() + ')' : '' }}</option>
            }
          </select>
        </label>
        <label class="field"><span class="field__label">Message</span><textarea rows="3" maxlength="1000" [value]="note()" (input)="note.set($any($event.target).value)"></textarea></label>
        <label class="xp__flag"><input type="checkbox" [checked]="isPublic()" (change)="isPublic.set($any($event.target).checked)" /> Show on the feed</label>
      </div>
    </gggs-form-dialog>
  `,
  styleUrl: './xp.css',
})
export class MyRewardsPage {
  private readonly api = inject(RewardsApi);

  protected readonly canGive = inject(PermissionStore).has('self.recognition.give');
  protected readonly tabs: readonly TabItem[] = [
    { key: 'feed', label: 'Feed' },
    { key: 'catalogue', label: 'Catalogue' },
    { key: 'redemptions', label: 'My redemptions' },
    { key: 'ledger', label: 'Points history' },
  ];
  protected readonly tab = signal('feed');
  protected readonly wallet = signal<RewardWallet | null>(null);
  protected readonly balance = computed(() => this.wallet()?.balance ?? 0);
  protected readonly feed = signal<readonly RecognitionItem[]>([]);
  protected readonly catalog = signal<readonly CatalogItem[]>([]);
  protected readonly redemptions = signal<readonly RedemptionItem[]>([]);
  protected readonly ledger = signal<readonly PointLedgerItem[]>([]);
  protected readonly types = signal<readonly RecognitionTypeItem[]>([]);
  protected readonly giveOpen = signal(false);
  protected readonly recipient = signal<string | null>(null);
  protected readonly typeId = signal('');
  protected readonly note = signal('');
  protected readonly isPublic = signal(true);
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.refresh();
    this.api.feed().then((feed) => this.feed.set(feed), () => undefined);
    this.api.catalog().then((catalog) => this.catalog.set(catalog), () => undefined);
    if (this.canGive) {
      this.api.types().then((types) => this.types.set(types.filter((type) => type.isActive)), () => undefined);
    }
  }

  protected openGive(): void {
    this.recipient.set(null);
    this.typeId.set('');
    this.note.set('');
    this.isPublic.set(true);
    this.formError.set(null);
    this.giveOpen.set(true);
  }

  protected async give(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      const given = await this.api.give({ recipientEmployeeId: this.recipient() ?? '', typeId: this.typeId(), message: this.note(), isPublic: this.isPublic() });
      this.giveOpen.set(false);
      this.notice.set(given.status === 'Pending' ? 'Sent for approval.' : `${given.recipientName} has been recognised.`);
      this.feed.set(await this.api.feed());
      this.refresh();
    } catch (error) {
      this.formError.set(message(error, 'The recognition could not be sent.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async redeem(item: CatalogItem): Promise<void> {
    if (!confirm(`Spend ${item.pointsCost} points on ${item.name}?`)) {
      return;
    }

    try {
      await this.api.redeem(item.id);
      this.notice.set(`${item.name} is on its way - the rewards team will be in touch.`);
      this.catalog.set(await this.api.catalog());
      this.refresh();
    } catch (error) {
      this.error.set(message(error, 'The reward could not be redeemed.'));
    }
  }

  protected async cancel(id: string): Promise<void> {
    try {
      await this.api.cancelMine(id);
      this.refresh();
    } catch (error) {
      this.error.set(message(error, 'The redemption could not be cancelled.'));
    }
  }

  private refresh(): void {
    this.api.wallet().then((wallet) => this.wallet.set(wallet), (error: unknown) => this.error.set(message(error, 'Your points could not be loaded.')));
    this.api.ledger().then((ledger) => this.ledger.set(ledger), () => undefined);
    this.api.myRedemptions().then((redemptions) => this.redemptions.set(redemptions), () => undefined);
  }
}
