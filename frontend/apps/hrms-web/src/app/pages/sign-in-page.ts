import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import {
  SessionStore,
  safeReturnUrl,
  type PasswordSignInResult,
  type SsoOption,
} from '@gggs/core/auth';
import { Button, FormField } from '@gggs/shared/ui';
import { SignInShowcase } from './sign-in-showcase';

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
 * The organisation's single sign-on providers are the other way in: they leave for the provider and
 * come back signed in (or with the reason they did not).
 */
@Component({
  selector: 'gggs-sign-in-page',
  imports: [ReactiveFormsModule, FormField, Button, SignInShowcase],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sign-in">
      <gggs-sign-in-showcase />
      <main class="sign-in__main">
        <form
          class="sign-in__form rise-in"
          [formGroup]="form"
          (ngSubmit)="submit()"
          novalidate
        >
          <h1 class="sign-in__title">{{ returning ? 'Welcome back' : 'Sign in' }}</h1>

          @if (changingTenant()) {
            <gggs-form-field
              label="Organisation code"
              hint="The short code your organisation was given, for example acme."
              hintId="tenant-hint"
            >
              <input
                #tenantInput
                type="text"
                formControlName="tenantCode"
                autocomplete="organization"
                autocapitalize="none"
                spellcheck="false"
                [attr.aria-describedby]="'tenant-hint'"
                (blur)="loadSingleSignOn()"
              />
            </gggs-form-field>
          } @else {
            <div class="sign-in__org">
              <span>Organisation: <strong>{{ form.controls.tenantCode.value }}</strong></span>
              <button type="button" class="btn btn--link btn--sm" aria-label="Change organisation" (click)="changeTenant()">Change</button>
            </div>
          }

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

          <div class="sign-in__password">
            <gggs-form-field label="Password" required>
              <input
                [type]="showPassword() ? 'text' : 'password'"
                formControlName="password"
                autocomplete="current-password"
                required
              />
            </gggs-form-field>
            <button type="button" class="sign-in__eye" [attr.aria-pressed]="showPassword()" aria-label="Show password" (click)="showPassword.set(!showPassword())">
              {{ showPassword() ? 'Hide' : 'Show' }}
            </button>
          </div>

          @if (message()) {
            <!-- assertive: a failed sign-in must reach a screen reader immediately, because the
                 person cannot proceed until they have heard it. -->
            <p class="auth__error" role="alert" aria-live="assertive">
              {{ message() }}
            </p>
          }

          <gggs-button type="submit" [loading]="isBusy()" [fullWidth]="true">
            {{ isBusy() ? 'Signing in…' : 'Sign in' }}
          </gggs-button>

          @if (ssoOptions().length) {
            <p class="sign-in__or">or</p>
            @for (option of ssoOptions(); track option.key) {
              <button type="button" class="btn sign-in__sso" (click)="singleSignOn(option)">
                <i class="pi pi-key" aria-hidden="true"></i> Sign in with {{ option.displayName }}
              </button>
            }
          }

          <p class="sign-in__help">Trouble signing in? Ask your HR team.</p>
        </form>
        <p class="sign-in__maker">Powered by GGGS Technologies Pvt Ltd</p>
      </main>
    </div>
  `,
  styles: `
    .sign-in {
      display: grid;
      grid-template-columns: minmax(0, 1.25fr) minmax(0, 1fr);
      min-height: 100dvh;
      background: var(--gggs-color-surface-raised);

      @media (max-width: 60rem) {
        grid-template-columns: minmax(0, 1fr);
        align-content: start;
      }
    }

    .sign-in__main {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--gggs-space-6);
      min-width: 0;
      padding: var(--gggs-space-8) var(--gggs-space-6);

      @media (max-width: 60rem) {
        justify-content: flex-start;
        padding: var(--gggs-space-6) var(--gggs-space-5);
      }
    }

    .sign-in__form {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-4);
      width: 100%;
      max-width: 22.25rem;
    }

    .sign-in__title {
      margin: 0;
      font-family: var(--gggs-font-family-display);
      font-size: 1.6rem;
      font-weight: var(--gggs-font-weight-bold);
      letter-spacing: -0.02em;
    }

    .sign-in__org {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--gggs-space-2);
      min-height: 2.625rem;
      padding: 0.25rem var(--gggs-space-3);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-sunken);
      overflow-wrap: anywhere;
    }

    .sign-in__password {
      position: relative;

      input {
        padding-inline-end: 3.75rem;
      }
    }

    .sign-in__eye {
      position: absolute;
      inset-block-end: 0.3rem;
      inset-inline-end: 0.3rem;
      height: 1.875rem;
      padding: 0 var(--gggs-space-2);
      border: 0;
      border-radius: var(--gggs-radius-sm);
      background: transparent;
      color: var(--gggs-color-text-muted);
      font: inherit;
      font-size: var(--gggs-font-size-sm);
      font-weight: var(--gggs-font-weight-medium);
      cursor: pointer;

      &:hover {
        background: var(--gggs-color-surface-hover);
        color: var(--gggs-color-text);
      }

      &:focus-visible {
        outline: var(--gggs-focus-outline);
      }
    }

    .sign-in__help,
    .sign-in__maker {
      margin: 0;
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-sm);
    }

    .sign-in__maker {
      text-align: center;
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
  `,
})
export class SignInPage {
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);
  private readonly injector = inject(Injector);
  private readonly tenantInput = viewChild<ElementRef<HTMLInputElement>>('tenantInput');

  protected readonly isBusy = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly ssoOptions = signal<readonly SsoOption[]>([]);
  protected readonly showPassword = signal(false);

  /** Someone this device has seen before: their organisation is one line, with a way to change it. */
  protected readonly returning = !!rememberedTenant();
  protected readonly changingTenant = signal(!this.returning);

  protected readonly form = this.formBuilder.nonNullable.group({
    tenantCode: [''],
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  constructor() {
    // Single sign-on comes back here with the reason when it did not finish.
    const ssoError = new URLSearchParams(window.location.search).get('ssoError');
    if (ssoError) {
      this.message.set(describeSso(ssoError));
    }

    const remembered = rememberedTenant();
    if (remembered) {
      this.form.controls.tenantCode.setValue(remembered);
      void this.loadSingleSignOn();
    }
  }

  protected changeTenant(): void {
    this.changingTenant.set(true);
    afterNextRender(() => this.tenantInput()?.nativeElement.focus(), { injector: this.injector });
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

      remember(tenantCode.trim());

      // The URL the user was originally heading for, read here rather than kept in a store, so a
      // deep link survives the round trip.
      const returnUrl = new URLSearchParams(window.location.search).get('returnUrl');
      await this.router.navigateByUrl(safeReturnUrl(returnUrl));
    } catch {
      this.message.set('Sign-in is unavailable right now. Please try again.');
    } finally {
      this.isBusy.set(false);
    }
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
      return 'Your organisation account is not linked to a Nuts and Bolts account yet. Ask your administrator to invite you.';
    case 'SSO_ACCOUNT_MISMATCH':
      return 'This Nuts and Bolts account is linked to a different organisation account.';
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

    default:
      // One message for a wrong address, a wrong password and a tenant that does not exist. Being
      // more helpful here would tell an attacker which of the three they got right.
      return 'That email address and password do not match an account.';
  }
}
