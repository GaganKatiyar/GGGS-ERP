import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import {
  SessionStore,
  safeReturnUrl,
  type PasswordSignInResult,
  type SsoOption,
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
 *
 * Two further paths end here: the second step for someone with an authenticator app, and the
 * organisation's single sign-on providers, which leave for the provider and come back signed in
 * (or with the reason they did not).
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
        <h1 class="sign-in__title">{{ step() === 'mfa' ? 'Two-step sign-in' : 'Sign in' }}</h1>

        @if (step() === 'mfa') {
          <p class="sign-in__hint">Enter the six-digit code from your authenticator app, or one of your recovery codes.</p>
          <gggs-form-field label="Code" required>
            <input
              type="text"
              formControlName="code"
              autocomplete="one-time-code"
              maxlength="12"
              autocapitalize="characters"
              spellcheck="false"
            />
          </gggs-form-field>
        } @else {
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
              (blur)="loadSingleSignOn()"
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
        }

        @if (message()) {
          <!-- assertive: a failed sign-in must reach a screen reader immediately, because the
               person cannot proceed until they have heard it. -->
          <p class="sign-in__error" role="alert" aria-live="assertive">
            {{ message() }}
          </p>
        }

        <gggs-button type="submit" [loading]="isBusy()" [fullWidth]="true">
          {{ isBusy() ? 'Signing in…' : step() === 'mfa' ? 'Verify' : 'Sign in' }}
        </gggs-button>

        @if (step() === 'mfa') {
          <button type="button" class="btn--link" (click)="backToPassword()">Back to sign-in</button>
        } @else if (ssoOptions().length) {
          <p class="sign-in__or">or</p>
          @for (option of ssoOptions(); track option.key) {
            <button type="button" class="btn sign-in__sso" (click)="singleSignOn(option)">
              <i class="pi pi-key" aria-hidden="true"></i> Sign in with {{ option.displayName }}
            </button>
          }
        }
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

    .sign-in__hint {
      margin: 0;
      color: var(--gggs-color-text-muted, #5f6b7a);
    }

    .sign-in__or {
      margin: 0;
      text-align: center;
      color: var(--gggs-color-text-muted, #5f6b7a);
      font-size: var(--gggs-font-size-sm, 0.75rem);
    }

    .sign-in__sso {
      justify-content: center;
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
  protected readonly step = signal<'password' | 'mfa'>('password');
  protected readonly ssoOptions = signal<readonly SsoOption[]>([]);
  private challenge: string | null = null;

  protected readonly form = this.formBuilder.nonNullable.group({
    tenantCode: [''],
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
    code: [''],
  });

  constructor() {
    // Single sign-on comes back here with a second step to take, or with the reason it failed.
    const query = new URLSearchParams(window.location.search);
    const challenge = query.get('mfaChallenge');
    if (challenge) {
      this.beginMfa(challenge);
    }

    const ssoError = query.get('ssoError');
    if (ssoError) {
      this.message.set(describeSso(ssoError));
    }

    const remembered = rememberedTenant();
    if (remembered) {
      this.form.controls.tenantCode.setValue(remembered);
      void this.loadSingleSignOn();
    }
  }

  protected async loadSingleSignOn(): Promise<void> {
    const tenant = this.form.controls.tenantCode.value.trim();
    if (!tenant) {
      this.ssoOptions.set([]);
      return;
    }

    try {
      this.ssoOptions.set(await this.session.singleSignOnOptions(tenant));
    } catch {
      this.ssoOptions.set([]);
    }
  }

  protected singleSignOn(option: SsoOption): void {
    const tenant = this.form.controls.tenantCode.value.trim();
    remember(tenant);
    this.session.startSingleSignOn(
      tenant,
      option.key,
      safeReturnUrl(new URLSearchParams(window.location.search).get('returnUrl')),
    );
  }

  protected backToPassword(): void {
    this.challenge = null;
    this.step.set('password');
    this.form.controls.email.setValidators([Validators.required, Validators.email]);
    this.form.controls.password.setValidators(Validators.required);
    this.form.controls.code.clearValidators();
    this.form.controls.code.setValue('');
    this.refreshValidity();
  }

  protected async submit(): Promise<void> {
    if (this.step() === 'mfa') {
      await this.submitCode();
      return;
    }

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

      if (result.errorCode === 'MFA_REQUIRED' && result.mfaChallenge) {
        remember(tenantCode.trim());
        this.beginMfa(result.mfaChallenge);
        return;
      }

      if (!result.succeeded) {
        this.message.set(describe(result));
        return;
      }

      remember(tenantCode.trim());
      await this.finish(result);
    } catch {
      this.message.set('Sign-in is unavailable right now. Please try again.');
    } finally {
      this.isBusy.set(false);
    }
  }

  private async submitCode(): Promise<void> {
    const code = this.form.controls.code.value.trim();
    if (!code || !this.challenge || this.isBusy()) {
      this.form.controls.code.markAsTouched();
      return;
    }

    this.isBusy.set(true);
    this.message.set(null);

    try {
      const result = await this.session.completeMfa(this.challenge, code);
      if (result.succeeded) {
        await this.finish(result);
        return;
      }

      if (result.errorCode === 'MFA_CHALLENGE_EXPIRED') {
        this.backToPassword();
        this.message.set('That took too long. Sign in again.');
        return;
      }

      this.form.controls.code.setValue('');
      this.message.set(describe(result));
    } catch {
      this.message.set('Sign-in is unavailable right now. Please try again.');
    } finally {
      this.isBusy.set(false);
    }
  }

  private beginMfa(challenge: string): void {
    this.challenge = challenge;
    this.step.set('mfa');
    this.form.controls.email.clearValidators();
    this.form.controls.password.clearValidators();
    this.form.controls.code.setValidators(Validators.required);
    this.refreshValidity();
  }

  private refreshValidity(): void {
    for (const control of [this.form.controls.email, this.form.controls.password, this.form.controls.code]) {
      control.updateValueAndValidity();
    }
  }

  private async finish(result: PasswordSignInResult): Promise<void> {
    // Someone the organisation requires to use an authenticator is sent to set one up first.
    if (result.mfaEnrollmentRequired) {
      await this.router.navigateByUrl('/me/security');
      return;
    }

    // The URL the user was originally heading for, read here rather than kept in a store, so a
    // deep link survives the round trip.
    const returnUrl = new URLSearchParams(window.location.search).get('returnUrl');
    await this.router.navigateByUrl(safeReturnUrl(returnUrl));
  }
}

const TENANT_KEY = 'gggs.sign-in.tenant';

/** The organisation code, remembered on this device so its sign-in options appear next time. */
function remember(tenantCode: string): void {
  try {
    if (tenantCode) {
      localStorage.setItem(TENANT_KEY, tenantCode);
    }
  } catch {
    // Storage is blocked: the code is simply typed again next time.
  }
}

function rememberedTenant(): string | null {
  try {
    return localStorage.getItem(TENANT_KEY);
  } catch {
    return null;
  }
}

/** Why single sign-on did not finish, in words. */
function describeSso(code: string): string {
  switch (code) {
    case 'SSO_NO_ACCOUNT':
      return 'Your organisation account is not linked to a PeopleOS account yet. Ask your administrator to invite you.';
    case 'SSO_ACCOUNT_MISMATCH':
      return 'This PeopleOS account is linked to a different organisation account.';
    case 'SSO_CANCELLED':
      return 'Single sign-on was cancelled.';
    case 'ACCOUNT_LOCKED':
      return 'Too many attempts. Try again shortly.';
    default:
      return 'Single sign-on did not complete. Try again, or sign in with your password.';
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

    case 'MFA_INVALID_CODE':
      return 'That code does not match. Try the newest code from the app.';

    default:
      // One message for a wrong address, a wrong password and a tenant that does not exist. Being
      // more helpful here would tell an attacker which of the three they got right.
      return 'That email address and password do not match an account.';
  }
}
