import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { SessionStore } from '@gggs/core/auth';
import { ApiError } from '@gggs/core/http';
import { PageHeader } from '@gggs/shared/ui';
import type { InviteUserResult, Role } from '@gggs/shared/models';
import { AdminApi } from '@gggs/shared/identity';

/**
 * Inviting someone to sign in.
 *
 * There is no Notification service yet, so the invitation link is handed straight back to the
 * inviter here rather than emailed - see `InviteUserResult`'s own note. Passing it on to the
 * invitee is a manual step until that changes.
 */
@Component({
  selector: 'gggs-invite-user-page',
  imports: [ReactiveFormsModule, RouterLink, PageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Invite user">
      <a routerLink="/admin/users" class="btn">Back to users</a>
    </gggs-page-header>

    @if (result(); as result) {
      <section class="panel">
        <h2 class="panel__heading">Invitation created</h2>
        <p>
          Share this link with {{ form.controls.displayName.value }} so they can set a password.
          It expires {{ result.expiresDate }}.
        </p>
        <code class="link">{{ invitationLink(result) }}</code>
        <a routerLink="/admin/users" class="btn btn--primary">Done</a>
      </section>
    } @else {
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="panel">
        <label class="field">
          <span class="field__label">Work email</span>
          <input type="email" formControlName="email" required />
        </label>

        <label class="field">
          <span class="field__label">Display name</span>
          <input type="text" formControlName="displayName" maxlength="200" required />
        </label>

        <fieldset class="field">
          <legend class="field__label">Roles</legend>
          @for (role of roles(); track role.id) {
            <label class="checkbox">
              <input
                type="checkbox"
                [checked]="isRoleSelected(role.id)"
                (change)="toggleRole(role.id, $event)"
              />
              {{ role.name }}
            </label>
          }
        </fieldset>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <button type="submit" class="btn btn--primary" [disabled]="isSaving()">
          {{ isSaving() ? 'Sending…' : 'Invite' }}
        </button>
      </form>
    }
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

    .panel__heading {
      margin: 0;
      font-size: 1rem;
    }

    .link {
      display: block;
      padding: 0.5rem;
      word-break: break-all;
      background: var(--gggs-color-surface, #f6f7f9);
      border-radius: 0.375rem;
      font-size: 0.8125rem;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
      border: none;
      padding: 0;
      margin: 0;
    }

    .field__label {
      font-weight: 600;
    }

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .checkbox {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-weight: 400;
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
export class InviteUserPage {
  private readonly api = inject(AdminApi);
  private readonly session = inject(SessionStore);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly result = signal<InviteUserResult | null>(null);
  protected readonly roles = signal<readonly Role[]>([]);
  private readonly selectedRoleIds = signal<ReadonlySet<string>>(new Set());

  protected readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    displayName: ['', [Validators.required, Validators.maxLength(200)]],
  });

  constructor() {
    void this.loadRoles();
  }

  protected isRoleSelected(roleId: string): boolean {
    return this.selectedRoleIds().has(roleId);
  }

  protected toggleRole(roleId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    const next = new Set(this.selectedRoleIds());

    if (checked) {
      next.add(roleId);
    } else {
      next.delete(roleId);
    }

    this.selectedRoleIds.set(next);
  }

  protected invitationLink(result: InviteUserResult): string {
    const tenantCode = this.session.user()?.tenant.code ?? '';
    return `${window.location.origin}/complete-invitation?tenantCode=${encodeURIComponent(tenantCode)}&userId=${result.userId}&token=${encodeURIComponent(result.invitationToken)}`;
  }

  private async loadRoles(): Promise<void> {
    this.roles.set(await this.api.listRoles());
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const { email, displayName } = this.form.getRawValue();

    try {
      this.result.set(
        await this.api.inviteUser({
          email,
          displayName,
          roleIds: [...this.selectedRoleIds()],
        }),
      );
    } catch (error) {
      this.errorMessage.set(describeError(error));
    } finally {
      this.isSaving.set(false);
    }
  }
}

export function describeError(error: unknown): string {
  if (error instanceof ApiError && error.errorCode === 'USER_EMAIL_IN_USE') {
    return 'Someone with that email address already has an account in this organization.';
  }

  return 'The invitation could not be sent. Please try again.';
}
