import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { AdminApi } from '@gggs/shared/identity';
import { Button, FormField } from '@gggs/shared/ui';
import { PRODUCT_NAME } from '../product-title-strategy';

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
  imports: [ReactiveFormsModule, FormField, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="auth">
      <form class="auth__card rise-in" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <p class="auth__brand"><span class="auth__logo" aria-hidden="true">P</span>{{ productName }}</p>
        <h1 class="auth__title">Set your password</h1>

        @if (!tenantCode() || !userId() || !token()) {
          <p class="auth__error" role="alert">
            This link is missing something it needs. Ask whoever invited you to send it again.
          </p>
        } @else {
          <gggs-form-field label="New password" hint="At least 12 characters." required>
            <input
              type="password"
              formControlName="newPassword"
              autocomplete="new-password"
              minlength="12"
              required
            />
          </gggs-form-field>

          @if (errorMessage()) {
            <p class="auth__error" role="alert">{{ errorMessage() }}</p>
          }

          @if (succeeded()) {
            <p class="auth__success" role="status">
              Password set. You can sign in now.
            </p>
          }

          <gggs-button
            type="submit"
            [loading]="isSaving()"
            [disabled]="succeeded()"
            [fullWidth]="true"
          >
            {{ isSaving() ? 'Saving…' : 'Set password' }}
          </gggs-button>
        }
      </form>
    </main>
  `,
  // Styled by the shared signed-out layout (.auth in styles/_depth.scss).
})
export class CompleteInvitationPage {
  protected readonly productName = PRODUCT_NAME;
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
