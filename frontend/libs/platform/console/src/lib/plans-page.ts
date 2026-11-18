import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ALL_MODULES, PlatformApi, moduleName, type PlansView } from '@gggs/platform/data-access';
import { PageHeader, Skeleton } from '@gggs/shared/ui';
import { messageOf } from './platform-permissions';

/** The plans on sale, which modules each includes, and how many organisations use each module. */
@Component({
  selector: 'gggs-console-plans-page',
  imports: [PageHeader, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Plans & modules" subtitle="What each plan includes. Add-ons and switch-offs are set per organisation." />

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (view(); as v) {
      <div class="grid-3">
        @for (plan of v.plans; track plan.code) {
          <section class="panel plan">
            <h2 class="panel__title">{{ plan.name }}</h2>
            <p class="muted text-sm">{{ plan.edition }} · {{ plan.maxEmployees ? 'up to ' + plan.maxEmployees + ' employees' : 'any number of employees' }}</p>
            <p class="plan__count"><strong>{{ plan.organisations }}</strong> organisation{{ plan.organisations === 1 ? '' : 's' }}</p>
            <p class="text-sm">{{ plan.features.length }} of {{ modules.length }} modules</p>
            @if (!plan.isActive) { <span class="badge badge--warning">No longer sold</span> }
          </section>
        }
      </div>

      <section class="panel panel--flush">
        <header class="panel__header"><h2 class="panel__title">Modules by plan</h2></header>
        <div class="table-wrap">
          <table class="table table--compact">
            <thead>
              <tr>
                <th>Module</th>
                @for (plan of v.plans; track plan.code) { <th class="center">{{ plan.name }}</th> }
                <th class="num">Organisations using it</th>
              </tr>
            </thead>
            <tbody>
              @for (code of modules; track code) {
                <tr>
                  <td>{{ name(code) }}</td>
                  @for (plan of v.plans; track plan.code) {
                    <td class="center">
                      @if (plan.features.includes(code)) {
                        <i class="pi pi-check text-success" aria-label="Included"></i>
                      } @else {
                        <span class="subtle" aria-label="Not included">—</span>
                      }
                    </td>
                  }
                  <td class="num">{{ usage(v, code) }}</td>
                </tr>
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
    .plan { display: flex; flex-direction: column; gap: var(--gggs-space-1); }
    .plan p { margin: 0; }
    .plan__count { font-size: var(--gggs-font-size-lg); }
    .center { text-align: center; }
  `,
})
export class ConsolePlansPage {
  private readonly api = inject(PlatformApi);

  protected readonly modules = ALL_MODULES;
  protected readonly view = signal<PlansView | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.api.plans().then(
      (view) => this.view.set(view),
      (error: unknown) => this.error.set(messageOf(error, 'The plans could not be loaded.')),
    );
  }

  protected name(code: string): string {
    return moduleName(code);
  }

  protected usage(view: PlansView, code: string): number {
    return view.modules.find((module) => module.code === code)?.organisations ?? 0;
  }
}
