import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import { LookupSelect } from '@gggs/shared/master-data';
import { LOOKUP_LISTS } from '@gggs/shared/models';
import { PageHeader } from '@gggs/shared/ui';
import { AssignmentFields, assignmentForm, toAssignmentInput } from './assignment-fields';

/**
 * Hire someone: who they are, how to reach them, when they join and where they sit. Statutory,
 * bank and family details come afterwards on Employee 360, under their own permissions.
 */
@Component({
  selector: 'gggs-hire-employee-page',
  imports: [ReactiveFormsModule, RouterLink, PageHeader, AssignmentFields, LookupSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Hire an employee" subtitle="Creates the employee record and their first employment record.">
      <a class="btn" routerLink="..">Cancel</a>
    </gggs-page-header>

    <form class="stack" [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <section class="panel">
        <h2 class="hire__heading">Who</h2>
        <div class="form-grid">
          <label class="field">
            <span class="field__label">First name *</span>
            <input type="text" formControlName="firstName" maxlength="100" autocomplete="off" />
          </label>
          <label class="field">
            <span class="field__label">Middle name</span>
            <input type="text" formControlName="middleName" maxlength="100" />
          </label>
          <label class="field">
            <span class="field__label">Last name</span>
            <input type="text" formControlName="lastName" maxlength="100" />
          </label>
          <label class="field">
            <span class="field__label">Preferred name</span>
            <input type="text" formControlName="preferredName" maxlength="100" />
          </label>
          <label class="field">
            <span class="field__label">Gender</span>
            <gggs-lookup-select [list]="lists.gender" formControlName="genderCode" ariaLabel="Gender" />
          </label>
          <label class="field">
            <span class="field__label">Date of birth</span>
            <input type="date" formControlName="dateOfBirth" />
          </label>
          <label class="field">
            <span class="field__label">Employee code</span>
            <input type="text" formControlName="employeeCode" maxlength="32" placeholder="Generated if left blank" />
          </label>
        </div>
      </section>

      <section class="panel">
        <h2 class="hire__heading">Contact</h2>
        <div class="form-grid">
          <label class="field">
            <span class="field__label">Official email</span>
            <input type="email" formControlName="officialEmail" maxlength="256" />
          </label>
          <label class="field">
            <span class="field__label">Personal email</span>
            <input type="email" formControlName="personalEmail" maxlength="256" />
          </label>
          <label class="field">
            <span class="field__label">Mobile</span>
            <input type="tel" formControlName="mobilePhone" maxlength="32" />
          </label>
        </div>
      </section>

      <section class="panel">
        <h2 class="hire__heading">Employment</h2>
        <div class="form-grid">
          <label class="field">
            <span class="field__label">Joining date *</span>
            <input type="date" formControlName="joiningDate" />
          </label>
          <label class="field">
            <span class="field__label">Probation ends</span>
            <input type="date" formControlName="probationEndDate" />
          </label>
        </div>
        <gggs-assignment-fields [form]="form.controls.assignment" />
      </section>

      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }

      <div class="toolbar">
        <button type="submit" class="btn btn--primary" [disabled]="saving()">{{ saving() ? 'Hiring…' : 'Hire' }}</button>
        <a class="btn" routerLink="..">Cancel</a>
      </div>
    </form>
  `,
  styles: `
    .hire__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }
  `,
})
export class HireEmployeePage {
  private readonly api = inject(EmployeeDirectoryApi);
  private readonly router = inject(Router);

  protected readonly lists = LOOKUP_LISTS;
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    employeeCode: new FormControl<string | null>(null),
    firstName: new FormControl<string | null>(null, Validators.required),
    middleName: new FormControl<string | null>(null),
    lastName: new FormControl<string | null>(null),
    preferredName: new FormControl<string | null>(null),
    genderCode: new FormControl<string | null>(null),
    dateOfBirth: new FormControl<string | null>(null),
    officialEmail: new FormControl<string | null>(null, Validators.email),
    personalEmail: new FormControl<string | null>(null, Validators.email),
    mobilePhone: new FormControl<string | null>(null),
    joiningDate: new FormControl<string | null>(new Date().toISOString().slice(0, 10), Validators.required),
    probationEndDate: new FormControl<string | null>(null),
    assignment: assignmentForm(),
  });

  protected async submit(): Promise<void> {
    const value = this.form.getRawValue();
    const assignment = toAssignmentInput(this.form.controls.assignment);

    if (this.form.invalid || (!assignment.positionId && !assignment.departmentId)) {
      this.form.markAllAsTouched();
      this.error.set('Fill in the first name, joining date, and a position or department.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    try {
      const blank = (text: string | null) => (text?.trim() ? text.trim() : null);
      const hired = await this.api.hire({
        employeeCode: blank(value.employeeCode),
        firstName: value.firstName!.trim(),
        middleName: blank(value.middleName),
        lastName: blank(value.lastName),
        preferredName: blank(value.preferredName),
        genderCode: value.genderCode,
        dateOfBirth: value.dateOfBirth || null,
        personalEmail: blank(value.personalEmail),
        officialEmail: blank(value.officialEmail),
        mobilePhone: blank(value.mobilePhone),
        joiningDate: value.joiningDate!,
        probationEndDate: value.probationEndDate || null,
        assignment,
      });

      void this.router.navigate(['/employees', hired.id]);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The employee could not be hired. Try again.');
    } finally {
      this.saving.set(false);
    }
  }
}
