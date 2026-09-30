import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { EmployeePicker } from '@gggs/shared/employee-reference';
import { PageHeader } from '@gggs/shared/ui';
import type { EmployeeListItem, OnboardingTemplateListItem } from '@gggs/shared/models';
import { OnboardingApi } from '../data/onboarding-api';

/** Starting a new onboarding case: pick the employee, the template, and a start date. */
@Component({
  selector: 'gggs-start-onboarding-case-page',
  imports: [ReactiveFormsModule, RouterLink, EmployeePicker, PageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Start onboarding" subtitle="Copies every task on the chosen template.">
      <a routerLink="/onboarding" class="btn">Cancel</a>
    </gggs-page-header>

    <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="panel">
      <div class="field">
        <span class="field__label" id="employee-label">Employee</span>
        <gggs-employee-picker aria-labelledby="employee-label" (employeeSelected)="onEmployeeSelected($event)" />
        @if (selectedEmployeeName()) {
          <span class="selected-name">{{ selectedEmployeeName() }}</span>
        }
      </div>

      <label class="field">
        <span class="field__label">Template</span>
        <select formControlName="templateId" required>
          <option value="" disabled>Choose one</option>
          @for (template of templates(); track template.id) {
            <option [value]="template.id">{{ template.name }}</option>
          }
        </select>
      </label>

      <label class="field">
        <span class="field__label">Start date</span>
        <input type="date" formControlName="startDate" required />
      </label>

      @if (errorMessage()) {
        <p class="field__error" role="alert">{{ errorMessage() }}</p>
      }

      <button type="submit" class="btn btn--primary" [disabled]="isSaving()">
        {{ isSaving() ? 'Starting…' : 'Start onboarding' }}
      </button>
    </form>
  `,
  styles: `
    .panel {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1rem;
      max-width: 32rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised, #ffffff);
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

    .selected-name {
      font-size: 0.8125rem;
      color: var(--gggs-color-text-muted, #5b6472);
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
export class StartOnboardingCasePage {
  private readonly api = inject(OnboardingApi);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);

  protected readonly templates = signal<readonly OnboardingTemplateListItem[]>([]);
  protected readonly selectedEmployeeName = signal<string | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    employeeId: ['', Validators.required],
    templateId: ['', Validators.required],
    startDate: ['', Validators.required],
  });

  constructor() {
    void this.loadTemplates();
  }

  protected onEmployeeSelected(employee: EmployeeListItem): void {
    this.form.controls.employeeId.setValue(employee.id);
    this.selectedEmployeeName.set(employee.displayName);
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
      const onboardingCase = await this.api.startCase({
        employeeId: value.employeeId ?? '',
        templateId: value.templateId ?? '',
        startDate: value.startDate ?? '',
      });

      await this.router.navigate(['/onboarding/cases', onboardingCase.id]);
    } catch {
      this.errorMessage.set('The case could not be started. Check the template has tasks and try again.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async loadTemplates(): Promise<void> {
    this.templates.set(await this.api.listTemplates());
  }
}
