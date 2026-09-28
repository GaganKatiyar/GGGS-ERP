import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmptyState, PageHeader } from '@gggs/shared/ui';
import type { Role } from '@gggs/shared/models';
import { AdminApi } from '@gggs/shared/identity';

/** Roles: named bundles of permissions, assigned to users on the "manage roles" screen. */
@Component({
  selector: 'gggs-role-list-page',
  imports: [ReactiveFormsModule, RouterLink, HasPermissionDirective, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Roles" subtitle="Named bundles of permissions, granted to users.">
      <button
        type="button"
        class="btn btn--primary"
        *gggsHasPermission="'admin.role.manage'"
        (click)="isFormOpen.set(!isFormOpen())"
      >
        {{ isFormOpen() ? 'Cancel' : 'New role' }}
      </button>
    </gggs-page-header>

    @if (isFormOpen()) {
      <form class="panel" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" formControlName="name" maxlength="100" required />
        </label>

        <label class="field">
          <span class="field__label">Description (optional)</span>
          <input type="text" formControlName="description" maxlength="500" />
        </label>

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
    } @else if (roles().length === 0) {
      <gggs-empty-state title="No roles yet" description="Create the first one." />
    } @else {
      <table class="list-table">
        <thead>
          <tr>
            <th scope="col">Name</th>
            <th scope="col">Description</th>
            <th scope="col">Permissions</th>
            <th scope="col"></th>
          </tr>
        </thead>
        <tbody>
          @for (role of roles(); track role.id) {
            <tr>
              <td>{{ role.name }} @if (role.isSystem) { <span class="badge">system</span> }</td>
              <td>{{ role.description ?? '—' }}</td>
              <td>{{ role.permissions.length }}</td>
              <td>
                <a [routerLink]="['/admin/roles', role.id]" class="btn">Manage permissions</a>
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
      max-width: 28rem;
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

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .badge {
      margin-inline-start: 0.375rem;
      padding: 0.0625rem 0.375rem;
      font-size: 0.6875rem;
      font-weight: 600;
      border-radius: 0.25rem;
      background: var(--gggs-color-surface, #f3f4f7);
      color: var(--gggs-color-text-muted, #5b6472);
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

    .btn {
      display: inline-flex;
      align-items: center;
      padding: 0.375rem 0.75rem;
      font-weight: 600;
      font-family: inherit;
      font-size: 0.8125rem;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #ffffff);
      text-decoration: none;
      color: inherit;
      cursor: pointer;
    }

    .btn--primary {
      background: var(--gggs-color-primary, #2f5fd6);
      border-color: transparent;
      color: #fff;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
  `,
})
export class RoleListPage {
  private readonly api = inject(AdminApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly isFormOpen = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly isLoading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly roles = signal<readonly Role[]>([]);

  protected readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    description: [''],
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      this.roles.set(await this.api.listRoles());
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

    const { name, description } = this.form.getRawValue();

    try {
      await this.api.createRole({ name, description: description || undefined });
      this.form.reset();
      this.isFormOpen.set(false);
      await this.load();
    } catch {
      this.errorMessage.set('The role could not be created. Please try again.');
    } finally {
      this.isSaving.set(false);
    }
  }
}
