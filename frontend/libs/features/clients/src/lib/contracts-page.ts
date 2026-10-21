import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective, PermissionStore } from '@gggs/core/permissions';
import { DataTable, DataTableCell, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { ContractsApi, isoToday } from '@gggs/shared/delivery';
import type { ContractDetail, ContractItem, ContractStatus, ContractType, PagedResult } from '@gggs/shared/models';
import { BILLING_MODEL_LABELS, CONTRACT_TYPE_LABELS } from '@gggs/shared/models';
import { Drawer, FormDialog, PageHeader, StatusBadge } from '@gggs/shared/ui';
import { ContractForm } from './contract-form';

/** Contracts (spec §38): every MSA, SOW, PO, SLA and retainer, with what is up for renewal. */
@Component({
  selector: 'gggs-contracts-page',
  imports: [DatePipe, DecimalPipe, RouterLink, HasPermissionDirective, PageHeader, DataTable, DataTableCell, StatusBadge, Drawer, FormDialog, ContractForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Contracts" subtitle="Agreements with clients: terms, value and renewals.">
      <div class="toolbar">
        <a class="btn" routerLink="/clients">Clients</a>
        <button type="button" class="btn btn--primary" *gggsHasPermission="'contract.contract.manage'" (click)="edit(null)">New contract</button>
      </div>
    </gggs-page-header>

    <section class="panel toolbar contracts__filters">
      <label class="field contracts__search">
        <span class="field__label">Search</span>
        <input type="search" placeholder="Number or title" [value]="search()" (input)="onSearch($any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Type</span>
        <select [value]="type()" (change)="setType($any($event.target).value)">
          <option value="">Any</option>
          @for (option of types; track option) {
            <option [value]="option">{{ typeLabels[option] }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="setStatus($any($event.target).value)">
          <option value="">Any</option>
          <option value="Draft">Draft</option>
          <option value="PendingApproval">Pending approval</option>
          <option value="Active">Active</option>
          <option value="Expired">Expired</option>
          <option value="Terminated">Terminated</option>
        </select>
      </label>
      <label class="field">
        <span><input type="checkbox" [checked]="renewals()" (change)="toggleRenewals()" /> Renewal due in 30 days</span>
      </label>
    </section>

    <gggs-data-table [result]="result()" [columns]="columns" [loading]="loading()" exportName="contracts" emptyTitle="No contracts" (queryChange)="onQuery($event)" (rowClick)="show($event)">
      <ng-template gggsCell="number" let-row><strong>{{ row.number }}</strong> <span class="muted">{{ row.title }}</span></ng-template>
      <ng-template gggsCell="type" let-row>{{ typeLabel(row.type) }}</ng-template>
      <ng-template gggsCell="startDate" let-row>{{ row.startDate | date: 'd MMM y' }} – {{ row.endDate ? (row.endDate | date: 'd MMM y') : 'open' }}</ng-template>
      <ng-template gggsCell="value" let-row>{{ row.value === undefined || row.value === null ? '—' : row.currency + ' ' + (row.value | number: '1.0-0') }}</ng-template>
      <ng-template gggsCell="status" let-row>
        <gggs-status-badge [status]="row.status" />
        @if (row.daysToRenewal !== undefined && row.daysToRenewal !== null && row.daysToRenewal <= 30) {
          <span class="muted contracts__renewal">renewal in {{ row.daysToRenewal }}d</span>
        }
      </ng-template>
    </gggs-data-table>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-drawer [(visible)]="drawerOpen" [title]="selected()?.contract?.number ?? ''">
      @if (selected(); as detail) {
        <div class="stack">
          <div class="toolbar">
            <gggs-status-badge [status]="detail.contract.status" />
            <span class="contracts__spacer"></span>
            @if (canManage) {
              @if (detail.contract.status !== 'Terminated' && detail.contract.status !== 'PendingApproval') {
                <button type="button" class="btn btn--sm" (click)="edit(detail.contract)">Edit</button>
              }
              @if (detail.contract.status === 'Draft') {
                <button type="button" class="btn btn--primary btn--sm" (click)="activate(detail.contract)">Activate</button>
              }
              @if (detail.contract.status === 'Active' || detail.contract.status === 'Expired') {
                <button type="button" class="btn btn--danger btn--sm" (click)="terminateOpen.set(true)">Terminate</button>
              }
            }
          </div>
          <h3 class="contracts__title">{{ detail.contract.title }}</h3>
          <dl class="contracts__facts">
            <dt>Client</dt>
            <dd><a [routerLink]="['/clients', detail.contract.clientId]">{{ detail.contract.clientName }}</a></dd>
            <dt>Type</dt>
            <dd>{{ typeLabels[detail.contract.type] }}{{ detail.contract.parentNumber ? ' under ' + detail.contract.parentNumber : '' }}</dd>
            <dt>Term</dt>
            <dd>{{ detail.contract.startDate | date: 'd MMM y' }} – {{ detail.contract.endDate ? (detail.contract.endDate | date: 'd MMM y') : 'open-ended' }}{{ detail.contract.autoRenew ? ' · renews itself' : '' }}</dd>
            <dt>Value</dt>
            <dd>{{ detail.contract.value === undefined ? 'Not set' : detail.contract.currency + ' ' + (detail.contract.value | number: '1.0-0') }} · {{ billingLabels[detail.contract.billingModel] }}</dd>
            <dt>Payment</dt>
            <dd>{{ detail.contract.paymentTermsDays !== undefined ? detail.contract.paymentTermsDays + ' days' : '—' }} {{ detail.contract.paymentTerms ?? '' }}</dd>
            <dt>Tax</dt>
            <dd>{{ detail.contract.taxTerms ?? '—' }}</dd>
            <dt>Renewal due</dt>
            <dd>{{ detail.contract.renewalDate ? (detail.contract.renewalDate | date: 'd MMM y') : '—' }}</dd>
            <dt>Owner</dt>
            <dd>{{ detail.contract.ownerName ?? '—' }}</dd>
            @if (detail.contract.terminatedOn) {
              <dt>Terminated</dt>
              <dd>{{ detail.contract.terminatedOn | date: 'd MMM y' }} - {{ detail.contract.terminationReason }}</dd>
            }
          </dl>
          @if (detail.contract.notes) {
            <p>{{ detail.contract.notes }}</p>
          }
          @if (detail.children.length > 0) {
            <h4 class="contracts__subhead">Under this agreement</h4>
            @for (child of detail.children; track child.id) {
              <button type="button" class="contracts__child" (click)="load(child.id)">
                <strong>{{ child.number }}</strong> {{ child.title }} <gggs-status-badge [status]="child.status" />
              </button>
            }
          }
          @if (message()) {
            <p class="contracts__message" role="status">{{ message() }}</p>
          }
        </div>
      }
    </gggs-drawer>

    <gggs-contract-form [(visible)]="formOpen" [contract]="editing()" (saved)="saved($event)" />

    <gggs-form-dialog [(visible)]="terminateOpen" title="Terminate the contract" saveLabel="Terminate" [busy]="busy()" [error]="dialogError()" (save)="terminate()">
      <div class="stack">
        <label class="field">
          <span class="field__label">Effective</span>
          <input type="date" [value]="terminateOn()" (input)="terminateOn.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Reason *</span>
          <textarea rows="3" maxlength="500" [value]="terminateReason()" (input)="terminateReason.set($any($event.target).value)"></textarea>
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .contracts__filters {
      margin-block-end: var(--gggs-space-3);
      flex-wrap: wrap;
      align-items: flex-end;
    }

    .contracts__search {
      flex: 1;
      min-width: 14rem;
    }

    .contracts__renewal {
      margin-inline-start: var(--gggs-space-2);
      font-size: var(--gggs-font-size-xs);
    }

    .contracts__spacer {
      flex: 1;
    }

    .contracts__title {
      margin: 0;
    }

    .contracts__facts {
      display: grid;
      grid-template-columns: 9rem minmax(0, 1fr);
      gap: var(--gggs-space-2);
      margin: 0;
    }

    .contracts__facts dt {
      color: var(--gggs-color-text-muted);
    }

    .contracts__facts dd {
      margin: 0;
    }

    .contracts__subhead {
      margin: var(--gggs-space-2) 0 0;
    }

    .contracts__child {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
      padding: var(--gggs-space-2);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: transparent;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .contracts__message {
      color: var(--gggs-color-success);
    }
  `,
})
export class ContractsPage {
  private readonly api = inject(ContractsApi);

  /** Bound from the `open` query parameter: a link from a notification or a client page. */
  readonly open = input<string | undefined>(undefined);

  protected readonly typeLabels = CONTRACT_TYPE_LABELS;
  protected readonly billingLabels = BILLING_MODEL_LABELS;
  protected readonly types = Object.keys(CONTRACT_TYPE_LABELS) as ContractType[];
  protected readonly canManage = inject(PermissionStore).has('contract.contract.manage');
  protected readonly columns: readonly DataTableColumn<ContractItem>[] = [
    { field: 'number', header: 'Contract', sortable: true },
    { field: 'clientName', header: 'Client' },
    { field: 'type', header: 'Type', width: '9rem' },
    { field: 'startDate', header: 'Term', sortable: true },
    { field: 'value', header: 'Value', sortable: true, align: 'right' },
    { field: 'ownerName', header: 'Owner', hidden: true },
    { field: 'status', header: 'Status', sortable: true },
  ];

  protected readonly result = signal<PagedResult<ContractItem> | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly type = signal<ContractType | ''>('');
  protected readonly status = signal<ContractStatus | ''>('');
  protected readonly renewals = signal(false);
  protected readonly selected = signal<ContractDetail | null>(null);
  protected readonly drawerOpen = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly editing = signal<ContractItem | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly terminateOpen = signal(false);
  protected readonly terminateOn = signal(isoToday());
  protected readonly terminateReason = signal('');
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);
  private query: DataTableQuery = { page: 1, pageSize: 25 };
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    void this.refresh();
    queueMicrotask(() => {
      const open = this.open();
      if (open) {
        void this.load(open);
      }
    });
  }

  protected typeLabel(type: string): string {
    return CONTRACT_TYPE_LABELS[type as ContractType] ?? type;
  }

  protected onQuery(query: DataTableQuery): void {
    this.query = query;
    void this.refresh();
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.query = { ...this.query, page: 1 };
      void this.refresh();
    }, 300);
  }

  protected setType(value: ContractType | ''): void {
    this.type.set(value);
    void this.refresh();
  }

  protected setStatus(value: ContractStatus | ''): void {
    this.status.set(value);
    void this.refresh();
  }

  protected toggleRenewals(): void {
    this.renewals.update((value) => !value);
    void this.refresh();
  }

  protected show(contract: ContractItem): void {
    void this.load(contract.id);
  }

  protected edit(contract: ContractItem | null): void {
    this.editing.set(contract);
    this.formOpen.set(true);
  }

  protected async saved(detail: ContractDetail): Promise<void> {
    this.selected.set(detail);
    this.drawerOpen.set(true);
    await this.refresh();
  }

  protected async activate(contract: ContractItem): Promise<void> {
    this.message.set(null);
    try {
      const result = await this.api.activate(contract.id);
      this.message.set(result.status === 'PendingApproval' ? 'Sent for approval.' : `The contract is ${result.status.toLowerCase()}.`);
      await this.load(contract.id);
      await this.refresh();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The contract could not be activated.');
    }
  }

  protected async terminate(): Promise<void> {
    const contract = this.selected()?.contract;
    if (!contract || !this.terminateReason().trim()) {
      this.dialogError.set('Give a reason for terminating.');
      return;
    }

    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await this.api.terminate(contract.id, this.terminateOn(), this.terminateReason().trim());
      this.terminateOpen.set(false);
      await this.load(contract.id);
      await this.refresh();
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The contract could not be terminated.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async load(id: string): Promise<void> {
    try {
      this.selected.set(await this.api.get(id));
      this.drawerOpen.set(true);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The contract could not be loaded.');
    }
  }

  private async refresh(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.result.set(
        await this.api.list({
          search: this.search(),
          type: this.type(),
          status: this.status(),
          renewalWithinDays: this.renewals() ? 30 : undefined,
          page: this.query.page,
          pageSize: this.query.pageSize,
          sort: this.query.sort,
        }),
      );
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Contracts could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
