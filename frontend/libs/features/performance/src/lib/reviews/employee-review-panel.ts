import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmployeePicker } from '@gggs/shared/employee-reference';
import { Button } from '@gggs/shared/ui';
import type { EmployeeListItem, Review } from '@gggs/shared/models';
import { PerformanceApi } from '../data/performance-api';

/** One employee's review for a cycle - shows the existing review and its self/manager/release
 * actions, or (while the cycle is Open and no review exists yet) a form to raise one. */
@Component({
  selector: 'gggs-employee-review-panel',
  imports: [ReactiveFormsModule, HasPermissionDirective, EmployeePicker, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isLoading()) {
      <p class="muted">Loading…</p>
    } @else if (review(); as r) {
      <dl class="review-grid">
        <dt>Status</dt>
        <dd>{{ r.status }}</dd>
        @if (r.selfRating) {
          <dt>Self rating</dt>
          <dd>{{ r.selfRating }} / 5 - {{ r.selfComments || '—' }}</dd>
        }
        @if (r.managerRating) {
          <dt>Manager rating</dt>
          <dd>{{ r.managerRating }} / 5 - {{ r.managerComments || '—' }}</dd>
        }
      </dl>

      @if (r.status === 'Draft') {
        <form
          *gggsHasPermission="'performance.review.manage-own'"
          [formGroup]="ratingForm"
          (ngSubmit)="submitSelf()"
          novalidate
          class="rating-form"
        >
          <label class="field">
            <span class="field__label">Self rating (1-5)</span>
            <input type="number" formControlName="rating" min="1" max="5" required />
          </label>
          <label class="field">
            <span class="field__label">Comments (optional)</span>
            <input type="text" formControlName="comments" maxlength="2000" />
          </label>
          <gggs-button type="submit" size="sm" [disabled]="isSaving()">
            {{ isSaving() ? 'Submitting…' : 'Submit self-review' }}
          </gggs-button>
        </form>
      }

      @if (r.status === 'SelfSubmitted') {
        <form
          *gggsHasPermission="'performance.review.manage-as-manager'"
          [formGroup]="ratingForm"
          (ngSubmit)="submitManager()"
          novalidate
          class="rating-form"
        >
          <label class="field">
            <span class="field__label">Manager rating (1-5)</span>
            <input type="number" formControlName="rating" min="1" max="5" required />
          </label>
          <label class="field">
            <span class="field__label">Comments (optional)</span>
            <input type="text" formControlName="comments" maxlength="2000" />
          </label>
          <gggs-button type="submit" size="sm" [disabled]="isSaving()">
            {{ isSaving() ? 'Submitting…' : 'Submit manager review' }}
          </gggs-button>
        </form>
      }

      @if (r.status === 'ManagerSubmitted') {
        <gggs-button
          *gggsHasPermission="'performance.review.manage-as-manager'"
          size="sm"
          (click)="release()"
          [disabled]="isSaving()"
        >
          {{ isSaving() ? 'Releasing…' : 'Release review' }}
        </gggs-button>
      }

      @if (errorMessage()) {
        <p class="field__error" role="alert">{{ errorMessage() }}</p>
      }
    } @else if (cycleIsOpen()) {
      <form
        *gggsHasPermission="'performance.review.manage-own'"
        [formGroup]="createForm"
        (ngSubmit)="submitCreate()"
        novalidate
        class="create-form"
      >
        <div class="field">
          <span class="field__label" id="manager-label">Reviewing manager</span>
          <gggs-employee-picker aria-labelledby="manager-label" (employeeSelected)="onManagerSelected($event)" />
          @if (selectedManagerName()) {
            <span class="selected-name">{{ selectedManagerName() }}</span>
          }
        </div>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Creating…' : 'Raise review' }}
        </gggs-button>
      </form>
    } @else {
      <p class="muted">No review yet.</p>
    }
  `,
  styles: `
    .review-grid {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 0.25rem 0.75rem;
      margin: 0 0 0.75rem;
      font-size: 0.875rem;
    }

    .review-grid dt {
      font-weight: 600;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .review-grid dd {
      margin: 0;
    }

    .rating-form,
    .create-form {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      max-width: 24rem;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
    }

    .field__label {
      font-weight: 600;
    }

    .selected-name {
      font-size: 0.8125rem;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0;
    }
  `,
})
export class EmployeeReviewPanel {
  private readonly api = inject(PerformanceApi);
  private readonly formBuilder = inject(FormBuilder);

  readonly cycleId = input.required<string>();
  readonly employeeId = input.required<string>();
  readonly cycleIsOpen = input.required<boolean>();

  protected readonly review = signal<Review | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly selectedManagerId = signal<string | null>(null);
  protected readonly selectedManagerName = signal<string | null>(null);

  protected readonly createForm = this.formBuilder.group({
    managerEmployeeId: ['', Validators.required],
  });

  protected readonly ratingForm = this.formBuilder.group({
    rating: [3, [Validators.required, Validators.min(1), Validators.max(5)]],
    comments: [''],
  });

  constructor() {
    void this.load();
  }

  protected onManagerSelected(employee: EmployeeListItem): void {
    this.selectedManagerId.set(employee.id);
    this.selectedManagerName.set(employee.displayName);
  }

  protected async submitCreate(): Promise<void> {
    const managerId = this.selectedManagerId();
    if (!managerId || this.isSaving()) {
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    try {
      this.review.set(
        await this.api.createReview({ cycleId: this.cycleId(), employeeId: this.employeeId(), managerEmployeeId: managerId }),
      );
    } catch {
      this.errorMessage.set('The review could not be raised. Please try again.');
    } finally {
      this.isSaving.set(false);
    }
  }

  protected async submitSelf(): Promise<void> {
    await this.submitRating((id, rating, comments) => this.api.submitSelfReview(id, { rating, comments }));
  }

  protected async submitManager(): Promise<void> {
    await this.submitRating((id, rating, comments) => this.api.submitManagerReview(id, { rating, comments }));
  }

  protected async release(): Promise<void> {
    const reviewId = this.review()?.id;
    if (!reviewId || this.isSaving()) {
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    try {
      this.review.set(await this.api.releaseReview(reviewId));
    } catch {
      this.errorMessage.set('The review could not be released. Please try again.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async submitRating(run: (id: string, rating: number, comments: string | undefined) => Promise<Review>): Promise<void> {
    const reviewId = this.review()?.id;
    if (!reviewId || this.ratingForm.invalid || this.isSaving()) {
      this.ratingForm.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const value = this.ratingForm.getRawValue();

    try {
      this.review.set(await run(reviewId, value.rating ?? 3, value.comments || undefined));
      this.ratingForm.reset({ rating: 3 });
    } catch {
      this.errorMessage.set('That could not be submitted. Please try again.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      this.review.set(await this.api.getReviewForEmployeeAndCycle(this.employeeId(), this.cycleId()));
    } finally {
      this.isLoading.set(false);
    }
  }
}
