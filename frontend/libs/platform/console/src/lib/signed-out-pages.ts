import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { SessionStore, safeReturnUrl } from '@gggs/core/auth';
import { PlatformApi } from '@gggs/platform/data-access';
import { messageOf } from './platform-permissions';

/** The frame every signed-out console page sits in. */
@Component({
  selector: 'gggs-console-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="gate">
      <section class="gate__card panel stack">
        <header class="gate__brand">
          <span class="gate__logo" aria-hidden="true"><i class="pi pi-cog"></i></span>
          <span><strong>Nuts and Bolts</strong><small>Platform console</small></span>
        </header>
        <h1 class="gate__title">{{ heading() }}</h1>
        @if (lead()) {
          <p class="muted gate__lead">{{ lead() }}</p>
        }
        <ng-content />
      </section>
      <p class="gate__foot subtle text-xs">For GGGS staff. Organisations sign in to the HR application, not here.</p>
    </div>
  `,
  styles: `
    :host { display: block; }
    .gate { min-height: 100vh; display: grid; place-content: center; gap: var(--gggs-space-3); padding: var(--gggs-space-4); background: linear-gradient(160deg, #1e1b4b, #312e81); }
    .gate__card { width: min(26rem, calc(100vw - 2rem)); box-sizing: border-box; }
    .gate__brand { display: flex; gap: var(--gggs-space-3); align-items: center; }
    .gate__brand small { display: block; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    .gate__logo { display: grid; place-items: center; width: 2.25rem; height: 2.25rem; border-radius: var(--gggs-radius-md); background: #4f46e5; color: #fff; }
    .gate__title { margin: var(--gggs-space-2) 0 0; font-size: var(--gggs-font-size-xl); }
    .gate__lead { margin: 0; }
    .gate__foot { margin: 0; text-align: center; color: #c7d2fe; }
  `,
})
export class ConsoleCard {
  readonly heading = input.required<string>();
  readonly lead = input<string | undefined>(undefined);
}

/** Password sign-in for platform staff. No organisation code: staff belong to none. */
@Component({
  selector: 'gggs-console-sign-in-page',
  imports: [ReactiveFormsModule, RouterLink, ConsoleCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-console-card heading="Sign in" lead="Use your GGGS platform staff account.">
      <form class="stack" [formGroup]="form" (ngSubmit)="submit()">
        <label class="field">
          <span class="field__label">Email</span>
          <input type="email" formControlName="email" autocomplete="username" />
        </label>
        <label class="field">
          <span class="field__label">Password</span>
          <span class="pw">
            <input [type]="showPassword() ? 'text' : 'password'" formControlName="password" autocomplete="current-password" />
            <button type="button" class="auth__eye" [attr.aria-pressed]="showPassword()" [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'" (click)="showPassword.set(!showPassword())">
              <i [class]="showPassword() ? 'pi pi-eye-slash' : 'pi pi-eye'" aria-hidden="true"></i>
            </button>
          </span>
        </label>
        @if (error()) {
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
        }
        <button type="submit" class="btn btn--primary" [disabled]="busy()">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
        @if (setupOpen()) {
          <p class="muted text-sm">First time here? <a routerLink="/setup">Create the first administrator</a>.</p>
        }
      </form>
    </gggs-console-card>
  `,
  styles: `
    .pw { position: relative; display: block; }
    .pw input { width: 100%; box-sizing: border-box; padding-inline-end: 2.75rem; }
  `,
})
export class ConsoleSignInPage {
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly api = inject(PlatformApi);

  readonly returnUrl = input<string | undefined>(undefined);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly showPassword = signal(false);
  protected readonly setupOpen = signal(false);

  protected readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  constructor() {
    this.api.setupOpen().then((state) => this.setupOpen.set(state.open), () => this.setupOpen.set(false));
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.error.set('Enter your email address and password.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const result = await this.session.signInWithPassword(this.form.getRawValue());
      if (!result.succeeded) {
        this.error.set(
          result.errorCode === 'ACCOUNT_LOCKED'
            ? 'Too many wrong attempts. The account is locked for a while; another administrator can unlock it.'
            : 'That email address and password do not match a platform staff account.',
        );
        return;
      }
      await this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
    } catch (error) {
      this.error.set(messageOf(error, 'Sign-in failed. Try again.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** First-run setup: the very first platform administrator creates their account with the setup key. */
@Component({
  selector: 'gggs-console-setup-page',
  imports: [ReactiveFormsModule, RouterLink, ConsoleCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-console-card heading="Set up the platform console" lead="Create the first platform administrator. This page closes for good once any platform account exists.">
      @if (open() === false) {
        <p class="alert alert--warning"><i class="pi pi-lock" aria-hidden="true"></i><span>Setup is closed: platform staff already exist, or no setup key is configured on the server.</span></p>
        <a class="btn btn--secondary" routerLink="/sign-in">Go to sign in</a>
      } @else if (done()) {
        <p class="alert alert--success" role="status"><i class="pi pi-check" aria-hidden="true"></i><span>Your administrator account is ready. Sign in with it now.</span></p>
        <a class="btn btn--primary" routerLink="/sign-in">Sign in</a>
      } @else {
        <form class="stack" [formGroup]="form" (ngSubmit)="submit()">
          <label class="field">
            <span class="field__label">Setup key *</span>
            <input type="password" formControlName="setupKey" autocomplete="off" />
            <span class="field__hint">From the platform server's configuration (Gggs:Identity:PlatformBootstrap:SetupKey).</span>
          </label>
          <label class="field">
            <span class="field__label">Your name *</span>
            <input formControlName="displayName" autocomplete="name" />
          </label>
          <label class="field">
            <span class="field__label">Email *</span>
            <input type="email" formControlName="email" autocomplete="username" />
          </label>
          <label class="field">
            <span class="field__label">Password *</span>
            <input type="password" formControlName="password" autocomplete="new-password" />
            <span class="field__hint">At least 6 characters. It must not contain your name or email address.</span>
          </label>
          @if (error()) {
            <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
          }
          <button type="submit" class="btn btn--primary" [disabled]="busy()">{{ busy() ? 'Creating…' : 'Create administrator' }}</button>
        </form>
      }
    </gggs-console-card>
  `,
})
export class ConsoleSetupPage {
  private readonly api = inject(PlatformApi);

  protected readonly open = signal<boolean | null>(null);
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    setupKey: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    displayName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200)] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  constructor() {
    this.api.setupOpen().then((state) => this.open.set(state.open), () => this.open.set(false));
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.error.set('Fill in every field with a valid email address.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.setup(this.form.getRawValue());
      this.done.set(true);
    } catch (error) {
      this.error.set(messageOf(error, 'Setup failed.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Where a new staff member's one-time link lands: they choose their password. */
@Component({
  selector: 'gggs-console-accept-invitation-page',
  imports: [ReactiveFormsModule, RouterLink, ConsoleCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-console-card heading="Join the platform team" lead="Choose a password for your platform staff account.">
      @if (!valid()) {
        <p class="alert alert--danger"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>This link is incomplete. Ask whoever invited you for a new one.</span></p>
      } @else if (done()) {
        <p class="alert alert--success" role="status"><i class="pi pi-check" aria-hidden="true"></i><span>Your account is ready.</span></p>
        <a class="btn btn--primary" routerLink="/sign-in">Sign in</a>
      } @else {
        <form class="stack" [formGroup]="form" (ngSubmit)="submit()">
          <label class="field">
            <span class="field__label">New password *</span>
            <input type="password" formControlName="password" autocomplete="new-password" />
            <span class="field__hint">At least 6 characters. It must not contain your name or email address.</span>
          </label>
          <label class="field">
            <span class="field__label">Type it again *</span>
            <input type="password" formControlName="confirm" autocomplete="new-password" />
          </label>
          @if (error()) {
            <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
          }
          <button type="submit" class="btn btn--primary" [disabled]="busy()">{{ busy() ? 'Saving…' : 'Set password' }}</button>
        </form>
      }
    </gggs-console-card>
  `,
})
export class ConsoleAcceptInvitationPage {
  private readonly api = inject(PlatformApi);

  readonly id = input<string | undefined>(undefined);
  readonly token = input<string | undefined>(undefined);

  protected readonly valid = computed(() => !!this.id() && !!this.token());
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    confirm: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected async submit(): Promise<void> {
    const { password, confirm } = this.form.getRawValue();
    if (!password || password !== confirm) {
      this.error.set('The two passwords must match.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.completeInvitation(this.id() ?? '', this.token() ?? '', password);
      this.done.set(true);
    } catch (error) {
      this.error.set(messageOf(error, 'That invitation is no longer valid.'));
    } finally {
      this.busy.set(false);
    }
  }
}
