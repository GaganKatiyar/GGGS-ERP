import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import { LookupSelect } from '@gggs/shared/master-data';
import type { Address, EmployeeProfile } from '@gggs/shared/models';
import { LOOKUP_LISTS } from '@gggs/shared/models';
import { FormDialog } from '@gggs/shared/ui';

const blank = (text: string | null | undefined) => (text?.trim() ? text.trim() : null);

function addressForm(address?: Address) {
  return new FormGroup({
    line1: new FormControl(address?.line1 ?? null),
    line2: new FormControl(address?.line2 ?? null),
    city: new FormControl(address?.city ?? null),
    countryCode: new FormControl(address?.countryCode ?? null),
    stateCode: new FormControl(address?.stateCode ?? null),
    postalCode: new FormControl(address?.postalCode ?? null),
  });
}

type AddressForm = ReturnType<typeof addressForm>;

function addressInput(form: AddressForm) {
  const value = form.getRawValue();
  const result = {
    line1: blank(value.line1),
    line2: blank(value.line2),
    city: blank(value.city),
    stateCode: value.stateCode || null,
    countryCode: value.countryCode || null,
    postalCode: blank(value.postalCode),
  };
  return Object.values(result).some((part) => part) ? result : null;
}

/** Personal and contact details, each edited in its own dialog. */
@Component({
  selector: 'gggs-personal-tab',
  imports: [DatePipe, ReactiveFormsModule, HasPermissionDirective, FormDialog, LookupSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let p = profile();
    <div class="stack">
      <section class="panel stack">
        <div class="toolbar">
          <h2 class="personal__heading">Personal</h2>
          <button type="button" class="btn btn--sm" *gggsHasPermission="'employee.profile.edit'" (click)="openPersonal()">Edit</button>
        </div>
        <dl class="definition-list">
          <dt>Full name</dt><dd>{{ fullName(p) }}</dd>
          <dt>Preferred name</dt><dd>{{ p.preferredName ?? '—' }}</dd>
          <dt>Gender</dt><dd>{{ p.gender?.label ?? '—' }}</dd>
          <dt>Date of birth</dt><dd>{{ (p.dateOfBirth | date: 'mediumDate') ?? '—' }}</dd>
          <dt>Marital status</dt><dd>{{ p.maritalStatus?.label ?? '—' }}</dd>
          <dt>Nationality</dt><dd>{{ p.nationality?.label ?? '—' }}</dd>
          <dt>Blood group</dt><dd>{{ p.bloodGroup?.label ?? '—' }}</dd>
        </dl>
      </section>

      <section class="panel stack">
        <div class="toolbar">
          <h2 class="personal__heading">Contact</h2>
          <button type="button" class="btn btn--sm" *gggsHasPermission="'employee.profile.edit'" (click)="openContact()">Edit</button>
        </div>
        <dl class="definition-list">
          <dt>Official email</dt><dd>{{ p.officialEmail ?? '—' }}</dd>
          <dt>Personal email</dt><dd>{{ p.personalEmail ?? '—' }}</dd>
          <dt>Mobile</dt><dd>{{ p.mobilePhone ?? '—' }}</dd>
          <dt>Alternate phone</dt><dd>{{ p.alternatePhone ?? '—' }}</dd>
          <dt>Current address</dt><dd>{{ formatAddress(p.currentAddress) }}</dd>
          <dt>Permanent address</dt><dd>{{ formatAddress(p.permanentAddress) }}</dd>
        </dl>
      </section>
    </div>

    <gggs-form-dialog [(visible)]="personalOpen" title="Edit personal details" [busy]="busy()" [error]="error()" (save)="savePersonal()">
      <div class="form-grid" [formGroup]="personal">
        <label class="field">
          <span class="field__label">First name *</span>
          <input type="text" formControlName="firstName" maxlength="100" />
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
          <span class="field__label">Marital status</span>
          <gggs-lookup-select [list]="lists.maritalStatus" formControlName="maritalStatusCode" ariaLabel="Marital status" />
        </label>
        <label class="field">
          <span class="field__label">Nationality</span>
          <gggs-lookup-select [list]="lists.country" formControlName="nationalityCode" ariaLabel="Nationality" />
        </label>
        <label class="field">
          <span class="field__label">Blood group</span>
          <gggs-lookup-select [list]="lists.bloodGroup" formControlName="bloodGroupCode" ariaLabel="Blood group" />
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="contactOpen" title="Edit contact details" width="min(48rem, 96vw)" [busy]="busy()" [error]="error()" (save)="saveContact()">
      <div class="stack" [formGroup]="contact">
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
          <label class="field">
            <span class="field__label">Alternate phone</span>
            <input type="tel" formControlName="alternatePhone" maxlength="32" />
          </label>
        </div>
        @for (part of addressParts; track part.key) {
          <fieldset class="personal__address" [formGroup]="contact.controls[part.key]">
            <legend>{{ part.label }}</legend>
            <div class="form-grid">
              <label class="field form-grid__full">
                <span class="field__label">Line 1</span>
                <input type="text" formControlName="line1" maxlength="200" />
              </label>
              <label class="field form-grid__full">
                <span class="field__label">Line 2</span>
                <input type="text" formControlName="line2" maxlength="200" />
              </label>
              <label class="field">
                <span class="field__label">City</span>
                <input type="text" formControlName="city" maxlength="100" />
              </label>
              <label class="field">
                <span class="field__label">Country</span>
                <gggs-lookup-select [list]="lists.country" formControlName="countryCode" ariaLabel="Country" />
              </label>
              <label class="field">
                <span class="field__label">State</span>
                <gggs-lookup-select [list]="lists.state" [parent]="contact.controls[part.key].controls.countryCode.value" formControlName="stateCode" ariaLabel="State" />
              </label>
              <label class="field">
                <span class="field__label">Postal code</span>
                <input type="text" formControlName="postalCode" maxlength="16" />
              </label>
            </div>
          </fieldset>
        }
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .personal__heading {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .personal__address {
      margin: 0;
      padding: var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
    }
  `,
})
export class PersonalTab {
  private readonly api = inject(EmployeeDirectoryApi);

  readonly profile = input.required<EmployeeProfile>();
  readonly changed = output<void>();

  protected readonly lists = LOOKUP_LISTS;
  protected readonly addressParts = [
    { key: 'currentAddress', label: 'Current address' },
    { key: 'permanentAddress', label: 'Permanent address' },
  ] as const;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly personalOpen = signal(false);
  protected readonly contactOpen = signal(false);

  protected readonly personal = new FormGroup({
    firstName: new FormControl<string | null>(null, Validators.required),
    middleName: new FormControl<string | null>(null),
    lastName: new FormControl<string | null>(null),
    preferredName: new FormControl<string | null>(null),
    genderCode: new FormControl<string | null>(null),
    dateOfBirth: new FormControl<string | null>(null),
    maritalStatusCode: new FormControl<string | null>(null),
    nationalityCode: new FormControl<string | null>(null),
    bloodGroupCode: new FormControl<string | null>(null),
  });

  protected readonly contact = new FormGroup({
    officialEmail: new FormControl<string | null>(null, Validators.email),
    personalEmail: new FormControl<string | null>(null, Validators.email),
    mobilePhone: new FormControl<string | null>(null),
    alternatePhone: new FormControl<string | null>(null),
    currentAddress: addressForm(),
    permanentAddress: addressForm(),
  });

  protected fullName(p: EmployeeProfile): string {
    return [p.firstName, p.middleName, p.lastName].filter((part) => !!part).join(' ');
  }

  protected formatAddress(address?: Address): string {
    if (!address) {
      return '—';
    }
    return [address.line1, address.line2, address.city, address.stateCode, address.postalCode, address.countryCode].filter(Boolean).join(', ') || '—';
  }

  protected openPersonal(): void {
    const p = this.profile();
    this.personal.reset({
      firstName: p.firstName,
      middleName: p.middleName ?? null,
      lastName: p.lastName ?? null,
      preferredName: p.preferredName ?? null,
      genderCode: p.gender?.code ?? null,
      dateOfBirth: p.dateOfBirth ?? null,
      maritalStatusCode: p.maritalStatus?.code ?? null,
      nationalityCode: p.nationality?.code ?? null,
      bloodGroupCode: p.bloodGroup?.code ?? null,
    });
    this.error.set(null);
    this.personalOpen.set(true);
  }

  protected openContact(): void {
    const p = this.profile();
    this.contact.setControl('currentAddress', addressForm(p.currentAddress));
    this.contact.setControl('permanentAddress', addressForm(p.permanentAddress));
    this.contact.patchValue({
      officialEmail: p.officialEmail ?? null,
      personalEmail: p.personalEmail ?? null,
      mobilePhone: p.mobilePhone ?? null,
      alternatePhone: p.alternatePhone ?? null,
    });
    this.error.set(null);
    this.contactOpen.set(true);
  }

  protected async savePersonal(): Promise<void> {
    if (this.personal.invalid) {
      this.error.set('The first name is required.');
      return;
    }

    const value = this.personal.getRawValue();
    await this.run(
      () =>
        this.api.updatePersonal(this.profile().id, {
          firstName: value.firstName!.trim(),
          middleName: blank(value.middleName),
          lastName: blank(value.lastName),
          preferredName: blank(value.preferredName),
          genderCode: value.genderCode || null,
          dateOfBirth: value.dateOfBirth || null,
          maritalStatusCode: value.maritalStatusCode || null,
          nationalityCode: value.nationalityCode || null,
          bloodGroupCode: value.bloodGroupCode || null,
        }),
      () => this.personalOpen.set(false),
    );
  }

  protected async saveContact(): Promise<void> {
    if (this.contact.invalid) {
      this.error.set('Check the email addresses.');
      return;
    }

    const value = this.contact.getRawValue();
    await this.run(
      () =>
        this.api.updateContact(this.profile().id, {
          officialEmail: blank(value.officialEmail),
          personalEmail: blank(value.personalEmail),
          mobilePhone: blank(value.mobilePhone),
          alternatePhone: blank(value.alternatePhone),
          currentAddress: addressInput(this.contact.controls.currentAddress),
          permanentAddress: addressInput(this.contact.controls.permanentAddress),
        }),
      () => this.contactOpen.set(false),
    );
  }

  private async run(call: () => Promise<void>, close: () => void): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await call();
      close();
      this.changed.emit();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The details could not be saved. Try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
