import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { BulkHireResult, BulkHireRowRequest, EmploymentType } from '@gggs/shared/models';
import { EmployeesApi } from '../data/employees-api';

/**
 * Bulk hire: up to 500 rows entered directly (paste from a spreadsheet, one row per employee),
 * organizational references given as codes rather than picked from a dropdown - the same way
 * `POST /employees/bulk` accepts them, since a code is what a spreadsheet import actually has.
 * <para>
 * One bad row never blocks the others: every row is submitted in one request and the server
 * returns a result per row, shown here as an inline status per row rather than an all-or-nothing
 * error - matching the import framework's row-level-errors principle even in this simple form.
 * </para>
 */
@Component({
  selector: 'gggs-bulk-import-employees-page',
  imports: [ReactiveFormsModule, RouterLink, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header
      title="Bulk import employees"
      subtitle="Hire up to 500 employees at once. Organizational references are codes (e.g. legal entity, branch, department, designation), not picked from a list."
    >
      <a routerLink="/employees" class="btn">Cancel</a>
    </gggs-page-header>

    <gggs-card title="Rows">
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <table class="rows-table">
          <thead>
            <tr>
              <th scope="col">Employee code</th>
              <th scope="col">First name</th>
              <th scope="col">Last name</th>
              <th scope="col">Date of joining</th>
              <th scope="col">Legal entity code</th>
              <th scope="col">Branch code</th>
              <th scope="col">Department code</th>
              <th scope="col">Designation code</th>
              <th scope="col">Grade code</th>
              <th scope="col">Employment type</th>
              <th scope="col"></th>
              <th scope="col">Result</th>
            </tr>
          </thead>
          <tbody>
            @for (row of form.controls.rows.controls; track $index) {
              <tr [formGroup]="row" [class.row--failed]="rowResult($index)?.succeeded === false">
                <td><input type="text" formControlName="employeeCode" maxlength="30" required /></td>
                <td><input type="text" formControlName="firstName" maxlength="100" required /></td>
                <td><input type="text" formControlName="lastName" maxlength="100" /></td>
                <td><input type="date" formControlName="dateOfJoining" required /></td>
                <td><input type="text" formControlName="legalEntityCode" required /></td>
                <td><input type="text" formControlName="branchCode" required /></td>
                <td><input type="text" formControlName="departmentCode" required /></td>
                <td><input type="text" formControlName="designationCode" required /></td>
                <td><input type="text" formControlName="gradeCode" /></td>
                <td>
                  <select formControlName="employmentType" required>
                    <option value="Permanent">Permanent</option>
                    <option value="Contract">Contract</option>
                    <option value="Intern">Intern</option>
                    <option value="Consultant">Consultant</option>
                  </select>
                </td>
                <td>
                  <button
                    type="button"
                    class="btn-icon"
                    [disabled]="form.controls.rows.length <= 1"
                    (click)="removeRow($index)"
                    aria-label="Remove row"
                  >
                    ✕
                  </button>
                </td>
                <td class="result-cell">
                  @if (rowResult($index); as result) {
                    @if (result.succeeded) {
                      <span class="result result--ok">Hired</span>
                    } @else {
                      <span class="result result--error">{{ result.errorMessage ?? 'Failed' }}</span>
                    }
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>

        <div class="actions">
          <button type="button" class="btn" (click)="addRow()">Add row</button>
          <gggs-button type="submit" [disabled]="isSaving()">
            {{ isSaving() ? 'Importing…' : 'Import all rows' }}
          </gggs-button>
        </div>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        @if (summary(); as s) {
          <p class="summary">
            {{ s.totalRows }} row(s): {{ s.successCount }} hired, {{ s.failureCount }} failed.
          </p>
        }
      </form>
    </gggs-card>
  `,
  styles: `
    .rows-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.8125rem;
    }

    .rows-table th,
    .rows-table td {
      padding: 0.375rem 0.5rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      text-align: left;
    }

    .rows-table input,
    .rows-table select {
      width: 100%;
      min-width: 6rem;
      box-sizing: border-box;
    }

    .row--failed {
      background: color-mix(in srgb, var(--gggs-color-danger, #b3261e) 8%, transparent);
    }

    .result-cell {
      white-space: nowrap;
    }

    .result--ok {
      color: var(--gggs-color-success, #1a7f37);
    }

    .result--error {
      color: var(--gggs-color-danger, #b3261e);
    }

    .actions {
      display: flex;
      gap: 0.75rem;
      align-items: center;
      margin-block-start: 1rem;
    }

    .field__error {
      margin: 0.5rem 0 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .summary {
      margin: 0.5rem 0 0;
      font-weight: 600;
    }

    .btn,
    .btn-icon {
      padding: 0.5rem 1rem;
      font-weight: 600;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #ffffff);
      text-decoration: none;
      color: inherit;
      cursor: pointer;
    }

    .btn-icon {
      padding: 0.25rem 0.5rem;
    }

    .btn-icon:disabled {
      opacity: 0.4;
      cursor: not-allowed;
    }
  `,
})
export class BulkImportEmployeesPage {
  private readonly api = inject(EmployeesApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly summary = signal<BulkHireResult | null>(null);

  protected readonly form = this.formBuilder.group({
    rows: this.formBuilder.array([this.buildRow()]),
  });

  protected addRow(): void {
    this.form.controls.rows.push(this.buildRow());
  }

  protected removeRow(index: number): void {
    this.form.controls.rows.removeAt(index);
  }

  protected rowResult(index: number) {
    return this.summary()?.rows[index];
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);
    this.summary.set(null);

    const rows: BulkHireRowRequest[] = this.form.controls.rows.controls.map((row) => {
      const value = row.getRawValue();
      return {
        employeeCode: value.employeeCode ?? '',
        firstName: value.firstName ?? '',
        lastName: value.lastName || undefined,
        dateOfJoining: value.dateOfJoining ?? '',
        legalEntityCode: value.legalEntityCode ?? '',
        branchCode: value.branchCode ?? '',
        departmentCode: value.departmentCode ?? '',
        designationCode: value.designationCode ?? '',
        gradeCode: value.gradeCode || undefined,
        employmentType: (value.employmentType ?? 'Permanent') as EmploymentType,
      };
    });

    try {
      const result = await this.api.hireBulk(rows);
      this.summary.set(result);
    } catch (error) {
      this.errorMessage.set(describeBulkImportError(error));
    } finally {
      this.isSaving.set(false);
    }
  }

  private buildRow() {
    return this.formBuilder.group({
      employeeCode: ['', [Validators.required, Validators.maxLength(30)]],
      firstName: ['', [Validators.required, Validators.maxLength(100)]],
      lastName: [''],
      dateOfJoining: ['', Validators.required],
      legalEntityCode: ['', Validators.required],
      branchCode: ['', Validators.required],
      departmentCode: ['', Validators.required],
      designationCode: ['', Validators.required],
      gradeCode: [''],
      employmentType: ['Permanent' as EmploymentType, Validators.required],
    });
  }
}

export function describeBulkImportError(error: unknown): string {
  if (error instanceof ApiError && error.status === 422) {
    return 'Too many rows, or no rows at all - a bulk import accepts 1 to 500 rows.';
  }

  return 'The import could not be submitted. Please try again.';
}
