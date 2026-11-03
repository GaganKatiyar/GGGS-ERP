import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminApi } from '@gggs/shared/identity';
import { Button, FormField } from '@gggs/shared/ui';
import { rememberedTenant } from './remembered-tenant';
import { SignedOutLayout } from './signed-out-layout';

/**
 * Asking for a link to choose a new password. The answer is the same whether or not the account
 * exists, so this page cannot be used to find out who works where.
 */
@Component({
  selector: 'gggs-forgot-password-page',
  imports: [ReactiveFormsModule, RouterLink, FormField, Button, SignedOutLayout],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-signed-out-layout>
      @if (sent()) {
        <div class="auth__form rise-in" role="status">
          <h1 class="auth__title">Check your email</h1>
          <p class="auth__note">If that address has an account, a link to choose a new password is on its way. It works for one hour. Look in your spam folder if it does not arrive in a few minutes.</p>
          <p class="auth__help"><a routerLink="/sign-in">Back to sign in</a></p>
        </div>
      } @else {
        <form class="auth__form rise-in" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <h1 class="auth__title">Forgot your password?</h1>
          <p class="auth__note">Enter your work email and we will send you a link to choose a new one.</p>

          <gggs-form-field label="Organisation code" hint="The short code your organisation was given, for example acme." hintId="forgot-tenant-hint">
            <input type="text" formControlName="tenantCode" autocomplete="organization" autocapitalize="none" spellcheck="false" [attr.aria-describedby]="'forgot-tenant-hint'" />
          </gggs-form-field>

          <gggs-form-field label="Work email" required>
            <input type="email" formControlName="email" autocomplete="username" autocapitalize="none" spellcheck="false" required />
          </gggs-form-field>

          @if (error()) {
            <p class="auth__error" role="alert">{{ error() }}</p>
          }

          <gggs-button type="submit" [loading]="isBusy()" [fullWidth]="true">
            {{ isBusy() ? 'Sending…' : 'Send the link' }}
          </gggs-button>

          <p class="auth__help"><a routerLink="/sign-in">Back to sign in</a></p>
        </form>
      }
    </gggs-signed-out-layout>
  `,
})
export class ForgotPasswordPage {
  private readonly api = inject(AdminApi);

  protected readonly isBusy = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    tenantCode: [rememberedTenant() ?? ''],
    email: ['', [Validators.required, Validators.email]],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isBusy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isBusy.set(true);
    this.error.set(null);
    const { tenantCode, email } = this.form.getRawValue();

    try {
      await this.api.forgotPassword({ tenantCode: tenantCode.trim() || undefined, email: email.trim() });
      this.sent.set(true);
    } catch {
      this.error.set('The link could not be sent just now. Please try again in a minute.');
    } finally {
      this.isBusy.set(false);
    }
  }
}
