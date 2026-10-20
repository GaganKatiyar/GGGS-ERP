import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { OnboardingCaseItem } from '@gggs/shared/models';
import { EmptyState, StatusBadge } from '@gggs/shared/ui';
import { OnboardingApi } from './hiring-apis';

/** Employee 360 onboarding tab: the joiner's onboarding, how far along it is and what is late. */
@Component({
  selector: 'gggs-employee-onboarding',
  imports: [DatePipe, RouterLink, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      @for (item of cases(); track item.id) {
        <a class="hiring-row" [routerLink]="['/onboarding/cases', item.id]">
          <strong>{{ item.templateName }}</strong>
          <span>Joining {{ item.joiningDate | date: 'd MMM y' }}{{ item.buddyName ? ' · buddy ' + item.buddyName : '' }}</span>
          <span>{{ item.done }} of {{ item.tasks }} tasks done{{ item.overdue ? ' · ' + item.overdue + ' overdue' : '' }}</span>
          <gggs-status-badge [status]="item.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-sign-in" title="No onboarding on record" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>
  `,
  styles: `
    .hiring-row {
      display: grid;
      grid-template-columns: 1.4fr 1.6fr 1.4fr 8rem;
      gap: var(--gggs-space-3);
      align-items: center;
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
      color: inherit;
      text-decoration: none;
    }

    @media (max-width: 48rem) {
      .hiring-row {
        grid-template-columns: 1fr 1fr;
      }
    }
  `,
})
export class EmployeeOnboarding {
  private readonly api = inject(OnboardingApi);

  readonly employeeId = input.required<string>();

  protected readonly cases = signal<readonly OnboardingCaseItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.cases({ employeeId: this.employeeId() }).then(
        (cases) => this.cases.set(cases),
        () => this.error.set('Onboarding could not be loaded.'),
      );
    });
  }
}
