import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmptyState, PageHeader } from '@gggs/shared/ui';
import type { GradeListItem } from '@gggs/shared/models';
import { OrgReferenceApi } from '@gggs/shared/org-reference';

/** Grades: pay bands, ranked so "is this a promotion" has an answer. */
@Component({
  selector: 'gggs-grades-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Grades" subtitle="Pay bands, ranked from lowest to highest.">
      <button
        type="button"
        class="btn btn--primary"
        *gggsHasPermission="'org.structure.manage'"
        (click)="isFormOpen.set(!isFormOpen())"
      >
        {{ isFormOpen() ? 'Cancel' : 'New grade' }}
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
            <span class="field__label">Name</span>
            <input type="text" formControlName="name" maxlength="150" required />
          </label>

          <label class="field">
            <span class="field__label">Rank</span>
            <input
              type="number"
              formControlName="rank"
              min="1"
              required
              aria-describedby="rank-hint"
            />
            <span id="rank-hint" class="field__hint">
              Higher ranks outrank lower ones - this is what tells a transfer from a promotion.
            </span>
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
    } @else if (grades().length === 0) {
      <gggs-empty-state title="No grades yet" description="Create the first pay band." />
    } @else {
      <table class="list-table">
        <thead>
          <tr>
            <th scope="col">Code</th>
            <th scope="col">Name</th>
            <th scope="col">Rank</th>
            <th scope="col">Salary band</th>
          </tr>
        </thead>
        <tbody>
          @for (grade of grades(); track grade.id) {
            <tr>
              <td>{{ grade.code }}</td>
              <td>{{ grade.name }}</td>
              <td>{{ grade.rank }}</td>
              <td>{{ salaryBand(grade) }}</td>
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

    .field__hint {
      font-size: 0.75rem;
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
      border: 1px solid transparent;
      cursor: pointer;
    }

    .btn--primary {
      background: var(--gggs-color-primary, #2f5fd6);
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
export class GradesPage {
  private readonly api = inject(OrgReferenceApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly isFormOpen = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly isLoading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly grades = signal<readonly GradeListItem[]>([]);

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(32)]],
    name: ['', [Validators.required, Validators.maxLength(150)]],
    rank: [1, [Validators.required, Validators.min(1)]],
  });

  constructor() {
    void this.load();
  }

  protected salaryBand(grade: GradeListItem): string {
    if (grade.salaryBandMinimum === undefined && grade.salaryBandMaximum === undefined) {
      return '—';
    }

    const currency = grade.currency ?? '';
    return `${currency} ${grade.salaryBandMinimum ?? '?'} – ${grade.salaryBandMaximum ?? '?'}`.trim();
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      this.grades.set(await this.api.listGrades());
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

    try {
      await this.api.createGrade(this.form.getRawValue());
      this.form.reset({ rank: 1 });
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

  return 'The grade could not be created. Please try again.';
}
