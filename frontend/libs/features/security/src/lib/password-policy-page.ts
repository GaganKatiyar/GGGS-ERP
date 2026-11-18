import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { PageHeader, Skeleton } from '@gggs/shared/ui';
import { SecurityApi, type PasswordPolicy } from './security-api';

/**
 * The organisation's password rules. They apply to every password set from now on - invitations,
 * resets and changes - and to sign-in lockouts; nobody is made to change a password they already have.
 */
@Component({
  selector: 'gggs-password-policy-page',
  imports: [ReactiveFormsModule, PageHeader, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Password policy" subtitle="The rules every password must meet, and what happens after wrong attempts." />

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else {
      <form class="panel stack policy" [formGroup]="form" (ngSubmit)="save()">
        @if (isDefault()) {
          <p class="muted policy__note">These are the built-in defaults. Nothing has been saved for your organisation yet.</p>
        }

        <h2 class="policy__heading">Passwords</h2>
        <div class="form-grid">
          <label class="field">
            <span class="field__label">Minimum length *</span>
            <input type="number" formControlName="minimumLength" [min]="lowest()" max="128" />
            <span class="field__hint">At least {{ lowest() }}. Longer passwords are much harder to crack: 12 or more is the stronger choice.</span>
          </label>
          <label class="field">
            <span class="field__label">Remember previous passwords</span>
            <input type="number" formControlName="historyCount" min="0" max="24" />
            <span class="field__hint">How many recent passwords cannot be used again. 0 allows any.</span>
          </label>
          <label class="field">
            <span class="field__label">Passwords expire after (days)</span>
            <input type="number" formControlName="maximumAgeDays" min="0" max="3650" />
            <span class="field__hint">0 means never. Forced changes tend to produce weaker passwords (Password1, Password2…).</span>
          </label>
          <div class="field policy__checks">
            <label><input type="checkbox" formControlName="requireComplexity" /> Require upper case, lower case, a digit and a symbol</label>
            <label><input type="checkbox" formControlName="rejectBreachedPasswords" /> Refuse passwords known from data breaches</label>
          </div>
        </div>

        <h2 class="policy__heading">Wrong attempts</h2>
        <div class="form-grid">
          <label class="field">
            <span class="field__label">Lock the account after (attempts)</span>
            <input type="number" formControlName="lockoutThreshold" min="3" max="50" />
          </label>
          <label class="field">
            <span class="field__label">Keep it locked for (minutes)</span>
            <input type="number" formControlName="lockoutMinutes" min="1" max="1440" />
            <span class="field__hint">An administrator can unlock it sooner from Settings › Users.</span>
          </label>
        </div>

        <p class="muted policy__note">A password may never contain the person's name or email address. New rules apply to passwords set from now on; nobody has to change their current one.</p>

        @if (error()) {
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
        }
        @if (saved()) {
          <p class="alert alert--success" role="status"><i class="pi pi-check" aria-hidden="true"></i><span>Saved. The new rules apply from now on.</span></p>
        }

        @if (canManage) {
          <div class="toolbar">
            <button type="submit" class="btn btn--primary" [disabled]="busy()">{{ busy() ? 'Saving…' : 'Save policy' }}</button>
          </div>
        }
      </form>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .policy { max-width: 56rem; }
    .policy__heading { margin: 0; font-size: var(--gggs-font-size-md); }
    .policy__note { margin: 0; }
    .policy__checks { display: flex; flex-direction: column; gap: var(--gggs-space-2); justify-content: center; }
  `,
})
export class PasswordPolicyPage {
  private readonly api = inject(SecurityApi);

  protected readonly canManage = inject(PermissionStore).has('admin.setting.manage');
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal(false);
  protected readonly isDefault = signal(false);
  protected readonly lowest = signal(6);

  protected readonly form = new FormGroup({
    minimumLength: new FormControl(6, { nonNullable: true, validators: [Validators.required, Validators.min(6), Validators.max(128)] }),
    requireComplexity: new FormControl(false, { nonNullable: true }),
    rejectBreachedPasswords: new FormControl(true, { nonNullable: true }),
    historyCount: new FormControl(5, { nonNullable: true, validators: [Validators.min(0), Validators.max(24)] }),
    maximumAgeDays: new FormControl(0, { nonNullable: true, validators: [Validators.min(0), Validators.max(3650)] }),
    lockoutThreshold: new FormControl(5, { nonNullable: true, validators: [Validators.min(3), Validators.max(50)] }),
    lockoutMinutes: new FormControl(15, { nonNullable: true, validators: [Validators.min(1), Validators.max(1440)] }),
  });

  constructor() {
    if (!this.canManage) {
      this.form.disable();
    }
    void this.load();
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.error.set(`Check the numbers: minimum length ${this.lowest()} to 128, history 0 to 24, expiry 0 to 3650 days, 3 to 50 attempts, 1 to 1440 minutes.`);
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.saved.set(false);
    try {
      this.apply(await this.api.savePasswordPolicy(this.form.getRawValue()));
      this.saved.set(true);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The policy could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  private apply(policy: PasswordPolicy): void {
    this.isDefault.set(policy.isDefault);
    this.lowest.set(policy.lowestMinimumLength);
    this.form.controls.minimumLength.setValidators([Validators.required, Validators.min(policy.lowestMinimumLength), Validators.max(128)]);
    this.form.reset({
      minimumLength: policy.minimumLength,
      requireComplexity: policy.requireComplexity,
      rejectBreachedPasswords: policy.rejectBreachedPasswords,
      historyCount: policy.historyCount,
      maximumAgeDays: policy.maximumAgeDays,
      lockoutThreshold: policy.lockoutThreshold,
      lockoutMinutes: policy.lockoutMinutes,
    });
  }

  private async load(): Promise<void> {
    try {
      this.apply(await this.api.passwordPolicy());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The password policy could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
