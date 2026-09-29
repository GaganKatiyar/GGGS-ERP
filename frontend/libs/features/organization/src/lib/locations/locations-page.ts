import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmptyState, PageHeader } from '@gggs/shared/ui';
import {
  LOCATION_TYPE_LABELS,
  type BranchListItem,
  type LocationListItem,
  type LocationType,
} from '@gggs/shared/models';
import { OrgReferenceApi } from '@gggs/shared/org-reference';

/**
 * Locations: a floor, a client site, or "remote" - inside a branch. Distinct from `Branch` because
 * one registered branch often has several floors or client sites, and "remote" has to be
 * representable without inventing a branch that does not exist.
 */
@Component({
  selector: 'gggs-locations-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Locations" subtitle="Floors, sites and remote work, inside a branch.">
      <button
        type="button"
        class="btn btn--primary"
        *gggsHasPermission="'org.structure.manage'"
        (click)="isFormOpen.set(!isFormOpen())"
      >
        {{ isFormOpen() ? 'Cancel' : 'New location' }}
      </button>
    </gggs-page-header>

    @if (isFormOpen()) {
      <form class="panel" [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="panel__grid">
          <label class="field">
            <span class="field__label">Code</span>
            <input type="text" formControlName="code" maxlength="32" required />
          </label>

          <label class="field">
            <span class="field__label">Name</span>
            <input type="text" formControlName="name" maxlength="200" required />
          </label>

          <label class="field">
            <span class="field__label">Branch</span>
            <select formControlName="branchId" required>
              <option value="" disabled>Choose one</option>
              @for (branch of branches(); track branch.id) {
                <option [value]="branch.id">{{ branch.name }}</option>
              }
            </select>
          </label>

          <label class="field">
            <span class="field__label">Type</span>
            <select formControlName="locationType" required>
              <option value="Office">Office</option>
              <option value="Site">Site</option>
              <option value="Remote">Remote</option>
            </select>
          </label>
        </div>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <button type="submit" class="btn btn--primary" [disabled]="isSaving()">
          {{ isSaving() ? 'Saving…' : 'Create' }}
        </button>
      </form>
    }

    @if (isLoading()) {
      <p>Loading…</p>
    } @else if (locations().length === 0) {
      <gggs-empty-state
        title="No locations yet"
        description="Every location belongs to a branch - create a branch first if none exists."
      />
    } @else {
      <table class="list-table">
        <thead>
          <tr>
            <th scope="col">Code</th>
            <th scope="col">Name</th>
            <th scope="col">Branch</th>
            <th scope="col">Type</th>
          </tr>
        </thead>
        <tbody>
          @for (location of locations(); track location.id) {
            <tr>
              <td>{{ location.code }}</td>
              <td>{{ location.name }}</td>
              <td>{{ branchName(location.branchId) }}</td>
              <td>{{ locationTypeLabel(location.locationType) }}</td>
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
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.5rem;
      background: var(--gggs-color-surface-raised, #ffffff);
    }

    .panel__grid {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
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

    .btn {
      align-self: flex-start;
      padding: 0.5rem 1rem;
      font-weight: 600;
      border-radius: 0.375rem;
      border: 1px solid transparent;
      cursor: pointer;
    }

    .btn--primary {
      background: var(--gggs-color-primary, #1d4ed8);
      color: #fff;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
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
    }
  `,
})
export class LocationsPage {
  private readonly api = inject(OrgReferenceApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly isFormOpen = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly isLoading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly locations = signal<readonly LocationListItem[]>([]);

  // Fetched in full for the dropdown's sake - same reasoning as BranchesPage's legalEntities list.
  protected readonly branches = signal<readonly BranchListItem[]>([]);

  protected readonly form = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(32)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    branchId: ['', Validators.required],
    locationType: ['Office' as LocationType, Validators.required],
  });

  constructor() {
    void this.load();
  }

  protected branchName(id: string): string {
    return this.branches().find((branch) => branch.id === id)?.name ?? id;
  }

  protected locationTypeLabel(type: LocationType): string {
    return LOCATION_TYPE_LABELS[type];
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      const [branches, locations] = await Promise.all([
        this.api.listBranches(),
        this.api.listLocations(),
      ]);

      this.branches.set(branches);
      this.locations.set(locations);
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

    try {
      await this.api.createLocation(this.form.getRawValue());
      this.form.reset({ locationType: 'Office' });
      this.isFormOpen.set(false);
      this.locations.set(await this.api.listLocations());
    } catch (error) {
      this.errorMessage.set(describeError(error));
    } finally {
      this.isSaving.set(false);
    }
  }
}

export function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.errorCode === 'ORGANIZATION_CODE_IN_USE') {
      return 'That code is already in use.';
    }

    if (error.status === 404) {
      return 'The chosen branch could not be found.';
    }
  }

  return 'The location could not be created. Please try again.';
}
