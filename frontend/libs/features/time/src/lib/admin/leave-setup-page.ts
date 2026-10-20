import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { LookupSelect } from '@gggs/shared/master-data';
import type { LeavePolicy, LeavePolicyInput, LeaveType } from '@gggs/shared/models';
import { LOOKUP_LISTS } from '@gggs/shared/models';
import { LeaveApi } from '@gggs/shared/time';
import { FormDialog, PageHeader, StatusBadge } from '@gggs/shared/ui';

type PolicyDraft = { -readonly [K in keyof LeavePolicyInput]: LeavePolicyInput[K] };

const blankPolicy = (typeId: string): PolicyDraft => ({
  leaveTypeId: typeId,
  employmentTypeCode: undefined,
  annualEntitlement: 12,
  accrual: 'Monthly',
  carryForwardLimit: 0,
  allowDuringProbation: true,
  maxNegativeBalance: 0,
  maxConsecutiveDays: undefined,
  minNoticeDays: 0,
  countSandwichedDays: false,
});

/**
 * Leave types and the rules each follows (spec §20): entitlement and accrual, carry-forward,
 * probation, notice, the longest stretch, negative balances and the sandwich rule.
 */
@Component({
  selector: 'gggs-leave-setup-page',
  imports: [DecimalPipe, ReactiveFormsModule, RouterLink, HasPermissionDirective, PageHeader, StatusBadge, FormDialog, LookupSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Leave types & policies" subtitle="What kinds of leave exist, and the rules each follows.">
      <div class="toolbar">
        <button type="button" class="btn" (click)="openAccrual()">Run accrual</button>
        <a class="btn" routerLink="../requests">Back to leave</a>
      </div>
    </gggs-page-header>

    <div class="stack">
      <section class="panel stack">
        <div class="toolbar">
          <h2 class="setup__heading">Leave types</h2>
          <button type="button" class="btn btn--primary btn--sm" *gggsHasPermission="'leave.type.manage'" (click)="openType(null)">Add type</button>
        </div>
        <table class="grid">
          <thead>
            <tr>
              <th scope="col">Type</th>
              <th scope="col">Paid</th>
              <th scope="col">Half days</th>
              <th scope="col">Status</th>
              <th scope="col"><span class="grid__hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (type of types(); track type.id) {
              <tr>
                <td><span class="setup__dot" [style.background]="type.color" aria-hidden="true"></span> <strong>{{ type.name }}</strong> <span class="muted">{{ type.code }}</span></td>
                <td>{{ type.isPaid ? 'Paid' : 'Unpaid' }}</td>
                <td>{{ type.allowHalfDay ? 'Allowed' : 'No' }}</td>
                <td><gggs-status-badge [status]="type.isActive ? 'Active' : 'Inactive'" /></td>
                <td class="grid__actions" *gggsHasPermission="'leave.type.manage'">
                  <button type="button" class="btn btn--link btn--sm" (click)="openType(type)">Edit</button>
                  <button type="button" class="btn btn--link btn--sm" (click)="toggleType(type)">{{ type.isActive ? 'Retire' : 'Restore' }}</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </section>

      <section class="panel stack">
        <div class="toolbar">
          <h2 class="setup__heading">Policies</h2>
          <button type="button" class="btn btn--primary btn--sm" (click)="openPolicy(null)">Add policy</button>
        </div>
        <p class="muted setup__hint">The most specific active policy applies: an employment type's own, else the one for everyone.</p>
        <table class="grid">
          <thead>
            <tr>
              <th scope="col">Type</th>
              <th scope="col">For</th>
              <th scope="col">Per year</th>
              <th scope="col">Accrual</th>
              <th scope="col">Carry forward</th>
              <th scope="col">Rules</th>
              <th scope="col">Status</th>
              <th scope="col"><span class="grid__hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (policy of policies(); track policy.id) {
              <tr>
                <td>{{ policy.leaveTypeName }}</td>
                <td>{{ policy.employmentTypeCode ?? 'Everyone' }}</td>
                <td>{{ policy.annualEntitlement | number: '1.0-2' }}</td>
                <td>{{ policy.accrual }}</td>
                <td>{{ policy.carryForwardLimit === null || policy.carryForwardLimit === undefined ? 'All' : (policy.carryForwardLimit | number: '1.0-2') }}</td>
                <td class="setup__rules">{{ rules(policy) }}</td>
                <td><gggs-status-badge [status]="policy.isActive ? 'Active' : 'Inactive'" /></td>
                <td class="grid__actions">
                  <button type="button" class="btn btn--link btn--sm" (click)="openPolicy(policy)">Edit</button>
                  <button type="button" class="btn btn--link btn--sm" (click)="togglePolicy(policy)">{{ policy.isActive ? 'Retire' : 'Restore' }}</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </section>
      @if (message()) {
        <p class="setup__message" role="status">{{ message() }}</p>
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </div>

    <gggs-form-dialog [(visible)]="typeOpen" [title]="editingType() ? 'Edit leave type' : 'Add leave type'" [busy]="busy()" [error]="dialogError()" (save)="saveType()">
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Code *</span>
          <input type="text" maxlength="16" [disabled]="!!editingType()" [value]="typeCode()" (input)="typeCode.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Name *</span>
          <input type="text" maxlength="100" [value]="typeName()" (input)="typeName.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Colour</span>
          <input type="color" [value]="typeColor()" (input)="typeColor.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span><input type="checkbox" [checked]="typePaid()" (change)="typePaid.set($any($event.target).checked)" /> Paid</span>
        </label>
        <label class="field">
          <span><input type="checkbox" [checked]="typeHalf()" (change)="typeHalf.set($any($event.target).checked)" /> Half days allowed</span>
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="policyOpen" [title]="editingPolicy() ? 'Edit policy' : 'Add policy'" size="lg" [busy]="busy()" [error]="dialogError()" (save)="savePolicy()">
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Leave type *</span>
          <select [disabled]="!!editingPolicy()" [value]="policy().leaveTypeId" (change)="patch('leaveTypeId', $any($event.target).value)">
            @for (type of types(); track type.id) {
              <option [value]="type.id">{{ type.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">For employment type</span>
          <gggs-lookup-select [list]="lists.employmentType" placeholder="Everyone" ariaLabel="Employment type" [formControl]="employmentType" />
        </label>
        <label class="field">
          <span class="field__label">Days per year *</span>
          <input type="number" min="0" max="366" step="0.5" [value]="policy().annualEntitlement" (input)="patch('annualEntitlement', +$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Accrual</span>
          <select [value]="policy().accrual" (change)="patch('accrual', $any($event.target).value)">
            <option value="Monthly">A twelfth each month</option>
            <option value="Yearly">All at the start of the year</option>
            <option value="None">Granted by hand</option>
          </select>
        </label>
        <label class="field">
          <span class="field__label">Carry forward up to <span class="muted">(empty: all)</span></span>
          <input type="number" min="0" step="0.5" [value]="policy().carryForwardLimit ?? ''" (input)="patch('carryForwardLimit', numberOrNull($any($event.target).value))" />
        </label>
        <label class="field">
          <span class="field__label">May go negative by</span>
          <input type="number" min="0" step="0.5" [value]="policy().maxNegativeBalance" (input)="patch('maxNegativeBalance', +$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Longest stretch (days) <span class="muted">(empty: no limit)</span></span>
          <input type="number" min="1" [value]="policy().maxConsecutiveDays ?? ''" (input)="patch('maxConsecutiveDays', numberOrNull($any($event.target).value))" />
        </label>
        <label class="field">
          <span class="field__label">Notice (days)</span>
          <input type="number" min="0" [value]="policy().minNoticeDays" (input)="patch('minNoticeDays', +$any($event.target).value)" />
        </label>
        <label class="field">
          <span><input type="checkbox" [checked]="policy().allowDuringProbation" (change)="patch('allowDuringProbation', $any($event.target).checked)" /> Allowed during probation</span>
        </label>
        <label class="field">
          <span><input type="checkbox" [checked]="policy().countSandwichedDays" (change)="patch('countSandwichedDays', $any($event.target).checked)" /> Sandwich rule: count weekends and holidays between leave days</span>
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="accrualOpen" title="Run accrual" saveLabel="Run" [busy]="busy()" [error]="dialogError()" (save)="runAccrual()">
      <div class="stack">
        <p class="muted">Accrual runs by itself on the first of each month. Running a month again adds nothing that was already credited.</p>
        <label class="field">
          <span class="field__label">Month</span>
          <input type="month" [value]="accrualMonth()" (input)="accrualMonth.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .setup__heading {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .setup__hint {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .setup__dot {
      display: inline-block;
      width: 0.6rem;
      height: 0.6rem;
      border-radius: 50%;
    }

    .setup__rules {
      font-size: var(--gggs-font-size-sm);
    }

    .setup__message {
      color: var(--gggs-color-success);
    }

    .grid {
      width: 100%;
      border-collapse: collapse;
    }

    .grid th,
    .grid td {
      padding: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
    }

    .grid__actions {
      white-space: nowrap;
    }

    .grid__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class LeaveSetupPage {
  private readonly api = inject(LeaveApi);

  protected readonly lists = LOOKUP_LISTS;
  protected readonly types = signal<readonly LeaveType[]>([]);
  protected readonly policies = signal<readonly LeavePolicy[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  protected readonly typeOpen = signal(false);
  protected readonly editingType = signal<LeaveType | null>(null);
  protected readonly typeCode = signal('');
  protected readonly typeName = signal('');
  protected readonly typeColor = signal('#1E88E5');
  protected readonly typePaid = signal(true);
  protected readonly typeHalf = signal(true);

  protected readonly policyOpen = signal(false);
  protected readonly editingPolicy = signal<LeavePolicy | null>(null);
  protected readonly policy = signal<PolicyDraft>(blankPolicy(''));
  protected readonly employmentType = new FormControl<string | null>(null);

  protected readonly accrualOpen = signal(false);
  protected readonly accrualMonth = signal(new Date().toISOString().slice(0, 7));

  constructor() {
    void this.load();
  }

  protected rules(policy: LeavePolicy): string {
    const parts: string[] = [];
    if (!policy.allowDuringProbation) {
      parts.push('not in probation');
    }
    if (policy.minNoticeDays) {
      parts.push(`${policy.minNoticeDays}d notice`);
    }
    if (policy.maxConsecutiveDays) {
      parts.push(`max ${policy.maxConsecutiveDays}d at a time`);
    }
    if (policy.maxNegativeBalance) {
      parts.push(`can go ${policy.maxNegativeBalance}d negative`);
    }
    if (policy.countSandwichedDays) {
      parts.push('sandwich rule');
    }
    return parts.join(', ') || '—';
  }

  protected numberOrNull(value: string): number | undefined {
    return value === '' ? undefined : Number(value);
  }

  protected patch<K extends keyof PolicyDraft>(field: K, value: PolicyDraft[K]): void {
    this.policy.update((draft) => ({ ...draft, [field]: value }));
  }

  protected openType(type: LeaveType | null): void {
    this.editingType.set(type);
    this.typeCode.set(type?.code ?? '');
    this.typeName.set(type?.name ?? '');
    this.typeColor.set(type?.color ?? '#1E88E5');
    this.typePaid.set(type?.isPaid ?? true);
    this.typeHalf.set(type?.allowHalfDay ?? true);
    this.dialogError.set(null);
    this.typeOpen.set(true);
  }

  protected async saveType(): Promise<void> {
    if (!this.typeCode().trim() || !this.typeName().trim()) {
      this.dialogError.set('Give the type a code and a name.');
      return;
    }
    await this.inDialog(async () => {
      await this.api.saveType(this.editingType()?.id ?? null, {
        code: this.typeCode().trim(),
        name: this.typeName().trim(),
        color: this.typeColor(),
        isPaid: this.typePaid(),
        allowHalfDay: this.typeHalf(),
      });
      this.typeOpen.set(false);
    });
  }

  protected async toggleType(type: LeaveType): Promise<void> {
    await this.inPage(() => this.api.setTypeActive(type.id, !type.isActive));
  }

  protected openPolicy(policy: LeavePolicy | null): void {
    this.editingPolicy.set(policy);
    this.policy.set(policy ? { ...policy } : blankPolicy(this.types()[0]?.id ?? ''));
    this.employmentType.setValue(policy?.employmentTypeCode ?? null);
    this.dialogError.set(null);
    this.policyOpen.set(true);
  }

  protected async savePolicy(): Promise<void> {
    const draft: LeavePolicyInput = { ...this.policy(), employmentTypeCode: this.employmentType.value ?? undefined };
    await this.inDialog(async () => {
      await this.api.savePolicy(this.editingPolicy()?.id ?? null, draft);
      this.policyOpen.set(false);
    });
  }

  protected async togglePolicy(policy: LeavePolicy): Promise<void> {
    await this.inPage(() => this.api.setPolicyActive(policy.id, !policy.isActive));
  }

  protected openAccrual(): void {
    this.dialogError.set(null);
    this.accrualOpen.set(true);
  }

  protected async runAccrual(): Promise<void> {
    const [year, month] = this.accrualMonth().split('-').map(Number);
    await this.inDialog(async () => {
      const result = await this.api.runAccrual(year, month);
      this.accrualOpen.set(false);
      this.message.set(`Accrual ${result.period}: ${result.accrued} credit(s), ${result.lapsed} lapse(s).`);
    });
  }

  private async inDialog(call: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await call();
      await this.load();
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async inPage(call: () => Promise<void>): Promise<void> {
    this.error.set(null);
    try {
      await call();
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    }
  }

  private async load(): Promise<void> {
    try {
      const [types, policies] = await Promise.all([this.api.types(true), this.api.policies()]);
      this.types.set(types);
      this.policies.set(policies);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Leave setup could not be loaded.');
    }
  }
}
