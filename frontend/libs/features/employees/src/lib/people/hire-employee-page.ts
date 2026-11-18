import { DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal, type OnInit } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { DocumentsApi } from '@gggs/shared/documents';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import { AssetsApi } from '@gggs/shared/experience';
import { RecruitmentApi } from '@gggs/shared/hiring';
import { AdminApi } from '@gggs/shared/identity';
import { LookupSelect } from '@gggs/shared/master-data';
import type { AttendanceMode, HireAddress, HireDetails, HireableOffer, HolidayCalendar, Role, StructureItem, WorkShift } from '@gggs/shared/models';
import { ATTENDANCE_MODES, LOOKUP_LISTS } from '@gggs/shared/models';
import { PayrollApi } from '@gggs/shared/money';
import { AttendanceApi, LeaveApi, ShiftsApi } from '@gggs/shared/time';
import { localIsoDate, momentTime, PageHeader, timeRange } from '@gggs/shared/ui';
import { startWith } from 'rxjs';
import { AssignmentFields, assignmentForm, toAssignmentInput } from './assignment-fields';

type StepKey = 'person' | 'contact' | 'job' | 'background' | 'ids' | 'pay' | 'login' | 'review';

const STEPS: readonly { readonly key: StepKey; readonly label: string }[] = [
  { key: 'person', label: 'Candidate & personal' },
  { key: 'contact', label: 'Contact & address' },
  { key: 'job', label: 'Job & attendance' },
  { key: 'background', label: 'Education & experience' },
  { key: 'ids', label: 'ID & documents' },
  { key: 'pay', label: 'Pay & bank' },
  { key: 'login', label: 'Login & roles' },
  { key: 'review', label: 'Review' },
];

/** The steps' names, for the Draft hires list to say where each draft stopped. */
export const HIRE_STEP_LABELS: readonly string[] = STEPS.map((item) => item.label);

/** What a draft keeps: the form's whole value and the choices held outside it. Read back only by this page. */
type DraftPayload = {
  readonly form?: unknown;
  readonly sameAddress?: boolean;
  readonly modes?: readonly AttendanceMode[];
  readonly roleIds?: readonly string[];
  readonly offerId?: string | null;
  readonly furthest?: number;
};

/** The document uploads the ID step offers, by the document type they are filed as. */
const DOCUMENTS = [
  { key: 'aadhaar', code: 'AADHAAR', label: 'Aadhaar card' },
  { key: 'pan', code: 'PAN', label: 'PAN card' },
  { key: 'passport', code: 'PASSPORT', label: 'Passport' },
  { key: 'offer', code: 'OFFER_LETTER', label: 'Offer letter' },
] as const;

type DocumentKey = (typeof DOCUMENTS)[number]['key'];

/** Everything the wizard collected, as one value. */
type HireFormValue = ReturnType<HireEmployeePage['form']['getRawValue']>;

/** One thing done after the employee exists, shown on the result checklist. */
interface FollowUp {
  readonly label: string;
  readonly run: () => Promise<void>;
  status: 'pending' | 'done' | 'failed';
  error?: string;
}

const AADHAAR = /^[2-9][0-9]{11}$/;
const PAN = /^[A-Z]{5}[0-9]{4}[A-Z]$/;
const PASSPORT = /^[A-Z0-9]{6,9}$/;
const MAX_PHOTO_BYTES = 2 * 1024 * 1024;

function addressGroup() {
  return new FormGroup({
    line1: new FormControl<string | null>(null),
    line2: new FormControl<string | null>(null),
    city: new FormControl<string | null>(null),
    countryCode: new FormControl<string | null>('IN'),
    stateCode: new FormControl<string | null>(null),
    postalCode: new FormControl<string | null>(null),
  });
}

type AddressGroup = ReturnType<typeof addressGroup>;

function blank(text: string | null | undefined): string | null {
  return text?.trim() ? text.trim() : null;
}

function toAddress(group: AddressGroup): HireAddress | null {
  const value = group.getRawValue();
  const address = { line1: blank(value.line1), line2: blank(value.line2), city: blank(value.city), stateCode: value.stateCode, countryCode: value.countryCode, postalCode: blank(value.postalCode) };
  return address.line1 || address.city || address.postalCode ? address : null;
}

function message(error: unknown, fallback: string): string {
  return error instanceof ApiError ? error.message : fallback;
}

/**
 * Hire an employee, step by step: from an offered candidate (pre-filled, and hired through the offer so
 * the requisition, referral and salary follow) or by hand. Everything the employee record holds is saved
 * with the hire in one go; the rest - photo, documents, shift, attendance modes, access card, salary and
 * login - follows, each shown on a checklist that can retry what failed without losing the hire.
 */
@Component({
  selector: 'gggs-hire-employee-page',
  imports: [ReactiveFormsModule, RouterLink, DecimalPipe, NgTemplateOutlet, PageHeader, AssignmentFields, LookupSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Hire an employee" subtitle="Creates the employee record and everything they need on day one.">
      <a class="btn" routerLink="..">Cancel</a>
    </gggs-page-header>

    @if (hired(); as done) {
      <section class="panel stack">
        <h2 class="hire__heading"><i class="pi pi-check-circle hire__ok" aria-hidden="true"></i> {{ done.name }} is hired as {{ done.code }}</h2>
        <ul class="hire__checklist">
          @for (item of followUps(); track item.label) {
            <li>
              @switch (item.status) {
                @case ('done') { <i class="pi pi-check hire__ok" aria-label="Done"></i> }
                @case ('failed') { <i class="pi pi-times hire__bad" aria-label="Failed"></i> }
                @default { <i class="pi pi-spin pi-spinner" aria-label="Working"></i> }
              }
              <span>{{ item.label }}</span>
              @if (item.status === 'failed') {
                <span class="hire__error">{{ item.error }}</span>
                <button type="button" class="btn btn--sm" (click)="retry(item)">Retry</button>
              }
            </li>
          }
        </ul>
        @if (invitation()) {
          <p class="alert alert--info" role="status">
            <i class="pi pi-envelope" aria-hidden="true"></i>
            <span>Login created for {{ invitation() }}. They get an email to set their password.</span>
          </p>
        }
        <div class="toolbar">
          <a class="btn btn--primary" [routerLink]="['/employees', done.id]">Open Employee 360</a>
          <a class="btn" routerLink="..">Back to the directory</a>
        </div>
      </section>
    } @else {
      <ol class="hire__steps" aria-label="Steps">
        @for (item of steps; track item.key; let i = $index) {
          <li>
            <button type="button" class="hire__step" [class.hire__step--current]="i === step()" [class.hire__step--done]="i < step()" [attr.aria-current]="i === step() ? 'step' : null" [disabled]="i > furthest()" (click)="go(i)">
              <span class="hire__step-number">{{ i + 1 }}</span>{{ item.label }}
            </button>
          </li>
        }
      </ol>

      <form class="stack" [formGroup]="form" (ngSubmit)="next()" novalidate>
        @switch (current()) {
          @case ('person') {
            <section class="panel stack">
              <h2 class="hire__heading">Candidate</h2>
              <label class="field">
                <span class="field__label">From an offered candidate <span class="muted">(optional)</span></span>
                <select [value]="offer()?.offerId ?? ''" (change)="chooseOffer($any($event.target).value)" aria-label="Offered candidate">
                  <option value="">— Hire without a candidate —</option>
                  @for (row of offers(); track row.offerId) {
                    <option [value]="row.offerId">{{ row.firstName }} {{ row.lastName }} · {{ row.requisitionTitle }} · joins {{ row.joiningDate }}</option>
                  }
                </select>
                <span class="muted hire__hint">
                  @if (offer(); as o) {
                    Filled in from offer {{ o.offerNumber }} ({{ o.designation }}, {{ o.currency }} {{ o.annualCtc | number }}). Name, department, role and level come from the offer.
                  } @else if (offers().length === 0) {
                    No accepted offers are waiting to be hired.
                  } @else {
                    Only candidates who accepted an offer are listed.
                  }
                </span>
              </label>
            </section>
            <section class="panel stack" formGroupName="person">
              <h2 class="hire__heading">Personal</h2>
              <div class="hire__photo">
                <div class="hire__avatar" aria-hidden="true">
                  @if (photoUrl(); as url) { <img [src]="url" alt="" /> } @else { <i class="pi pi-user"></i> }
                </div>
                <label class="btn">
                  <i class="pi pi-upload" aria-hidden="true"></i> {{ photo() ? 'Change photo' : 'Upload photo' }}
                  <input type="file" accept="image/jpeg,image/png,image/webp" class="hire__file" (change)="choosePhoto($event)" />
                </label>
                @if (photo()) { <button type="button" class="btn btn--link" (click)="clearPhoto()">Remove</button> }
                <span class="muted hire__hint">JPEG, PNG or WebP, up to 2 MB.</span>
              </div>
              <div class="form-grid">
                <label class="field"><span class="field__label">First name *</span><input type="text" formControlName="firstName" maxlength="100" autocomplete="off" /></label>
                <label class="field"><span class="field__label">Middle name</span><input type="text" formControlName="middleName" maxlength="100" /></label>
                <label class="field"><span class="field__label">Last name</span><input type="text" formControlName="lastName" maxlength="100" /></label>
                <label class="field"><span class="field__label">Preferred name</span><input type="text" formControlName="preferredName" maxlength="100" /></label>
                <label class="field"><span class="field__label">Gender</span><gggs-lookup-select [list]="lists.gender" formControlName="genderCode" ariaLabel="Gender" /></label>
                <label class="field"><span class="field__label">Date of birth</span><input type="date" formControlName="dateOfBirth" [max]="today" /></label>
                <label class="field"><span class="field__label">Marital status</span><gggs-lookup-select [list]="lists.maritalStatus" formControlName="maritalStatusCode" ariaLabel="Marital status" /></label>
                <label class="field"><span class="field__label">Blood group</span><gggs-lookup-select [list]="lists.bloodGroup" formControlName="bloodGroupCode" ariaLabel="Blood group" /></label>
                <label class="field"><span class="field__label">Nationality</span><gggs-lookup-select [list]="lists.country" formControlName="nationalityCode" ariaLabel="Nationality" /></label>
                <label class="field"><span class="field__label">Employee code</span><input type="text" formControlName="employeeCode" maxlength="32" placeholder="Generated if left blank" /></label>
              </div>
            </section>
          }

          @case ('contact') {
            <section class="panel stack" formGroupName="contact">
              <h2 class="hire__heading">Contact</h2>
              <div class="form-grid">
                <label class="field"><span class="field__label">Official email</span><input type="email" formControlName="officialEmail" maxlength="256" /></label>
                <label class="field"><span class="field__label">Personal email</span><input type="email" formControlName="personalEmail" maxlength="256" /></label>
                <label class="field"><span class="field__label">Mobile</span><input type="tel" formControlName="mobilePhone" maxlength="32" /></label>
              </div>
            </section>
            <section class="panel stack">
              <h2 class="hire__heading">Present address</h2>
              <ng-container *ngTemplateOutlet="address; context: { $implicit: form.controls.present, label: 'Present' }" />
            </section>
            <section class="panel stack">
              <h2 class="hire__heading">Permanent address</h2>
              <label class="hire__check"><input type="checkbox" [formControl]="sameAddress" /> Same as present address</label>
              @if (!sameAddress.value) {
                <ng-container *ngTemplateOutlet="address; context: { $implicit: form.controls.permanent, label: 'Permanent' }" />
              }
            </section>
          }

          @case ('job') {
            <section class="panel stack" formGroupName="job">
              <h2 class="hire__heading">Employment</h2>
              <div class="form-grid">
                <label class="field"><span class="field__label">Joining date *</span><input type="date" formControlName="joiningDate" /></label>
                <label class="field"><span class="field__label">Probation ends</span><input type="date" formControlName="probationEndDate" /></label>
              </div>
              <gggs-assignment-fields [form]="form.controls.assignment" />
            </section>
            <section class="panel stack" formGroupName="job">
              <h2 class="hire__heading">Attendance</h2>
              <div class="form-grid">
                <label class="field">
                  <span class="field__label">Shift timing</span>
                  <select formControlName="shiftId" aria-label="Shift">
                    <option value="">Default shift{{ defaultShift() ? ' (' + shiftLabel(defaultShift()!) + ')' : '' }}</option>
                    @for (shift of shifts(); track shift.id) {
                      <option [value]="shift.id">{{ shiftLabel(shift) }}</option>
                    }
                  </select>
                </label>
                <div class="field">
                  <span class="field__label">Holiday calendar</span>
                  <p class="hire__readonly">{{ holidayCalendar()?.name ?? 'None set up' }}</p>
                  <span class="muted hire__hint">Follows the office location chosen above.</span>
                </div>
                <label class="field"><span class="field__label">Access card no.</span><input type="text" formControlName="accessCard" maxlength="60" autocomplete="off" /><span class="muted hire__hint">Issued as an access-card asset, collected back at exit.</span></label>
              </div>
              <fieldset class="hire__fieldset">
                <legend class="field__label">Attendance mode</legend>
                @for (mode of attendanceModes; track mode.value) {
                  <label class="hire__check"><input type="checkbox" [checked]="modes().includes(mode.value)" (change)="toggleMode(mode.value)" /> {{ mode.label }} <span class="muted">— {{ mode.hint }}</span></label>
                }
              </fieldset>
            </section>
          }

          @case ('background') {
            <section class="panel stack" formGroupName="background">
              <h2 class="hire__heading">Education</h2>
              <div class="form-grid">
                <label class="field"><span class="field__label">Graduation</span><input type="text" formControlName="graduationDegree" maxlength="120" placeholder="B.Tech, B.Com, BA…" /></label>
                <label class="field"><span class="field__label">Graduation institute</span><input type="text" formControlName="graduationInstitution" maxlength="200" /></label>
                <label class="field"><span class="field__label">Graduation year</span><input type="number" formControlName="graduationYear" min="1950" max="2100" /></label>
                <label class="field"><span class="field__label">Post graduation</span><input type="text" formControlName="postGraduationDegree" maxlength="120" placeholder="MBA, M.Tech, MCA…" /></label>
                <label class="field"><span class="field__label">Post graduation institute</span><input type="text" formControlName="postGraduationInstitution" maxlength="200" /></label>
                <label class="field"><span class="field__label">Post graduation year</span><input type="number" formControlName="postGraduationYear" min="1950" max="2100" /></label>
              </div>
            </section>
            <section class="panel stack" formGroupName="background">
              <h2 class="hire__heading">Experience before joining</h2>
              <div class="form-grid">
                <label class="field"><span class="field__label">Total experience (years)</span><input type="number" formControlName="experienceYears" min="0" max="60" step="0.5" /></label>
                <label class="field"><span class="field__label">Last employer</span><input type="text" formControlName="lastCompany" maxlength="200" /></label>
                <label class="field"><span class="field__label">Last title</span><input type="text" formControlName="lastTitle" maxlength="120" /></label>
                <label class="field"><span class="field__label">From</span><input type="month" formControlName="lastFrom" /></label>
                <label class="field"><span class="field__label">To</span><input type="month" formControlName="lastTo" /></label>
              </div>
            </section>
          }

          @case ('ids') {
            <section class="panel stack" formGroupName="ids">
              <h2 class="hire__heading">Identity</h2>
              <div class="form-grid">
                <label class="field">
                  <span class="field__label">Aadhaar number *</span>
                  <input type="text" formControlName="aadhaar" inputmode="numeric" maxlength="14" autocomplete="off" placeholder="12 digits" />
                  @if (form.controls.ids.controls.aadhaar.touched && form.controls.ids.controls.aadhaar.invalid) { <span class="hire__error">Enter the 12-digit Aadhaar number.</span> }
                </label>
                <label class="field">
                  <span class="field__label">PAN</span>
                  <input type="text" formControlName="pan" maxlength="10" autocomplete="off" placeholder="ABCDE1234F" />
                  @if (form.controls.ids.controls.pan.touched && form.controls.ids.controls.pan.invalid) { <span class="hire__error">A PAN is five letters, four digits and a letter.</span> }
                </label>
                <label class="field"><span class="field__label">Passport number</span><input type="text" formControlName="passportNumber" maxlength="9" autocomplete="off" /></label>
                <label class="field"><span class="field__label">Passport expiry</span><input type="date" formControlName="passportExpiry" [min]="today" /></label>
              </div>
            </section>
            <section class="panel stack">
              <h2 class="hire__heading">Documents</h2>
              <p class="muted hire__hint">Filed in the employee's documents, the same place the onboarding "joining documents" task collects them.</p>
              <ul class="hire__docs">
                @for (doc of documents; track doc.key) {
                  <li>
                    <span>{{ doc.label }}</span>
                    <span class="muted">{{ files()[doc.key]?.name ?? 'No file' }}</span>
                    <label class="btn btn--sm">
                      <i class="pi pi-paperclip" aria-hidden="true"></i> {{ files()[doc.key] ? 'Replace' : 'Attach' }}
                      <input type="file" class="hire__file" accept=".pdf,.jpg,.jpeg,.png" (change)="chooseFile(doc.key, $event)" />
                    </label>
                  </li>
                }
              </ul>
            </section>
          }

          @case ('pay') {
            <section class="panel stack" formGroupName="pay">
              <h2 class="hire__heading">Compensation</h2>
              @if (offer(); as o) {
                <p class="alert alert--info"><i class="pi pi-info-circle" aria-hidden="true"></i><span>The salary is recorded from the offer: {{ o.currency }} {{ o.annualCtc | number }} a year.</span></p>
              } @else {
                <div class="form-grid">
                  <label class="field"><span class="field__label">Annual CTC</span><input type="number" formControlName="annualCtc" min="0" step="1000" /></label>
                  <label class="field">
                    <span class="field__label">Salary structure</span>
                    <select formControlName="structureId" aria-label="Salary structure">
                      <option value="">Choose…</option>
                      @for (structure of structures(); track structure.id) { <option [value]="structure.id">{{ structure.name }}</option> }
                    </select>
                  </label>
                </div>
              }
              <h2 class="hire__heading">Bank</h2>
              <div class="form-grid">
                <label class="field"><span class="field__label">Bank name</span><input type="text" formControlName="bankName" maxlength="200" /></label>
                <label class="field"><span class="field__label">IFSC</span><input type="text" formControlName="ifsc" maxlength="11" autocomplete="off" /></label>
                <label class="field"><span class="field__label">Account holder</span><input type="text" formControlName="accountHolder" maxlength="200" /></label>
                <label class="field"><span class="field__label">Account number</span><input type="text" formControlName="accountNumber" maxlength="18" inputmode="numeric" autocomplete="off" /></label>
              </div>
            </section>
          }

          @case ('login') {
            <section class="panel stack" formGroupName="login">
              <h2 class="hire__heading">Login</h2>
              <label class="hire__check"><input type="checkbox" formControlName="create" /> Create a login for this employee</label>
              @if (form.controls.login.controls.create.value) {
                <div class="form-grid">
                  <label class="field">
                    <span class="field__label">User ID (email) *</span>
                    <input type="email" formControlName="userId" maxlength="256" autocomplete="off" />
                    <span class="muted hire__hint">They sign in with this and get an email to set their password.</span>
                  </label>
                </div>
                <fieldset class="hire__fieldset">
                  <legend class="field__label">Roles & rights</legend>
                  @for (role of roles(); track role.id) {
                    <label class="hire__check"><input type="checkbox" [checked]="roleIds().includes(role.id)" (change)="toggleRole(role.id)" /> {{ role.name }} @if (role.description) { <span class="muted">— {{ role.description }}</span> }</label>
                  } @empty {
                    <p class="muted">No roles to choose from.</p>
                  }
                </fieldset>
              }
            </section>
          }

          @case ('review') {
            <section class="panel stack">
              <h2 class="hire__heading">Review</h2>
              <dl class="hire__review">
                @for (row of review(); track row[0]) {
                  <dt>{{ row[0] }}</dt><dd>{{ row[1] || '—' }}</dd>
                }
              </dl>
            </section>
          }
        }

        @if (error()) {
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
        }
        @if (draftNote()) {
          <p class="alert alert--info" role="status"><i class="pi pi-save" aria-hidden="true"></i><span>{{ draftNote() }}</span></p>
        }

        <div class="toolbar">
          @if (step() > 0) { <button type="button" class="btn" (click)="back()"><i class="pi pi-arrow-left" aria-hidden="true"></i> Back</button> }
          @if (current() === 'review') {
            <button type="button" class="btn btn--primary" [disabled]="saving()" (click)="hire()">{{ saving() ? 'Hiring…' : 'Hire' }}</button>
          } @else {
            <button type="submit" class="btn btn--primary">Next <i class="pi pi-arrow-right" aria-hidden="true"></i></button>
          }
          <button type="button" class="btn" [disabled]="draftBusy() || saving()" (click)="saveDraft()">
            <i class="pi pi-save" aria-hidden="true"></i> {{ draftBusy() ? 'Saving…' : 'Save as draft' }}
          </button>
          <a class="btn btn--link" routerLink="..">Cancel</a>
        </div>
      </form>
    }

    <ng-template #address let-group let-label="label">
      <div class="form-grid" [formGroup]="group">
        <label class="field form-grid__full"><span class="field__label">Address line 1</span><input type="text" formControlName="line1" maxlength="200" /></label>
        <label class="field form-grid__full"><span class="field__label">Address line 2</span><input type="text" formControlName="line2" maxlength="200" /></label>
        <label class="field"><span class="field__label">City</span><input type="text" formControlName="city" maxlength="100" /></label>
        <label class="field"><span class="field__label">Country</span><gggs-lookup-select [list]="lists.country" formControlName="countryCode" [ariaLabel]="label + ' country'" /></label>
        <label class="field"><span class="field__label">State</span><gggs-lookup-select [list]="lists.state" [parent]="group.controls.countryCode.value" formControlName="stateCode" [ariaLabel]="label + ' state'" /></label>
        <label class="field"><span class="field__label">PIN code</span><input type="text" formControlName="postalCode" maxlength="12" /></label>
      </div>
    </ng-template>
  `,
  styles: `
    .hire__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .hire__steps {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .hire__step {
      display: inline-flex;
      align-items: center;
      gap: var(--gggs-space-2);
      padding: var(--gggs-space-1) var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: 999px;
      background: var(--gggs-color-surface);
      color: var(--gggs-color-text-muted);
      font: inherit;
      font-size: var(--gggs-font-size-sm);
      cursor: pointer;
    }

    .hire__step:disabled {
      cursor: default;
      opacity: 0.6;
    }

    .hire__step--current {
      border-color: var(--gggs-color-primary);
      background: var(--gggs-color-primary-subtle);
      color: var(--gggs-color-primary);
      font-weight: 600;
    }

    .hire__step--done {
      color: var(--gggs-color-text);
    }

    .hire__step-number {
      display: inline-grid;
      place-items: center;
      width: 1.4rem;
      height: 1.4rem;
      border-radius: 50%;
      background: var(--gggs-color-border);
      font-size: var(--gggs-font-size-xs);
    }

    .hire__step--current .hire__step-number {
      background: var(--gggs-color-primary);
      color: var(--gggs-color-primary-contrast);
    }

    .hire__photo,
    .hire__docs li,
    .hire__checklist li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--gggs-space-3);
    }

    .hire__avatar {
      display: grid;
      place-items: center;
      width: 4rem;
      height: 4rem;
      overflow: hidden;
      border-radius: 50%;
      background: var(--gggs-color-primary-subtle);
      color: var(--gggs-color-primary);
      font-size: 1.5rem;
    }

    .hire__avatar img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    .hire__file {
      position: absolute;
      width: 1px;
      height: 1px;
      opacity: 0;
    }

    .hire__docs,
    .hire__checklist {
      display: grid;
      gap: var(--gggs-space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .hire__docs li > span:first-child {
      min-width: 9rem;
      font-weight: 600;
    }

    .hire__fieldset {
      display: grid;
      gap: var(--gggs-space-2);
      margin: 0;
      padding: 0;
      border: 0;
    }

    .hire__check {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-2);
    }

    .hire__hint,
    .hire__error {
      font-size: var(--gggs-font-size-xs);
    }

    .hire__error,
    .hire__bad {
      color: var(--gggs-color-danger);
    }

    .hire__ok {
      color: var(--gggs-color-success);
    }

    .hire__readonly {
      margin: 0;
      padding: var(--gggs-space-2) 0;
      font-weight: 600;
    }

    .hire__review {
      display: grid;
      grid-template-columns: minmax(10rem, max-content) 1fr;
      gap: var(--gggs-space-2) var(--gggs-space-4);
      margin: 0;
    }

    .hire__review dt {
      color: var(--gggs-color-text-muted);
    }

    .hire__review dd {
      margin: 0;
    }
  `,
})
export class HireEmployeePage implements OnInit {
  private readonly directory = inject(EmployeeDirectoryApi);
  private readonly hiring = inject(RecruitmentApi);
  private readonly shiftsApi = inject(ShiftsApi);
  private readonly leave = inject(LeaveApi);
  private readonly attendance = inject(AttendanceApi);
  private readonly payroll = inject(PayrollApi);
  private readonly admin = inject(AdminApi);
  private readonly assets = inject(AssetsApi);
  private readonly documentsApi = inject(DocumentsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly lists = LOOKUP_LISTS;
  protected readonly steps = STEPS;
  protected readonly documents = DOCUMENTS;
  protected readonly attendanceModes = ATTENDANCE_MODES;
  protected readonly today = localIsoDate();

  protected readonly step = signal(0);
  protected readonly furthest = signal(0);
  protected readonly current = computed(() => STEPS[this.step()].key);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly offers = signal<readonly HireableOffer[]>([]);
  protected readonly offer = signal<HireableOffer | null>(null);
  protected readonly shifts = signal<readonly WorkShift[]>([]);
  protected readonly calendars = signal<readonly HolidayCalendar[]>([]);
  protected readonly structures = signal<readonly StructureItem[]>([]);
  protected readonly roles = signal<readonly Role[]>([]);

  protected readonly photo = signal<File | null>(null);
  protected readonly photoUrl = signal<string | null>(null);
  protected readonly files = signal<Partial<Record<DocumentKey, File>>>({});
  protected readonly modes = signal<readonly AttendanceMode[]>(['Web', 'Device']);
  protected readonly roleIds = signal<readonly string[]>([]);

  protected readonly hired = signal<{ id: string; code: string; name: string } | null>(null);
  protected readonly followUps = signal<FollowUp[]>([]);
  protected readonly invitation = signal<string | null>(null);

  /** The draft this form is saved as, once saved or opened from the Draft hires list. */
  protected readonly draftId = signal<string | null>(null);
  protected readonly draftBusy = signal(false);
  protected readonly draftNote = signal<string | null>(null);
  /** The file each slot holds on the server, so a save sends only what changed. */
  private readonly draftFiles: Partial<Record<string, File>> = {};

  protected readonly sameAddress = new FormControl(true, { nonNullable: true });

  readonly form = new FormGroup({
    person: new FormGroup({
      employeeCode: new FormControl<string | null>(null),
      firstName: new FormControl<string | null>(null, Validators.required),
      middleName: new FormControl<string | null>(null),
      lastName: new FormControl<string | null>(null),
      preferredName: new FormControl<string | null>(null),
      genderCode: new FormControl<string | null>(null),
      dateOfBirth: new FormControl<string | null>(null),
      maritalStatusCode: new FormControl<string | null>(null),
      bloodGroupCode: new FormControl<string | null>(null),
      nationalityCode: new FormControl<string | null>('IN'),
    }),
    contact: new FormGroup({
      officialEmail: new FormControl<string | null>(null, Validators.email),
      personalEmail: new FormControl<string | null>(null, Validators.email),
      mobilePhone: new FormControl<string | null>(null),
    }),
    present: addressGroup(),
    permanent: addressGroup(),
    job: new FormGroup({
      joiningDate: new FormControl<string | null>(localIsoDate(), Validators.required),
      probationEndDate: new FormControl<string | null>(null),
      shiftId: new FormControl<string>('', { nonNullable: true }),
      accessCard: new FormControl<string | null>(null),
    }),
    assignment: assignmentForm(),
    background: new FormGroup({
      graduationDegree: new FormControl<string | null>(null),
      graduationInstitution: new FormControl<string | null>(null),
      graduationYear: new FormControl<number | null>(null),
      postGraduationDegree: new FormControl<string | null>(null),
      postGraduationInstitution: new FormControl<string | null>(null),
      postGraduationYear: new FormControl<number | null>(null),
      experienceYears: new FormControl<number | null>(null, [Validators.min(0), Validators.max(60)]),
      lastCompany: new FormControl<string | null>(null),
      lastTitle: new FormControl<string | null>(null),
      lastFrom: new FormControl<string | null>(null),
      lastTo: new FormControl<string | null>(null),
    }),
    ids: new FormGroup({
      aadhaar: new FormControl<string | null>(null, [Validators.required, (control) => (AADHAAR.test(String(control.value ?? '').replace(/\s/g, '')) ? null : { aadhaar: true })]),
      pan: new FormControl<string | null>(null, (control) => (!control.value || PAN.test(String(control.value).trim().toUpperCase()) ? null : { pan: true })),
      passportNumber: new FormControl<string | null>(null, (control) => (!control.value || PASSPORT.test(String(control.value).trim().toUpperCase()) ? null : { passport: true })),
      passportExpiry: new FormControl<string | null>(null),
    }),
    pay: new FormGroup({
      annualCtc: new FormControl<number | null>(null, Validators.min(0)),
      structureId: new FormControl<string>('', { nonNullable: true }),
      bankName: new FormControl<string | null>(null),
      ifsc: new FormControl<string | null>(null),
      accountHolder: new FormControl<string | null>(null),
      accountNumber: new FormControl<string | null>(null),
    }),
    login: new FormGroup({
      create: new FormControl(true, { nonNullable: true }),
      userId: new FormControl<string | null>(null, Validators.email),
    }),
  });

  private readonly locationId = toSignal(this.form.controls.assignment.controls.locationId.valueChanges.pipe(startWith(this.form.controls.assignment.controls.locationId.value)), {
    initialValue: null,
  });

  protected readonly defaultShift = computed(() => this.shifts().find((shift) => shift.isDefault) ?? null);

  /** The calendar that follows the chosen office, else the organisation's default. */
  protected readonly holidayCalendar = computed(() => {
    const location = this.locationId();
    const calendars = this.calendars();
    return (location ? calendars.find((calendar) => calendar.locationIds.includes(location)) : undefined) ?? calendars.find((calendar) => calendar.isDefault) ?? null;
  });

  async ngOnInit(): Promise<void> {
    // Each list is optional: someone without a module's permission simply gets no choices from it.
    const quietly = <T>(promise: Promise<readonly T[]>): Promise<readonly T[]> => promise.catch(() => []);
    const [offers, shifts, calendars, structures, roles] = await Promise.all([
      quietly(this.hiring.hireable()),
      quietly(this.shiftsApi.list()),
      quietly(this.leave.calendars()),
      quietly(this.payroll.structures()),
      quietly(this.admin.listRoles()),
    ]);
    this.offers.set(offers);
    this.shifts.set(shifts.filter((shift) => shift.isActive !== false));
    this.calendars.set(calendars);
    this.structures.set(structures.filter((structure) => structure.isActive));
    this.roles.set(roles);

    const draft = this.route.snapshot.queryParamMap.get('draft');
    if (draft) {
      await this.openDraft(draft);
    }
  }

  /** Saves everything typed so far, files included, to finish later. Nothing is checked: a draft may be incomplete. */
  protected async saveDraft(): Promise<void> {
    this.draftBusy.set(true);
    this.error.set(null);
    try {
      const value = this.form.getRawValue();
      const name = [value.person.firstName, value.person.middleName, value.person.lastName].filter((part) => part?.trim()).join(' ');
      const offerId = this.offer()?.offerId ?? null;
      const payload: DraftPayload = { form: value, sameAddress: this.sameAddress.value, modes: this.modes(), roleIds: this.roleIds(), offerId, furthest: this.furthest() };
      const saved = await this.directory.saveHireDraft(this.draftId(), { displayName: name || null, step: this.step(), offerId, payload });
      if (!this.draftId()) {
        this.draftId.set(saved.id);
        // The address now opens this draft, so a refresh or a bookmark carries on from here.
        void this.router.navigate([], { relativeTo: this.route, queryParams: { draft: saved.id }, replaceUrl: true });
      }
      await this.syncDraftFiles(saved.id);
      const time = momentTime(new Date());
      this.draftNote.set(`Draft saved at ${time}. Carry on now, or later from HR Portal › Directory › Draft hires.`);
    } catch (error) {
      this.error.set(message(error, 'The draft could not be saved. Try again.'));
    } finally {
      this.draftBusy.set(false);
    }
  }

  /** Puts a saved draft back into the form, with its photo and documents, on the step where it stopped. */
  private async openDraft(id: string): Promise<void> {
    try {
      const draft = await this.directory.hireDraft(id);
      const saved = draft.payload as DraftPayload;
      if (saved.offerId) {
        if (this.offers().some((offer) => offer.offerId === saved.offerId)) {
          this.chooseOffer(saved.offerId);
        } else {
          this.error.set('The offer this draft was hiring from is no longer open, so the candidate is not filled in. Choose again or hire directly.');
        }
      }
      if (saved.form && typeof saved.form === 'object') {
        this.form.patchValue(saved.form as Parameters<HireEmployeePage['form']['patchValue']>[0]);
      }
      if (typeof saved.sameAddress === 'boolean') {
        this.sameAddress.setValue(saved.sameAddress);
      }
      if (Array.isArray(saved.modes)) {
        this.modes.set(saved.modes);
      }
      if (Array.isArray(saved.roleIds)) {
        this.roleIds.set(saved.roleIds);
      }
      const furthest = Math.min(Math.max(saved.furthest ?? draft.step, draft.step), STEPS.length - 1);
      this.furthest.set(furthest);
      this.step.set(Math.min(draft.step, furthest));
      this.draftId.set(id);

      for (const file of draft.files) {
        const kept = new File([await this.directory.hireDraftFile(id, file.slot)], file.fileName, { type: file.contentType });
        this.draftFiles[file.slot] = kept;
        if (file.slot === 'photo') {
          this.clearPhoto();
          this.photo.set(kept);
          this.photoUrl.set(URL.createObjectURL(kept));
        } else {
          const doc = DOCUMENTS.find((candidate) => candidate.code === file.slot);
          if (doc) {
            this.files.update((files) => ({ ...files, [doc.key]: kept }));
          }
        }
      }
      this.draftNote.set(`Carrying on the draft for ${draft.displayName}. Save it again any time.`);
    } catch (error) {
      this.error.set(message(error, 'The draft could not be opened. It may have been hired or deleted already.'));
    }
  }

  /** Sends each photo or document that changed since the last save, and removes one taken away. */
  private async syncDraftFiles(id: string): Promise<void> {
    const wanted: Record<string, File | undefined> = { photo: this.photo() ?? undefined };
    for (const doc of DOCUMENTS) {
      wanted[doc.code] = this.files()[doc.key];
    }
    for (const [slot, file] of Object.entries(wanted)) {
      const stored = this.draftFiles[slot];
      if (file && file !== stored) {
        await this.directory.putHireDraftFile(id, slot, file);
        this.draftFiles[slot] = file;
      } else if (!file && stored) {
        await this.directory.removeHireDraftFile(id, slot);
        delete this.draftFiles[slot];
      }
    }
  }

  protected shiftLabel(shift: WorkShift): string {
    return `${shift.name} (${timeRange(shift.startTime, shift.endTime)})`;
  }

  /** Fills the form from an accepted offer; the offer's own terms (name, department, role, level) are locked. */
  protected chooseOffer(offerId: string): void {
    const offer = this.offers().find((row) => row.offerId === offerId) ?? null;
    this.offer.set(offer);
    const person = this.form.controls.person.controls;
    const assignment = this.form.controls.assignment.controls;
    // The offer hire takes these from the candidate and the requisition, so they cannot be changed here.
    const locked = [person.employeeCode, person.firstName, person.lastName, person.middleName, assignment.departmentId, assignment.roleId, assignment.levelId, assignment.designationId];

    if (!offer) {
      locked.forEach((control) => control.enable());
      return;
    }

    person.firstName.setValue(offer.firstName);
    person.middleName.setValue(null);
    person.lastName.setValue(offer.lastName ?? null);
    this.form.controls.contact.controls.personalEmail.setValue(offer.email);
    this.form.controls.contact.controls.mobilePhone.setValue(offer.mobile ?? null);
    this.form.controls.job.controls.joiningDate.setValue(offer.joiningDate);
    assignment.departmentId.setValue(offer.departmentId ?? null);
    assignment.roleId.setValue(offer.roleId ?? null);
    assignment.levelId.setValue(offer.levelId ?? null);
    assignment.locationId.setValue(offer.locationId ?? null);
    assignment.managerEmployeeId.setValue(offer.managerEmployeeId ?? null);
    if (offer.employmentTypeCode) {
      assignment.employmentTypeCode.setValue(offer.employmentTypeCode);
    }
    if (offer.workModeCode) {
      assignment.workModeCode.setValue(offer.workModeCode);
    }
    this.form.controls.background.controls.experienceYears.setValue(offer.experienceYears ?? null);
    this.form.controls.pay.controls.annualCtc.setValue(offer.annualCtc);
    locked.forEach((control) => control.disable());
  }

  protected choosePhoto(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    if (!file) {
      return;
    }
    if (file.size > MAX_PHOTO_BYTES) {
      this.error.set('The photo is larger than 2 MB. Choose a smaller one.');
      return;
    }
    this.error.set(null);
    this.clearPhoto();
    this.photo.set(file);
    this.photoUrl.set(URL.createObjectURL(file));
  }

  protected clearPhoto(): void {
    const url = this.photoUrl();
    if (url) {
      URL.revokeObjectURL(url);
    }
    this.photo.set(null);
    this.photoUrl.set(null);
  }

  protected chooseFile(key: DocumentKey, event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) {
      this.files.update((files) => ({ ...files, [key]: file }));
    }
  }

  protected toggleMode(mode: AttendanceMode): void {
    this.modes.update((modes) => (modes.includes(mode) ? modes.filter((item) => item !== mode) : [...modes, mode]));
  }

  protected toggleRole(id: string): void {
    this.roleIds.update((ids) => (ids.includes(id) ? ids.filter((item) => item !== id) : [...ids, id]));
  }

  protected go(index: number): void {
    if (index <= this.furthest()) {
      this.error.set(null);
      this.step.set(index);
    }
  }

  protected back(): void {
    this.go(this.step() - 1);
  }

  /** Checks the step and moves on. */
  protected next(): void {
    const problem = this.check(this.current());
    if (problem) {
      this.error.set(problem);
      return;
    }

    this.error.set(null);
    if (this.current() === 'contact' && !this.form.controls.login.controls.userId.value) {
      this.form.controls.login.controls.userId.setValue(blank(this.form.controls.contact.controls.officialEmail.value));
    }
    const nextStep = Math.min(this.step() + 1, STEPS.length - 1);
    this.step.set(nextStep);
    this.furthest.update((furthest) => Math.max(furthest, nextStep));
  }

  private check(step: StepKey): string | null {
    const controls = this.form.controls;
    switch (step) {
      case 'person':
        controls.person.markAllAsTouched();
        return controls.person.controls.firstName.value?.trim() ? null : 'Fill in the first name.';
      case 'contact':
        controls.contact.markAllAsTouched();
        return controls.contact.valid ? null : 'Check the email addresses.';
      case 'job':
        controls.job.markAllAsTouched();
        controls.assignment.markAllAsTouched();
        if (!controls.job.controls.joiningDate.value || !controls.assignment.controls.departmentId.value) {
          return 'Fill in the joining date and department.';
        }
        return this.modes().length ? null : 'Choose at least one attendance mode.';
      case 'background':
        return controls.background.valid ? null : 'Total experience must be between 0 and 60 years.';
      case 'ids': {
        controls.ids.markAllAsTouched();
        const ids = controls.ids.controls;
        if (ids.aadhaar.invalid) {
          return 'Enter the 12-digit Aadhaar number.';
        }
        if (ids.pan.invalid) {
          return 'That PAN is not valid (five letters, four digits, a letter).';
        }
        if (ids.passportNumber.invalid) {
          return 'A passport number is six to nine letters or digits.';
        }
        if (ids.passportNumber.value && !ids.passportExpiry.value) {
          return 'Add the passport expiry date.';
        }
        return null;
      }
      case 'pay': {
        const pay = controls.pay.controls;
        if (!this.offer() && pay.annualCtc.value && !pay.structureId.value) {
          return 'Choose the salary structure for the CTC.';
        }
        return pay.accountNumber.value && !pay.ifsc.value ? 'A bank account needs the IFSC code.' : null;
      }
      case 'login': {
        const login = controls.login.controls;
        if (login.create.value && !login.userId.value?.trim()) {
          return 'Enter the user ID (email) they will sign in with.';
        }
        return login.userId.valid ? null : 'The user ID must be an email address.';
      }
      default:
        return null;
    }
  }

  /** What the review step lists, as label and value pairs. */
  protected readonly review = computed((): readonly [string, string][] => {
    this.step();
    const value = this.form.getRawValue();
    const offer = this.offer();
    const name = [value.person.firstName, value.person.middleName, value.person.lastName].filter(Boolean).join(' ');
    const shift = this.shifts().find((row) => row.id === value.job.shiftId);
    const docs = DOCUMENTS.filter((doc) => this.files()[doc.key]).map((doc) => doc.label);
    return [
      ['Candidate', offer ? `${offer.firstName} ${offer.lastName ?? ''} (offer ${offer.offerNumber})` : 'Direct hire'],
      ['Name', name],
      ['Date of birth', value.person.dateOfBirth ?? ''],
      ['Official email', value.contact.officialEmail ?? ''],
      ['Mobile', value.contact.mobilePhone ?? ''],
      ['Joining date', value.job.joiningDate ?? ''],
      ['Shift', shift ? this.shiftLabel(shift) : 'Default shift'],
      ['Holiday calendar', this.holidayCalendar()?.name ?? ''],
      ['Attendance mode', ATTENDANCE_MODES.filter((mode) => this.modes().includes(mode.value)).map((mode) => mode.label).join(', ')],
      ['Access card', value.job.accessCard ?? ''],
      ['Graduation', [value.background.graduationDegree, value.background.graduationInstitution].filter(Boolean).join(', ')],
      ['Post graduation', [value.background.postGraduationDegree, value.background.postGraduationInstitution].filter(Boolean).join(', ')],
      ['Experience before', value.background.experienceYears !== null ? `${value.background.experienceYears} years` : ''],
      ['Aadhaar', value.ids.aadhaar ? `•••• •••• ${String(value.ids.aadhaar).replace(/\s/g, '').slice(-4)}` : ''],
      ['PAN', value.ids.pan ? 'Given' : ''],
      ['Passport', value.ids.passportNumber ? `Given, expires ${value.ids.passportExpiry}` : ''],
      ['Documents', docs.join(', ')],
      ['Annual CTC', value.pay.annualCtc ? String(value.pay.annualCtc) : ''],
      ['Bank', [value.pay.bankName, value.pay.ifsc].filter(Boolean).join(', ')],
      ['Login', value.login.create ? `${value.login.userId} · ${this.roles().filter((role) => this.roleIds().includes(role.id)).map((role) => role.name).join(', ') || 'no roles'}` : 'Not now'],
    ];
  });

  protected async hire(): Promise<void> {
    for (const key of STEPS.map((item) => item.key)) {
      const problem = this.check(key);
      if (problem) {
        this.error.set(problem);
        this.step.set(STEPS.findIndex((item) => item.key === key));
        return;
      }
    }

    this.saving.set(true);
    this.error.set(null);
    const value = this.form.getRawValue();
    const present = toAddress(this.form.controls.present);
    const details: HireDetails = {
      genderCode: value.person.genderCode,
      dateOfBirth: value.person.dateOfBirth || null,
      preferredName: blank(value.person.preferredName),
      maritalStatusCode: value.person.maritalStatusCode,
      nationalityCode: value.person.nationalityCode,
      bloodGroupCode: value.person.bloodGroupCode,
      officialEmail: blank(value.contact.officialEmail),
      presentAddress: present,
      permanentAddress: this.sameAddress.value ? present : toAddress(this.form.controls.permanent),
      aadhaar: String(value.ids.aadhaar ?? '').replace(/\s/g, ''),
      pan: blank(value.ids.pan)?.toUpperCase() ?? null,
      passportNumber: blank(value.ids.passportNumber)?.toUpperCase() ?? null,
      passportExpiry: value.ids.passportExpiry || null,
      bankName: blank(value.pay.bankName),
      bankIfsc: blank(value.pay.ifsc)?.toUpperCase() ?? null,
      bankAccountHolder: blank(value.pay.accountHolder),
      bankAccountNumber: blank(value.pay.accountNumber),
      previousExperienceYears: value.background.experienceYears,
      probationEndDate: value.job.probationEndDate || null,
    };

    try {
      const offer = this.offer();
      const assignment = toAssignmentInput(this.form.controls.assignment);
      let id: string;
      let code: string;
      if (offer) {
        const result = await this.hiring.hire(offer.offerId, {
          managerEmployeeId: assignment.managerEmployeeId,
          locationId: assignment.locationId,
          employmentTypeCode: assignment.employmentTypeCode,
          workModeCode: assignment.workModeCode,
          details,
        });
        [id, code] = [result.employeeId, result.employeeCode];
      } else {
        const result = await this.directory.hire({
          employeeCode: blank(value.person.employeeCode),
          firstName: value.person.firstName!.trim(),
          middleName: blank(value.person.middleName),
          lastName: blank(value.person.lastName),
          personalEmail: blank(value.contact.personalEmail),
          mobilePhone: blank(value.contact.mobilePhone),
          joiningDate: value.job.joiningDate!,
          probationEndDate: value.job.probationEndDate || null,
          assignment,
          details,
        });
        [id, code] = [result.id, result.employeeCode];
      }

      const name = [value.person.firstName, value.person.lastName].filter(Boolean).join(' ');
      this.hired.set({ id, code, name });

      // The draft has done its job. Its files are already in memory for the steps below, so it can go now.
      const draft = this.draftId();
      if (draft) {
        this.draftId.set(null);
        this.draftNote.set(null);
        this.directory.deleteHireDraft(draft).catch(() => undefined);
      }
      this.followUps.set(this.plan(id, name, value));
      await this.runAll();
    } catch (error) {
      this.error.set(message(error, 'The employee could not be hired. Check the details and try again.'));
    } finally {
      this.saving.set(false);
    }
  }

  protected async retry(item: FollowUp): Promise<void> {
    await this.runOne(item);
  }

  /** Everything that follows the hire, in order; only what was filled in. */
  private plan(employeeId: string, name: string, value: HireFormValue): FollowUp[] {
    const items: FollowUp[] = [];
    const add = (label: string, run: () => Promise<void>) => items.push({ label, run, status: 'pending' });

    const photo = this.photo();
    if (photo) {
      add('Photo', () => this.directory.uploadPhoto(employeeId, photo, photo.name));
    }

    const background = value.background;
    if (background.graduationDegree?.trim()) {
      add('Graduation', () => this.directory.saveEducation({ degree: background.graduationDegree!.trim(), institution: background.graduationInstitution?.trim() || '—', endYear: background.graduationYear ?? undefined }, null, employeeId));
    }
    if (background.postGraduationDegree?.trim()) {
      add('Post graduation', () =>
        this.directory.saveEducation({ degree: background.postGraduationDegree!.trim(), institution: background.postGraduationInstitution?.trim() || '—', endYear: background.postGraduationYear ?? undefined }, null, employeeId),
      );
    }
    if (background.lastCompany?.trim() && background.lastFrom) {
      add('Previous employer', () =>
        this.directory.saveWorkExperience(
          { company: background.lastCompany!.trim(), title: background.lastTitle?.trim() || '—', fromDate: `${background.lastFrom}-01`, toDate: background.lastTo ? `${background.lastTo}-01` : undefined },
          null,
          employeeId,
        ),
      );
    }

    for (const doc of DOCUMENTS) {
      const file = this.files()[doc.key];
      if (file) {
        add(doc.label, () => this.uploadDocument(employeeId, name, doc.code, doc.label, file, doc.key === 'passport' ? value.ids.passportExpiry : null));
      }
    }

    if (value.job.shiftId) {
      add('Shift', async () => {
        await this.shiftsApi.assign({ employeeIds: [employeeId], shiftId: value.job.shiftId, effectiveFrom: value.job.joiningDate! });
      });
    }

    const modes = this.modes();
    add('Attendance mode', async () => {
      await this.attendance.setModes(employeeId, modes);
    });

    const card = blank(value.job.accessCard);
    if (card) {
      add('Access card', () => this.issueAccessCard(employeeId, card, value.assignment.locationId));
    }

    if (!this.offer() && value.pay.annualCtc && value.pay.structureId) {
      add('Salary', async () => {
        await this.payroll.assignSalary(employeeId, { structureId: value.pay.structureId, annualCtc: value.pay.annualCtc!, effectiveFrom: value.job.joiningDate!, reason: 'Hire' });
      });
    }

    const userId = blank(value.login.userId);
    if (value.login.create && userId) {
      const roleIds = this.roleIds();
      add('Login & roles', async () => {
        await this.admin.inviteUser({ email: userId, displayName: name, employeeId, roleIds });
        this.invitation.set(userId);
      });
    }

    return items;
  }

  private async runAll(): Promise<void> {
    for (const item of this.followUps()) {
      await this.runOne(item);
    }
  }

  private async runOne(item: FollowUp): Promise<void> {
    item.status = 'pending';
    item.error = undefined;
    this.followUps.update((items) => [...items]);
    try {
      await item.run();
      item.status = 'done';
    } catch (error) {
      item.status = 'failed';
      item.error = message(error, 'This could not be done.');
    }
    this.followUps.update((items) => [...items]);
  }

  private async uploadDocument(employeeId: string, name: string, typeCode: string, label: string, file: File, expiresOn: string | null): Promise<void> {
    const type = (await this.documentsApi.types()).find((row) => row.code === typeCode);
    if (!type) {
      throw new Error(`There is no "${label}" document type.`);
    }
    await this.documentsApi.upload({ ownerType: 'Employee', ownerId: employeeId, documentTypeId: type.id, title: `${label} - ${name}`, expiresOn, file });
  }

  private async issueAccessCard(employeeId: string, number: string, locationId: string | null): Promise<void> {
    const category = (await this.assets.categories()).find((row) => row.code === 'ACCESS_CARD' && row.isActive);
    if (!category) {
      throw new Error('There is no active "Access card" asset category.');
    }
    const asset = await this.assets.register(
      { categoryId: category.id, name: `Access card ${number}`, make: null, model: null, serialNumber: number, purchaseDate: null, purchaseCost: null, vendor: null, warrantyUntil: null, locationId, notes: null },
      'New',
    );
    await this.assets.assign(asset.asset.id, employeeId);
  }
}
