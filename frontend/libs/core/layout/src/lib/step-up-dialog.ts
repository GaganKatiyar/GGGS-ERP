import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { SessionStore, StepUpPrompt } from '@gggs/core/auth';
import { ApiError } from '@gggs/core/http';

/** Loaded on first use (deferred in the shell), so it costs the initial bundle nothing. */
/** The confirmation itself: an authenticator code, or the password for someone without one. */
@Component({
  selector: 'gggs-step-up-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (prompt.open()) {
      <div class="step-up__backdrop">
        <form class="step-up" role="dialog" aria-modal="true" aria-labelledby="step-up-title" (submit)="$event.preventDefault(); confirm()">
          <h2 id="step-up-title" class="step-up__title">Confirm it's you</h2>
          <p class="step-up__text">This action is protected. {{ mode() === 'code' ? 'Enter the code from your authenticator app.' : 'Enter your password.' }}</p>
          @if (mode() === 'code') {
            <input aria-label="Authenticator code" autocomplete="one-time-code" inputmode="numeric" maxlength="7" [value]="value()" (input)="value.set($any($event.target).value)" />
          } @else {
            <input aria-label="Password" type="password" autocomplete="current-password" [value]="value()" (input)="value.set($any($event.target).value)" />
          }
          @if (error()) {
            <p class="step-up__error" role="alert">{{ error() }}</p>
          }
          <button type="button" class="btn--link step-up__switch" (click)="switchMode()">
            {{ mode() === 'code' ? 'I have no authenticator app - use my password' : 'Use an authenticator code' }}
          </button>
          <div class="step-up__actions">
            <button type="button" class="btn" (click)="cancel()">Cancel</button>
            <button type="submit" class="btn btn--primary" [disabled]="busy() || !value()">Confirm</button>
          </div>
        </form>
      </div>
    }
  `,
  styles: `
    .step-up__backdrop {
      position: fixed;
      inset: 0;
      z-index: 1100;
      display: grid;
      place-items: center;
      background: rgb(0 0 0 / 40%);
      padding: 1rem;
    }
    .step-up {
      display: grid;
      gap: 0.75rem;
      width: min(24rem, 100%);
      padding: 1.5rem;
      border-radius: var(--gggs-radius-lg, 0.75rem);
      background: var(--gggs-color-surface-raised, #fff);
      box-shadow: var(--gggs-shadow-md, none);
    }
    .step-up__title {
      margin: 0;
      font-size: var(--gggs-font-size-lg, 1.1rem);
    }
    .step-up__text,
    .step-up__error {
      margin: 0;
    }
    .step-up__error {
      color: var(--gggs-color-danger, #b3261e);
    }
    .step-up__switch {
      justify-self: start;
    }
    .step-up__actions {
      display: flex;
      justify-content: flex-end;
      gap: 0.5rem;
    }
  `,
})
export class StepUpDialog {
  protected readonly prompt = inject(StepUpPrompt);
  private readonly session = inject(SessionStore);
  protected readonly mode = signal<'code' | 'password'>('code');
  protected readonly value = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected switchMode(): void {
    this.mode.set(this.mode() === 'code' ? 'password' : 'code');
    this.value.set('');
    this.error.set(null);
  }

  protected cancel(): void {
    this.reset();
    this.prompt.finish(false);
  }

  protected async confirm(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.session.stepUp(this.mode() === 'code' ? { code: this.value() } : { password: this.value() });
      this.reset();
      this.prompt.finish(true);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private reset(): void {
    this.value.set('');
    this.error.set(null);
  }
}
