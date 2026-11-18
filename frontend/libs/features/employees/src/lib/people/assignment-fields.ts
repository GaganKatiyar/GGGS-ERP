import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { LookupSelect } from '@gggs/shared/master-data';
import type { EmployeeAssignment, EmployeeAssignmentInput, OrgUnit } from '@gggs/shared/models';
import { LOOKUP_LISTS } from '@gggs/shared/models';
import { OrgSelect, OrgStructureApi, designationsFor, rolesFor } from '@gggs/shared/org-reference';
import type { Subscription } from 'rxjs';

export type AssignmentForm = FormGroup<{
  departmentId: FormControl<string | null>;
  roleId: FormControl<string | null>;
  designationId: FormControl<string | null>;
  levelId: FormControl<string | null>;
  locationId: FormControl<string | null>;
  costCenterId: FormControl<string | null>;
  companyId: FormControl<string | null>;
  managerEmployeeId: FormControl<string | null>;
  employmentTypeCode: FormControl<string | null>;
  workModeCode: FormControl<string | null>;
}>;

/** A fresh assignment form, optionally prefilled from someone's current assignment. */
export function assignmentForm(current?: EmployeeAssignment): AssignmentForm {
  return new FormGroup({
    departmentId: new FormControl(current?.department?.id ?? null, Validators.required),
    roleId: new FormControl(current?.role?.id ?? null),
    designationId: new FormControl(current?.designationRef?.id ?? null),
    levelId: new FormControl(current?.level?.id ?? null),
    locationId: new FormControl(current?.location?.id ?? null),
    costCenterId: new FormControl(current?.costCenter?.id ?? null),
    companyId: new FormControl(current?.company?.id ?? null),
    managerEmployeeId: new FormControl(current?.manager?.id ?? null),
    employmentTypeCode: new FormControl(current?.employmentType.code ?? 'FULL_TIME', Validators.required),
    workModeCode: new FormControl(current?.workMode.code ?? 'OFFICE', Validators.required),
  });
}

/** The form's value as the API wants it. An empty cost center is filled on the server from the location. */
export function toAssignmentInput(form: AssignmentForm): EmployeeAssignmentInput {
  const value = form.getRawValue();

  return {
    departmentId: value.departmentId,
    roleId: value.roleId,
    designationId: value.designationId,
    levelId: value.levelId,
    locationId: value.locationId,
    costCenterId: value.costCenterId,
    companyId: value.companyId,
    managerEmployeeId: value.managerEmployeeId,
    employmentTypeCode: value.employmentTypeCode ?? '',
    workModeCode: value.workModeCode ?? '',
  };
}

/**
 * Where someone sits and what they do: the department (or one of its functions), their role,
 * level and designation, and the office and company name they work under.
 * - The role list shows the department's roles (a function shares its department's).
 * - A designation is a role at a level ("Business Analyst L2"): picking the role and level picks
 *   it, and picking it fills in the role and level.
 * - Picking an office fills its cost center (or the company default).
 */
@Component({
  selector: 'gggs-assignment-fields',
  imports: [ReactiveFormsModule, OrgSelect, LookupSelect, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form-grid" [formGroup]="form()">
      <p class="assignment__section form-grid__full">Where they sit</p>
      <label class="field">
        <span class="field__label">Department *</span>
        <gggs-org-select kind="departments" formControlName="departmentId" placeholder="Choose a department or function" ariaLabel="Department" />
        <span class="field__hint">A function reads "Department › Function".</span>
      </label>
      <label class="field">
        <span class="field__label">Manager</span>
        <gggs-employee-select formControlName="managerEmployeeId" ariaLabel="Manager" [excludeId]="employeeId()" />
      </label>
      <label class="field">
        <span class="field__label">Location</span>
        <gggs-org-select kind="locations" formControlName="locationId" ariaLabel="Location" />
      </label>
      <label class="field">
        <span class="field__label">Cost center</span>
        <gggs-org-select kind="cost-centers" formControlName="costCenterId" placeholder="From the location, or the company default" ariaLabel="Cost center" />
        <span class="field__hint">{{ costCenterHint() ?? 'The company name they work under.' }}</span>
      </label>
      <label class="field">
        <span class="field__label">Company</span>
        <gggs-org-select kind="companies" formControlName="companyId" ariaLabel="Company" />
      </label>

      <p class="assignment__section form-grid__full">What they do</p>
      <label class="field">
        <span class="field__label">Role</span>
        <gggs-org-select kind="roles" formControlName="roleId" [where]="roleFilter()" placeholder="Choose a role" ariaLabel="Role" />
        <span class="field__hint">{{ roleHint() ?? 'The roles of the chosen department.' }}</span>
      </label>
      <label class="field">
        <span class="field__label">Level</span>
        <gggs-org-select kind="levels" formControlName="levelId" placeholder="Choose a level" ariaLabel="Level" />
      </label>
      <label class="field">
        <span class="field__label">Designation</span>
        <gggs-org-select kind="designations" formControlName="designationId" [where]="designationFilter()" placeholder="Choose a designation" ariaLabel="Designation" />
        <span class="field__hint">{{ designationHint() ?? 'Picked from the role and level. The title on letters and payslips.' }}</span>
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
  styles: `
    .assignment__section {
      margin: 0.25rem 0 -0.25rem;
      font-size: 0.75rem;
      font-weight: 600;
      letter-spacing: 0.05em;
      text-transform: uppercase;
      color: var(--text-muted, #6b7280);
    }
  `,
})
export class AssignmentFields {
  private readonly api = inject(OrgStructureApi);

  readonly form = input.required<AssignmentForm>();
  /** The employee being edited, so they cannot be picked as their own manager. */
  readonly employeeId = input<string | null>(null);
  protected readonly lists = LOOKUP_LISTS;

  protected readonly costCenterHint = signal<string | null>(null);
  protected readonly roleHint = signal<string | null>(null);
  protected readonly designationHint = signal<string | null>(null);

  private readonly departments = signal<readonly OrgUnit[]>([]);
  private readonly departmentId = signal<string | null>(null);
  private readonly roleId = signal<string | null>(null);
  protected readonly roleFilter = computed(() => rolesFor(this.departmentId(), this.departments()));
  protected readonly designationFilter = computed(() => designationsFor(this.roleId()));

  private roles: readonly OrgUnit[] = [];
  private designations: readonly OrgUnit[] = [];
  private locations: readonly OrgUnit[] = [];
  private costCenters: readonly OrgUnit[] = [];

  constructor() {
    void this.loadReferences();

    let subscriptions: Subscription[] = [];
    inject(DestroyRef).onDestroy(() => subscriptions.forEach((subscription) => subscription.unsubscribe()));

    // Only changes the user makes trigger a suggestion, so opening a saved assignment keeps it as it is.
    effect(() => {
      const form = this.form();
      subscriptions.forEach((subscription) => subscription.unsubscribe());
      untracked(() => {
        this.departmentId.set(form.controls.departmentId.value);
        this.roleId.set(form.controls.roleId.value);
      });
      subscriptions = [
        form.controls.departmentId.valueChanges.subscribe((id) => this.departmentChanged(form, id)),
        form.controls.roleId.valueChanges.subscribe((id) => {
          this.roleId.set(id);
          this.pickDesignation(form);
        }),
        form.controls.levelId.valueChanges.subscribe(() => this.pickDesignation(form)),
        form.controls.designationId.valueChanges.subscribe((id) => this.designationChanged(form, id)),
        form.controls.locationId.valueChanges.subscribe((id) => this.fillCostCenter(form, id)),
        form.controls.companyId.valueChanges.subscribe(() => this.fillCostCenter(form, form.controls.locationId.value)),
      ];
    });
  }

  private async loadReferences(): Promise<void> {
    const [departments, roles, designations, locations, costCenters] = await Promise.all([
      this.api.active('departments'),
      this.api.active('roles'),
      this.api.active('designations'),
      this.api.active('locations'),
      this.api.active('cost-centers'),
    ]);
    this.departments.set(departments);
    [this.roles, this.designations, this.locations, this.costCenters] = [roles, designations, locations, costCenters];
  }

  /** A role that is not one of the new department's is cleared, with its designation. */
  private departmentChanged(form: AssignmentForm, departmentId: string | null): void {
    this.departmentId.set(departmentId);
    const fits = rolesFor(departmentId, this.departments());
    const role = this.roles.find((row) => row.id === form.controls.roleId.value);
    if (role && fits && !fits(role)) {
      form.controls.roleId.setValue(null, { emitEvent: false });
      form.controls.designationId.setValue(null, { emitEvent: false });
      this.roleId.set(null);
      this.roleHint.set(`${role.name} is not a role of this department, so it was cleared.`);
      this.designationHint.set(null);
      return;
    }
    this.roleHint.set(null);
  }

  /** Role and level chosen: the designation for that pair, when there is one. */
  private pickDesignation(form: AssignmentForm): void {
    const roleId = form.controls.roleId.value;
    const levelId = form.controls.levelId.value;
    if (!roleId || !levelId) {
      return;
    }
    const match = this.designations.find((row) => row['roleId'] === roleId && row['levelId'] === levelId);
    if (!match) {
      this.designationHint.set(null);
      return;
    }
    form.controls.designationId.setValue(match.id, { emitEvent: false });
    this.designationHint.set(`${match.name}, from the role and level.`);
  }

  /** A designation that is a role at a level fills in both. */
  private designationChanged(form: AssignmentForm, designationId: string | null): void {
    const designation = this.designations.find((row) => row.id === designationId);
    const roleId = typeof designation?.['roleId'] === 'string' ? (designation['roleId'] as string) : null;
    const levelId = typeof designation?.['levelId'] === 'string' ? (designation['levelId'] as string) : null;
    if (!designation || !roleId) {
      this.designationHint.set(null);
      return;
    }
    form.controls.roleId.setValue(roleId, { emitEvent: false });
    this.roleId.set(roleId);
    if (levelId) {
      form.controls.levelId.setValue(levelId, { emitEvent: false });
    }
    this.designationHint.set('The role and level were filled in from it.');
  }

  private fillCostCenter(form: AssignmentForm, locationId: string | null): void {
    const location = this.locations.find((row) => row.id === locationId);
    const own = typeof location?.['costCenterId'] === 'string' ? (location['costCenterId'] as string) : null;
    const company = form.controls.companyId.value ?? (typeof location?.['companyId'] === 'string' ? (location['companyId'] as string) : null);
    const defaults = this.costCenters.filter((row) => row['isDefault'] === true);
    const fallback = defaults.find((row) => row['companyId'] === company) ?? (defaults.length === 1 ? defaults[0] : undefined);
    const chosen = own ?? fallback?.id ?? null;

    if (!chosen) {
      this.costCenterHint.set(null);
      return;
    }

    form.controls.costCenterId.setValue(chosen);
    this.costCenterHint.set(own ? `Filled from the ${location?.name} office.` : `${location ? 'This office has no cost center of its own, so the' : 'The'} company default is used.`);
  }
}
