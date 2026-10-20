import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { AssetItem, ClaimItem, RecognitionItem, TicketItem } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { EmptyState, StatusBadge } from '@gggs/shared/ui';
import { AssetsApi, ExpensesApi, HelpdeskApi, RewardsApi } from './experience-apis';

const ROW = `
  .xp-row {
    display: grid;
    grid-template-columns: 1.8fr 1.2fr 1.2fr 8rem;
    gap: var(--gggs-space-3);
    align-items: center;
    padding-block: var(--gggs-space-2);
    border-block-end: 1px solid var(--gggs-color-border);
    color: inherit;
    text-decoration: none;
  }

  @media (max-width: 48rem) {
    .xp-row {
      grid-template-columns: 1fr 1fr;
    }
  }
`;

/** Employee 360 rewards tab: recognition received, newest first. */
@Component({
  selector: 'gggs-employee-rewards',
  imports: [DatePipe, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      @for (item of items(); track item.id) {
        <div class="xp-row">
          <span><strong>{{ item.typeName ?? item.ruleName }}</strong> <span class="muted">from {{ item.giverName ?? 'PeopleOS' }}</span></span>
          <span>{{ item.message }}</span>
          <span class="muted">{{ item.givenOn | date: 'd MMM y' }}</span>
          <span>+{{ item.points }} pts</span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-star" title="No recognition yet" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>
  `,
  styles: ROW,
})
export class EmployeeRewards {
  private readonly api = inject(RewardsApi);

  readonly employeeId = input.required<string>();

  protected readonly items = signal<readonly RecognitionItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.received(this.employeeId()).then(
        (items) => this.items.set(items),
        () => this.error.set('Recognition could not be loaded.'),
      );
    });
  }
}

/** Employee 360 assets tab: what the person holds and its book value. */
@Component({
  selector: 'gggs-employee-assets',
  imports: [DatePipe, DecimalPipe, RouterLink, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      @for (asset of assets(); track asset.id) {
        <a class="xp-row" [routerLink]="['/assets', asset.id]">
          <span><strong>{{ asset.name }}</strong> <span class="muted">{{ asset.tag }}</span></span>
          <span>{{ asset.categoryName }}{{ asset.serialNumber ? ' · ' + asset.serialNumber : '' }}</span>
          <span class="muted">since {{ asset.assignedOn | date: 'd MMM y' }} · book {{ asset.bookValue | number: '1.0-0' }}</span>
          <gggs-status-badge [status]="asset.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-desktop" title="No assets held" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>
  `,
  styles: ROW,
})
export class EmployeeAssets {
  private readonly api = inject(AssetsApi);

  readonly employeeId = input.required<string>();

  protected readonly assets = signal<readonly AssetItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.assets({ employeeId: this.employeeId() }).then(
        (assets) => this.assets.set(assets.filter((asset) => asset.status === 'Assigned')),
        () => this.error.set('Assets could not be loaded.'),
      );
    });
  }
}

/** Employee 360 expenses tab: claims and their status. */
@Component({
  selector: 'gggs-employee-expenses',
  imports: [DatePipe, DecimalPipe, RouterLink, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      @for (claim of claims(); track claim.id) {
        <a class="xp-row" [routerLink]="['/expenses/claims', claim.id]">
          <span><strong>{{ claim.title }}</strong> <span class="muted">{{ claim.number }}</span></span>
          <span>{{ claim.total | number: '1.2-2' }} {{ claim.currency }}{{ claim.policyFlags ? ' · ' + claim.policyFlags + ' outside policy' : '' }}</span>
          <span class="muted">{{ claim.createdDate | date: 'd MMM y' }}</span>
          <gggs-status-badge [status]="claim.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-receipt" title="No expense claims" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>
  `,
  styles: ROW,
})
export class EmployeeExpenses {
  private readonly api = inject(ExpensesApi);

  readonly employeeId = input.required<string>();

  protected readonly claims = signal<readonly ClaimItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.claims({ employeeId: this.employeeId() }).then(
        (claims) => this.claims.set(claims),
        () => this.error.set('Expense claims could not be loaded.'),
      );
    });
  }
}

/** Employee 360 helpdesk tab: tickets the person raised (confidential ones only for those allowed). */
@Component({
  selector: 'gggs-employee-helpdesk',
  imports: [DatePipe, RouterLink, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      @for (ticket of tickets(); track ticket.id) {
        <a class="xp-row" [routerLink]="['/helpdesk/tickets', ticket.id]">
          <span><strong>{{ ticket.subject }}</strong> <span class="muted">{{ ticket.number }}</span></span>
          <span>{{ ticket.categoryName }} · {{ humanize(ticket.priority) }}</span>
          <span class="muted">{{ ticket.createdDate | date: 'd MMM y' }}{{ ticket.satisfactionRating ? ' · ' + ticket.satisfactionRating + ' ★' : '' }}</span>
          <gggs-status-badge [status]="ticket.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-question-circle" title="No tickets" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>
  `,
  styles: ROW,
})
export class EmployeeHelpdesk {
  private readonly api = inject(HelpdeskApi);

  readonly employeeId = input.required<string>();

  protected readonly tickets = signal<readonly TicketItem[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly humanize = humanize;

  constructor() {
    effect(() => {
      this.api.tickets({ requesterId: this.employeeId() }).then(
        (tickets) => this.tickets.set(tickets),
        () => this.error.set('Tickets could not be loaded.'),
      );
    });
  }
}
