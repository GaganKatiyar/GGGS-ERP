import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PermissionStore } from '@gggs/core/permissions';
import { PlatformApi, statusTone, type OrganisationRow, type TenantStatus } from '@gggs/platform/data-access';
import { PageHeader, Skeleton, dateTime } from '@gggs/shared/ui';
import { PLATFORM, messageOf } from './platform-permissions';

const STATUSES: readonly TenantStatus[] = ['Active', 'Provisioning', 'Suspended', 'Offboarding', 'Archived'];

/** Every organisation, filtered by status and searched by name or code. */
@Component({
  selector: 'gggs-console-organisations-page',
  imports: [FormsModule, RouterLink, PageHeader, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Organisations" subtitle="The companies using Nuts and Bolts: their plan, size and state." />

    <div class="filter-toolbar">
      <input class="search-input" type="search" placeholder="Search by name or code" [(ngModel)]="search" (ngModelChange)="queueReload()" aria-label="Search organisations" />
      <select [(ngModel)]="status" (ngModelChange)="reload()" aria-label="Status">
        <option [ngValue]="null">All statuses</option>
        @for (option of statuses; track option) {
          <option [ngValue]="option">{{ option }}</option>
        }
      </select>
      <span class="toolbar__spacer"></span>
      @if (canCreate) {
        <a class="btn btn--primary" routerLink="/organisations/new"><i class="pi pi-plus" aria-hidden="true"></i> New organisation</a>
      }
    </div>

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (rows(); as list) {
      <div class="table-wrap panel panel--flush">
        <table class="table">
          <thead>
            <tr>
              <th>Organisation</th>
              <th>Status</th>
              <th>Plan</th>
              <th class="num">Employees</th>
              <th class="num">Users</th>
              <th>Trial ends / renews</th>
              <th>Last sign-in</th>
            </tr>
          </thead>
          <tbody>
            @for (row of list; track row.id) {
              <tr>
                <td>
                  <a [routerLink]="['/organisations', row.id]"><strong>{{ row.name }}</strong></a>
                  <div class="subtle text-xs">{{ row.code }} · region {{ row.dataRegion.toUpperCase() }}</div>
                </td>
                <td><span [class]="'badge badge--' + tone(row.status)">{{ row.status }}</span></td>
                <td>{{ row.planName ?? '—' }}@if (row.subscriptionStatus === 'Trial') { <span class="badge badge--warning">Trial</span> }</td>
                <td class="num">{{ row.usage?.employees ?? '—' }}@if (row.seatLimit) { <span class="subtle"> / {{ row.seatLimit }}</span> }</td>
                <td class="num">{{ row.usage?.users ?? '—' }}</td>
                <td class="nowrap">{{ row.trialEndsOn ? 'Trial ends ' + day(row.trialEndsOn) : row.renewsOn ? 'Renews ' + day(row.renewsOn) : '—' }}</td>
                <td class="nowrap">{{ row.usage?.lastSignIn ? when(row.usage!.lastSignIn!) : '—' }}</td>
              </tr>
            } @empty {
              <tr><td colspan="7" class="muted">No organisations match.</td></tr>
            }
          </tbody>
        </table>
      </div>
      <p class="subtle text-xs">Employees, users and sign-ins are measured every hour.</p>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .search-input { min-width: 16rem; }
  `,
})
export class ConsoleOrganisationsPage {
  private readonly api = inject(PlatformApi);

  protected readonly canCreate = inject(PermissionStore).has(PLATFORM.tenantManage);
  protected readonly statuses = STATUSES;
  protected readonly rows = signal<OrganisationRow[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected search = '';
  protected status: TenantStatus | null = null;
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    void this.reload();
  }

  protected queueReload(): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.reload(), 300);
  }

  protected async reload(): Promise<void> {
    this.error.set(null);
    try {
      this.rows.set(await this.api.organisations(this.status, this.search));
    } catch (error) {
      this.error.set(messageOf(error, 'The organisations could not be loaded.'));
    }
  }

  protected tone(status: string): string {
    return statusTone(status);
  }

  protected day(value: string): string {
    return dateTime(value, 'd MMM y');
  }

  protected when(value: string): string {
    return dateTime(value);
  }
}
