import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import type { FeedbackItem, PipItem, ReviewItem } from '@gggs/shared/models';
import { RATING_LABELS, humanize } from '@gggs/shared/models';
import { PerformanceApi } from '@gggs/shared/talent';
import { EmptyState, FormDialog, PageHeader, StatusBadge, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';
import { errorText } from './goals-pages';

/** My reviews, the reviews I give as a manager, and any improvement plan I am on. */
@Component({
  selector: 'gggs-my-reviews-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, EmptyState, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My reviews" subtitle="Your performance reviews, and the reviews you write for your team." />
    <gggs-tabs [tabs]="tabs()" [(selected)]="tab" />
    <section class="panel stack">
      @if (tab() === 'mine') {
        @for (review of mine(); track review.id) {
          <a class="perf__row" [routerLink]="['/performance/reviews', review.id]">
            <span><strong>{{ review.cycleName }}</strong></span>
            <span class="muted">{{ review.managerName ?? '' }}</span>
            <span>{{ review.finalLabel ?? next(review) }}</span>
            <span class="muted">{{ review.releasedDate ? 'released ' + (review.releasedDate | date: 'd MMM y') : '' }}</span>
            <span><gggs-status-badge [status]="review.status" /></span>
          </a>
        } @empty {
          <gggs-empty-state icon="pi pi-chart-line" title="No reviews yet" />
        }
        @for (plan of pips(); track plan.id) {
          <a class="perf__row" [routerLink]="['/performance/pips', plan.id]">
            <span><strong>Improvement plan</strong></span>
            <span class="muted">{{ plan.managerName ?? '' }}</span>
            <span>{{ plan.startDate | date: 'd MMM' }} – {{ plan.endDate | date: 'd MMM y' }}</span>
            <span class="muted">{{ plan.outcome ?? '' }}</span>
            <span><gggs-status-badge [status]="plan.status" /></span>
          </a>
        }
      } @else {
        @for (review of team(); track review.id) {
          <a class="perf__row" [routerLink]="['/performance/reviews', review.id]">
            <span><strong>{{ review.employeeName ?? '—' }}</strong> <span class="muted">{{ review.cycleName }}</span></span>
            <span class="muted">{{ review.designation ?? '' }}</span>
            <span>{{ review.selfRating ? 'self ' + review.selfRating + '/5' : 'self-review pending' }}</span>
            <span>{{ review.managerRating ? 'you ' + review.managerRating + '/5' : '' }}</span>
            <span><gggs-status-badge [status]="review.status" /></span>
          </a>
        } @empty {
          <gggs-empty-state icon="pi pi-users" title="No reviews to write" />
        }
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
  `,
  styleUrl: './performance.css',
})
export class MyReviewsPage {
  private readonly api = inject(PerformanceApi);

  protected readonly isManager = inject(PermissionStore).has('performance.review.manage-as-manager');
  protected readonly mine = signal<readonly ReviewItem[]>([]);
  protected readonly team = signal<readonly ReviewItem[]>([]);
  protected readonly pips = signal<readonly PipItem[]>([]);
  protected readonly tab = signal('mine');
  protected readonly error = signal<string | null>(null);
  protected readonly tabs = computed<readonly TabItem[]>(() => [
    { key: 'mine', label: 'My reviews' },
    ...(this.isManager ? [{ key: 'team', label: 'My team', note: `${this.team().filter((row) => row.status === 'Pending' || row.status === 'SelfSubmitted').length} to do` }] : []),
  ]);

  constructor() {
    Promise.all([this.api.myReviews(), this.api.myPips(), this.isManager ? this.api.teamReviews() : Promise.resolve([])]).then(
      ([mine, pips, team]) => {
        this.mine.set(mine);
        this.pips.set(pips);
        this.team.set(team);
      },
      (error: unknown) => this.error.set(error instanceof ApiError && error.status === 422 ? 'Your account is not linked to an employee record.' : errorText(error, 'Your reviews could not be loaded.')),
    );
  }

  protected next(review: ReviewItem): string {
    return review.status === 'Pending' ? 'Your self-review is due' : review.status === 'SelfSubmitted' ? 'With your manager' : 'Being finalised';
  }
}

/** Feedback colleagues have asked me for. */
@Component({
  selector: 'gggs-my-feedback-page',
  imports: [DatePipe, PageHeader, StatusBadge, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My feedback" subtitle="Colleagues who asked for your 360 feedback. It goes to their manager, and to them anonymously once the review is released." />
    <section class="panel stack">
      @for (item of requests(); track item.id) {
        <div class="perf__row perf__row--static">
          <span><strong>{{ item.employeeName ?? '—' }}</strong> <span class="muted">{{ item.cycleName }}</span></span>
          <span class="muted">as {{ humanize(item.relationship) }}</span>
          <span class="muted">{{ item.respondedDate ? (item.respondedDate | date: 'd MMM y') : '' }}</span>
          <span class="toolbar">
            @if (item.status === 'Requested') {
              <button type="button" class="btn btn--primary btn--sm" (click)="open(item)">Give feedback</button>
              <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="decline(item)">Decline</button>
            }
          </span>
          <span><gggs-status-badge [status]="item.status" /></span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-comments" title="No feedback requests" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
    <gggs-form-dialog [(visible)]="dialogOpen" [title]="'Feedback for ' + (current()?.employeeName ?? '')" saveLabel="Send" [busy]="busy()" [error]="formError()" (save)="submit()">
      @for (competency of current()?.competencies ?? []; track competency) {
        <div class="perf__rate">
          <span>{{ competency }}</span>
          <span class="perf__stars">
            @for (score of scores; track score) {
              <button type="button" class="perf__star" [class.perf__star--on]="rating(competency) >= score" [title]="labels[score]" (click)="rate(competency, score)">{{ score }}</button>
            }
          </span>
        </div>
      }
      <label class="field">
        <span class="field__label">Strengths</span>
        <textarea rows="3" maxlength="4000" [value]="strengths()" (input)="strengths.set($any($event.target).value)"></textarea>
      </label>
      <label class="field">
        <span class="field__label">Something to work on</span>
        <textarea rows="3" maxlength="4000" [value]="improvements()" (input)="improvements.set($any($event.target).value)"></textarea>
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './performance.css',
})
export class MyFeedbackPage {
  private readonly api = inject(PerformanceApi);

  protected readonly humanize = humanize;
  protected readonly scores = [1, 2, 3, 4, 5];
  protected readonly labels = RATING_LABELS;
  protected readonly requests = signal<readonly FeedbackItem[]>([]);
  protected readonly dialogOpen = signal(false);
  protected readonly current = signal<FeedbackItem | null>(null);
  protected readonly ratings = signal<Readonly<Record<string, number>>>({});
  protected readonly strengths = signal('');
  protected readonly improvements = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.myFeedback().then(
      (requests) => this.requests.set(requests),
      (error: unknown) => this.error.set(errorText(error, 'Feedback requests could not be loaded.')),
    );
  }

  protected rating(competency: string): number {
    return this.ratings()[competency] ?? 0;
  }

  protected rate(competency: string, score: number): void {
    this.ratings.update((ratings) => ({ ...ratings, [competency]: score }));
  }

  protected open(item: FeedbackItem): void {
    this.current.set(item);
    this.ratings.set({});
    this.strengths.set('');
    this.improvements.set('');
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected async submit(): Promise<void> {
    const item = this.current();
    if (!item) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      this.requests.set(await this.api.giveFeedback(item.id, { ratings: this.ratings(), strengths: this.strengths() || null, improvements: this.improvements() || null }));
      this.dialogOpen.set(false);
    } catch (error) {
      this.formError.set(errorText(error, 'Your feedback could not be sent.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async decline(item: FeedbackItem): Promise<void> {
    this.busy.set(true);
    try {
      this.requests.set(await this.api.declineFeedback(item.id));
    } catch (error) {
      this.error.set(errorText(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}
