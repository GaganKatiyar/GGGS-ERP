import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { AdminApi } from '@gggs/shared/identity';
import { EmptyState, PageHeader } from '@gggs/shared/ui';
import type { TenantSettingItem } from '@gggs/shared/models';

/**
 * Tenant settings: a flat module + key + JSON value store
 * (`Gggs.Platform.Api.Endpoints.TenantSettingEndpoints`).
 *
 * No module has a defined settings schema yet - every value here is opaque JSON the server only
 * checks parses, not what shape it takes - so this screen is deliberately a raw editor rather than
 * a form per module. The first module to actually need typed settings gets its own screen for
 * them; until then, this is what "administration" has to offer for tenant configuration.
 */
@Component({
  selector: 'gggs-tenant-settings-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Settings" subtitle="Tenant-level configuration, by module and key.">
      <button
        type="button"
        class="btn btn--primary"
        *gggsHasPermission="'admin.setting.manage'"
        (click)="isFormOpen.set(!isFormOpen())"
      >
        {{ isFormOpen() ? 'Cancel' : 'Add or update setting' }}
      </button>
    </gggs-page-header>

    @if (isFormOpen()) {
      <form class="panel" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="panel__grid">
          <label class="field">
            <span class="field__label">Module</span>
            <input type="text" formControlName="module" maxlength="50" required />
          </label>

          <label class="field">
            <span class="field__label">Key</span>
            <input type="text" formControlName="key" maxlength="100" required />
          </label>
        </div>

        <label class="field">
          <span class="field__label">Value (JSON)</span>
          <textarea formControlName="valueJson" rows="4" required></textarea>
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <button type="submit" class="btn btn--primary" [disabled]="isSaving()">
          {{ isSaving() ? 'Saving…' : 'Save' }}
        </button>
      </form>
    }

    @if (isLoading()) {
      <p>Loading…</p>
    } @else if (settings().length === 0) {
      <gggs-empty-state
        title="No settings yet"
        description="Nothing has been configured for this tenant."
      />
    } @else {
      <table class="list-table">
        <thead>
          <tr>
            <th scope="col">Module</th>
            <th scope="col">Key</th>
            <th scope="col">Value</th>
          </tr>
        </thead>
        <tbody>
          @for (setting of settings(); track setting.module + '.' + setting.key) {
            <tr>
              <td>{{ setting.module }}</td>
              <td>{{ setting.key }}</td>
              <td><code>{{ setting.valueJson }}</code></td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
  styles: `
    .panel {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      padding: 1rem;
      margin-block-end: 1rem;
      max-width: 32rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised, #ffffff);
    }

    .panel__grid {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
    }

    .field__label {
      font-weight: 600;
    }

    textarea {
      font-family: ui-monospace, monospace;
      font-size: 0.8125rem;
    }

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .list-table {
      width: 100%;
      border-collapse: collapse;
    }

    .list-table th,
    .list-table td {
      padding: 0.5rem 0.75rem;
      text-align: left;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      vertical-align: top;
    }

    .list-table code {
      font-size: 0.8125rem;
      word-break: break-all;
    }

    .btn {
      align-self: flex-start;
      padding: 0.5rem 1rem;
      font-weight: 600;
      font-family: inherit;
      font-size: inherit;
      border-radius: 0.375rem;
      border: 1px solid transparent;
      cursor: pointer;
    }

    .btn--primary {
      background: var(--gggs-color-primary, #2f5fd6);
      color: #fff;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
  `,
})
export class TenantSettingsPage {
  private readonly api = inject(AdminApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly isFormOpen = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly isLoading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly settings = signal<readonly TenantSettingItem[]>([]);

  protected readonly form = this.formBuilder.nonNullable.group({
    module: ['', [Validators.required, Validators.maxLength(50)]],
    key: ['', [Validators.required, Validators.maxLength(100)]],
    valueJson: ['', Validators.required],
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      this.settings.set(await this.api.listSettings());
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const { module, key, valueJson } = this.form.getRawValue();

    if (!isValidJson(valueJson)) {
      this.errorMessage.set('The value must be valid JSON, for example "hello" or {"days": 5}.');
      this.isSaving.set(false);
      return;
    }

    try {
      await this.api.upsertSetting(module, key, { valueJson });
      this.form.reset();
      this.isFormOpen.set(false);
      await this.load();
    } catch {
      this.errorMessage.set('The setting could not be saved. Please try again.');
    } finally {
      this.isSaving.set(false);
    }
  }
}

export function isValidJson(value: string): boolean {
  try {
    JSON.parse(value);
    return true;
  } catch {
    return false;
  }
}
