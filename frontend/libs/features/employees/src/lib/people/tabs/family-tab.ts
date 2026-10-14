import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import { LookupSelect, MasterDataApi } from '@gggs/shared/master-data';
import type { Dependent, EmergencyContactEntry } from '@gggs/shared/models';
import { LOOKUP_LISTS } from '@gggs/shared/models';
import { EmptyState, FormDialog } from '@gggs/shared/ui';

const blank = (text: string | null | undefined) => (text?.trim() ? text.trim() : null);

/** Dependents and emergency contacts: small lists, added and removed in place. */
@Component({
  selector: 'gggs-family-tab',
  imports: [DatePipe, ReactiveFormsModule, HasPermissionDirective, FormDialog, LookupSelect, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      <section class="panel stack">
        <div class="toolbar">
          <h2 class="family__heading">Emergency contacts</h2>
          <button type="button" class="btn btn--sm" *gggsHasPermission="'employee.profile.edit'" (click)="openContact()">Add contact</button>
        </div>
        @for (contact of contacts(); track contact.id) {
          <div class="family__row">
            <span class="family__main">
              <strong>{{ contact.fullName }}</strong>
              @if (contact.isPrimary) {
                <span class="family__primary">Primary</span>
              }
              <span class="muted">{{ relationship(contact.relationshipCode) }} · {{ contact.phone }}{{ contact.email ? ' · ' + contact.email : '' }}</span>
            </span>
            <button type="button" class="btn btn--link btn--sm" *gggsHasPermission="'employee.profile.edit'" [attr.aria-label]="'Remove ' + contact.fullName" (click)="removeContact(contact)">Remove</button>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-phone" title="No emergency contacts" description="Add at least one person to call in an emergency." />
        }
      </section>

      <section class="panel stack">
        <div class="toolbar">
          <h2 class="family__heading">Dependents</h2>
          <button type="button" class="btn btn--sm" *gggsHasPermission="'employee.profile.edit'" (click)="openDependent()">Add dependent</button>
        </div>
        @for (dependent of dependents(); track dependent.id) {
          <div class="family__row">
            <span class="family__main">
              <strong>{{ dependent.fullName }}</strong>
              <span class="muted">{{ relationship(dependent.relationshipCode) }}{{ dependent.dateOfBirth ? ' · born ' + (dependent.dateOfBirth | date: 'mediumDate') : '' }}</span>
            </span>
            <button type="button" class="btn btn--link btn--sm" *gggsHasPermission="'employee.profile.edit'" [attr.aria-label]="'Remove ' + dependent.fullName" (click)="removeDependent(dependent)">Remove</button>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-users" title="No dependents" description="Dependents are used later for benefits and insurance." />
        }
      </section>
      @if (listError()) {
        <p class="field__error" role="alert">{{ listError() }}</p>
      }
    </div>

    <gggs-form-dialog [(visible)]="contactOpen" title="Add emergency contact" [busy]="busy()" [error]="error()" (save)="saveContact()">
      <div class="form-grid" [formGroup]="contactForm">
        <label class="field">
          <span class="field__label">Full name *</span>
          <input type="text" formControlName="fullName" maxlength="200" />
        </label>
        <label class="field">
          <span class="field__label">Relationship *</span>
          <gggs-lookup-select [list]="lists.relationship" formControlName="relationshipCode" ariaLabel="Relationship" />
        </label>
        <label class="field">
          <span class="field__label">Phone *</span>
          <input type="tel" formControlName="phone" maxlength="32" />
        </label>
        <label class="field">
          <span class="field__label">Email</span>
          <input type="email" formControlName="email" maxlength="256" />
        </label>
        <label class="field form-grid__full">
          <span><input type="checkbox" formControlName="isPrimary" /> Primary contact</span>
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="dependentOpen" title="Add dependent" [busy]="busy()" [error]="error()" (save)="saveDependent()">
      <div class="form-grid" [formGroup]="dependentForm">
        <label class="field">
          <span class="field__label">Full name *</span>
          <input type="text" formControlName="fullName" maxlength="200" />
        </label>
        <label class="field">
          <span class="field__label">Relationship *</span>
          <gggs-lookup-select [list]="lists.relationship" formControlName="relationshipCode" ariaLabel="Relationship" />
        </label>
        <label class="field">
          <span class="field__label">Date of birth</span>
          <input type="date" formControlName="dateOfBirth" />
        </label>
        <label class="field">
          <span class="field__label">Gender</span>
          <gggs-lookup-select [list]="lists.gender" formControlName="genderCode" ariaLabel="Gender" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .family__heading {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .family__row {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .family__row:last-of-type {
      border-block-end: 0;
    }

    .family__main {
      display: flex;
      flex: 1;
      flex-wrap: wrap;
      align-items: baseline;
      gap: var(--gggs-space-2);
    }

    .family__primary {
      padding: 0 var(--gggs-space-2);
      border-radius: 999px;
      background: var(--gggs-color-primary-subtle);
      color: var(--gggs-color-primary);
      font-size: var(--gggs-font-size-xs);
    }
  `,
})
export class FamilyTab {
  private readonly api = inject(EmployeeDirectoryApi);
  private readonly masterData = inject(MasterDataApi);

  readonly employeeId = input.required<string>();

  protected readonly lists = LOOKUP_LISTS;
  protected readonly contacts = signal<readonly EmergencyContactEntry[]>([]);
  protected readonly dependents = signal<readonly Dependent[]>([]);
  private readonly relationships = signal<ReadonlyMap<string, string>>(new Map());

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly listError = signal<string | null>(null);
  protected readonly contactOpen = signal(false);
  protected readonly dependentOpen = signal(false);

  protected readonly contactForm = new FormGroup({
    fullName: new FormControl<string | null>(null, Validators.required),
    relationshipCode: new FormControl<string | null>(null, Validators.required),
    phone: new FormControl<string | null>(null, Validators.required),
    email: new FormControl<string | null>(null, Validators.email),
    isPrimary: new FormControl(false, { nonNullable: true }),
  });

  protected readonly dependentForm = new FormGroup({
    fullName: new FormControl<string | null>(null, Validators.required),
    relationshipCode: new FormControl<string | null>(null, Validators.required),
    dateOfBirth: new FormControl<string | null>(null),
    genderCode: new FormControl<string | null>(null),
  });

  constructor() {
    void this.masterData.values(LOOKUP_LISTS.relationship).then((values) => this.relationships.set(new Map(values.map((value) => [value.code, value.label]))));
    effect(() => void this.load(this.employeeId()));
  }

  protected relationship(code: string): string {
    return this.relationships().get(code) ?? code;
  }

  protected openContact(): void {
    this.contactForm.reset({ fullName: null, relationshipCode: null, phone: null, email: null, isPrimary: this.contacts().length === 0 });
    this.error.set(null);
    this.contactOpen.set(true);
  }

  protected openDependent(): void {
    this.dependentForm.reset();
    this.error.set(null);
    this.dependentOpen.set(true);
  }

  protected async saveContact(): Promise<void> {
    if (this.contactForm.invalid) {
      this.error.set('Fill in the name, relationship and phone.');
      return;
    }
    const value = this.contactForm.getRawValue();
    await this.run(
      () =>
        this.api.addEmergencyContact(this.employeeId(), {
          fullName: value.fullName!.trim(),
          relationshipCode: value.relationshipCode,
          phone: value.phone!.trim(),
          email: blank(value.email),
          isPrimary: value.isPrimary,
        }),
      () => this.contactOpen.set(false),
    );
  }

  protected async saveDependent(): Promise<void> {
    if (this.dependentForm.invalid) {
      this.error.set('Fill in the name and relationship.');
      return;
    }
    const value = this.dependentForm.getRawValue();
    await this.run(
      () =>
        this.api.addDependent(this.employeeId(), {
          fullName: value.fullName!.trim(),
          relationshipCode: value.relationshipCode,
          dateOfBirth: value.dateOfBirth || null,
          genderCode: value.genderCode || null,
        }),
      () => this.dependentOpen.set(false),
    );
  }

  protected async removeContact(contact: EmergencyContactEntry): Promise<void> {
    await this.remove(() => this.api.removeEmergencyContact(this.employeeId(), contact.id));
  }

  protected async removeDependent(dependent: Dependent): Promise<void> {
    await this.remove(() => this.api.removeDependent(this.employeeId(), dependent.id));
  }

  private async remove(call: () => Promise<void>): Promise<void> {
    this.listError.set(null);
    try {
      await call();
      await this.load(this.employeeId());
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'It could not be removed. Try again.');
    }
  }

  private async run(call: () => Promise<void>, close: () => void): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await call();
      close();
      await this.load(this.employeeId());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'It could not be saved. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    try {
      const [contacts, dependents] = await Promise.all([this.api.emergencyContacts(id), this.api.dependents(id)]);
      this.contacts.set(contacts);
      this.dependents.set(dependents);
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'Family details could not be loaded.');
    }
  }
}
