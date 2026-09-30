import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { OnboardingAssigneeRole, OnboardingTemplateTask } from '@gggs/shared/models';
import { OnboardingApi } from '../data/onboarding-api';

/** One template's task list, and a form to add another task to it. */
@Component({
  selector: 'gggs-onboarding-template-detail-page',
  imports: [ReactiveFormsModule, RouterLink, HasPermissionDirective, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Template tasks">
      <a routerLink="/onboarding/templates" class="btn">Back</a>
    </gggs-page-header>

    <gggs-card title="Tasks">
      @if (tasks(); as items) {
        @if (items.length > 0) {
          <table class="tasks-table">
            <thead>
              <tr>
                <th scope="col">Title</th>
                <th scope="col">Assignee</th>
                <th scope="col">Due offset</th>
                <th scope="col">Mandatory</th>
              </tr>
            </thead>
            <tbody>
              @for (task of items; track task.id) {
                <tr>
                  <td>{{ task.title }}</td>
                  <td>{{ task.assigneeRole }}</td>
                  <td>{{ task.dueOffsetDays }} day(s)</td>
                  <td>{{ task.isMandatory ? 'Yes' : 'No' }}</td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No tasks yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }

      <form
        *gggsHasPermission="'onboarding.template.manage'"
        [formGroup]="form"
        (ngSubmit)="submit()"
        novalidate
        class="create-form"
      >
        <label class="field">
          <span class="field__label">Title</span>
          <input type="text" formControlName="title" maxlength="200" required />
        </label>

        <label class="field">
          <span class="field__label">Assignee</span>
          <select formControlName="assigneeRole" required>
            <option value="NewHire">New hire</option>
            <option value="Manager">Manager</option>
            <option value="Hr">HR</option>
            <option value="It">IT</option>
            <option value="Admin">Admin</option>
          </select>
        </label>

        <label class="field">
          <span class="field__label">Due offset (days)</span>
          <input type="number" formControlName="dueOffsetDays" required />
        </label>

        <label class="checkbox-field">
          <input type="checkbox" formControlName="isMandatory" />
          <span>Mandatory</span>
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Adding…' : 'Add task' }}
        </gggs-button>
      </form>
    </gggs-card>
  `,
  styles: `
    .tasks-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .tasks-table th,
    .tasks-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 1rem;
    }

    .create-form {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      align-items: end;
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

    .field__error {
      grid-column: 1 / -1;
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .checkbox-field {
      display: flex;
      align-items: center;
      gap: 0.375rem;
      font-size: 0.875rem;
    }

    .btn {
      padding: 0.5rem 1rem;
      font-weight: 600;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #ffffff);
      text-decoration: none;
      color: inherit;
      cursor: pointer;
    }
  `,
})
export class OnboardingTemplateDetailPage {
  private readonly api = inject(OnboardingApi);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly tasks = signal<readonly OnboardingTemplateTask[] | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    assigneeRole: ['NewHire' as OnboardingAssigneeRole, Validators.required],
    dueOffsetDays: [0, Validators.required],
    isMandatory: [true],
  });

  private readonly templateId = this.route.snapshot.paramMap.get('id') ?? '';

  constructor() {
    void this.load();
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
      await this.api.addTemplateTask(this.templateId, {
        title: value.title ?? '',
        assigneeRole: value.assigneeRole ?? 'NewHire',
        dueOffsetDays: value.dueOffsetDays ?? 0,
        isMandatory: value.isMandatory ?? false,
      });

      this.form.reset({ assigneeRole: 'NewHire', dueOffsetDays: 0, isMandatory: true });
      await this.load();
    } catch {
      this.errorMessage.set('The task could not be added. Please try again.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async load(): Promise<void> {
    this.tasks.set(await this.api.listTemplateTasks(this.templateId));
  }
}
