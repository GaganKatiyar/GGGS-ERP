import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { TravelAdvance, TravelRequest, TravelSettlement } from '@gggs/shared/models';
import { SpendApi } from '../data/spend-api';

/** Admin: one travel request's approval, its advances, and the trip's settlement once the
 * employee's actual expenses are in. Requesting/disbursing an advance and building/settling the
 * trip are always an admin/finance action (spend.travel-advance.manage) - never something the
 * traveling employee does themselves. */
@Component({
  selector: 'gggs-travel-request-detail-page',
  imports: [RouterLink, DecimalPipe, ReactiveFormsModule, HasPermissionDirective, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (request(); as r) {
      <gggs-page-header [title]="r.requestNo" [subtitle]="r.purpose">
        <a routerLink="/spend/travel-requests" class="btn">Back</a>
      </gggs-page-header>

      <gggs-card title="Request" class="section">
        <dl class="request-grid">
          <dt>Route</dt>
          <dd>{{ r.fromCity }} → {{ r.toCity }}</dd>
          <dt>Dates</dt>
          <dd>{{ r.fromDate }} – {{ r.toDate }}</dd>
          <dt>Estimated cost</dt>
          <dd>{{ r.currency }} {{ r.estimatedCost | number: '1.2-2' }}</dd>
          <dt>Status</dt>
          <dd>{{ r.status }}</dd>
        </dl>

        @if (r.status === 'Submitted') {
          <div class="actions" *gggsHasPermission="'spend.travel.approve'">
            <gggs-button size="sm" (click)="approve()" [disabled]="isActing()">Approve</gggs-button>
            <gggs-button size="sm" variant="secondary" (click)="reject()" [disabled]="isActing()">Reject</gggs-button>
          </div>
        }
      </gggs-card>

      @if (r.status === 'Approved') {
        <gggs-card title="Advances" class="section" *gggsHasPermission="'spend.travel-advance.manage'">
          @if (advances().length > 0) {
            <table class="advances-table">
              <thead>
                <tr>
                  <th scope="col">Amount</th>
                  <th scope="col">Status</th>
                  <th scope="col"></th>
                </tr>
              </thead>
              <tbody>
                @for (advance of advances(); track advance.id) {
                  <tr>
                    <td>{{ advance.currency }} {{ advance.amount | number: '1.2-2' }}</td>
                    <td>{{ advance.status }}</td>
                    <td>
                      @if (advance.status === 'Requested') {
                        <gggs-button size="sm" variant="ghost" (click)="disburse(advance.id)" [disabled]="isActing()">Disburse</gggs-button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          } @else {
            <p class="muted">No advance requested yet.</p>
          }

          <form [formGroup]="advanceForm" (ngSubmit)="requestAdvance()" novalidate class="advance-form">
            <label class="field">
              <span class="field__label">Amount</span>
              <input type="number" formControlName="amount" min="0.01" step="0.01" required />
            </label>

            <label class="field">
              <span class="field__label">Currency</span>
              <input type="text" formControlName="currency" maxlength="3" required />
            </label>

            <gggs-button type="submit" size="sm" [disabled]="isActing()">Request advance</gggs-button>
          </form>
        </gggs-card>

        <gggs-card title="Settlement" class="section" *gggsHasPermission="'spend.travel-advance.manage'">
          @if (settlement(); as s) {
            <dl class="request-grid">
              <dt>Advance total</dt>
              <dd>{{ s.advanceTotal | number: '1.2-2' }}</dd>
              <dt>Expense total</dt>
              <dd>{{ s.expenseTotal | number: '1.2-2' }}</dd>
              @if (s.netPayable > 0) {
                <dt>Net payable to employee</dt>
                <dd>{{ s.netPayable | number: '1.2-2' }}</dd>
              }
              @if (s.netRecoverable > 0) {
                <dt>Net recoverable from employee</dt>
                <dd>{{ s.netRecoverable | number: '1.2-2' }}</dd>
              }
              <dt>Status</dt>
              <dd>{{ s.status }}</dd>
            </dl>

            @if (s.status === 'Draft') {
              <gggs-button size="sm" (click)="settle(s.id)" [disabled]="isActing()">Mark settled</gggs-button>
            }
          } @else {
            <p class="muted">No settlement built yet.</p>
          }

          <gggs-button size="sm" variant="secondary" (click)="buildSettlement()" [disabled]="isActing()">
            {{ settlement() ? 'Rebuild settlement' : 'Build settlement' }}
          </gggs-button>
        </gggs-card>
      }
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .request-grid {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 0.25rem 0.75rem;
      margin: 0 0 1rem;
      font-size: 0.875rem;
    }

    .request-grid dt {
      font-weight: 600;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .request-grid dd {
      margin: 0;
    }

    .actions {
      display: flex;
      gap: 0.75rem;
    }

    .advances-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .advances-table th,
    .advances-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .advance-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
      flex-wrap: wrap;
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

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .btn {
      padding: 0.5rem 1rem;
      font-weight: 600;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #ffffff);
      text-decoration: none;
      color: inherit;
      cursor: pointer;
    }
  `,
})
export class TravelRequestDetailPage {
  private readonly api = inject(SpendApi);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly request = signal<TravelRequest | null>(null);
  protected readonly advances = signal<readonly TravelAdvance[]>([]);
  protected readonly settlement = signal<TravelSettlement | null>(null);
  protected readonly isActing = signal(false);

  private readonly requestId = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly advanceForm = this.formBuilder.group({
    amount: [0, [Validators.required, Validators.min(0.01)]],
    currency: ['INR', [Validators.required, Validators.maxLength(3)]],
  });

  constructor() {
    void this.load();
  }

  protected async approve(): Promise<void> {
    await this.act(() => this.api.approveTravelRequest(this.requestId));
  }

  protected async reject(): Promise<void> {
    await this.act(() => this.api.rejectTravelRequest(this.requestId));
  }

  protected async requestAdvance(): Promise<void> {
    if (this.advanceForm.invalid || this.isActing()) {
      this.advanceForm.markAllAsTouched();
      return;
    }

    const value = this.advanceForm.getRawValue();

    await this.act(() =>
      this.api.requestAdvance(this.requestId, {
        amount: value.amount ?? 0,
        currency: (value.currency ?? 'INR').toUpperCase(),
      }),
    );
    this.advanceForm.reset({ amount: 0, currency: 'INR' });
  }

  protected async disburse(advanceId: string): Promise<void> {
    await this.act(() => this.api.disburseAdvance(advanceId));
  }

  protected async buildSettlement(): Promise<void> {
    await this.act(() => this.api.buildSettlement(this.requestId));
  }

  protected async settle(settlementId: string): Promise<void> {
    await this.act(() => this.api.settleSettlement(settlementId));
  }

  private async act(run: () => Promise<unknown>): Promise<void> {
    if (this.isActing()) {
      return;
    }

    this.isActing.set(true);

    try {
      await run();
      await this.load();
    } finally {
      this.isActing.set(false);
    }
  }

  private async load(): Promise<void> {
    const request = await this.api.getTravelRequest(this.requestId);
    this.request.set(request);

    if (request.status === 'Approved') {
      this.advances.set(await this.api.listAdvances(this.requestId));
      this.settlement.set(await this.api.getSettlement(this.requestId));
    }
  }
}
