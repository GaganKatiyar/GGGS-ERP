import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { GoalItem, LearningEnrollment, PromotionItem, ReviewItem } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { EmptyState, StatusBadge } from '@gggs/shared/ui';
import { CareerApi, GoalsApi, LearningApi, PerformanceApi } from './talent-apis';

const ROW = `
  .talent-row {
    display: grid;
    grid-template-columns: minmax(0, 1.8fr) minmax(0, 1.2fr) minmax(0, 1.2fr) 8rem;
    gap: var(--gggs-space-3);
    align-items: center;
    padding-block: var(--gggs-space-2);
    border-block-end: 1px solid var(--gggs-color-border);
    color: inherit;
    text-decoration: none;
  }

  .talent-bar {
    display: block;
    height: 0.4rem;
    border-radius: 999px;
    background: var(--gggs-color-border);
    overflow: hidden;
  }

  .talent-bar span {
    display: block;
    height: 100%;
    background: var(--gggs-color-primary);
  }

  @media (max-width: 48rem) {
    .talent-row {
      grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
    }
  }
`;

/** A progress bar for a 0-100 value. */
@Component({
  selector: 'gggs-progress-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="talent-bar" role="progressbar" [attr.aria-label]="label()" [attr.aria-valuenow]="value()" aria-valuemin="0" aria-valuemax="100"><span [style.width.%]="value()"></span></span>`,
  styles: ROW,
})
export class ProgressBar {
  readonly value = input(0);
  /** What is progressing, for screen readers ("Progress" when the row already names it). */
  readonly label = input('Progress');
}

/** Employee 360 goals tab. */
@Component({
  selector: 'gggs-employee-goals',
  imports: [DatePipe, RouterLink, StatusBadge, EmptyState, ProgressBar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      @for (goal of goals(); track goal.id) {
        <a class="talent-row" [routerLink]="['/goals', goal.id]">
          <strong>{{ goal.title }}</strong>
          <span>{{ goal.progress }}% <gggs-progress-bar [value]="goal.progress" /></span>
          <span class="muted">due {{ goal.dueDate | date: 'd MMM y' }} · weight {{ goal.weight }}%</span>
          <gggs-status-badge [status]="goal.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-bullseye" title="No goals" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>
  `,
  styles: ROW,
})
export class EmployeeGoals {
  private readonly api = inject(GoalsApi);

  readonly employeeId = input.required<string>();

  protected readonly goals = signal<readonly GoalItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.goals({ ownerId: this.employeeId() }).then(
        (goals) => this.goals.set(goals),
        () => this.error.set('Goals could not be loaded.'),
      );
    });
  }
}

/** Employee 360 performance tab: reviews across cycles with their ratings. */
@Component({
  selector: 'gggs-employee-performance',
  imports: [RouterLink, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      @for (review of reviews(); track review.id) {
        <a class="talent-row" [routerLink]="['/performance/reviews', review.id]">
          <strong>{{ review.cycleName }}</strong>
          <span>{{ review.finalLabel ?? (review.managerRating ? 'Manager: ' + review.managerRating + '/5' : 'In progress') }}</span>
          <span class="muted">{{ review.managerName ?? '' }}</span>
          <gggs-status-badge [status]="review.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-chart-line" title="No reviews yet" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>
  `,
  styles: ROW,
})
export class EmployeePerformance {
  private readonly api = inject(PerformanceApi);

  readonly employeeId = input.required<string>();

  protected readonly reviews = signal<readonly ReviewItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const employeeId = this.employeeId();
      this.api.cycles().then(
        async (cycles) => {
          const lists = await Promise.all(cycles.filter((cycle) => cycle.status !== 'Draft').map((cycle) => this.api.reviews(cycle.id).catch(() => [] as readonly ReviewItem[])));
          this.reviews.set(lists.flat().filter((review) => review.employeeId === employeeId));
        },
        () => this.error.set('Reviews could not be loaded.'),
      );
    });
  }
}

/** Employee 360 learning tab. */
@Component({
  selector: 'gggs-employee-learning',
  imports: [DatePipe, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      @for (enrollment of enrollments(); track enrollment.id) {
        <div class="talent-row">
          <strong>{{ enrollment.courseTitle }}{{ enrollment.mandatory ? ' (mandatory)' : '' }}</strong>
          <span>{{ humanize(enrollment.source) }}{{ enrollment.dueDate ? ' · due ' + (enrollment.dueDate | date: 'd MMM y') : '' }}</span>
          <span class="muted">{{ enrollment.completedOn ? 'completed ' + (enrollment.completedOn | date: 'd MMM y') : enrollment.progress + '%' }}{{ enrollment.certificateExpiresOn ? ' · certificate to ' + (enrollment.certificateExpiresOn | date: 'd MMM y') : '' }}</span>
          <gggs-status-badge [status]="enrollment.overdue ? 'Overdue' : enrollment.status" />
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-book" title="No learning on record" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>
  `,
  styles: ROW,
})
export class EmployeeLearning {
  private readonly api = inject(LearningApi);

  readonly employeeId = input.required<string>();

  protected readonly humanize = humanize;
  protected readonly enrollments = signal<readonly LearningEnrollment[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.enrollments({ employeeId: this.employeeId() }).then(
        (enrollments) => this.enrollments.set(enrollments.filter((row) => row.status !== 'Cancelled')),
        () => this.error.set('Learning could not be loaded.'),
      );
    });
  }
}

/** Employee 360 career tab: promotions. */
@Component({
  selector: 'gggs-employee-career',
  imports: [DatePipe, RouterLink, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack">
      @for (promotion of promotions(); track promotion.id) {
        <a class="talent-row" [routerLink]="['/career/promotions', promotion.id]">
          <strong>{{ promotion.currentDesignation ?? '—' }} → {{ promotion.proposedDesignation }}</strong>
          <span>{{ promotion.increasePercent !== undefined ? '+' + promotion.increasePercent + '%' : '' }}</span>
          <span class="muted">effective {{ promotion.effectiveDate | date: 'd MMM y' }}</span>
          <gggs-status-badge [status]="promotion.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-arrow-up-right" title="No promotions on record" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>
  `,
  styles: ROW,
})
export class EmployeeCareer {
  private readonly api = inject(CareerApi);

  readonly employeeId = input.required<string>();

  protected readonly promotions = signal<readonly PromotionItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const employeeId = this.employeeId();
      this.api.promotions().then(
        (promotions) => this.promotions.set(promotions.filter((row) => row.employeeId === employeeId)),
        () => this.error.set('Promotions could not be loaded.'),
      );
    });
  }
}
