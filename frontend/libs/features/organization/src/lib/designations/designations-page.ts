import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmptyState, PageHeader } from '@gggs/shared/ui';
import type { DesignationListItem } from '@gggs/shared/models';
import { OrgReferenceApi } from '@gggs/shared/org-reference';

/** Designations: job titles, independent of where in the org chart someone sits. */
@Component({
  selector: 'gggs-designations-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Designations" subtitle="Job titles employees are hired into.">
      <button
        type="button"
        class="btn btn--primary"
        *gggsHasPermission="'org.structure.manage'"
        (click)="isFormOpen.set(!isFormOpen())"
      >
        {{ isFormOpen() ? 'Cancel' : 'New designation' }}
      </button>
    </gggs-page-header>

    @if (isFormOpen()) {
      <form class="panel" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="panel__grid">
          <label class="field">
            <span class="field__label">Code</span>
            <input type="text" formControlName="code" maxlength="32" required />
          </label>

          <label class="field">
            <span class="field__label">Title</span>
            <input type="text" formControlName="title" maxlength="150" required />
          </label>

          <label class="field">
            <span class="field__label">Job family</span>
            <input type="text" formControlName="jobFamily" maxlength="100" />
          </label>

          <label class="field">
            <span class="field__label">Job level</span>
            <input type="number" formControlName="jobLevel" min="1" />
          </label>
        </div>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <button type="submit" class="btn btn--primary" [disabled]="isSaving()">
          {{ isSaving() ? 'Saving…' : 'Create' }}
        </button>
      </form>
    }

    @if (isLoading()) {
      <p>Loading…</p>
    } @else if (designations().length === 0) {
      <gggs-empty-state title="No designations yet" description="Create the first job title." />
    } @else {
      <table class="list-table">
        <thead>
          <tr>
            <th scope="col">Code</th>
            <th scope="col">Title</th>
            <th scope="col">Job family</th>
            <th scope="col">Level</th>
          </tr>
        </thead>
        <tbody>
          @for (designation of designations(); track designation.id) {
            <tr>
              <td>{{ designation.code }}</td>
              <td>{{ designation.title }}</td>
              <td>{{ designation.jobFamily ?? '—' }}</td>
              <td>{{ designation.jobLevel ?? '—' }}</td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
  styles: `
    .panel {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1rem;
      margin-block-end: 1rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised, #ffffff);
    }

    .panel__grid {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
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
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .btn {
      align-self: flex-start;
      padding: 0.5rem 1rem;
      font-weight: 600;
      border-radius: 0.375rem;
      border: 1px solid transparent;
      cursor: pointer;
    }

    .btn--primary {
      background: var(--gggs-color-primary, #1d4ed8);
      color: #fff;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    .list-table {
      width: 100%;
      border-collapse: collapse;
    }

    .list-table th,
    .list-table td {
      padding: 0.5rem 0.75rem;
      text-align: left;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
    }
  `,
})
export class DesignationsPage {
  private readonly api = inject(OrgReferenceApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly isFormOpen = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly isLoading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly designations = signal<readonly DesignationListItem[]>([]);

  protected readonly form = this.formBuilder.group({
    code: ['', [Validators.required, Validators.maxLength(32)]],
    title: ['', [Validators.required, Validators.maxLength(150)]],
    jobFamily: [''],
    jobLevel: [null as number | null],
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      this.designations.set(await this.api.listDesignations());
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const { code, title, jobFamily, jobLevel } = this.form.getRawValue();

    try {
      await this.api.createDesignation({
        code: code ?? '',
        title: title ?? '',
        jobFamily: jobFamily || undefined,
        jobLevel: jobLevel ?? undefined,
      });

      this.form.reset();
      this.isFormOpen.set(false);
      await this.load();
    } catch (error) {
      this.errorMessage.set(describeError(error));
    } finally {
      this.isSaving.set(false);
    }
  }
}

export function describeError(error: unknown): string {
  if (error instanceof ApiError && error.errorCode === 'ORGANIZATION_CODE_IN_USE') {
    return 'That code is already in use.';
  }

  return 'The designation could not be created. Please try again.';
}
