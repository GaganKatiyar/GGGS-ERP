import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { ClientsApi, ProjectsApi } from '@gggs/shared/delivery';
import type { ClientItem, ServiceLevelRow } from '@gggs/shared/models';
import { EmptyState, localIsoDate, PageHeader, Skeleton } from '@gggs/shared/ui';
import { monthBefore } from '../workspace/billing-period';

/** The monthly SLA report, per client: how each project on a policy did, and what it cost. */
@Component({
  selector: 'gggs-service-levels-page',
  imports: [DecimalPipe, RouterLink, PageHeader, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Service levels" subtitle="The month's SLA results for each project on a policy, to review or send on to the client." />
    <div class="stack">
      <div class="toolbar">
        <label class="field sla__filter">
          <span class="field__label">Month</span>
          <input type="month" [value]="month()" (change)="chooseMonth($any($event.target).value)" />
        </label>
        <label class="field sla__filter">
          <span class="field__label">Client</span>
          <select [value]="clientId()" (change)="chooseClient($any($event.target).value)">
            <option value="">All clients</option>
            @for (client of clients(); track client.id) {
              <option [value]="client.id">{{ client.name }}</option>
            }
          </select>
        </label>
        <span class="sla__spacer"></span>
        <button type="button" class="btn btn--ghost btn--sm" [disabled]="exporting() || !rows()?.length" (click)="exportCsv()"><i class="pi pi-download" aria-hidden="true"></i> CSV</button>
      </div>

      @if (rows(); as list) {
        <section class="panel sla__scroll">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">Client</th><th scope="col">Project</th><th scope="col" class="sla__num">Opened</th><th scope="col" class="sla__num">Resolved</th>
                <th scope="col" class="sla__num">Response met</th><th scope="col" class="sla__num">Resolution met</th><th scope="col" class="sla__num">Breaches</th><th scope="col" class="sla__num">Excluded</th><th scope="col" class="sla__num">Service credit</th>
              </tr>
            </thead>
            <tbody>
              @for (row of list; track row.projectId) {
                <tr>
                  <td>{{ row.clientName ?? 'No client' }}</td>
                  <th scope="row"><a [routerLink]="['/projects', row.projectId]" [queryParams]="{ tab: 'service' }"><strong>{{ row.code }}</strong> {{ row.name }}</a></th>
                  <td class="sla__num">{{ row.report.opened }}</td>
                  <td class="sla__num">{{ row.report.resolved }}</td>
                  <td class="sla__num">{{ percent(row.report.responseMetPercent) }}</td>
                  <td class="sla__num">{{ percent(row.report.resolutionMetPercent) }}</td>
                  <td class="sla__num" [class.sla__danger]="row.report.breaches > 0">{{ row.report.breaches }}</td>
                  <td class="sla__num">{{ row.report.excluded }}</td>
                  <td class="sla__num">{{ row.report.creditPercent | number: '1.0-2' }}%</td>
                </tr>
              } @empty {
                <tr><td colspan="9"><gggs-empty-state icon="pi pi-stopwatch" title="No projects on a service level policy" description="Set one up on a project's Service tab." /></td></tr>
              }
            </tbody>
          </table>
        </section>
      } @else if (!error()) {
        <gggs-skeleton [lines]="6" />
      }

      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>
  `,
  styles: `
    .sla__filter {
      min-inline-size: 12rem;
    }

    .sla__spacer {
      flex: 1;
    }

    .sla__scroll {
      overflow-x: auto;
    }

    .sla__num {
      text-align: end !important;
      font-variant-numeric: tabular-nums;
    }

    .sla__danger {
      color: var(--gggs-color-danger);
    }
  `,
})
export class ServiceLevelsPage {
  private readonly api = inject(ProjectsApi);
  private readonly clientsApi = inject(ClientsApi);

  protected readonly month = signal(monthBefore(localIsoDate()));
  protected readonly clientId = signal('');
  protected readonly clients = signal<readonly ClientItem[]>([]);
  protected readonly rows = signal<readonly ServiceLevelRow[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly exporting = signal(false);

  constructor() {
    void this.load();

    // A viewer who cannot list clients still gets the report, unfiltered.
    this.clientsApi.active().then(
      (clients) => this.clients.set(clients),
      () => undefined,
    );
  }

  protected percent(value: number | undefined): string {
    return value === undefined ? 'None' : `${value}%`;
  }

  protected chooseMonth(month: string): void {
    if (month) {
      this.month.set(month);
      void this.load();
    }
  }

  protected chooseClient(clientId: string): void {
    this.clientId.set(clientId);
    void this.load();
  }

  protected async exportCsv(): Promise<void> {
    this.exporting.set(true);
    this.error.set(null);
    try {
      await this.api.exportServiceLevels(`${this.month()}-01`, this.clientId() || null);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The report could not be exported.');
    } finally {
      this.exporting.set(false);
    }
  }

  private async load(): Promise<void> {
    const month = this.month();
    const clientId = this.clientId();
    this.rows.set(null);
    this.error.set(null);
    try {
      const rows = await this.api.serviceLevelReport(`${month}-01`, clientId || null);
      if (month === this.month() && clientId === this.clientId()) {
        this.rows.set(rows);
      }
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The report could not be loaded.');
    }
  }
}
