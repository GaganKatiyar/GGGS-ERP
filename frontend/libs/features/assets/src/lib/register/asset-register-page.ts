import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmployeePicker } from '@gggs/shared/employee-reference';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { Asset, AssetCategory, EmployeeListItem } from '@gggs/shared/models';
import { AssetsApi } from '../data/assets-api';

/** The asset register: every asset, its status, and admin actions (send for repair, return to
 * stock, retire, assign to an employee, return from an employee). No maintenance history or
 * software licenses in this v1 slice. */
@Component({
  selector: 'gggs-asset-register-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, EmployeePicker, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Assets" subtitle="Register assets, assign them to employees, and track their status." />

    <gggs-card title="Register" class="section">
      @if (assets(); as items) {
        @if (items.length > 0) {
          <table class="assets-table">
            <thead>
              <tr>
                <th scope="col">Tag</th>
                <th scope="col">Make / Model</th>
                <th scope="col">Serial</th>
                <th scope="col">Status</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              @for (asset of items; track asset.id) {
                <tr [class.selected]="selectedAssetId() === asset.id">
                  <td>{{ asset.assetTag }}</td>
                  <td>{{ asset.make }} {{ asset.model }}</td>
                  <td>{{ asset.serialNumber ?? '—' }}</td>
                  <td>{{ asset.status }}</td>
                  <td class="asset-actions" *gggsHasPermission="'asset.asset.manage'">
                    @if (asset.status === 'InStock') {
                      <gggs-button size="sm" variant="ghost" (click)="selectForAssignment(asset.id)">Assign</gggs-button>
                      <gggs-button size="sm" variant="ghost" (click)="sendForRepair(asset.id)" [disabled]="isActing()">Send for repair</gggs-button>
                      <gggs-button size="sm" variant="ghost" (click)="retire(asset.id)" [disabled]="isActing()">Retire</gggs-button>
                    }
                    @if (asset.status === 'InRepair') {
                      <gggs-button size="sm" variant="ghost" (click)="returnFromRepair(asset.id)" [disabled]="isActing()">Return to stock</gggs-button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No assets registered yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }

      @if (selectedAssetId()) {
        <div class="assign-panel">
          <span class="field__label" id="assign-employee-label">Assign to</span>
          <gggs-employee-picker aria-labelledby="assign-employee-label" (employeeSelected)="assign($event)" />
        </div>
      }

      <form
        *gggsHasPermission="'asset.asset.manage'"
        [formGroup]="form"
        (ngSubmit)="submit()"
        novalidate
        class="create-form"
      >
        <label class="field">
          <span class="field__label">Category</span>
          <select formControlName="categoryId" required>
            <option value="" disabled>Select a category</option>
            @for (category of activeCategories(); track category.id) {
              <option [value]="category.id">{{ category.name }}</option>
            }
          </select>
        </label>

        <label class="field">
          <span class="field__label">Asset tag</span>
          <input type="text" formControlName="assetTag" maxlength="30" required />
        </label>

        <label class="field">
          <span class="field__label">Make</span>
          <input type="text" formControlName="make" maxlength="100" required />
        </label>

        <label class="field">
          <span class="field__label">Model</span>
          <input type="text" formControlName="model" maxlength="100" required />
        </label>

        <label class="field">
          <span class="field__label">Serial number</span>
          <input type="text" formControlName="serialNumber" maxlength="100" />
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Registering…' : 'Register asset' }}
        </gggs-button>
      </form>
    </gggs-card>
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .assets-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .assets-table th,
    .assets-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .assets-table tr.selected {
      background: var(--gggs-color-surface-sunken, #f5f6f8);
    }

    .asset-actions {
      display: flex;
      gap: 0.5rem;
      flex-wrap: wrap;
    }

    .assign-panel {
      margin-block-end: 1rem;
      padding: 0.75rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.375rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 1rem;
    }

    .create-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
      flex-wrap: wrap;
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

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }
  `,
})
export class AssetRegisterPage {
  private readonly api = inject(AssetsApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly assets = signal<readonly Asset[] | null>(null);
  protected readonly categories = signal<readonly AssetCategory[]>([]);
  protected readonly selectedAssetId = signal<string | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly isActing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly activeCategories = () => this.categories().filter((c) => c.isActive);

  protected readonly form = this.formBuilder.group({
    categoryId: ['', Validators.required],
    assetTag: ['', [Validators.required, Validators.maxLength(30)]],
    make: ['', [Validators.required, Validators.maxLength(100)]],
    model: ['', [Validators.required, Validators.maxLength(100)]],
    serialNumber: [''],
  });

  constructor() {
    void this.load();
    void this.loadCategories();
  }

  protected selectForAssignment(assetId: string): void {
    this.selectedAssetId.set(this.selectedAssetId() === assetId ? null : assetId);
  }

  protected async assign(employee: EmployeeListItem): Promise<void> {
    const assetId = this.selectedAssetId();
    if (!assetId || this.isActing()) {
      return;
    }

    await this.act(() => this.api.assign(assetId, { employeeId: employee.id }));
    this.selectedAssetId.set(null);
  }

  protected async sendForRepair(id: string): Promise<void> {
    await this.act(() => this.api.sendForRepair(id));
  }

  protected async returnFromRepair(id: string): Promise<void> {
    await this.act(() => this.api.returnFromRepair(id));
  }

  protected async retire(id: string): Promise<void> {
    await this.act(() => this.api.retire(id));
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      await this.api.createAsset({
        categoryId: value.categoryId ?? '',
        assetTag: value.assetTag ?? '',
        make: value.make ?? '',
        model: value.model ?? '',
        serialNumber: value.serialNumber || undefined,
      });

      this.form.reset();
      await this.load();
    } catch {
      this.errorMessage.set('The asset could not be registered. A tag may already be in use.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async act(run: () => Promise<unknown>): Promise<void> {
    if (this.isActing()) {
      return;
    }

    this.isActing.set(true);

    try {
      await run();
      await this.load();
    } finally {
      this.isActing.set(false);
    }
  }

  private async load(): Promise<void> {
    this.assets.set(await this.api.listAssets());
  }

  private async loadCategories(): Promise<void> {
    this.categories.set(await this.api.listCategories());
  }
}
