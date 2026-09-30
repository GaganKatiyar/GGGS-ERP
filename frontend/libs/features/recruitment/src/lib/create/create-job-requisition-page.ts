import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { EmployeePicker } from '@gggs/shared/employee-reference';
import { OrgReferenceApi } from '@gggs/shared/org-reference';
import { PageHeader } from '@gggs/shared/ui';
import type {
  BranchListItem,
  DepartmentListItem,
  DesignationListItem,
  EmployeeListItem,
} from '@gggs/shared/models';
import { RecruitmentApi } from '../data/recruitment-api';

/** Raising a new job requisition. Starts as Draft; opening it for hiring is a separate action on
 * the detail page. */
@Component({
  selector: 'gggs-create-job-requisition-page',
  imports: [ReactiveFormsModule, RouterLink, PageHeader, EmployeePicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="New requisition" subtitle="Starts as a draft.">
      <a routerLink="/recruitment/requisitions" class="btn">Cancel</a>
    </gggs-page-header>

    <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="panel">
      <div class="grid">
        <label class="field">
          <span class="field__label">Title</span>
          <input type="text" formControlName="title" maxlength="200" required />
        </label>

        <label class="field">
          <span class="field__label">Branch</span>
          <select formControlName="branchId" required>
            <option value="" disabled>Choose one</option>
            @for (branch of branches(); track branch.id) {
              <option [value]="branch.id">{{ branch.name }}</option>
            }
          </select>
        </label>

        <label class="field">
          <span class="field__label">Department</span>
          <select formControlName="departmentId" required>
            <option value="" disabled>Choose one</option>
            @for (department of departments(); track department.id) {
              <option [value]="department.id">{{ department.name }}</option>
            }
          </select>
        </label>

        <label class="field">
          <span class="field__label">Designation</span>
          <select formControlName="designationId" required>
            <option value="" disabled>Choose one</option>
            @for (designation of designations(); track designation.id) {
              <option [value]="designation.id">{{ designation.title }}</option>
            }
          </select>
        </label>

        <label class="field">
          <span class="field__label">Openings</span>
          <input type="number" formControlName="openings" min="1" required />
        </label>

        <div class="field">
          <span class="field__label" id="hiring-manager-label">Hiring manager (optional)</span>
          <gggs-employee-picker
            aria-labelledby="hiring-manager-label"
            (employeeSelected)="onHiringManagerSelected($event)"
          />
        </div>
      </div>

      <label class="field field--full">
        <span class="field__label">Justification (optional)</span>
        <textarea formControlName="justification" maxlength="1000" rows="3"></textarea>
      </label>

      @if (errorMessage()) {
        <p class="field__error" role="alert">{{ errorMessage() }}</p>
      }

      <button type="submit" class="btn btn--primary" [disabled]="isSaving()">
        {{ isSaving() ? 'Creating…' : 'Create requisition' }}
      </button>
    </form>
  `,
  styles: `
    .panel {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1rem;
      max-width: 60rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised, #ffffff);
    }

    .grid {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
    }

    .field--full {
      grid-column: 1 / -1;
    }

    .field__label {
      font-weight: 600;
    }

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .btn {
      align-self: flex-start;
      padding: 0.5rem 1rem;
      font-weight: 600;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #ffffff);
      text-decoration: none;
      color: inherit;
      cursor: pointer;
    }

    .btn--primary {
      background: var(--gggs-color-primary, #1d4ed8);
      border-color: transparent;
      color: #fff;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
  `,
})
export class CreateJobRequisitionPage {
  private readonly api = inject(RecruitmentApi);
  private readonly orgApi = inject(OrgReferenceApi);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);

  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly branches = signal<readonly BranchListItem[]>([]);
  protected readonly departments = signal<readonly DepartmentListItem[]>([]);
  protected readonly designations = signal<readonly DesignationListItem[]>([]);

  protected readonly form = this.formBuilder.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    branchId: ['', Validators.required],
    departmentId: ['', Validators.required],
    designationId: ['', Validators.required],
    openings: [1, [Validators.required, Validators.min(1)]],
    hiringManagerEmployeeId: [''],
    justification: [''],
  });

  constructor() {
    void this.loadReferenceData();
  }

  protected onHiringManagerSelected(employee: EmployeeListItem): void {
    this.form.controls.hiringManagerEmployeeId.setValue(employee.id);
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
      const requisition = await this.api.create({
        title: value.title ?? '',
        branchId: value.branchId ?? '',
        departmentId: value.departmentId ?? '',
        designationId: value.designationId ?? '',
        openings: value.openings ?? 1,
        hiringManagerEmployeeId: value.hiringManagerEmployeeId || undefined,
        justification: value.justification || undefined,
      });

      await this.router.navigate(['/recruitment/requisitions', requisition.id]);
    } catch (error) {
      this.errorMessage.set(
        error instanceof ApiError
          ? 'The requisition could not be created. Please check the fields and try again.'
          : 'The requisition could not be created. Please try again.',
      );
    } finally {
      this.isSaving.set(false);
    }
  }

  private async loadReferenceData(): Promise<void> {
    const [branches, departments, designations] = await Promise.all([
      this.orgApi.listBranches(),
      this.orgApi.listDepartments(),
      this.orgApi.listDesignations(),
    ]);

    this.branches.set(branches);
    this.departments.set(departments);
    this.designations.set(designations);
  }
}
