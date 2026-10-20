import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Badge, Button, Card, EmptyState, FormField, PageHeader } from '@gggs/shared/ui';
import type { Role } from '@gggs/shared/models';
import { AdminApi } from '@gggs/shared/identity';

/** Roles: named bundles of permissions, assigned to users on the "manage roles" screen. */
@Component({
  selector: 'gggs-role-list-page',
  imports: [
    ReactiveFormsModule,
    HasPermissionDirective,
    PageHeader,
    EmptyState,
    Card,
    FormField,
    Button,
    Badge,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Roles" subtitle="Named bundles of permissions, granted to users.">
      <gggs-button
        *gggsHasPermission="'admin.role.manage'"
        (click)="isFormOpen.set(!isFormOpen())"
      >
        {{ isFormOpen() ? 'Cancel' : 'New role' }}
      </gggs-button>
    </gggs-page-header>

    @if (isFormOpen()) {
      <gggs-card class="panel">
        <form
          class="panel__form"
          [formGroup]="form"
          (ngSubmit)="submit()"
          novalidate
        >
          <gggs-form-field label="Name" required>
            <input type="text" formControlName="name" maxlength="100" required />
          </gggs-form-field>

          <gggs-form-field label="Description (optional)">
            <input type="text" formControlName="description" maxlength="500" />
          </gggs-form-field>

          @if (errorMessage()) {
            <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ errorMessage() }}</span></p>
          }

          <gggs-button type="submit" [loading]="isSaving()">Create</gggs-button>
        </form>
      </gggs-card>
    }

    @if (isLoading()) {
      <p>Loading…</p>
    } @else if (roles().length === 0) {
      <gggs-empty-state title="No roles yet" description="Create the first one." />
    } @else {
      <table class="table list-table">
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
              <td>
                {{ role.name }}
                @if (role.isSystem) {
                  <gggs-badge label="system" />
                }
              </td>
              <td>{{ role.description ?? '—' }}</td>
              <td>{{ role.permissions.length }}</td>
              <td>
                <gggs-button
                  variant="secondary"
                  size="sm"
                  [link]="['/admin/roles', role.id]"
                >
                  Manage permissions
                </gggs-button>
              </td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
  styles: `
    .panel {
      display: block;
      max-width: 28rem;
      margin-block-end: var(--gggs-space-4, 1rem);
    }

    .panel__form {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-4, 1rem);
    }

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger);
      font-size: var(--gggs-font-size-base, 0.875rem);
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
