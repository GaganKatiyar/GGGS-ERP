import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, VERSION, computed, inject, signal } from '@angular/core';
import { AdminApi } from '@gggs/shared/identity';
import type { AboutInfo } from '@gggs/shared/models';
import { KpiCard, Tabs, type TabItem } from '@gggs/shared/ui';
import {
  ACCESS,
  BACKEND,
  BUILDING_BLOCKS,
  DECISIONS,
  FRONTEND,
  FRONTEND_LIBRARIES,
  ISOLATION_LAYERS,
  MODULE_LAYOUT,
  MODULE_RULES,
  OPERATIONS,
  TESTING,
} from './about-content';

/** How long the API has been up, to the nearest minute: "3 d 4 h", "2 h 15 min", "5 min". */
export function formatUptime(startedAt: string, serverTime: string): string {
  const minutes = Math.max(0, Math.floor((Date.parse(serverTime) - Date.parse(startedAt)) / 60_000));
  const days = Math.floor(minutes / 1440);
  const hours = Math.floor((minutes % 1440) / 60);
  if (days > 0) return `${days} d ${hours} h`;
  if (hours > 0) return `${hours} h ${minutes % 60} min`;
  return `${minutes} min`;
}

/**
 * Settings > Platform > About Nuts and Bolts: what the platform is built with, how it fits together, and
 * the build and health of the deployment the user is looking at. The explanations are fixed text
 * (about-content.ts); the build, environment, module list and database state come from
 * `/api/v1/about`, so they are never stale.
 */
@Component({
  selector: 'gggs-about-page',
  imports: [NgTemplateOutlet, KpiCard, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="about-hero">
      <h1>About Nuts and Bolts</h1>
      <p>A multi-tenant HR, project and resource management platform: one modular monolith on .NET with one SQL Server database, and an Angular single-page app in the browser.</p>
      <div class="about-hero__chips" aria-live="polite">
        @if (about(); as info) {
          <span class="about-chip">Version {{ info.version }}@if (shortCommit()) { · build {{ shortCommit() }}}</span>
          <span class="about-chip">Environment: {{ info.environment }}</span>
          <span class="about-chip" [class.about-chip--bad]="!info.databaseReachable">
            <i class="pi" [class.pi-check-circle]="info.databaseReachable" [class.pi-exclamation-triangle]="!info.databaseReachable" aria-hidden="true"></i>
            {{ info.databaseReachable ? 'API and database healthy' : 'Database unreachable' }}
          </span>
        } @else if (failed()) {
          <span class="about-chip about-chip--bad"><i class="pi pi-exclamation-triangle" aria-hidden="true"></i> The API did not answer</span>
        } @else {
          <span class="about-chip">Checking the API…</span>
        }
      </div>
    </section>

    <div class="xp__kpis">
      <gggs-kpi-card label="Business modules" icon="pi pi-th-large" [value]="about()?.modules?.length ?? null" hint="Running in one API process" />
      <gggs-kpi-card label="Server runtime" icon="pi pi-server" [value]="about()?.runtime ?? null" [hint]="about()?.operatingSystem ?? ''" />
      <gggs-kpi-card label="Angular" icon="pi pi-desktop" [value]="angular" hint="In this browser" />
      <gggs-kpi-card label="Database" icon="pi pi-database" [value]="databaseLabel()" hint="SQL Server, one schema per module" />
      <gggs-kpi-card label="API uptime" icon="pi pi-clock" [value]="uptime()" hint="Since the last restart" />
    </div>

    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />

    @switch (tab()) {
      @case ('architecture') {
        <div class="grid-2">
          <section class="panel">
            <div><h2>How the system fits together</h2><p class="muted">One deployable backend, one database, and a single-page app in the browser.</p></div>
            <div class="about-tier"><span>Browser</span><div class="about-box about-box--primary"><b>Angular single-page app</b><small>PrimeNG components and signals. An httpOnly session cookie, so no tokens in the browser.</small></div></div>
            <div class="about-tier"><span>Gggs.Api</span>
              <div class="about-box about-box--brand">
                <b>One ASP.NET Core process</b>
                <small>/bff/* session · /connect/* OpenID Connect · /api/v1/* module endpoints</small>
                <ul class="about-mods" aria-label="Modules">
                  @for (name of about()?.modules ?? []; track name) { <li>{{ name }}</li> }
                </ul>
              </div>
            </div>
            <div class="about-tier"><span>Data</span><div class="about-box"><b>SQL Server · Redis · file storage</b><small>One AppDbContext with a schema per module and row-level security; HybridCache; disk or Azure Blob per tenant.</small></div></div>
            <div class="about-tier"><span>Gggs.Worker</span><div class="about-box"><b>Background process</b><small>Hangfire jobs (accruals, payroll runs, reminders) and the outbox dispatcher for integration events.</small></div></div>
          </section>
          <section class="panel">
            <div><h2>Module rules</h2><p class="muted">The architecture tests check these on every build.</p></div>
            <ol class="about-rules">
              @for (rule of rules; track $index) { <li>{{ rule }}</li> }
            </ol>
            <h2>Inside a module</h2>
            <ng-container *ngTemplateOutlet="table; context: { head: ['Folder', 'Holds'], rows: moduleLayout }" />
          </section>
        </div>
      }
      @case ('frontend') {
        <div class="grid-2">
          @for (group of frontend; track group.title; let first = $first) {
            <section class="panel">
              <ng-container *ngTemplateOutlet="stack; context: { $implicit: group }" />
              @if (!first) {
                <ng-container *ngTemplateOutlet="table; context: { head: ['Library', 'Contents'], rows: frontendLibraries }" />
              }
            </section>
          }
        </div>
      }
      @case ('backend') {
        <div class="grid-2">
          <section class="panel"><ng-container *ngTemplateOutlet="stack; context: { $implicit: backend }" /></section>
          <section class="panel">
            <div><h2>Building blocks</h2><p class="muted">Cross-cutting libraries every module shares.</p></div>
            <ng-container *ngTemplateOutlet="table; context: { head: ['Block', 'Purpose'], rows: buildingBlocks }" />
          </section>
        </div>
      }
      @case ('security') {
        <div class="grid-2">
          <section class="panel">
            <div><h2>Tenant isolation: four independent layers</h2><p class="muted">Each customer’s data stays theirs even if one layer has a bug.</p></div>
            <ol class="about-rules">
              @for (layer of isolation; track layer.title) { <li><b>{{ layer.title }}</b><br /><span class="muted">{{ layer.note }}</span></li> }
            </ol>
          </section>
          <section class="panel">
            <div><h2>Sign-in and access</h2><p class="muted">How users are authenticated and authorised.</p></div>
            <ng-container *ngTemplateOutlet="table; context: { head: ['Area', 'How'], rows: access }" />
          </section>
        </div>
      }
      @case ('operations') {
        <div class="grid-3">
          @for (group of operations; track group.title) {
            <section class="panel"><ng-container *ngTemplateOutlet="stack; context: { $implicit: group }" /></section>
          }
          <section class="panel">
            <div><h2>Testing and quality</h2><p class="muted">The safety net on every change.</p></div>
            <ng-container *ngTemplateOutlet="table; context: { head: ['Suite', 'Tools'], rows: testing }" />
          </section>
        </div>
      }
      @case ('decisions') {
        <section class="panel">
          <div><h2>Architecture decision records</h2><p class="muted">Why the platform looks the way it does. The full text is in docs/adr.</p></div>
          <ng-container *ngTemplateOutlet="table; context: { head: ['ADR', 'Decision', 'Rejected alternative'], rows: decisions }" />
        </section>
      }
    }

    <ng-template #stack let-group>
      <div><h2>{{ group.title }}</h2><p class="muted">{{ group.subtitle }}</p></div>
      <ul class="about-stack">
        @for (item of group.items; track item.name) {
          <li>
            <span class="about-stack__mark" aria-hidden="true">{{ item.mark }}</span>
            <span>
              <b>{{ item.name }}</b>
              @if (item.name === 'Angular') { <code>{{ angular }}</code> } @else if (item.version) { <code>{{ item.version }}</code> }
              <br /><span class="muted">{{ item.note }}</span>
            </span>
          </li>
        }
      </ul>
    </ng-template>

    <ng-template #table let-head="head" let-rows="rows">
      <div class="table-wrap">
        <table class="table table--compact">
          <thead><tr>@for (h of head; track h) { <th scope="col">{{ h }}</th> }</tr></thead>
          <tbody>
            @for (row of rows; track $index) {
              <tr>@for (cell of row.cells; track $index) { <td>{{ cell }}</td> }</tr>
            }
          </tbody>
        </table>
      </div>
    </ng-template>
  `,
  styles: `
    h2 { margin: 0; font-size: var(--gggs-font-size-md); }
    .muted { margin: 0; }
    .about-hero {
      margin-block-end: var(--gggs-space-4);
      padding: var(--gggs-space-6);
      border-radius: var(--gggs-radius-xl);
      background: radial-gradient(120% 140% at 100% 0%, color-mix(in srgb, var(--gggs-color-primary) 55%, transparent), transparent 55%), var(--gggs-color-brand);
      color: var(--gggs-color-nav-text-active);
      box-shadow: var(--gggs-shadow-3);
    }
    .about-hero h1 { margin: 0; font-size: var(--gggs-font-size-2xl); }
    .about-hero p { max-width: 46rem; margin: var(--gggs-space-2) 0 0; color: var(--gggs-color-nav-text); }
    .about-hero__chips { display: flex; flex-wrap: wrap; gap: var(--gggs-space-2); margin-block-start: var(--gggs-space-4); }
    .about-chip {
      display: inline-flex; align-items: center; gap: var(--gggs-space-1);
      padding: var(--gggs-space-1) var(--gggs-space-3);
      border: 1px solid color-mix(in srgb, var(--gggs-color-nav-text-active) 18%, transparent);
      border-radius: var(--gggs-radius-pill);
      background: color-mix(in srgb, var(--gggs-color-nav-text-active) 10%, transparent);
      font-size: var(--gggs-font-size-sm); font-weight: 600;
    }
    .about-chip--bad { border-color: var(--gggs-color-accent-bright); }
    .about-tier { display: grid; grid-template-columns: 7rem 1fr; gap: var(--gggs-space-3); }
    .about-tier > span { padding-block-start: var(--gggs-space-3); color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); font-weight: 700; text-transform: uppercase; }
    .about-box {
      display: flex; flex-direction: column; gap: var(--gggs-space-1);
      padding: var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-sunken);
    }
    .about-box small { color: var(--gggs-color-text-muted); }
    .about-box--primary { border-color: var(--gggs-color-primary); background: var(--gggs-color-primary-subtle); }
    .about-box--brand { border-color: var(--gggs-color-brand); background: var(--gggs-color-brand); color: var(--gggs-color-nav-text-active); }
    .about-box--brand small { color: var(--gggs-color-nav-text); }
    .about-mods { display: flex; flex-wrap: wrap; gap: var(--gggs-space-1); margin: var(--gggs-space-2) 0 0; padding: 0; list-style: none; }
    .about-mods li {
      padding: 0 var(--gggs-space-2);
      border: 1px solid color-mix(in srgb, var(--gggs-color-nav-text-active) 14%, transparent);
      border-radius: var(--gggs-radius-sm);
      font-size: var(--gggs-font-size-xs);
    }
    .about-rules { display: grid; gap: var(--gggs-space-3); margin: 0; padding-inline-start: var(--gggs-space-5); }
    .about-rules li::marker { color: var(--gggs-color-primary); font-weight: 700; }
    .about-stack { display: grid; margin: 0; padding: 0; list-style: none; }
    .about-stack li { display: flex; gap: var(--gggs-space-3); padding-block: var(--gggs-space-3); border-block-start: 1px solid var(--gggs-color-border); }
    .about-stack li:first-child { border-block-start: 0; }
    .about-stack__mark {
      display: grid; flex: none; place-items: center;
      inline-size: 2.4rem; block-size: 2.4rem;
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-primary-subtle);
      color: var(--gggs-color-primary);
      font-size: var(--gggs-font-size-sm); font-weight: 800;
    }
    code { margin-inline-start: var(--gggs-space-1); padding: 0 var(--gggs-space-1); border-radius: var(--gggs-radius-sm); background: var(--gggs-color-surface-sunken); color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    @media (max-width: 48rem) {
      .about-hero { padding: var(--gggs-space-5); }
      .about-tier { grid-template-columns: 1fr; gap: var(--gggs-space-1); }
      .about-tier > span { padding: 0; }
    }
  `,
})
export class AboutPage {
  private readonly api = inject(AdminApi);

  protected readonly about = signal<AboutInfo | null>(null);
  protected readonly failed = signal(false);
  protected readonly tab = signal('architecture');

  protected readonly angular = VERSION.full;
  protected readonly tabs: readonly TabItem[] = [
    { key: 'architecture', label: 'Architecture' },
    { key: 'frontend', label: 'Front end' },
    { key: 'backend', label: 'Back end' },
    { key: 'security', label: 'Data & security' },
    { key: 'operations', label: 'Infrastructure & testing' },
    { key: 'decisions', label: 'Key decisions' },
  ];

  protected readonly rules = MODULE_RULES;
  protected readonly moduleLayout = MODULE_LAYOUT;
  protected readonly frontend = FRONTEND;
  protected readonly frontendLibraries = FRONTEND_LIBRARIES;
  protected readonly backend = BACKEND;
  protected readonly buildingBlocks = BUILDING_BLOCKS;
  protected readonly isolation = ISOLATION_LAYERS;
  protected readonly access = ACCESS;
  protected readonly operations = OPERATIONS;
  protected readonly testing = TESTING;
  protected readonly decisions = DECISIONS;

  protected readonly shortCommit = computed(() => this.about()?.commit?.slice(0, 7) ?? null);
  protected readonly uptime = computed(() => {
    const info = this.about();
    return info ? formatUptime(info.startedAt, info.serverTime) : null;
  });
  protected readonly databaseLabel = computed(() => {
    const info = this.about();
    if (!info) return this.failed() ? 'Unknown' : null;
    return info.databaseReachable ? 'Reachable' : 'Unreachable';
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.about.set(await this.api.getAbout());
    } catch {
      this.failed.set(true);
    }
  }
}
