import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { Payslip, PayslipListItem } from '@gggs/shared/models';
import { PayrollApi } from '../data/payroll-api';

/** Self-service: an employee's own payslips, the breakdown for whichever one they pick, and a
 * PDF download rendered fresh on each request by the backend's QuestPDF document. */
@Component({
  selector: 'gggs-my-payslips-page',
  imports: [DecimalPipe, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My payslips" subtitle="Published once a payroll run is finalized." />

    <gggs-card title="Payslips" class="section">
      @if (payslips(); as items) {
        @if (items.length > 0) {
          <table class="payslips-table">
            <thead>
              <tr>
                <th scope="col">Run</th>
                <th scope="col">Pay date</th>
                <th scope="col">Net pay</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              @for (item of items; track item.id) {
                <tr>
                  <td>{{ item.runNo }}</td>
                  <td>{{ item.payDate }}</td>
                  <td>{{ item.netPay | number: '1.2-2' }}</td>
                  <td class="actions">
                    <gggs-button size="sm" variant="ghost" (click)="view(item.id)">View</gggs-button>
                    <gggs-button size="sm" variant="ghost" (click)="downloadPdf(item.id, item.runNo)">Download PDF</gggs-button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No payslips published yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }
    </gggs-card>

    @if (selected(); as payslip) {
      <gggs-card title="{{ payslip.runNo }} - {{ payslip.periodCode }}">
        <dl class="payslip-grid">
          <dt>Pay date</dt>
          <dd>{{ payslip.payDate }}</dd>
          <dt>Gross</dt>
          <dd>{{ payslip.gross | number: '1.2-2' }}</dd>
          <dt>Net pay</dt>
          <dd>{{ payslip.netPay | number: '1.2-2' }}</dd>
          <dt>Published</dt>
          <dd>{{ payslip.publishedDate }}</dd>
        </dl>

        <table class="lines-table">
          <thead>
            <tr>
              <th scope="col">Component</th>
              <th scope="col">Type</th>
              <th scope="col">Amount</th>
            </tr>
          </thead>
          <tbody>
            @for (line of payslip.lines; track line.componentId) {
              <tr>
                <td>{{ line.componentName }}</td>
                <td>{{ line.kind }}</td>
                <td>{{ line.amount | number: '1.2-2' }}</td>
              </tr>
            }
          </tbody>
        </table>
      </gggs-card>
    }
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .payslips-table,
    .lines-table {
      width: 100%;
      border-collapse: collapse;
    }

    .payslips-table th,
    .payslips-table td,
    .lines-table th,
    .lines-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .payslip-grid {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 0.25rem 0.75rem;
      margin: 0 0 1rem;
      font-size: 0.875rem;
    }

    .payslip-grid dt {
      font-weight: 600;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .payslip-grid dd {
      margin: 0;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0;
    }

    .actions {
      display: flex;
      gap: 0.5rem;
    }
  `,
})
export class MyPayslipsPage {
  private readonly api = inject(PayrollApi);

  protected readonly payslips = signal<readonly PayslipListItem[] | null>(null);
  protected readonly selected = signal<Payslip | null>(null);

  constructor() {
    void this.load();
  }

  protected async view(id: string): Promise<void> {
    this.selected.set(await this.api.getPayslip(id));
  }

  protected async downloadPdf(id: string, runNo: string): Promise<void> {
    const blob = await this.api.getPayslipPdf(id);
    const url = URL.createObjectURL(blob);
    try {
      const link = document.createElement('a');
      link.href = url;
      link.download = `payslip-${runNo}.pdf`;
      link.click();
    } finally {
      URL.revokeObjectURL(url);
    }
  }

  private async load(): Promise<void> {
    this.payslips.set(await this.api.listMyPayslips());
  }
}
