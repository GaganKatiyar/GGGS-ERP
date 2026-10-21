import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { KpiCard, PageHeader } from '@gggs/shared/ui';
import { AssistantApi, type UsageRow } from './assistant-api';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/** What the assistants are used for and what they cost in tokens, by assistant, for administrators. */
@Component({
  selector: 'gggs-assistant-usage-page',
  imports: [DecimalPipe, RouterLink, PageHeader, KpiCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Assistant usage" subtitle="Conversations, tokens, tool calls and approved actions">
      <select aria-label="Period" class="usage__period" [value]="days()" (change)="days.set(+$any($event.target).value)">
        <option value="7">Last 7 days</option>
        <option value="30">Last 30 days</option>
        <option value="90">Last 90 days</option>
      </select>
      <a class="btn" routerLink="..">Back to assistants</a>
    </gggs-page-header>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    <div class="usage__kpis">
      <gggs-kpi-card label="Conversations" [value]="total().conversations" icon="pi pi-comments" />
      <gggs-kpi-card label="Turns" [value]="total().turns" icon="pi pi-reply" />
      <gggs-kpi-card label="Tokens" [value]="(total().inputTokens + total().outputTokens | number) ?? '0'" icon="pi pi-database" />
      <gggs-kpi-card label="Actions approved" [value]="total().actionsExecuted + ' of ' + total().actionsProposed" icon="pi pi-check-circle" />
    </div>
    <div class="panel">
      <table class="table usage__table">
        <thead>
          <tr>
            <th scope="col">Assistant</th>
            <th scope="col">Conversations</th>
            <th scope="col">Turns</th>
            <th scope="col">Input tokens</th>
            <th scope="col">Output tokens</th>
            <th scope="col">Tool calls</th>
            <th scope="col">Proposed</th>
            <th scope="col">Executed</th>
          </tr>
        </thead>
        <tbody>
          @for (row of rows(); track row.assistantKey) {
            <tr>
              <td>{{ row.assistantKey }}</td>
              <td>{{ row.conversations }}</td>
              <td>{{ row.turns }}</td>
              <td>{{ row.inputTokens | number }}</td>
              <td>{{ row.outputTokens | number }}</td>
              <td>{{ row.toolCalls }}</td>
              <td>{{ row.actionsProposed }}</td>
              <td>{{ row.actionsExecuted }}</td>
            </tr>
          } @empty {
            <tr><td colspan="8" class="muted">No assistant use in this period.</td></tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: `
    .usage__kpis {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(9rem, 45%), 1fr));
      gap: var(--gggs-space-3);
      margin-block-end: var(--gggs-space-3);
    }
    .usage__period {
      width: auto;
    }
  `,
})
export class UsagePage {
  private readonly api = inject(AssistantApi);
  protected readonly days = signal(30);
  protected readonly rows = signal<readonly UsageRow[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly total = computed(() =>
    this.rows().reduce(
      (sum, row) => ({
        conversations: sum.conversations + row.conversations,
        turns: sum.turns + row.turns,
        inputTokens: sum.inputTokens + row.inputTokens,
        outputTokens: sum.outputTokens + row.outputTokens,
        actionsProposed: sum.actionsProposed + row.actionsProposed,
        actionsExecuted: sum.actionsExecuted + row.actionsExecuted,
      }),
      { conversations: 0, turns: 0, inputTokens: 0, outputTokens: 0, actionsProposed: 0, actionsExecuted: 0 },
    ),
  );

  constructor() {
    effect(() => void this.load(this.days()));
  }

  private async load(days: number): Promise<void> {
    try {
      this.error.set(null);
      this.rows.set(await this.api.usage(days));
    } catch (error) {
      this.error.set(message(error, 'Usage could not be loaded.'));
    }
  }
}
