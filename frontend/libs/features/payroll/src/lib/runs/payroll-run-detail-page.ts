import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { PayrollRun, PayrollRunEmployee } from '@gggs/shared/models';
import { PayrollApi } from '../data/payroll-api';

/** One run's status actions (calculate/finalize) and its per-employee results, once calculated. */
@Component({
  selector: 'gggs-payroll-run-detail-page',
  imports: [RouterLink, DecimalPipe, HasPermissionDirective, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (run(); as r) {
      <gggs-page-header [title]="r.runNo" [subtitle]="r.status">
        <a routerLink="/payroll/runs" class="btn">Back</a>
      </gggs-page-header>

      <gggs-card title="Run" class="section">
        <dl class="run-grid">
          <dt>Employees</dt>
          <dd>{{ r.employeeCount }}</dd>
          <dt>Gross total</dt>
          <dd>{{ r.grossTotal | number: '1.2-2' }}</dd>
          <dt>Net total</dt>
          <dd>{{ r.netTotal | number: '1.2-2' }}</dd>
          @if (r.calculatedDate) {
            <dt>Calculated</dt>
            <dd>{{ r.calculatedDate }}</dd>
          }
          @if (r.finalizedDate) {
            <dt>Finalized</dt>
            <dd>{{ r.finalizedDate }}</dd>
          }
        </dl>

        <div class="actions">
          @if (r.status !== 'Finalized') {
            <gggs-button
              *gggsHasPermission="'payroll.run.process'"
              size="sm"
              (click)="calculate()"
              [disabled]="isActing()"
            >
              {{ r.status === 'Draft' ? 'Calculate' : 'Recalculate' }}
            </gggs-button>
          }
          @if (r.status === 'Calculated') {
            <gggs-button
              *gggsHasPermission="'payroll.run.finalize'"
              size="sm"
              variant="secondary"
              (click)="finalize()"
              [disabled]="isActing()"
            >
              Finalize
            </gggs-button>
          }
        </div>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }
      </gggs-card>

      @if (runEmployees().length > 0) {
        <gggs-card title="Employee results">
          <table class="results-table">
            <thead>
              <tr>
                <th scope="col">Employee</th>
                <th scope="col">Gross</th>
                <th scope="col">Net pay</th>
              </tr>
            </thead>
            <tbody>
              @for (employee of runEmployees(); track employee.id) {
                <tr>
                  <td>{{ employee.employeeId }}</td>
                  <td>{{ employee.gross | number: '1.2-2' }}</td>
                  <td>{{ employee.netPay | number: '1.2-2' }}</td>
                </tr>
              }
            </tbody>
          </table>
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

    .run-grid {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 0.25rem 0.75rem;
      margin: 0 0 1rem;
      font-size: 0.875rem;
    }

    .run-grid dt {
      font-weight: 600;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .run-grid dd {
      margin: 0;
    }

    .actions {
      display: flex;
      gap: 0.75rem;
    }

    .field__error {
      margin: 0.75rem 0 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .results-table {
      width: 100%;
      border-collapse: collapse;
    }

    .results-table th,
    .results-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
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
export class PayrollRunDetailPage {
  private readonly api = inject(PayrollApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly run = signal<PayrollRun | null>(null);
  protected readonly runEmployees = signal<readonly PayrollRunEmployee[]>([]);
  protected readonly isActing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  private readonly runId = this.route.snapshot.paramMap.get('id') ?? '';

  constructor() {
    void this.load();
  }

  protected async calculate(): Promise<void> {
    await this.act(() => this.api.calculatePayrollRun(this.runId));
  }

  protected async finalize(): Promise<void> {
    await this.act(() => this.api.finalizePayrollRun(this.runId));
  }

  private async act(run: () => Promise<PayrollRun>): Promise<void> {
    if (this.isActing()) {
      return;
    }

    this.isActing.set(true);
    this.errorMessage.set(null);

    try {
      this.run.set(await run());
      this.runEmployees.set(await this.api.listPayrollRunEmployees(this.runId));
    } catch {
      this.errorMessage.set('That action could not be completed. Please try again.');
    } finally {
      this.isActing.set(false);
    }
  }

  private async load(): Promise<void> {
    this.run.set(await this.api.getPayrollRun(this.runId));
    this.runEmployees.set(await this.api.listPayrollRunEmployees(this.runId));
  }
}
