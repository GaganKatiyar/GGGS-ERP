import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { Button, Card, FormField, PageHeader } from '@gggs/shared/ui';
import type {
  AccrualFrequency,
  LeavePolicyListItem,
  LeavePolicyRuleListItem,
  LeaveTypeListItem,
} from '@gggs/shared/models';
import { LeaveApi } from '../leave-api';

/**
 * Leave policy administration - `leave.policy.manage`'s screen, `/leave/policies` in the
 * navigation manifest.
 *
 * v1 has exactly one policy per tenant (`LeavePolicyResolver`'s own remarks explain why: no
 * `PolicyAssignment` targeting yet), so this page is "the policy" plus its per-leave-type rules,
 * not a list of many policies to choose between.
 */
@Component({
  selector: 'gggs-leave-policies-page',
  imports: [ReactiveFormsModule, PageHeader, Card, Button, FormField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header
      title="Leave policy"
      subtitle="One policy per tenant for now - configure a rule per leave type."
    />

    @if (!policy()) {
      <gggs-card title="Create the tenant's leave policy">
        <form [formGroup]="policyForm" (ngSubmit)="createPolicy()" novalidate class="inline-form">
          <gggs-form-field label="Policy name" required>
            <input type="text" formControlName="name" maxlength="150" required />
          </gggs-form-field>
          <gggs-button type="submit" [loading]="isSavingPolicy()">Create</gggs-button>
        </form>
      </gggs-card>
    } @else {
      <gggs-card [title]="policy()!.name" class="section">
        <p class="muted">Version {{ policy()!.version }}</p>
      </gggs-card>

      <gggs-card title="Rules by leave type">
        @if (rules().length > 0) {
          <table class="rules-table">
            <thead>
              <tr>
                <th scope="col">Leave type</th>
                <th scope="col">Accrual</th>
                <th scope="col">Eligible after</th>
                <th scope="col">Min notice</th>
                <th scope="col">Half day</th>
              </tr>
            </thead>
            <tbody>
              @for (rule of rules(); track rule.id) {
                <tr>
                  <td>{{ leaveTypeName(rule.leaveTypeId) }}</td>
                  <td>{{ rule.accrualAmount }} / {{ rule.accrualFrequency }}</td>
                  <td>{{ rule.eligibleAfterDays }} days</td>
                  <td>{{ rule.minNoticeDays }} days</td>
                  <td>{{ rule.allowHalfDay ? 'Allowed' : 'Not allowed' }}</td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No rules configured yet.</p>
        }

        <form [formGroup]="ruleForm" (ngSubmit)="addRule()" novalidate class="rule-form">
          <gggs-form-field label="Leave type" required>
            <select formControlName="leaveTypeId" required>
              <option value="" disabled>Select a leave type</option>
              @for (type of unruledLeaveTypes(); track type.id) {
                <option [value]="type.id">{{ type.name }}</option>
              }
            </select>
          </gggs-form-field>

          <gggs-form-field label="Accrual frequency" required>
            <select formControlName="accrualFrequency">
              <option value="Monthly">Monthly</option>
              <option value="Quarterly">Quarterly</option>
              <option value="Yearly">Yearly</option>
              <option value="OnJoining">On joining</option>
            </select>
          </gggs-form-field>

          <gggs-form-field label="Accrual amount" required>
            <input type="number" formControlName="accrualAmount" step="0.5" min="0" required />
          </gggs-form-field>

          <gggs-form-field label="Eligible after (days)" required>
            <input type="number" formControlName="eligibleAfterDays" min="0" required />
          </gggs-form-field>

          <gggs-form-field label="Min notice (days)" required>
            <input type="number" formControlName="minNoticeDays" min="0" required />
          </gggs-form-field>

          <gggs-form-field label="Min days per request" required>
            <input type="number" formControlName="minDaysPerRequest" step="0.5" min="0" required />
          </gggs-form-field>

          <label class="checkbox-field">
            <input type="checkbox" formControlName="allowHalfDay" />
            Allow half-day requests
          </label>

          @if (ruleErrorMessage()) {
            <p class="field-error" role="alert">{{ ruleErrorMessage() }}</p>
          }

          <gggs-button type="submit" [loading]="isSavingRule()">Add rule</gggs-button>
        </form>
      </gggs-card>
    }
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0;
    }

    .inline-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
    }

    .rules-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .rules-table th,
    .rules-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .rule-form {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      align-items: end;
    }

    .checkbox-field {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: 0.875rem;
    }

    .field-error {
      grid-column: 1 / -1;
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }
  `,
})
export class LeavePoliciesPage {
  private readonly api = inject(LeaveApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly policy = signal<LeavePolicyListItem | null>(null);
  protected readonly rules = signal<readonly LeavePolicyRuleListItem[]>([]);
  protected readonly leaveTypes = signal<readonly LeaveTypeListItem[]>([]);
  protected readonly isSavingPolicy = signal(false);
  protected readonly isSavingRule = signal(false);
  protected readonly ruleErrorMessage = signal<string | null>(null);

  protected readonly unruledLeaveTypes = () =>
    this.leaveTypes().filter(
      (type) => !this.rules().some((rule) => rule.leaveTypeId === type.id),
    );

  protected readonly policyForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
  });

  protected readonly ruleForm = this.formBuilder.nonNullable.group({
    leaveTypeId: ['', Validators.required],
    accrualFrequency: ['Monthly' as AccrualFrequency],
    accrualAmount: [1.5, [Validators.required, Validators.min(0)]],
    eligibleAfterDays: [0, [Validators.required, Validators.min(0)]],
    minNoticeDays: [0, [Validators.required, Validators.min(0)]],
    minDaysPerRequest: [0.5, [Validators.required, Validators.min(0)]],
    allowHalfDay: [true],
  });

  constructor() {
    void this.loadLeaveTypes();
    void this.loadPolicy();
  }

  protected leaveTypeName(leaveTypeId: string): string {
    return this.leaveTypes().find((type) => type.id === leaveTypeId)?.name ?? 'Unknown';
  }

  private async loadLeaveTypes(): Promise<void> {
    this.leaveTypes.set(await this.api.listLeaveTypes());
  }

  private async loadPolicy(): Promise<void> {
    const policies = await this.api.listLeavePolicies();
    const first = policies[0];
    this.policy.set(first ?? null);

    if (first) {
      this.rules.set(await this.api.listLeavePolicyRules(first.id));
    }
  }

  protected async createPolicy(): Promise<void> {
    if (this.policyForm.invalid || this.isSavingPolicy()) {
      this.policyForm.markAllAsTouched();
      return;
    }

    this.isSavingPolicy.set(true);

    try {
      const policy = await this.api.createLeavePolicy(this.policyForm.getRawValue());
      this.policy.set(policy);
    } finally {
      this.isSavingPolicy.set(false);
    }
  }

  protected async addRule(): Promise<void> {
    const policy = this.policy();
    if (!policy || this.ruleForm.invalid || this.isSavingRule()) {
      this.ruleForm.markAllAsTouched();
      return;
    }

    this.isSavingRule.set(true);
    this.ruleErrorMessage.set(null);

    try {
      const rule = await this.api.createLeavePolicyRule(policy.id, {
        ...this.ruleForm.getRawValue(),
        // Not collected by this form yet - carry-forward, encashment and negative-balance
        // targeting are a later pass; every rule created here starts with the safe defaults.
        proRataOnJoining: false,
        encashmentAllowed: false,
        allowNegativeBalance: false,
        negativeLimit: 0,
      });
      this.rules.update((rules) => [...rules, rule]);
      this.ruleForm.reset({
        accrualFrequency: 'Monthly',
        accrualAmount: 1.5,
        eligibleAfterDays: 0,
        minNoticeDays: 0,
        minDaysPerRequest: 0.5,
        allowHalfDay: true,
      });
    } catch (error) {
      this.ruleErrorMessage.set(
        error instanceof ApiError ? error.message : 'The rule could not be added.',
      );
    } finally {
      this.isSavingRule.set(false);
    }
  }
}
