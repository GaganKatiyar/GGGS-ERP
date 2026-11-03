import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { AdminApi } from '@gggs/shared/identity';
import { Button, FormField } from '@gggs/shared/ui';
import { SignedOutLayout } from './signed-out-layout';

/**
 * Choosing a new password from an emailed link. Like an invitation, the query parameters are the
 * credential the server checks, so the page needs no session.
 */
@Component({
  selector: 'gggs-reset-password-page',
  imports: [ReactiveFormsModule, RouterLink, FormField, Button, SignedOutLayout],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-signed-out-layout>
      <form class="auth__form rise-in" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <h1 class="auth__title">Choose a new password</h1>

        @if (!complete() || expired()) {
          <p class="auth__error" role="alert">This link has expired or has already been used.</p>
          <p class="auth__help"><a routerLink="/forgot-password">Send a new link</a></p>
        } @else if (succeeded()) {
          <p class="auth__success" role="status">Password changed, and you have been signed out everywhere. Taking you to sign in…</p>
        } @else {
          <div class="auth__password">
            <gggs-form-field label="New password" hint="At least 12 characters." required>
              <input [type]="showPassword() ? 'text' : 'password'" formControlName="newPassword" autocomplete="new-password" minlength="12" required />
            </gggs-form-field>
            <button type="button" class="auth__eye auth__eye--hinted" [attr.aria-pressed]="showPassword()" aria-label="Show password" (click)="showPassword.set(!showPassword())">
              {{ showPassword() ? 'Hide' : 'Show' }}
            </button>
          </div>

          @if (error()) {
            <p class="auth__error" role="alert">{{ error() }}</p>
          }

          <gggs-button type="submit" [loading]="isBusy()" [fullWidth]="true">
            {{ isBusy() ? 'Saving…' : 'Change password' }}
          </gggs-button>
        }
      </form>
    </gggs-signed-out-layout>
  `,
})
export class ResetPasswordPage {
  private readonly api = inject(AdminApi);
  private readonly router = inject(Router);

  readonly tenantCode = input<string>();
  readonly userId = input<string>();
  readonly token = input<string>();

  protected readonly complete = computed(() => !!this.tenantCode() && !!this.userId() && !!this.token());
  protected readonly isBusy = signal(false);
  protected readonly succeeded = signal(false);
  protected readonly expired = signal(false);
  protected readonly showPassword = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(12)]],
  });

  protected async submit(): Promise<void> {
    const tenantCode = this.tenantCode();
    const userId = this.userId();
    const token = this.token();
    if (this.form.invalid || this.isBusy() || !tenantCode || !userId || !token) {
      this.form.markAllAsTouched();
      return;
    }

    this.isBusy.set(true);
    this.error.set(null);

    try {
      await this.api.resetPassword({ tenantCode, userId, token, newPassword: this.form.getRawValue().newPassword });
      this.succeeded.set(true);
      setTimeout(() => void this.router.navigateByUrl('/sign-in'), 2500);
    } catch (error) {
      if (error instanceof ApiError && error.errorCode === 'PASSWORD_RESET_INVALID') {
        this.expired.set(true);
      } else if (error instanceof ApiError && error.errorCode === 'PASSWORD_REJECTED') {
        // The server's message says which rule failed.
        this.error.set(error.problem.detail ?? 'That password does not meet the policy. Please choose another.');
      } else {
        this.error.set('Your password could not be changed just now. Please try again in a minute.');
      }
    } finally {
      this.isBusy.set(false);
    }
  }
}
