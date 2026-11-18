import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PlatformApi, type HealthView } from '@gggs/platform/data-access';
import { PageHeader, Skeleton, dateTime } from '@gggs/shared/ui';
import { messageOf } from './platform-permissions';

/** The platform's moving parts: services, scheduled jobs, failed jobs and today's email per organisation. */
@Component({
  selector: 'gggs-console-health-page',
  imports: [RouterLink, PageHeader, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="System health" subtitle="Background workers, scheduled jobs and email delivery." />

    <div class="toolbar">
      <span class="toolbar__spacer"></span>
      <button type="button" class="btn btn--secondary btn--sm" [disabled]="loading()" (click)="load()"><i class="pi pi-refresh" aria-hidden="true"></i> Refresh</button>
    </div>
    @if (notice()) {
      <p class="alert alert--success" role="status"><i class="pi pi-check" aria-hidden="true"></i><span>{{ notice() }}</span></p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (view(); as v) {
      <div class="services">
        @for (s of v.services; track s.name) {
          <section class="panel service">
            <span class="dot" [class.dot--ok]="s.healthy" aria-hidden="true"></span>
            <div>
              <strong>{{ s.name }}</strong> <span class="badge" [class.badge--success]="s.healthy" [class.badge--danger]="!s.healthy">{{ s.state }}</span>
              <p class="muted text-sm">{{ s.detail }}</p>
            </div>
          </section>
        }
      </div>

      <section class="panel panel--flush">
        <header class="panel__header"><h2 class="panel__title">Failed jobs</h2><span class="muted text-sm">{{ v.failed.length }}</span></header>
        @if (v.failed.length === 0) {
          <p class="muted pad">No failed jobs.</p>
        } @else {
          <div class="table-wrap">
            <table class="table table--compact">
              <thead><tr><th>Job</th><th>Organisation</th><th>Failed</th><th>Reason</th><th></th></tr></thead>
              <tbody>
                @for (f of v.failed; track f.id) {
                  <tr>
                    <td>{{ f.job }}<div class="subtle text-xs">#{{ f.id }}</div></td>
                    <td>
                      @if (f.tenantId) { <a [routerLink]="['/organisations', f.tenantId]">{{ f.tenantName ?? 'Organisation' }}</a> } @else { <span class="muted">Platform</span> }
                    </td>
                    <td class="nowrap">{{ f.failedAt ? when(f.failedAt) : '—' }}</td>
                    <td class="reason">{{ f.reason ?? '—' }}</td>
                    <td class="actions"><button type="button" class="btn btn--ghost btn--sm" (click)="retry(f.id)">Retry</button></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>

      <section class="panel panel--flush">
        <header class="panel__header"><h2 class="panel__title">Scheduled jobs</h2></header>
        <div class="table-wrap">
          <table class="table table--compact">
            <thead><tr><th>Job</th><th>Schedule</th><th>Last run</th><th>Result</th><th>Next run</th></tr></thead>
            <tbody>
              @for (j of v.jobs; track j.id) {
                <tr>
                  <td>{{ j.id }}</td>
                  <td><code>{{ j.cron }}</code></td>
                  <td class="nowrap">{{ j.lastExecution ? when(j.lastExecution) : 'Not yet' }}</td>
                  <td>
                    <span class="badge" [class.badge--success]="j.lastState === 'Succeeded'" [class.badge--danger]="j.lastState === 'Failed'">{{ j.lastState ?? '—' }}</span>
                    @if (j.error) { <div class="text-xs text-danger">{{ j.error }}</div> }
                  </td>
                  <td class="nowrap">{{ j.nextExecution ? when(j.nextExecution) : '—' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      <section class="panel panel--flush">
        <header class="panel__header"><h2 class="panel__title">Email today</h2></header>
        <div class="table-wrap">
          <table class="table table--compact">
            <thead><tr><th>Organisation</th><th>Goes through</th><th class="num">Sent</th><th class="num">Failed</th><th class="num">Only logged</th></tr></thead>
            <tbody>
              @for (e of v.email; track e.tenantId) {
                <tr>
                  <td><a [routerLink]="['/organisations', e.tenantId]">{{ e.name }}</a></td>
                  <td>{{ e.channel ?? '—' }}</td>
                  <td class="num">{{ e.sent }}</td>
                  <td class="num" [class.text-danger]="e.failed > 0">{{ e.failed }}</td>
                  <td class="num">{{ e.logged }}</td>
                </tr>
              } @empty {
                <tr><td colspan="5" class="muted">No figures yet.</td></tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .services { display: grid; grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr)); gap: var(--gggs-space-3); }
    .service { display: flex; gap: var(--gggs-space-3); align-items: flex-start; }
    .service p { margin: var(--gggs-space-1) 0 0; }
    .dot { flex: none; width: 0.75rem; height: 0.75rem; margin-top: 0.35rem; border-radius: 50%; background: var(--gggs-color-danger, #dc2626); }
    .dot--ok { background: var(--gggs-color-success, #16a34a); }
    .reason { max-width: 28rem; font-size: var(--gggs-font-size-xs); word-break: break-word; }
    .pad { padding: 0 var(--gggs-space-4) var(--gggs-space-4); }
  `,
})
export class ConsoleHealthPage {
  private readonly api = inject(PlatformApi);

  protected readonly view = signal<HealthView | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected when(value: string): string {
    return dateTime(value);
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.view.set(await this.api.health());
    } catch (error) {
      this.error.set(messageOf(error, 'Health could not be loaded.'));
    } finally {
      this.loading.set(false);
    }
  }

  protected async retry(id: string): Promise<void> {
    try {
      await this.api.retryJob(id);
      this.notice.set(`Job #${id} was queued again.`);
      await this.load();
    } catch (error) {
      this.error.set(messageOf(error, 'The job could not be retried.'));
    }
  }
}
