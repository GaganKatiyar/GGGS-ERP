import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import type { MyPolicy, MyPolicyDetail } from '@gggs/shared/models';
import { POLICY_CATEGORY_LABELS } from '@gggs/shared/models';
import { EmptyState, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { PoliciesApi } from './policies-api';

/** The policies that apply to me; those still to acknowledge come first. */
@Component({
  selector: 'gggs-my-policies-page',
  imports: [DatePipe, PageHeader, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My policies" [subtitle]="pending() ? pending() + ' to read and acknowledge' : 'Company policies that apply to you.'" />

    @if (loading()) {
      <gggs-skeleton [lines]="5" />
    } @else if (error()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="Policies could not be shown" [description]="error()!" />
    } @else if (policies().length === 0) {
      <gggs-empty-state icon="pi pi-book" title="No policies yet" description="Policies HR publishes for you appear here." />
    } @else {
      <ul class="mine">
        @for (policy of policies(); track policy.policyId) {
          <li>
            <button type="button" class="mine__item" (click)="open(policy)">
              <span class="mine__main">
                <strong>{{ policy.title }}</strong>
                <span class="muted">{{ categories[policy.category] }} · version {{ policy.versionNumber }} · in effect since {{ policy.effectiveFrom | date: 'mediumDate' }}</span>
              </span>
              @if (policy.requiresAcknowledgement) {
                <gggs-status-badge [status]="policy.acknowledgedDate ? 'Acknowledged' : 'Pending'" [variants]="{ acknowledged: 'success' }" />
              }
            </button>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .mine {
      margin: 0;
      padding: 0;
      list-style: none;
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-raised);
    }

    .mine__item {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);
      width: 100%;
      padding: var(--gggs-space-3);
      border: 0;
      border-block-end: 1px solid var(--gggs-color-border);
      background: transparent;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .mine__item:hover {
      background: var(--gggs-color-surface-hover);
    }

    .mine__main {
      display: flex;
      flex: 1;
      flex-direction: column;
      gap: 0.125rem;
    }
  `,
})
export class MyPoliciesPage {
  private readonly api = inject(PoliciesApi);
  private readonly router = inject(Router);

  protected readonly categories = POLICY_CATEGORY_LABELS;
  protected readonly policies = signal<readonly MyPolicy[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly pending = computed(() => this.policies().filter((policy) => policy.requiresAcknowledgement && !policy.acknowledgedDate).length);

  constructor() {
    this.api.mine().then(
      (policies) => {
        this.policies.set(policies);
        this.loading.set(false);
      },
      (error: unknown) => {
        this.error.set(
          error instanceof ApiError && error.status === 422 ? 'Your account is not linked to an employee record.' : error instanceof ApiError ? error.message : 'Try again in a moment.',
        );
        this.loading.set(false);
      },
    );
  }

  protected open(policy: MyPolicy): void {
    void this.router.navigate(['/me/policies', policy.policyId]);
  }
}

/** One policy to read, and the button that records that I have. */
@Component({
  selector: 'gggs-my-policy-page',
  imports: [DatePipe, RouterLink, PageHeader, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="This policy could not be shown" [description]="error()!" />
    } @else if (!detail()) {
      <gggs-skeleton [lines]="10" />
    } @else {
      @let p = detail()!.policy;
      <gggs-page-header [title]="p.title" [subtitle]="'Version ' + p.versionNumber + ' · in effect since ' + (p.effectiveFrom | date: 'mediumDate')">
        <a class="btn" routerLink="/me/policies">All my policies</a>
      </gggs-page-header>

      <div class="stack">
        @if (detail()!.changeSummary) {
          <p class="muted"><strong>What changed:</strong> {{ detail()!.changeSummary }}</p>
        }
        <section class="panel">
          <pre class="policy__text">{{ detail()!.content }}</pre>
        </section>

        @if (p.requiresAcknowledgement) {
          <section class="panel toolbar">
            @if (p.acknowledgedDate) {
              <span><i class="pi pi-check-circle policy__done" aria-hidden="true"></i> You acknowledged this on {{ p.acknowledgedDate | date: 'medium' }}.</span>
            } @else {
              <span class="policy__ask">By acknowledging, you confirm you have read and understood this version.</span>
              <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="acknowledge()">I have read and understood it</button>
            }
          </section>
        }
        @if (actionError()) {
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ actionError() }}</span></p>
        }
      </div>
    }
  `,
  styles: `
    .policy__text {
      margin: 0;
      white-space: pre-wrap;
      font-family: inherit;
      line-height: 1.6;
    }

    .policy__ask {
      flex: 1;
    }

    .policy__done {
      color: var(--gggs-color-success);
    }
  `,
})
export class MyPolicyPage {
  private readonly api = inject(PoliciesApi);

  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  protected readonly detail = signal<MyPolicyDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  constructor() {
    effect(() => void this.load(this.id()));
  }

  protected async acknowledge(): Promise<void> {
    this.busy.set(true);
    this.actionError.set(null);
    try {
      await this.api.acknowledge(this.id());
      await this.load(this.id());
    } catch (error) {
      this.actionError.set(error instanceof ApiError ? error.message : 'Your acknowledgement could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    try {
      this.detail.set(await this.api.myPolicy(id));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Try again in a moment.');
    }
  }
}
