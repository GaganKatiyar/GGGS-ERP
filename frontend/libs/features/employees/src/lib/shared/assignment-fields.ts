import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { ReactiveFormsModule, type FormGroup } from '@angular/forms';
import type {
  BranchListItem,
  CostCenterListItem,
  DepartmentListItem,
  DesignationListItem,
  EmployeeListItem,
  GradeListItem,
  LegalEntityListItem,
} from '@gggs/shared/models';
import { OrgReferenceApi } from '@gggs/shared/org-reference';
import { EmployeesApi } from '../data/employees-api';

/**
 * The organizational assignment fields every hire, transfer and promotion needs - the same shape
 * as the server's `AssignmentBody`.
 *
 * Takes the parent's `FormGroup` directly rather than implementing `ControlValueAccessor`: this is
 * always a sub-group of a page-level form (hire adds name/code/joining-date around it, transfer
 * adds an effective date and a reason), never a value on its own, so there is nothing a
 * `ControlValueAccessor` would buy that binding the group straight to `[formGroup]` does not.
 *
 * Reference lists (legal entity, branch, department, designation, grade, cost center) and the
 * reporting-manager picker are all plain `<select>` elements fetched in full rather than searched -
 * a fine simplification at the tens-to-hundreds scale this pass targets, not at the tens of
 * thousands the real org-chart picker (architecture.md §11.3) will need to handle.
 */
@Component({
  selector: 'gggs-assignment-fields',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div [formGroup]="formGroup()" class="grid">
      <label class="field">
        <span class="field__label">Legal entity</span>
        <select formControlName="legalEntityId" required (change)="onLegalEntityChange()">
          <option value="" disabled>Choose one</option>
          @for (entity of legalEntities(); track entity.id) {
            <option [value]="entity.id">{{ entity.name }}</option>
          }
        </select>
      </label>

      <label class="field">
        <span class="field__label">Branch</span>
        <select formControlName="branchId" required>
          <option value="" disabled>Choose one</option>
          @for (branch of branchesForSelectedLegalEntity(); track branch.id) {
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
        <span class="field__label">Grade (optional)</span>
        <select formControlName="gradeId">
          <option value="">None</option>
          @for (grade of grades(); track grade.id) {
            <option [value]="grade.id">{{ grade.name }}</option>
          }
        </select>
      </label>

      <label class="field">
        <span class="field__label">Cost center (optional)</span>
        <select formControlName="costCenterId">
          <option value="">None</option>
          @for (costCenter of costCenters(); track costCenter.id) {
            <option [value]="costCenter.id">{{ costCenter.name }}</option>
          }
        </select>
      </label>

      <label class="field">
        <span class="field__label">Reporting manager (optional)</span>
        <select formControlName="reportingManagerId">
          <option value="">None</option>
          @for (manager of employees(); track manager.id) {
            @if (manager.id !== excludeEmployeeId()) {
              <option [value]="manager.id">{{ manager.displayName }}</option>
            }
          }
        </select>
      </label>

      <label class="field">
        <span class="field__label">Employment type</span>
        <select formControlName="employmentType" required>
          <option value="Permanent">Permanent</option>
          <option value="Contract">Contract</option>
          <option value="Intern">Intern</option>
          <option value="Consultant">Consultant</option>
        </select>
      </label>

      <label class="field">
        <span class="field__label">Probation end date (optional)</span>
        <input type="date" formControlName="probationEndDate" />
      </label>
    </div>
  `,
  styles: `
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

    .field__label {
      font-weight: 600;
    }
  `,
})
export class AssignmentFields {
  private readonly orgApi = inject(OrgReferenceApi);
  private readonly employeesApi = inject(EmployeesApi);

  readonly formGroup = input.required<FormGroup>();

  /** Kept out of its own "reporting manager" list, so nobody can accidentally report to themself. */
  readonly excludeEmployeeId = input<string | undefined>(undefined);

  protected readonly legalEntities = signal<readonly LegalEntityListItem[]>([]);
  protected readonly branches = signal<readonly BranchListItem[]>([]);
  protected readonly departments = signal<readonly DepartmentListItem[]>([]);
  protected readonly designations = signal<readonly DesignationListItem[]>([]);
  protected readonly grades = signal<readonly GradeListItem[]>([]);
  protected readonly costCenters = signal<readonly CostCenterListItem[]>([]);
  protected readonly employees = signal<readonly EmployeeListItem[]>([]);

  protected readonly branchesForSelectedLegalEntity = computed(() => {
    const legalEntityId = this.formGroup().get('legalEntityId')?.value as string | undefined;
    return legalEntityId
      ? this.branches().filter((branch) => branch.legalEntityId === legalEntityId)
      : this.branches();
  });

  constructor() {
    void this.loadReferenceData();
  }

  protected onLegalEntityChange(): void {
    // A branch chosen under the previous legal entity would silently point at the wrong company -
    // clearing it forces a fresh, valid choice rather than submitting a stale one.
    this.formGroup().get('branchId')?.setValue('');
  }

  private async loadReferenceData(): Promise<void> {
    const [legalEntities, branches, departments, designations, grades, costCenters, employees] =
      await Promise.all([
        this.orgApi.listLegalEntities({ page: 1, pageSize: 200 }),
        this.orgApi.listBranches(),
        this.orgApi.listDepartments(),
        this.orgApi.listDesignations(),
        this.orgApi.listGrades(),
        this.orgApi.listCostCenters(),
        this.employeesApi.list({ page: 1, pageSize: 200 }),
      ]);

    this.legalEntities.set(legalEntities.items);
    this.branches.set(branches);
    this.departments.set(departments);
    this.designations.set(designations);
    this.grades.set(grades);
    this.costCenters.set(costCenters);
    this.employees.set(employees.items);
  }
}
