import { ChangeDetectionStrategy, Component, type OnInit, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PageHeader } from '@gggs/shared/ui';
import type { Role } from '@gggs/shared/models';
import { AdminApi } from '@gggs/shared/identity';

/**
 * Which roles a user holds.
 *
 * `PUT /users/{id}/roles` replaces the whole set, not just one grant - there is no "add a role"
 * endpoint - so this loads the user's current roles first (`GET /users/{id}/roles`, added
 * alongside this screen) and pre-checks them, rather than presenting an empty form that would
 * silently clear every existing grant the moment it saved.
 */
@Component({
  selector: 'gggs-user-roles-page',
  imports: [RouterLink, PageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Manage roles">
      <a routerLink="/admin/users" class="btn">Back to users</a>
    </gggs-page-header>

    @if (isLoading()) {
      <p>Loading…</p>
    } @else {
      <form (ngSubmit)="submit()" class="panel">
        @for (role of roles(); track role.id) {
          <label class="checkbox">
            <input
              type="checkbox"
              [checked]="isSelected(role.id)"
              (change)="toggle(role.id, $event)"
            />
            {{ role.name }}
            @if (role.description) {
              <span class="checkbox__hint">{{ role.description }}</span>
            }
          </label>
        }

        @if (errorMessage()) {
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ errorMessage() }}</span></p>
        }

        <button type="submit" class="btn btn--primary" [disabled]="isSaving()">
          {{ isSaving() ? 'Saving…' : 'Save' }}
        </button>
      </form>
    }
  `,
  styles: `
    .panel {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      padding: 1rem;
      max-width: 28rem;
      border: 1px solid var(--gggs-color-border);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised);
    }

    .checkbox {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
      font-size: 0.875rem;
    }

    .checkbox__hint {
      flex-basis: 100%;
      margin-inline-start: 1.5rem;
      color: var(--gggs-color-text-muted);
      font-size: 0.8125rem;
    }

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger);
      font-size: 0.875rem;
    }

    .btn {
      align-self: flex-start;
      padding: 0.5rem 1rem;
      font-weight: 600;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border);
      background: var(--gggs-color-surface-raised);
      text-decoration: none;
      color: inherit;
      cursor: pointer;
    }

    .btn--primary {
      background: var(--gggs-color-primary);
      border-color: transparent;
      color: var(--gggs-color-primary-contrast);
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
  `,
})
export class UserRolesPage implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly router = inject(Router);

  readonly id = input.required<string>();

  protected readonly isLoading = signal(true);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly roles = signal<readonly Role[]>([]);
  private readonly selectedRoleIds = signal<ReadonlySet<string>>(new Set());

  // Not the constructor: the route's :id input is only set once the component is initialised.
  ngOnInit(): void {
    void this.load();
  }

  protected isSelected(roleId: string): boolean {
    return this.selectedRoleIds().has(roleId);
  }

  protected toggle(roleId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const next = new Set(this.selectedRoleIds());

    if (checked) {
      next.add(roleId);
    } else {
      next.delete(roleId);
    }

    this.selectedRoleIds.set(next);
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      const [roles, currentRoleIds] = await Promise.all([
        this.api.listRoles(),
        this.api.getUserRoles(this.id()),
      ]);

      this.roles.set(roles);
      this.selectedRoleIds.set(new Set(currentRoleIds));
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async submit(): Promise<void> {
    if (this.isSaving()) {
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    try {
      await this.api.setUserRoles(this.id(), [...this.selectedRoleIds()]);
      await this.router.navigate(['/admin/users']);
    } catch (error) {
      this.errorMessage.set(describeError(error));
    } finally {
      this.isSaving.set(false);
    }
  }
}

export function describeError(error: unknown): string {
  if (error instanceof ApiError && error.errorCode === 'LAST_ADMINISTRATOR') {
    return 'This is the only account that can manage users. Give someone else that permission first.';
  }

  return 'The roles could not be saved. Please try again.';
}
