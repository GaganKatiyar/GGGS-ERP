import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { AdminApi } from '@gggs/shared/identity';

/**
 * Setting a password from an invitation link.
 *
 * Outside the shell and the auth guard, like sign-in: the invitee has no session yet, and the
 * three query parameters below are the credential the server actually checks
 * (`POST /api/v1/auth/complete-invitation` is `AllowAnonymous` for exactly this reason). Reads
 * them as component inputs rather than injecting `ActivatedRoute` directly, the same way the id on
 * every employee detail page does - `withComponentInputBinding()` in app.config.ts binds query
 * params to inputs by name, not only path params.
 */
@Component({
  selector: 'gggs-complete-invitation-page',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="page">
      <form class="card" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <h1 class="card__title">Set your password</h1>

        @if (!tenantCode() || !userId() || !token()) {
          <p class="card__error" role="alert">
            This link is missing something it needs. Ask whoever invited you to send it again.
          </p>
        } @else {
          <label class="field">
            <span class="field__label">New password</span>
            <input
              type="password"
              formControlName="newPassword"
              autocomplete="new-password"
              minlength="12"
              required
            />
            <span class="field__hint">At least 12 characters.</span>
          </label>

          @if (errorMessage()) {
            <p class="card__error" role="alert">{{ errorMessage() }}</p>
          }

          @if (succeeded()) {
            <p class="card__success" role="status">
              Password set. You can sign in now.
            </p>
          }

          <button type="submit" class="card__submit" [disabled]="isSaving() || succeeded()">
            {{ isSaving() ? 'Saving…' : 'Set password' }}
          </button>
        }
      </form>
    </main>
  `,
  styles: `
    .page {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100dvh;
      padding: 1rem;
    }

    .card {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      width: 100%;
      max-width: 24rem;
      padding: 2rem;
      border: 1px solid var(--gggs-color-border, #d8dde6);
      border-radius: 0.75rem;
      background: var(--gggs-color-surface, #ffffff);
    }

    .card__title {
      margin: 0;
      font-size: 1.25rem;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }

    .field__label {
      font-weight: 600;
      font-size: 0.875rem;
    }

    .field__hint {
      font-size: 0.75rem;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .card__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .card__success {
      margin: 0;
      color: var(--gggs-color-success, #1e7d34);
      font-size: 0.875rem;
    }

    .card__submit {
      margin-block-start: 0.5rem;
      padding: 0.625rem 1rem;
      font-weight: 600;
    }
  `,
})
export class CompleteInvitationPage {
  private readonly api = inject(AdminApi);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);

  readonly tenantCode = input<string>();
  readonly userId = input<string>();
  readonly token = input<string>();

  protected readonly isSaving = signal(false);
  protected readonly succeeded = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(12)]],
  });

  protected async submit(): Promise<void> {
    const tenantCode = this.tenantCode();
    const userId = this.userId();
    const token = this.token();

    if (this.form.invalid || this.isSaving() || !tenantCode || !userId || !token) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    try {
      await this.api.completeInvitation({
        tenantCode,
        userId,
        token,
        newPassword: this.form.getRawValue().newPassword,
      });

      this.succeeded.set(true);
      setTimeout(() => void this.router.navigateByUrl('/sign-in'), 2000);
    } catch (error) {
      this.errorMessage.set(describeError(error));
    } finally {
      this.isSaving.set(false);
    }
  }
}

function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.errorCode === 'INVITATION_INVALID') {
      return 'That invitation is no longer valid. Ask whoever invited you to send a new one.';
    }

    if (error.errorCode === 'PASSWORD_REJECTED') {
      // The server's own message already explains exactly which rule failed.
      return error.problem.detail ?? 'That password does not meet the policy. Please choose another.';
    }
  }

  return 'Your password could not be set. Please try again.';
}
