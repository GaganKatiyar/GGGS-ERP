import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { ClientsApi } from '@gggs/shared/delivery';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { LookupSelect } from '@gggs/shared/master-data';
import type { ClientDetail, ClientItem, ClientStatus } from '@gggs/shared/models';
import { LOOKUP_LISTS } from '@gggs/shared/models';
import { FormDialog } from '@gggs/shared/ui';

/** Creating or editing a client, with the primary contact on the way in. */
@Component({
  selector: 'gggs-client-form',
  imports: [ReactiveFormsModule, FormDialog, LookupSelect, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog [(visible)]="visible" [title]="client() ? 'Edit ' + client()?.name : 'Add a client'" size="lg" [busy]="busy()" [error]="error()" (save)="save()">
      <div class="form-grid" [formGroup]="form">
        <label class="field">
          <span class="field__label">Code *</span>
          <input type="text" formControlName="code" maxlength="32" />
        </label>
        <label class="field">
          <span class="field__label">Name *</span>
          <input type="text" formControlName="name" maxlength="200" />
        </label>
        <label class="field">
          <span class="field__label">Legal name</span>
          <input type="text" formControlName="legalName" maxlength="200" />
        </label>
        <label class="field">
          <span class="field__label">Status</span>
          <select formControlName="status">
            <option value="Prospect">Prospect</option>
            <option value="Active">Active</option>
            <option value="Inactive">Inactive</option>
          </select>
        </label>
        <label class="field">
          <span class="field__label">Industry</span>
          <gggs-lookup-select [list]="lists.industry" placeholder="Not set" ariaLabel="Industry" formControlName="industryCode" />
        </label>
        <label class="field">
          <span class="field__label">Account manager</span>
          <gggs-employee-select formControlName="accountManagerEmployeeId" ariaLabel="Account manager" />
        </label>
        <label class="field">
          <span class="field__label">Country</span>
          <gggs-lookup-select [list]="lists.country" placeholder="Not set" ariaLabel="Country" formControlName="countryCode" />
        </label>
        <label class="field">
          <span class="field__label">State</span>
          <gggs-lookup-select [list]="lists.state" placeholder="Not set" ariaLabel="State" formControlName="stateCode" />
        </label>
        <label class="field">
          <span class="field__label">City</span>
          <input type="text" formControlName="city" maxlength="100" />
        </label>
        <label class="field">
          <span class="field__label">Postal code</span>
          <input type="text" formControlName="postalCode" maxlength="20" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Address</span>
          <input type="text" formControlName="address" maxlength="500" />
        </label>
        <label class="field">
          <span class="field__label">Website</span>
          <input type="url" formControlName="website" maxlength="200" />
        </label>
        <label class="field">
          <span class="field__label">Email</span>
          <input type="email" formControlName="email" maxlength="256" />
        </label>
        <label class="field">
          <span class="field__label">Phone</span>
          <input type="tel" formControlName="phone" maxlength="32" />
        </label>
        <label class="field">
          <span class="field__label">Tax ID (GSTIN / VAT)</span>
          <input type="text" formControlName="taxId" maxlength="32" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Notes</span>
          <textarea formControlName="notes" rows="2" maxlength="2000"></textarea>
        </label>
        @if (!client()) {
          <fieldset class="client-form__contact form-grid__full">
            <legend>Primary contact</legend>
            <div class="form-grid">
              <label class="field">
                <span class="field__label">Name</span>
                <input type="text" formControlName="contactName" maxlength="200" />
              </label>
              <label class="field">
                <span class="field__label">Designation</span>
                <input type="text" formControlName="contactDesignation" maxlength="100" />
              </label>
              <label class="field">
                <span class="field__label">Email</span>
                <input type="email" formControlName="contactEmail" maxlength="256" />
              </label>
              <label class="field">
                <span class="field__label">Phone</span>
                <input type="tel" formControlName="contactPhone" maxlength="32" />
              </label>
            </div>
          </fieldset>
        }
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .client-form__contact {
      margin: 0;
      padding: var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
    }
  `,
})
export class ClientForm {
  private readonly api = inject(ClientsApi);

  readonly visible = model(false);
  readonly client = input<ClientItem | null>(null);
  readonly saved = output<ClientDetail>();

  protected readonly lists = LOOKUP_LISTS;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: Validators.required }),
    name: new FormControl('', { nonNullable: true, validators: Validators.required }),
    legalName: new FormControl<string | null>(null),
    status: new FormControl<ClientStatus>('Active', { nonNullable: true }),
    industryCode: new FormControl<string | null>(null),
    accountManagerEmployeeId: new FormControl<string | null>(null),
    countryCode: new FormControl<string | null>(null),
    stateCode: new FormControl<string | null>(null),
    city: new FormControl<string | null>(null),
    postalCode: new FormControl<string | null>(null),
    address: new FormControl<string | null>(null),
    website: new FormControl<string | null>(null),
    email: new FormControl<string | null>(null),
    phone: new FormControl<string | null>(null),
    taxId: new FormControl<string | null>(null),
    notes: new FormControl<string | null>(null),
    contactName: new FormControl<string | null>(null),
    contactDesignation: new FormControl<string | null>(null),
    contactEmail: new FormControl<string | null>(null),
    contactPhone: new FormControl<string | null>(null),
  });

  constructor() {
    effect(() => {
      if (!this.visible()) {
        return;
      }

      const client = this.client();
      this.error.set(null);
      this.form.reset({
        code: client?.code ?? '',
        name: client?.name ?? '',
        legalName: client?.legalName ?? null,
        status: client?.status ?? 'Active',
        industryCode: client?.industryCode ?? null,
        accountManagerEmployeeId: client?.accountManagerEmployeeId ?? null,
        countryCode: client?.countryCode ?? null,
        stateCode: client?.stateCode ?? null,
        city: client?.city ?? null,
        postalCode: client?.postalCode ?? null,
        address: client?.address ?? null,
        website: client?.website ?? null,
        email: client?.email ?? null,
        phone: client?.phone ?? null,
        taxId: client?.taxId ?? null,
        notes: client?.notes ?? null,
      });
      if (client) {
        this.form.controls.code.disable();
      } else {
        this.form.controls.code.enable();
      }
    });
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.error.set('Give the client a code and a name.');
      return;
    }

    const value = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    try {
      const saved = await this.api.save(this.client()?.id ?? null, {
        code: value.code.trim(),
        name: value.name.trim(),
        legalName: value.legalName,
        status: value.status,
        industryCode: value.industryCode,
        accountManagerEmployeeId: value.accountManagerEmployeeId,
        countryCode: value.countryCode,
        stateCode: value.stateCode,
        city: value.city,
        postalCode: value.postalCode,
        address: value.address,
        website: value.website,
        email: value.email,
        phone: value.phone,
        taxId: value.taxId,
        notes: value.notes,
        primaryContact: !this.client() && value.contactName?.trim()
          ? { name: value.contactName.trim(), designation: value.contactDesignation, email: value.contactEmail, phone: value.contactPhone }
          : null,
      });
      this.visible.set(false);
      this.saved.emit(saved);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The client could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }
}
