import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import {
  SessionStore,
  safeReturnUrl,
  type PasswordSignInResult,
} from '@gggs/core/auth';
import { Button, FormField } from '@gggs/shared/ui';
import { PRODUCT_NAME } from '../product-title-strategy';

/**
 * Signing in.
 *
 * The one page that collects a credential, and the only one outside the shell. It posts to the
 * API and never sees a token: the API sets an httpOnly cookie, so nothing this page runs
 * could read or store one even if it tried.
 *
 * The tenant code is a hint about where to look, not an assertion about who the user is. It picks
 * which customer's directory is searched; which tenant the session actually belongs to comes from
 * the user record the server finds, so a wrong code cannot get anyone into someone else's data.
 */
@Component({
  selector: 'gggs-sign-in-page',
  imports: [ReactiveFormsModule, FormField, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="sign-in">
      <form
        class="sign-in__card"
        [formGroup]="form"
        (ngSubmit)="submit()"
        novalidate
      >
        <p class="sign-in__product">{{ productName }}</p>
        <h1 class="sign-in__title">Sign in</h1>

        <gggs-form-field
          label="Organisation code"
          hint="The short code your organisation was given, for example acme."
          hintId="tenant-hint"
        >
          <input
            type="text"
            formControlName="tenantCode"
            autocomplete="organization"
            autocapitalize="none"
            spellcheck="false"
            [attr.aria-describedby]="'tenant-hint'"
          />
        </gggs-form-field>

        <gggs-form-field label="Work email" required>
          <input
            type="email"
            formControlName="email"
            autocomplete="username"
            autocapitalize="none"
            spellcheck="false"
            required
          />
        </gggs-form-field>

        <gggs-form-field label="Password" required>
          <input
            type="password"
            formControlName="password"
            autocomplete="current-password"
            required
          />
        </gggs-form-field>

        @if (message()) {
          <!-- assertive: a failed sign-in must reach a screen reader immediately, because the
               person cannot proceed until they have heard it. -->
          <p class="sign-in__error" role="alert" aria-live="assertive">
            {{ message() }}
          </p>
        }

        <gggs-button type="submit" [loading]="isBusy()" [fullWidth]="true">
          {{ isBusy() ? 'Signing in…' : 'Sign in' }}
        </gggs-button>
      </form>
    </main>
  `,
  styles: `
    .sign-in {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100dvh;
      padding: var(--gggs-space-4, 1rem);
    }

    .sign-in__card {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-4, 1rem);
      width: 100%;
      max-width: 24rem;
      padding: var(--gggs-space-8, 2rem);
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: var(--gggs-radius-lg, 0.75rem);
      background: var(--gggs-color-surface-raised, #ffffff);
      box-shadow: var(--gggs-shadow-md, none);
    }

    .sign-in__product {
      margin: 0;
      color: var(--gggs-color-primary, #3b5bdb);
      font-size: var(--gggs-font-size-sm, 0.75rem);
      font-weight: var(--gggs-font-weight-semibold, 600);
      letter-spacing: 0.08em;
      text-transform: uppercase;
    }

    .sign-in__title {
      margin: 0;
      font-size: var(--gggs-font-size-xl, 1.25rem);
      font-weight: var(--gggs-font-weight-semibold, 600);
    }

    .sign-in__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: var(--gggs-font-size-base, 0.875rem);
    }
  `,
})
export class SignInPage {
  protected readonly productName = PRODUCT_NAME;
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly isBusy = signal(false);
  protected readonly message = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    tenantCode: [''],
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isBusy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isBusy.set(true);
    this.message.set(null);

    const { tenantCode, email, password } = this.form.getRawValue();

    try {
      const result = await this.session.signInWithPassword({
        email,
        password,
        tenantCode: tenantCode.trim() === '' ? undefined : tenantCode.trim(),
      });

      if (!result.succeeded) {
        this.message.set(describe(result));
        return;
      }

      // The URL the user was originally heading for, read here rather than kept in a store, so a
      // deep link survives the round trip.
      const returnUrl = new URLSearchParams(window.location.search).get(
        'returnUrl',
      );

      await this.router.navigateByUrl(safeReturnUrl(returnUrl));
    } catch {
      this.message.set('Sign-in is unavailable right now. Please try again.');
    } finally {
      this.isBusy.set(false);
    }
  }
}

/** Turns the server's error code into something a person can act on. */
function describe(result: PasswordSignInResult): string {
  switch (result.errorCode) {
    case 'ACCOUNT_LOCKED':
      return result.lockoutEndDate
        ? `Too many attempts. Try again after ${new Date(result.lockoutEndDate).toLocaleTimeString()}.`
        : 'Too many attempts. Try again shortly.';

    case 'SIGN_IN_UNAVAILABLE':
      return 'Sign-in is unavailable right now. Please try again.';

    default:
      // One message for a wrong address, a wrong password and a tenant that does not exist. Being
      // more helpful here would tell an attacker which of the three they got right.
      return 'That email address and password do not match an account.';
  }
}
