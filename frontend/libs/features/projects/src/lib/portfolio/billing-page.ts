import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import type { BillingPreview, BillingRun } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { MoneyPipe, ProjectFinancialsApi } from '@gggs/shared/money';
import { EmptyState, KpiCard, localIsoDate, PageHeader, Skeleton } from '@gggs/shared/ui';
import { monthBefore, monthBounds } from '../workspace/billing-period';
import { PrepareInvoiceDialog } from '../workspace/prepare-invoice-dialog';

/** The month's billing across projects: what is ready to invoice, what is overdue, and the accounting system's copy. */
@Component({
  selector: 'gggs-billing-page',
  imports: [DatePipe, RouterLink, MoneyPipe, PageHeader, KpiCard, EmptyState, Skeleton, PrepareInvoiceDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Billing" subtitle="What is ready to invoice across your projects, and what is overdue." />
    <div class="stack">
      <div class="toolbar">
        <label class="field bill__month">
          <span class="field__label">Month</span>
          <input type="month" [value]="month()" (change)="chooseMonth($any($event.target).value)" />
        </label>
      </div>

      @if (run(); as r) {
        <div class="xp__kpis">
          <gggs-kpi-card label="Projects to invoice" [value]="readyCount()" icon="pi pi-file-edit" />
          <gggs-kpi-card label="Drafts not sent" [value]="r.drafts" />
          <gggs-kpi-card label="Overdue invoices" [value]="r.overdue.length" />
        </div>

        <section class="panel bill__scroll">
          <h3 class="bill__heading">Ready to invoice</h3>
          <table class="table">
            <thead>
              <tr>
                <th scope="col">Project</th><th scope="col">Engagement</th><th scope="col" class="bill__num">Ready</th><th scope="col">Needs a look</th><th scope="col"><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (row of r.ready; track row.projectId) {
                <tr>
                  <th scope="row"><a [routerLink]="['/projects', row.projectId]" [queryParams]="{ tab: 'financials' }"><strong>{{ row.code }}</strong> {{ row.name }}</a></th>
                  <td>{{ humanize(row.billingType) }}</td>
                  <td class="bill__num">{{ row.amount | gggsMoney: row.currency }}</td>
                  <td class="muted">{{ notes(row) }}</td>
                  <td class="bill__num">
                    @if (canManage() && row.lines.length) {
                      <button type="button" class="btn btn--link btn--sm" (click)="prepare(row)">Prepare</button>
                    }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="5"><gggs-empty-state icon="pi pi-check-circle" title="Nothing waiting to be invoiced for this month" /></td></tr>
              }
            </tbody>
          </table>
        </section>

        <section class="panel bill__scroll">
          <h3 class="bill__heading">Overdue</h3>
          <table class="table">
            <thead>
              <tr><th scope="col">Invoice</th><th scope="col">Project</th><th scope="col">Was due</th><th scope="col" class="bill__num">Days over</th><th scope="col" class="bill__num">Owed</th></tr>
            </thead>
            <tbody>
              @for (row of r.overdue; track row.invoiceId) {
                <tr>
                  <th scope="row">{{ row.number }}</th>
                  <td><a [routerLink]="['/projects', row.projectId]" [queryParams]="{ tab: 'financials' }">{{ row.projectCode }}</a></td>
                  <td>{{ row.dueDate | date: 'd MMM y' }}</td>
                  <td class="bill__num bill__danger">{{ row.daysOverdue }}</td>
                  <td class="bill__num">{{ row.amount | gggsMoney: row.currency }}</td>
                </tr>
              } @empty {
                <tr><td colspan="5" class="muted">Nothing is overdue.</td></tr>
              }
            </tbody>
          </table>
        </section>
      } @else if (!error()) {
        <gggs-skeleton [lines]="8" />
      }

      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }

      <section class="panel stack">
        <h3 class="bill__heading">Export for accounts</h3>
        <p class="muted bill__note">
          Invoices and credit notes dated in the month, for the projects you can see. Drafts and invoices awaiting approval stay out. Each client's name must match its ledger name in Tally.
          GST filing and e-invoicing stay in your accounting system.
        </p>
        <div class="bill__grid">
          <label class="field">
            <span class="field__label">Tally company</span>
            <input type="text" maxlength="100" [value]="company()" (input)="company.set($any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">Sales ledger</span>
            <input type="text" maxlength="100" [value]="salesLedger()" (input)="salesLedger.set($any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">Tax ledger</span>
            <input type="text" maxlength="100" [value]="taxLedger()" (input)="taxLedger.set($any($event.target).value)" />
          </label>
        </div>
        <div class="toolbar">
          <button type="button" class="btn btn--sm" [disabled]="exporting() || !company().trim()" (click)="exportAs('tally')"><i class="pi pi-download" aria-hidden="true"></i> Tally XML</button>
          <button type="button" class="btn btn--ghost btn--sm" [disabled]="exporting()" (click)="exportAs('csv')"><i class="pi pi-download" aria-hidden="true"></i> CSV</button>
        </div>
      </section>
    </div>

    @if (target(); as t) {
      <gggs-prepare-invoice-dialog [(visible)]="preparing" [projectId]="t.projectId" [month]="month()" (prepared)="load()" />
    }
  `,
  styles: `
    .bill__month {
      max-inline-size: 12rem;
    }

    .bill__scroll {
      overflow-x: auto;
    }

    .bill__heading {
      margin: 0 0 var(--gggs-space-2);
      font-size: var(--gggs-font-size-md);
    }

    .bill__note {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .bill__num {
      text-align: end !important;
      font-variant-numeric: tabular-nums;
    }

    .bill__danger {
      color: var(--gggs-color-danger);
    }

    .bill__grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      gap: var(--gggs-space-3);
    }
  `,
})
export class BillingPage {
  private readonly api = inject(ProjectFinancialsApi);
  private readonly permissions = inject(PermissionStore);

  protected readonly humanize = humanize;
  protected readonly canManage = computed(() => this.permissions.has('project.financials.manage'));
  protected readonly month = signal(monthBefore(localIsoDate()));
  protected readonly run = signal<BillingRun | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly target = signal<BillingPreview | null>(null);
  protected readonly preparing = signal(false);
  protected readonly company = signal('');
  protected readonly salesLedger = signal('Sales');
  protected readonly taxLedger = signal('Output GST');
  protected readonly exporting = signal(false);
  protected readonly readyCount = computed(() => this.run()?.ready.filter((row) => row.lines.length > 0).length ?? 0);

  constructor() {
    void this.load();
  }

  protected chooseMonth(month: string): void {
    if (month) {
      this.month.set(month);
      void this.load();
    }
  }

  protected prepare(row: BillingPreview): void {
    this.target.set(row);
    this.preparing.set(true);
  }

  protected notes(row: BillingPreview): string {
    const notes: string[] = [];
    if (row.heldByCap > 0) {
      notes.push('Some held back by the cap');
    }
    if (row.creditsWaiting > 0) {
      notes.push('Service credits waiting');
    }
    if (row.unpricedHours > 0) {
      notes.push(`${row.unpricedHours} h with no rate`);
    }
    if (row.typedInvoices.length) {
      notes.push(`Check against ${row.typedInvoices.join(', ')}`);
    }
    return notes.join(' · ');
  }

  protected async exportAs(format: 'tally' | 'csv'): Promise<void> {
    const { from, to } = monthBounds(this.month());
    this.exporting.set(true);
    this.error.set(null);
    try {
      await this.api.exportInvoices({ from, to, format, company: this.company().trim(), salesLedger: this.salesLedger().trim(), taxLedger: this.taxLedger().trim() });
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The export could not be made.');
    } finally {
      this.exporting.set(false);
    }
  }

  protected async load(): Promise<void> {
    const month = this.month();
    this.run.set(null);
    this.error.set(null);
    try {
      const run = await this.api.billingRun(`${month}-01`);
      if (month === this.month()) {
        this.run.set(run);
      }
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Billing could not be loaded.');
    }
  }
}
