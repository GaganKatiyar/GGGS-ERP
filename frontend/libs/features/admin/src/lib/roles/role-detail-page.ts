import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmptyState, PageHeader } from '@gggs/shared/ui';
import type { DataScope, PermissionCatalogItem, Role } from '@gggs/shared/models';
import { AdminApi } from '@gggs/shared/identity';

/**
 * One role's permissions: what it currently grants, and a form to grant or revoke against the
 * server's own permission catalog (`GET /api/v1/permissions`) rather than a hand-maintained list -
 * a screen offering a permission nothing enforces would let an administrator grant access that
 * does nothing (`RoleEndpoints.cs`'s own reasoning for serving the catalog at all).
 */
@Component({
  selector: 'gggs-role-detail-page',
  imports: [ReactiveFormsModule, RouterLink, HasPermissionDirective, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isLoading()) {
      <p>Loading…</p>
    } @else if (role(); as role) {
      <gggs-page-header [title]="role.name" [subtitle]="role.description">
        <a routerLink="/admin/roles" class="btn">Back to roles</a>
      </gggs-page-header>

      <section class="panel">
        <h2 class="panel__heading">Granted permissions</h2>

        @if (role.permissions.length === 0) {
          <gggs-empty-state title="No permissions granted yet" />
        } @else {
          <table class="list-table">
            <thead>
              <tr>
                <th scope="col">Permission</th>
                <th scope="col">Scope</th>
                <th scope="col" *gggsHasPermission="'admin.role.manage'"></th>
              </tr>
            </thead>
            <tbody>
              @for (permission of role.permissions; track permission.permissionCode) {
                <tr>
                  <td><code>{{ permission.permissionCode }}</code></td>
                  <td>{{ permission.scope }}</td>
                  <td *gggsHasPermission="'admin.role.manage'">
                    <button
                      type="button"
                      class="btn btn--danger"
                      [disabled]="isActing()"
                      (click)="revoke(permission.permissionCode)"
                    >
                      Revoke
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </section>

      <section class="panel" *gggsHasPermission="'admin.role.manage'">
        <h2 class="panel__heading">Grant a permission</h2>

        <form [formGroup]="form" (ngSubmit)="grant()" novalidate class="grant-form">
          <label class="field">
            <span class="field__label">Permission</span>
            <select formControlName="permissionCode" required>
              <option value="" disabled>Choose one</option>
              @for (permission of ungranted(); track permission.code) {
                <option [value]="permission.code">
                  {{ permission.code }} ({{ permission.module }})
                </option>
              }
            </select>
          </label>

          <label class="field">
            <span class="field__label">Scope</span>
            <select formControlName="scope" required>
              <option value="Own">Own</option>
              <option value="DirectReports">Direct reports</option>
              <option value="AllReports">All reports</option>
              <option value="Department">Department</option>
              <option value="Branch">Branch</option>
              <option value="LegalEntity">Legal entity</option>
              <option value="BusinessUnit">Business unit</option>
              <option value="Tenant">Tenant</option>
            </select>
          </label>

          @if (errorMessage()) {
            <p class="field__error" role="alert">{{ errorMessage() }}</p>
          }

          <button type="submit" class="btn btn--primary" [disabled]="isActing()">
            Grant
          </button>
        </form>
      </section>
    } @else {
      <gggs-empty-state title="Role not found" />
    }
  `,
  styles: `
    .panel {
      padding: 1rem;
      margin-block-end: 1rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised, #ffffff);
    }

    .panel__heading {
      margin: 0 0 0.75rem;
      font-size: 1rem;
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

    .grant-form {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: 0.75rem;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
      min-width: 16rem;
    }

    .field__label {
      font-weight: 600;
    }

    .field__error {
      flex-basis: 100%;
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .btn {
      display: inline-flex;
      align-items: center;
      padding: 0.5rem 1rem;
      font-weight: 600;
      font-family: inherit;
      font-size: inherit;
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

    .btn--danger {
      border-color: var(--gggs-color-danger, #b3261e);
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.8125rem;
      padding: 0.375rem 0.75rem;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
  `,
})
export class RoleDetailPage {
  private readonly api = inject(AdminApi);
  private readonly formBuilder = inject(FormBuilder);

  readonly id = input.required<string>();

  protected readonly isLoading = signal(true);
  protected readonly isActing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly role = signal<Role | null>(null);
  protected readonly catalog = signal<readonly PermissionCatalogItem[]>([]);

  protected readonly form = this.formBuilder.nonNullable.group({
    permissionCode: ['', Validators.required],
    scope: ['Tenant' as DataScope, Validators.required],
  });

  constructor() {
    void this.load();
  }

  /** The catalog minus whatever this role already holds - granting the same code twice is a
   * revoke-then-grant away, not a state this form needs to represent. */
  protected ungranted(): readonly PermissionCatalogItem[] {
    const granted = new Set(this.role()?.permissions.map((permission) => permission.permissionCode));
    return this.catalog().filter((permission) => !granted.has(permission.code));
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      const [roles, catalog] = await Promise.all([
        this.api.listRoles(),
        this.api.listPermissionCatalog(),
      ]);

      this.role.set(roles.find((role) => role.id === this.id()) ?? null);
      this.catalog.set(catalog);
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async grant(): Promise<void> {
    if (this.form.invalid || this.isActing()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isActing.set(true);
    this.errorMessage.set(null);

    const { permissionCode, scope } = this.form.getRawValue();

    try {
      this.role.set(await this.api.grantPermission(this.id(), { permissionCode, scope }));
      this.form.reset({ scope: 'Tenant' });
    } catch (error) {
      this.errorMessage.set(describeError(error));
    } finally {
      this.isActing.set(false);
    }
  }

  protected async revoke(permissionCode: string): Promise<void> {
    this.isActing.set(true);
    this.errorMessage.set(null);

    try {
      this.role.set(await this.api.revokePermission(this.id(), permissionCode));
    } catch {
      this.errorMessage.set('That permission could not be revoked. Please try again.');
    } finally {
      this.isActing.set(false);
    }
  }
}

export function describeError(error: unknown): string {
  if (error instanceof ApiError && error.errorCode === 'PERMISSION_UNKNOWN') {
    return 'That is not a permission this platform defines.';
  }

  return 'That permission could not be granted. Please try again.';
}
