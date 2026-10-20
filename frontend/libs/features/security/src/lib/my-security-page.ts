import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { PageHeader, StatusBadge } from '@gggs/shared/ui';
import { type MfaSetup, type MfaStatus, SecurityApi } from './security-api';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/**
 * The signed-in person's own sign-in security: an authenticator app for a second step at sign-in,
 * and one-time recovery codes for when the phone is not to hand.
 */
@Component({
  selector: 'gggs-my-security-page',
  imports: [DatePipe, PageHeader, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Sign-in security" subtitle="Protect your account with an authenticator app" />
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }

    @if (status(); as s) {
      <section class="panel stack sec">
        <div class="sec__head">
          <h2 class="sec__title">Authenticator app</h2>
          <gggs-status-badge [status]="s.enabled ? 'On' : 'Off'" [variants]="{ On: 'success', Off: s.required ? 'danger' : 'neutral' }" />
        </div>
        @if (s.required && !s.enabled) {
          <p class="sec__warn" role="status"><i class="pi pi-exclamation-triangle" aria-hidden="true"></i> Your organisation requires an authenticator app for your account. Set one up now.</p>
        }

        @if (s.enabled) {
          <p>Turned on {{ s.enrolledDate | date: 'd MMM y' }}. Signing in asks for a six-digit code from the app after your password. {{ s.recoveryCodesLeft }} recovery code(s) left.</p>
          <div class="field sec__code">
            <label class="field__label" for="sec-code">Code from the app (or a recovery code to switch off)</label>
            <input id="sec-code" autocomplete="one-time-code" inputmode="text" maxlength="12" [value]="code()" (input)="code.set($any($event.target).value)" />
          </div>
          <div class="toolbar">
            <button type="button" class="btn" [disabled]="busy() || !code().trim()" (click)="regenerate()">New recovery codes</button>
            @if (!s.required) {
              <button type="button" class="btn btn--danger" [disabled]="busy() || !code().trim()" (click)="disable()">Turn off</button>
            }
          </div>
        } @else if (setup(); as pending) {
          <ol class="stack sec__steps">
            <li>Open your authenticator app (Microsoft Authenticator, Google Authenticator, Authy, 1Password…) and add an account.</li>
            <li>
              Enter this key, or on your phone <a [href]="pending.provisioningUri">open it in the app</a>:
              <code class="sec__secret">{{ grouped() }}</code>
            </li>
            <li>
              Type the six-digit code it shows:
              <div class="toolbar sec__confirm">
                <input aria-label="Six-digit code" autocomplete="one-time-code" inputmode="numeric" maxlength="7" [value]="code()" (input)="code.set($any($event.target).value)" />
                <button type="button" class="btn btn--primary" [disabled]="busy() || code().trim().length < 6" (click)="confirm()">Turn on</button>
              </div>
            </li>
          </ol>
        } @else {
          <p>Add a second step to signing in, so a stolen password alone is not enough.</p>
          <div class="toolbar">
            <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="begin()">Set up authenticator app</button>
          </div>
        }
      </section>
    }

    @if (recoveryCodes().length) {
      <section class="panel stack sec">
        <h2 class="sec__title">Your recovery codes</h2>
        <p>Each one signs you in once if you lose your phone. Store them somewhere safe - they will not be shown again.</p>
        <ul class="sec__codes">
          @for (item of recoveryCodes(); track item) {
            <li><code>{{ item }}</code></li>
          }
        </ul>
        <div class="toolbar">
          <button type="button" class="btn" (click)="copy()">{{ copied() ? 'Copied' : 'Copy all' }}</button>
          <button type="button" class="btn btn--primary" (click)="recoveryCodes.set([])">I have saved them</button>
        </div>
      </section>
    }
  `,
  styles: `
    .sec {
      max-width: 44rem;
      margin-block-end: var(--gggs-space-3);
    }
    .sec__head {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .sec__title {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }
    .sec__warn {
      display: flex;
      gap: var(--gggs-space-2);
      color: var(--gggs-color-danger, #b91c1c);
    }
    .sec__steps {
      padding-inline-start: 1.25rem;
    }
    .sec__secret {
      display: block;
      margin-block: var(--gggs-space-2);
      font-size: 1.1rem;
      letter-spacing: 0.08em;
      word-break: break-all;
    }
    .sec__confirm input,
    .sec__code input {
      max-width: 12rem;
    }
    .sec__codes {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(8rem, 1fr));
      gap: var(--gggs-space-2);
      list-style: none;
      padding: 0;
      margin: 0;
    }
  `,
})
export class MySecurityPage {
  private readonly api = inject(SecurityApi);
  protected readonly status = signal<MfaStatus | null>(null);
  protected readonly setup = signal<MfaSetup | null>(null);
  protected readonly recoveryCodes = signal<readonly string[]>([]);
  protected readonly code = signal('');
  protected readonly busy = signal(false);
  protected readonly copied = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly grouped = computed(() => (this.setup()?.secret.match(/.{1,4}/g) ?? []).join(' '));

  constructor() {
    void this.load();
  }

  protected begin(): Promise<void> {
    return this.run(async () => {
      this.setup.set(await this.api.setup());
      this.code.set('');
    });
  }

  protected confirm(): Promise<void> {
    return this.run(async () => {
      this.recoveryCodes.set((await this.api.confirm(this.code())).recoveryCodes);
      this.setup.set(null);
      this.code.set('');
      await this.load();
    });
  }

  protected regenerate(): Promise<void> {
    return this.run(async () => {
      this.recoveryCodes.set((await this.api.regenerate(this.code())).recoveryCodes);
      this.code.set('');
      await this.load();
    });
  }

  protected disable(): Promise<void> {
    return this.run(async () => {
      await this.api.disable(this.code());
      this.code.set('');
      await this.load();
    });
  }

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.recoveryCodes().join('\n'));
      this.copied.set(true);
    } catch {
      this.copied.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.status.set(await this.api.mfa());
    } catch (error) {
      this.error.set(message(error, 'Your sign-in security could not be loaded.'));
    }
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await work();
    } catch (error) {
      this.error.set(message(error, 'That did not work. Try again.'));
    } finally {
      this.busy.set(false);
    }
  }
}
