import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { PayFrequency, PayGroup, PayPeriod } from '@gggs/shared/models';
import { PayrollApi } from '../data/payroll-api';

/** Every pay group this tenant has defined, plus a form to add a new one and an expandable panel
 * per row for that group's periods - the same embedded-panel pattern used across Recruitment and
 * Performance, rather than a separate route per group. */
@Component({
  selector: 'gggs-pay-group-list-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Pay groups" subtitle="Employees paid together on the same cadence. Periods are opened against a group." />

    <gggs-card title="Pay groups">
      @if (payGroups(); as items) {
        @if (items.length > 0) {
          <table class="groups-table">
            <thead>
              <tr>
                <th scope="col">Code</th>
                <th scope="col">Name</th>
                <th scope="col">Frequency</th>
                <th scope="col">Currency</th>
                <th scope="col">Status</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              @for (group of items; track group.id) {
                <tr>
                  <td>{{ group.code }}</td>
                  <td>{{ group.name }}</td>
                  <td>{{ group.frequency }}</td>
                  <td>{{ group.currency }}</td>
                  <td>{{ group.isActive ? 'Active' : 'Inactive' }}</td>
                  <td class="group-actions">
                    <gggs-button size="sm" variant="ghost" (click)="toggleExpand(group.id)">
                      {{ expandedGroupId() === group.id ? 'Hide periods' : 'Periods' }}
                    </gggs-button>
                    @if (group.isActive) {
                      <gggs-button
                        *gggsHasPermission="'payroll.pay-group.manage'"
                        size="sm"
                        variant="ghost"
                        (click)="deactivate(group.id)"
                        [disabled]="isActing()"
                      >
                        Deactivate
                      </gggs-button>
                    } @else {
                      <gggs-button
                        *gggsHasPermission="'payroll.pay-group.manage'"
                        size="sm"
                        variant="ghost"
                        (click)="reactivate(group.id)"
                        [disabled]="isActing()"
                      >
                        Reactivate
                      </gggs-button>
                    }
                  </td>
                </tr>
                @if (expandedGroupId() === group.id) {
                  <tr>
                    <td colspan="6" class="periods-cell">
                      @if (periods().length > 0) {
                        <table class="periods-table">
                          <thead>
                            <tr>
                              <th scope="col">Period</th>
                              <th scope="col">Start</th>
                              <th scope="col">End</th>
                              <th scope="col">Pay date</th>
                              <th scope="col">Status</th>
                            </tr>
                          </thead>
                          <tbody>
                            @for (period of periods(); track period.id) {
                              <tr>
                                <td>{{ period.periodCode }}</td>
                                <td>{{ period.startDate }}</td>
                                <td>{{ period.endDate }}</td>
                                <td>{{ period.payDate }}</td>
                                <td>{{ period.status }}</td>
                              </tr>
                            }
                          </tbody>
                        </table>
                      } @else {
                        <p class="muted">No periods opened yet.</p>
                      }

                      <form
                        *gggsHasPermission="'payroll.pay-group.manage'"
                        [formGroup]="periodForm"
                        (ngSubmit)="submitPeriod(group.id)"
                        novalidate
                        class="period-form"
                      >
                        <label class="field">
                          <span class="field__label">Period code</span>
                          <input type="text" formControlName="periodCode" maxlength="20" required />
                        </label>

                        <label class="field">
                          <span class="field__label">Start date</span>
                          <input type="date" formControlName="startDate" required />
                        </label>

                        <label class="field">
                          <span class="field__label">End date</span>
                          <input type="date" formControlName="endDate" required />
                        </label>

                        <label class="field">
                          <span class="field__label">Pay date</span>
                          <input type="date" formControlName="payDate" required />
                        </label>

                        @if (periodErrorMessage()) {
                          <p class="field__error" role="alert">{{ periodErrorMessage() }}</p>
                        }

                        <gggs-button type="submit" size="sm" [disabled]="isSavingPeriod()">
                          {{ isSavingPeriod() ? 'Opening…' : 'Open period' }}
                        </gggs-button>
                      </form>
                    </td>
                  </tr>
                }
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No pay groups yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }

      <form
        *gggsHasPermission="'payroll.pay-group.manage'"
        [formGroup]="form"
        (ngSubmit)="submit()"
        novalidate
        class="create-form"
      >
        <label class="field">
          <span class="field__label">Code</span>
          <input type="text" formControlName="code" maxlength="30" required />
        </label>

        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" formControlName="name" maxlength="100" required />
        </label>

        <label class="field">
          <span class="field__label">Frequency</span>
          <select formControlName="frequency" required>
            <option value="Monthly">Monthly</option>
            <option value="SemiMonthly">Semi-monthly</option>
            <option value="BiWeekly">Bi-weekly</option>
            <option value="Weekly">Weekly</option>
          </select>
        </label>

        <label class="field">
          <span class="field__label">Currency</span>
          <input type="text" formControlName="currency" maxlength="3" required />
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Creating…' : 'Create pay group' }}
        </gggs-button>
      </form>
    </gggs-card>
  `,
  styles: `
    .groups-table,
    .periods-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .groups-table th,
    .groups-table td,
    .periods-table th,
    .periods-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .group-actions {
      display: flex;
      gap: 0.5rem;
      flex-wrap: wrap;
    }

    .periods-cell {
      background: var(--gggs-color-surface-sunken, #f5f6f8);
      padding: 0.75rem 1rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 1rem;
    }

    .create-form,
    .period-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
      flex-wrap: wrap;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
    }

    .field__label {
      font-weight: 600;
    }

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }
  `,
})
export class PayGroupListPage {
  private readonly api = inject(PayrollApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly payGroups = signal<readonly PayGroup[] | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly isActing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly expandedGroupId = signal<string | null>(null);
  protected readonly periods = signal<readonly PayPeriod[]>([]);
  protected readonly isSavingPeriod = signal(false);
  protected readonly periodErrorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    code: ['', [Validators.required, Validators.maxLength(30)]],
    name: ['', [Validators.required, Validators.maxLength(100)]],
    frequency: ['Monthly' as PayFrequency, Validators.required],
    currency: ['INR', [Validators.required, Validators.maxLength(3)]],
  });

  protected readonly periodForm = this.formBuilder.group({
    periodCode: ['', [Validators.required, Validators.maxLength(20)]],
    startDate: ['', Validators.required],
    endDate: ['', Validators.required],
    payDate: ['', Validators.required],
  });

  constructor() {
    void this.load();
  }

  protected async toggleExpand(groupId: string): Promise<void> {
    if (this.expandedGroupId() === groupId) {
      this.expandedGroupId.set(null);
      return;
    }

    this.expandedGroupId.set(groupId);
    this.periodErrorMessage.set(null);
    this.periodForm.reset();
    this.periods.set(await this.api.listPayPeriods(groupId));
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      await this.api.createPayGroup({
        code: value.code ?? '',
        name: value.name ?? '',
        frequency: value.frequency ?? 'Monthly',
        currency: value.currency ?? 'INR',
      });

      this.form.reset({ frequency: 'Monthly', currency: 'INR' });
      await this.load();
    } catch {
      this.errorMessage.set('The pay group could not be created. A code may already be in use.');
    } finally {
      this.isSaving.set(false);
    }
  }

  protected async deactivate(id: string): Promise<void> {
    await this.act(() => this.api.deactivatePayGroup(id));
  }

  protected async reactivate(id: string): Promise<void> {
    await this.act(() => this.api.reactivatePayGroup(id));
  }

  private async act(run: () => Promise<unknown>): Promise<void> {
    if (this.isActing()) {
      return;
    }

    this.isActing.set(true);

    try {
      await run();
      await this.load();
    } finally {
      this.isActing.set(false);
    }
  }

  protected async submitPeriod(payGroupId: string): Promise<void> {
    if (this.periodForm.invalid || this.isSavingPeriod()) {
      this.periodForm.markAllAsTouched();
      return;
    }

    this.isSavingPeriod.set(true);
    this.periodErrorMessage.set(null);

    const value = this.periodForm.getRawValue();

    try {
      const period = await this.api.createPayPeriod(payGroupId, {
        periodCode: value.periodCode ?? '',
        startDate: value.startDate ?? '',
        endDate: value.endDate ?? '',
        payDate: value.payDate ?? '',
      });

      this.periods.update((periods) => [...periods, period]);
      this.periodForm.reset();
    } catch {
      this.periodErrorMessage.set('That period could not be opened. The code may already be in use for this group.');
    } finally {
      this.isSavingPeriod.set(false);
    }
  }

  private async load(): Promise<void> {
    this.payGroups.set(await this.api.listPayGroups());
  }
}
