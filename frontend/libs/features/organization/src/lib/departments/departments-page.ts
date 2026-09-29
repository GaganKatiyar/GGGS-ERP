import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmptyState, PageHeader } from '@gggs/shared/ui';
import type { DepartmentListItem } from '@gggs/shared/models';
import { OrgReferenceApi } from '@gggs/shared/org-reference';

/**
 * Departments.
 *
 * A flat table with the parent's name resolved client-side, rather than a tree control - there is
 * no tree component in shared/ui yet (architecture.md notes the same simplification server-side:
 * an adjacency list, not `HIERARCHYID`). "Move" is kept front and centre because it is the one
 * operation the server actually guards against a real mistake - moving a department under its own
 * descendant - and is worth being able to see work from here.
 */
@Component({
  selector: 'gggs-departments-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Departments" subtitle="The reporting structure employees sit in.">
      <button
        type="button"
        class="btn btn--primary"
        *gggsHasPermission="'org.department.manage'"
        (click)="isFormOpen.set(!isFormOpen())"
      >
        {{ isFormOpen() ? 'Cancel' : 'New department' }}
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
            <input type="text" formControlName="name" maxlength="200" required />
          </label>

          <label class="field">
            <span class="field__label">Parent department</span>
            <select formControlName="parentDepartmentId">
              <option value="">None - top level</option>
              @for (department of departments(); track department.id) {
                <option [value]="department.id">{{ department.name }}</option>
              }
            </select>
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

    @if (moveError()) {
      <p class="field__error" role="alert">{{ moveError() }}</p>
    }

    @if (isLoading()) {
      <p>Loading…</p>
    } @else if (departments().length === 0) {
      <gggs-empty-state
        title="No departments yet"
        description="Create the first one - it can always be moved under another one later."
      />
    } @else {
      <table class="list-table">
        <thead>
          <tr>
            <th scope="col">Code</th>
            <th scope="col">Name</th>
            <th scope="col">Parent</th>
            <th scope="col" *gggsHasPermission="'org.department.manage'">Move under</th>
          </tr>
        </thead>
        <tbody>
          @for (department of departments(); track department.id) {
            <tr>
              <td>{{ department.code }}</td>
              <td>{{ department.name }}</td>
              <td>{{ parentName(department.parentDepartmentId) }}</td>
              <td *gggsHasPermission="'org.department.manage'">
                <select
                  [value]="department.parentDepartmentId ?? ''"
                  (change)="move(department, $event)"
                >
                  <option value="">None - top level</option>
                  @for (candidate of departments(); track candidate.id) {
                    @if (candidate.id !== department.id) {
                      <option [value]="candidate.id">{{ candidate.name }}</option>
                    }
                  }
                </select>
              </td>
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
      margin: 0 0 1rem;
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
export class DepartmentsPage {
  private readonly api = inject(OrgReferenceApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly isFormOpen = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly isLoading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly moveError = signal<string | null>(null);
  protected readonly departments = signal<readonly DepartmentListItem[]>([]);

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(32)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    parentDepartmentId: [''],
  });

  constructor() {
    void this.load();
  }

  protected parentName(parentId: string | undefined): string {
    if (!parentId) {
      return '—';
    }

    return this.departments().find((department) => department.id === parentId)?.name ?? parentId;
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      this.departments.set(await this.api.listDepartments());
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

    const { code, name, parentDepartmentId } = this.form.getRawValue();

    try {
      await this.api.createDepartment({
        code,
        name,
        parentDepartmentId: parentDepartmentId || undefined,
      });

      this.form.reset();
      this.isFormOpen.set(false);
      await this.load();
    } catch (error) {
      this.errorMessage.set(describeCreateError(error));
    } finally {
      this.isSaving.set(false);
    }
  }

  protected async move(department: DepartmentListItem, event: Event): Promise<void> {
    const newParentId = (event.target as HTMLSelectElement).value || undefined;
    this.moveError.set(null);

    try {
      await this.api.moveDepartment(department.id, newParentId);
      await this.load();
    } catch (error) {
      this.moveError.set(describeMoveError(error));
      await this.load(); // resets the <select> to the department's real current parent
    }
  }
}

export function describeCreateError(error: unknown): string {
  if (error instanceof ApiError && error.errorCode === 'ORGANIZATION_CODE_IN_USE') {
    return 'That code is already in use.';
  }

  return 'The department could not be created. Please try again.';
}

export function describeMoveError(error: unknown): string {
  if (error instanceof ApiError && error.errorCode === 'DEPARTMENT_MOVE_WOULD_CREATE_CYCLE') {
    return 'That move would make the department a descendant of itself.';
  }

  return 'The department could not be moved. Please try again.';
}
