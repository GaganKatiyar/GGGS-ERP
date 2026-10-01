import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { PayGroup, PayPeriod, PayrollRun } from '@gggs/shared/models';
import { PayrollApi } from '../data/payroll-api';

/** Every payroll run, plus a form to start a new one against an open period. */
@Component({
  selector: 'gggs-payroll-run-list-page',
  imports: [DecimalPipe, ReactiveFormsModule, HasPermissionDirective, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Payroll runs" subtitle="Calculate net pay for a pay group's open period." />

    <gggs-card title="Runs">
      @if (runs(); as items) {
        @if (items.length > 0) {
          <table class="runs-table">
            <thead>
              <tr>
                <th scope="col">Run No</th>
                <th scope="col">Status</th>
                <th scope="col">Employees</th>
                <th scope="col">Gross</th>
                <th scope="col">Net</th>
              </tr>
            </thead>
            <tbody>
              @for (run of items; track run.id) {
                <tr (click)="open(run.id)" class="run-row">
                  <td>{{ run.runNo }}</td>
                  <td>{{ run.status }}</td>
                  <td>{{ run.employeeCount }}</td>
                  <td>{{ run.grossTotal | number: '1.2-2' }}</td>
                  <td>{{ run.netTotal | number: '1.2-2' }}</td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No runs yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }

      <form
        *gggsHasPermission="'payroll.run.process'"
        [formGroup]="form"
        (ngSubmit)="submit()"
        novalidate
        class="create-form"
      >
        <label class="field">
          <span class="field__label">Pay group</span>
          <select formControlName="payGroupId" (change)="onPayGroupChange()" required>
            <option value="" disabled>Select a pay group</option>
            @for (group of payGroups(); track group.id) {
              <option [value]="group.id">{{ group.name }}</option>
            }
          </select>
        </label>

        <label class="field">
          <span class="field__label">Period</span>
          <select formControlName="payPeriodId" required>
            <option value="" disabled>Select a period</option>
            @for (period of openPeriods(); track period.id) {
              <option [value]="period.id">{{ period.periodCode }}</option>
            }
          </select>
        </label>

        <label class="field">
          <span class="field__label">Run number</span>
          <input type="text" formControlName="runNo" maxlength="30" required />
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Starting…' : 'Start run' }}
        </gggs-button>
      </form>
    </gggs-card>
  `,
  styles: `
    .runs-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .runs-table th,
    .runs-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .run-row {
      cursor: pointer;
    }

    .run-row:hover {
      background: var(--gggs-color-surface-sunken, #f5f6f8);
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 1rem;
    }

    .create-form {
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
export class PayrollRunListPage {
  private readonly api = inject(PayrollApi);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);

  protected readonly runs = signal<readonly PayrollRun[] | null>(null);
  protected readonly payGroups = signal<readonly PayGroup[]>([]);
  protected readonly periodsByGroup = signal<readonly PayPeriod[]>([]);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly openPeriods = computed(() => this.periodsByGroup().filter((p) => p.status === 'Open'));

  protected readonly form = this.formBuilder.group({
    payGroupId: ['', Validators.required],
    payPeriodId: ['', Validators.required],
    runNo: ['', [Validators.required, Validators.maxLength(30)]],
  });

  constructor() {
    void this.load();
    void this.loadPayGroups();
  }

  protected open(runId: string): void {
    void this.router.navigate(['/payroll/runs', runId]);
  }

  protected async onPayGroupChange(): Promise<void> {
    const payGroupId = this.form.get('payGroupId')?.value;
    this.periodsByGroup.set(payGroupId ? await this.api.listPayPeriods(payGroupId) : []);
    this.form.patchValue({ payPeriodId: '' });
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
      const run = await this.api.createPayrollRun({
        payGroupId: value.payGroupId ?? '',
        payPeriodId: value.payPeriodId ?? '',
        runNo: value.runNo ?? '',
      });

      this.form.reset();
      await this.router.navigate(['/payroll/runs', run.id]);
    } catch {
      this.errorMessage.set('The run could not be started. The run number may already be in use, or the period is not Open.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async load(): Promise<void> {
    this.runs.set(await this.api.listPayrollRuns());
  }

  private async loadPayGroups(): Promise<void> {
    const groups = await this.api.listPayGroups();
    this.payGroups.set(groups.filter((g) => g.isActive));
  }
}
