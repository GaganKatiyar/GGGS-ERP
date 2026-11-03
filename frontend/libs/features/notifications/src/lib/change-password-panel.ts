import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { AdminApi } from '@gggs/shared/identity';

/** The words for a refused change: the server's own where it says which rule failed. */
export function describePasswordError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.errorCode === 'CURRENT_PASSWORD_INCORRECT') {
      return 'Your current password is not correct.';
    }

    if (['PASSWORD_REJECTED', 'PASSWORD_REUSED', 'PASSWORD_BREACHED'].includes(error.errorCode)) {
      return error.problem.detail ?? 'That password does not meet the policy. Please choose another.';
    }
  }

  return 'Your password could not be changed just now. Please try again in a minute.';
}

/** Changing your own password: this browser stays signed in, and every other sign-in ends. */
@Component({
  selector: 'gggs-change-password-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="elevated password" aria-labelledby="password-heading" novalidate (submit)="save($event)">
      <h2 id="password-heading">Change password</h2>
      <p class="muted">You stay signed in here. Anywhere else your account is signed in will be signed out.</p>
      <label class="field">
        <span class="field__label">Current password</span>
        <input [type]="show() ? 'text' : 'password'" autocomplete="current-password" [value]="current()" (input)="current.set($any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">New password</span>
        <input [type]="show() ? 'text' : 'password'" autocomplete="new-password" aria-describedby="password-hint" [value]="next()" (input)="next.set($any($event.target).value)" />
        <span id="password-hint" class="field__hint">At least 12 characters.</span>
      </label>
      <label class="password__show">
        <input type="checkbox" [checked]="show()" (change)="show.set($any($event.target).checked)" />
        Show passwords
      </label>
      <div>
        <button type="submit" class="btn btn--primary" [disabled]="busy() || !current() || !next()">{{ busy() ? 'Changing…' : 'Change password' }}</button>
      </div>
      @if (message()) {
        <p class="alert alert--success" role="status">{{ message() }}</p>
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </form>
  `,
  styles: `
    .password { display: flex; flex-direction: column; gap: var(--gggs-space-3); max-width: 36rem; padding: var(--gggs-space-5); }
    .password h2 { margin: 0; font-size: var(--gggs-font-size-lg); }
    .password p { margin: 0; }
    .password__show { display: flex; align-items: center; gap: var(--gggs-space-2); }
  `,
})
export class ChangePasswordPanel {
  private readonly api = inject(AdminApi);

  protected readonly current = signal('');
  protected readonly next = signal('');
  protected readonly show = signal(false);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy() || !this.current() || !this.next()) {
      return;
    }

    this.busy.set(true);
    this.message.set(null);
    this.error.set(null);

    try {
      await this.api.changeOwnPassword({ currentPassword: this.current(), newPassword: this.next() });
      this.current.set('');
      this.next.set('');
      this.show.set(false);
      this.message.set('Password changed. Anywhere else your account was signed in has been signed out.');
    } catch (error) {
      this.error.set(describePasswordError(error));
    } finally {
      this.busy.set(false);
    }
  }
}
