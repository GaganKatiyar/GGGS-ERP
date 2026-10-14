import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import type { OrgUnit, PolicyCategory, PolicyInput, PolicyItem } from '@gggs/shared/models';
import { POLICY_CATEGORY_LABELS } from '@gggs/shared/models';
import { OrgStructureApi } from '@gggs/shared/org-reference';

export type PolicyForm = FormGroup<{
  code: FormControl<string>;
  title: FormControl<string>;
  summary: FormControl<string | null>;
  category: FormControl<PolicyCategory>;
  requiresAcknowledgement: FormControl<boolean>;
  departmentIds: FormControl<readonly string[]>;
  locationIds: FormControl<readonly string[]>;
}>;

export function policyForm(policy?: PolicyItem): PolicyForm {
  return new FormGroup({
    code: new FormControl(policy?.code ?? '', { nonNullable: true, validators: Validators.required }),
    title: new FormControl(policy?.title ?? '', { nonNullable: true, validators: Validators.required }),
    summary: new FormControl<string | null>(policy?.summary ?? null),
    category: new FormControl<PolicyCategory>(policy?.category ?? 'General', { nonNullable: true }),
    requiresAcknowledgement: new FormControl(policy?.requiresAcknowledgement ?? true, { nonNullable: true }),
    departmentIds: new FormControl<readonly string[]>(policy?.audienceDepartmentIds ?? [], { nonNullable: true }),
    locationIds: new FormControl<readonly string[]>(policy?.audienceLocationIds ?? [], { nonNullable: true }),
  });
}

export function toPolicyInput(form: PolicyForm, content?: { content: string; effectiveFrom: string }): PolicyInput {
  const value = form.getRawValue();
  return {
    code: value.code.trim(),
    title: value.title.trim(),
    summary: value.summary?.trim() || null,
    category: value.category,
    requiresAcknowledgement: value.requiresAcknowledgement,
    audienceDepartmentIds: value.departmentIds,
    audienceLocationIds: value.locationIds,
    content: content?.content ?? null,
    effectiveFrom: content?.effectiveFrom ?? null,
  };
}

/**
 * A policy's details and audience. The audience is departments and locations - either one
 * matching is enough; leaving both empty means everyone.
 */
@Component({
  selector: 'gggs-policy-fields',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form-grid" [formGroup]="form()">
      <label class="field">
        <span class="field__label">Code *</span>
        <input type="text" formControlName="code" maxlength="32" />
      </label>
      <label class="field">
        <span class="field__label">Title *</span>
        <input type="text" formControlName="title" maxlength="200" />
      </label>
      <label class="field">
        <span class="field__label">Category</span>
        <select formControlName="category">
          @for (category of categoryKeys; track category) {
            <option [value]="category">{{ categories[category] }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span><input type="checkbox" formControlName="requiresAcknowledgement" /> People must acknowledge it</span>
      </label>
      <label class="field form-grid__full">
        <span class="field__label">Summary</span>
        <input type="text" formControlName="summary" maxlength="1000" />
      </label>

      <fieldset class="audience form-grid__full">
        <legend>Applies to <span class="muted">(nothing ticked: everyone)</span></legend>
        <div class="audience__columns">
          <div>
            <strong class="audience__title">Departments</strong>
            @for (department of departments(); track department.id) {
              <label class="audience__option">
                <input type="checkbox" [checked]="isChosen('departmentIds', department.id)" (change)="toggle('departmentIds', department.id)" />
                {{ department.name }}
              </label>
            }
          </div>
          <div>
            <strong class="audience__title">Locations</strong>
            @for (location of locations(); track location.id) {
              <label class="audience__option">
                <input type="checkbox" [checked]="isChosen('locationIds', location.id)" (change)="toggle('locationIds', location.id)" />
                {{ location.name }}
              </label>
            }
          </div>
        </div>
      </fieldset>
    </div>
  `,
  styles: `
    .audience {
      margin: 0;
      padding: var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
    }

    .audience__columns {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      gap: var(--gggs-space-3);
      max-height: 14rem;
      overflow-y: auto;
    }

    .audience__title {
      display: block;
      margin-block-end: var(--gggs-space-1, 0.25rem);
    }

    .audience__option {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
    }
  `,
})
export class PolicyFields {
  private readonly org = inject(OrgStructureApi);

  readonly form = input.required<PolicyForm>();

  protected readonly categories = POLICY_CATEGORY_LABELS;
  protected readonly categoryKeys = Object.keys(POLICY_CATEGORY_LABELS) as PolicyCategory[];
  protected readonly departments = signal<readonly OrgUnit[]>([]);
  protected readonly locations = signal<readonly OrgUnit[]>([]);

  constructor() {
    void this.org.active('departments').then((rows) => this.departments.set(rows));
    void this.org.active('locations').then((rows) => this.locations.set(rows));
  }

  protected isChosen(control: 'departmentIds' | 'locationIds', id: string): boolean {
    return this.form().controls[control].value.includes(id);
  }

  protected toggle(control: 'departmentIds' | 'locationIds', id: string): void {
    const current = this.form().controls[control].value;
    this.form().controls[control].setValue(current.includes(id) ? current.filter((value) => value !== id) : [...current, id]);
  }
}
