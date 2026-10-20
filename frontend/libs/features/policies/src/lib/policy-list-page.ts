import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { DataTable, DataTableCell, type DataTableColumn } from '@gggs/shared/data-table';
import type { PagedResult, PolicyItem } from '@gggs/shared/models';
import { POLICY_CATEGORY_LABELS } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, StatusBadge } from '@gggs/shared/ui';
import { PoliciesApi } from './policies-api';
import { PolicyFields, policyForm, toPolicyInput } from './policy-form';

const today = () => new Date().toISOString().slice(0, 10);

/** Every company policy, with the version in effect and any draft in progress. */
@Component({
  selector: 'gggs-policy-list-page',
  imports: [HasPermissionDirective, PageHeader, DataTable, DataTableCell, StatusBadge, EmptyState, FormDialog, PolicyFields],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Policies" subtitle="Write, version and publish company policies; see who has read them.">
      <div class="toolbar">
        <label class="policies__retired"><input type="checkbox" [checked]="includeRetired()" (change)="toggleRetired()" /> Show retired</label>
        <button type="button" class="btn btn--primary" *gggsHasPermission="'policy.policy.manage'" (click)="openCreate()">
          <i class="pi pi-plus" aria-hidden="true"></i> New policy
        </button>
      </div>
    </gggs-page-header>

    @if (error()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="Policies could not be loaded" [description]="error()!" />
    } @else {
      <gggs-data-table
        [result]="result()"
        [columns]="columns"
        [loading]="loading()"
        exportName="policies"
        emptyTitle="No policies yet"
        emptyDescription="Create your first policy to publish it to employees."
        (rowClick)="open($event)"
      >
        <ng-template gggsCell="title" let-row>
          <strong>{{ row.title }}</strong>
          @if (!row.isActive) {
            <span class="muted"> · retired</span>
          }
        </ng-template>
        <ng-template gggsCell="category" let-row>{{ categoryLabel(row) }}</ng-template>
        <ng-template gggsCell="publishedVersion" let-row>{{ row.publishedVersion ? 'v' + row.publishedVersion : '—' }}</ng-template>
        <ng-template gggsCell="openVersionStatus" let-row>
          @if (row.openVersionStatus) {
            <gggs-status-badge [status]="row.openVersionStatus" />
          }
        </ng-template>
      </gggs-data-table>
    }

    <gggs-form-dialog [(visible)]="createOpen" title="New policy" size="lg" saveLabel="Create draft" [busy]="busy()" [error]="formError()" (save)="create()">
      <div class="stack">
        <gggs-policy-fields [form]="form" />
        <label class="field">
          <span class="field__label">Text (Markdown) *</span>
          <textarea rows="10" [value]="content()" (input)="content.set($any($event.target).value)"></textarea>
        </label>
        <label class="field policies__date">
          <span class="field__label">Takes effect</span>
          <input type="date" [value]="effectiveFrom()" (input)="effectiveFrom.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .policies__retired {
      font-size: var(--gggs-font-size-sm);
    }

    .policies__date {
      max-width: 14rem;
    }
  `,
})
export class PolicyListPage {
  private readonly api = inject(PoliciesApi);
  private readonly router = inject(Router);

  protected readonly categories = POLICY_CATEGORY_LABELS;
  protected readonly columns: readonly DataTableColumn<PolicyItem>[] = [
    { field: 'code', header: 'Code', width: '8rem' },
    { field: 'title', header: 'Policy' },
    { field: 'category', header: 'Category' },
    { field: 'audience', header: 'Applies to' },
    { field: 'publishedVersion', header: 'In effect', width: '7rem' },
    { field: 'effectiveFrom', header: 'Since', format: 'date', width: '8rem' },
    { field: 'openVersionStatus', header: 'Next version', width: '10rem' },
  ];

  protected readonly result = signal<PagedResult<PolicyItem> | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly includeRetired = signal(false);

  protected readonly createOpen = signal(false);
  protected readonly form = policyForm();
  protected readonly content = signal('');
  protected readonly effectiveFrom = signal(today());
  protected readonly busy = signal(false);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected categoryLabel(row: PolicyItem): string {
    return this.categories[row.category];
  }

  protected toggleRetired(): void {
    this.includeRetired.update((value) => !value);
    void this.load();
  }

  protected open(row: PolicyItem): void {
    void this.router.navigate(['/policies', row.id]);
  }

  protected openCreate(): void {
    this.form.reset();
    this.content.set('# Title\n\nWrite the policy here.');
    this.effectiveFrom.set(today());
    this.formError.set(null);
    this.createOpen.set(true);
  }

  protected async create(): Promise<void> {
    if (this.form.invalid || !this.content().trim()) {
      this.formError.set('Fill in the code, title and text.');
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      const created = await this.api.create(toPolicyInput(this.form, { content: this.content(), effectiveFrom: this.effectiveFrom() }));
      this.createOpen.set(false);
      void this.router.navigate(['/policies', created.policy.id]);
    } catch (error) {
      this.formError.set(error instanceof ApiError ? error.message : 'The policy could not be created.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const policies = await this.api.list(this.includeRetired());
      this.result.set({ items: policies, page: 1, pageSize: policies.length || 1, totalCount: policies.length, totalPages: 1, hasNextPage: false });
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Try again in a moment.');
    } finally {
      this.loading.set(false);
    }
  }
}
