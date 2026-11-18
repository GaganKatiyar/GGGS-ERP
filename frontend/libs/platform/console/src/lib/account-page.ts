import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { SessionStore } from '@gggs/core/auth';
import { PlatformApi } from '@gggs/platform/data-access';
import { PageHeader } from '@gggs/shared/ui';
import { messageOf } from './platform-permissions';

/** Changing your own console password. Every other session of yours is signed out. */
@Component({
  selector: 'gggs-console-account-page',
  imports: [ReactiveFormsModule, PageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Change password" [subtitle]="session.user()?.email ?? ''" />
    <form class="panel stack account" [formGroup]="form" (ngSubmit)="save()">
      <label class="field">
        <span class="field__label">Current password *</span>
        <input type="password" formControlName="current" autocomplete="current-password" />
      </label>
      <label class="field">
        <span class="field__label">New password *</span>
        <input type="password" formControlName="next" autocomplete="new-password" />
        <span class="field__hint">At least 6 characters, not one of your last 10, and not containing your name or email address.</span>
      </label>
      <label class="field">
        <span class="field__label">Type it again *</span>
        <input type="password" formControlName="confirm" autocomplete="new-password" />
      </label>
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
      @if (saved()) {
        <p class="alert alert--success" role="status"><i class="pi pi-check" aria-hidden="true"></i><span>Password changed. Your other sessions were signed out.</span></p>
      }
      <div class="toolbar"><button type="submit" class="btn btn--primary" [disabled]="busy()">{{ busy() ? 'Saving…' : 'Change password' }}</button></div>
    </form>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .account { max-width: 30rem; }
  `,
})
export class ConsoleAccountPage {
  private readonly api = inject(PlatformApi);
  protected readonly session = inject(SessionStore);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal(false);
  protected readonly form = new FormGroup({
    current: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    next: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    confirm: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected async save(): Promise<void> {
    const { current, next, confirm } = this.form.getRawValue();
    if (!current || !next || next !== confirm) {
      this.error.set('Fill in every field; the two new passwords must match.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.saved.set(false);
    try {
      await this.api.changeOwnPassword(current, next);
      this.saved.set(true);
      this.form.reset();
    } catch (error) {
      this.error.set(messageOf(error, 'The password could not be changed.'));
    } finally {
      this.busy.set(false);
    }
  }
}
