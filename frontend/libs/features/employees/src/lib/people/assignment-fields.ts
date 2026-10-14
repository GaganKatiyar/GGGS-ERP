import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { LookupSelect } from '@gggs/shared/master-data';
import type { EmployeeAssignment, EmployeeAssignmentInput } from '@gggs/shared/models';
import { LOOKUP_LISTS } from '@gggs/shared/models';
import { OrgSelect } from '@gggs/shared/org-reference';

export type AssignmentForm = FormGroup<{
  positionId: FormControl<string | null>;
  departmentId: FormControl<string | null>;
  jobId: FormControl<string | null>;
  gradeId: FormControl<string | null>;
  locationId: FormControl<string | null>;
  legalEntityId: FormControl<string | null>;
  managerEmployeeId: FormControl<string | null>;
  employmentTypeCode: FormControl<string | null>;
  workModeCode: FormControl<string | null>;
  designation: FormControl<string | null>;
}>;

/** A fresh assignment form, optionally prefilled from someone's current assignment. */
export function assignmentForm(current?: EmployeeAssignment): AssignmentForm {
  return new FormGroup({
    positionId: new FormControl(current?.position?.id ?? null),
    departmentId: new FormControl(current?.department?.id ?? null),
    jobId: new FormControl(current?.job?.id ?? null),
    gradeId: new FormControl(current?.grade?.id ?? null),
    locationId: new FormControl(current?.location?.id ?? null),
    legalEntityId: new FormControl(current?.legalEntity?.id ?? null),
    managerEmployeeId: new FormControl(current?.manager?.id ?? null),
    employmentTypeCode: new FormControl(current?.employmentType.code ?? 'FULL_TIME', Validators.required),
    workModeCode: new FormControl(current?.workMode.code ?? 'OFFICE', Validators.required),
    designation: new FormControl(current?.designation ?? null),
  });
}

/** The form's value as the API wants it: a position, when chosen, decides job and department. */
export function toAssignmentInput(form: AssignmentForm): EmployeeAssignmentInput {
  const value = form.getRawValue();
  const hasPosition = !!value.positionId;

  return {
    positionId: value.positionId,
    departmentId: hasPosition ? null : value.departmentId,
    jobId: hasPosition ? null : value.jobId,
    gradeId: value.gradeId,
    locationId: value.locationId,
    legalEntityId: value.legalEntityId,
    managerEmployeeId: value.managerEmployeeId,
    employmentTypeCode: value.employmentTypeCode ?? '',
    workModeCode: value.workModeCode ?? '',
    designation: value.designation || null,
  };
}

/**
 * The fields of an assignment. Choosing a position fills in its job and department on the server,
 * so those two are hidden while a position is selected - one way to say where someone sits.
 */
@Component({
  selector: 'gggs-assignment-fields',
  imports: [ReactiveFormsModule, OrgSelect, LookupSelect, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form-grid" [formGroup]="form()">
      <label class="field">
        <span class="field__label">Position</span>
        <gggs-org-select kind="positions" formControlName="positionId" placeholder="No position (choose a department)" ariaLabel="Position" />
        <span class="field__hint">A position brings its job, department and usually grade and location.</span>
      </label>

      @if (!form().controls.positionId.value) {
        <label class="field">
          <span class="field__label">Department *</span>
          <gggs-org-select kind="departments" formControlName="departmentId" placeholder="Choose a department" ariaLabel="Department" />
        </label>
        <label class="field">
          <span class="field__label">Job</span>
          <gggs-org-select kind="jobs" formControlName="jobId" ariaLabel="Job" />
        </label>
      }

      <label class="field">
        <span class="field__label">Designation</span>
        <input type="text" formControlName="designation" maxlength="200" placeholder="Title shown on letters and the directory" />
      </label>
      <label class="field">
        <span class="field__label">Manager</span>
        <gggs-employee-select formControlName="managerEmployeeId" ariaLabel="Manager" [excludeId]="employeeId()" />
      </label>
      <label class="field">
        <span class="field__label">Grade</span>
        <gggs-org-select kind="grades" formControlName="gradeId" placeholder="From the position, or none" ariaLabel="Grade" />
      </label>
      <label class="field">
        <span class="field__label">Location</span>
        <gggs-org-select kind="locations" formControlName="locationId" placeholder="From the position, or none" ariaLabel="Location" />
      </label>
      <label class="field">
        <span class="field__label">Company</span>
        <gggs-org-select kind="legal-entities" formControlName="legalEntityId" ariaLabel="Company" />
      </label>
      <label class="field">
        <span class="field__label">Employment type *</span>
        <gggs-lookup-select [list]="lists.employmentType" formControlName="employmentTypeCode" ariaLabel="Employment type" />
      </label>
      <label class="field">
        <span class="field__label">Work mode *</span>
        <gggs-lookup-select [list]="lists.workMode" formControlName="workModeCode" ariaLabel="Work mode" />
      </label>
    </div>
  `,
})
export class AssignmentFields {
  readonly form = input.required<AssignmentForm>();
  /** The employee being edited, so they cannot be picked as their own manager. */
  readonly employeeId = input<string | null>(null);
  protected readonly lists = LOOKUP_LISTS;
}
