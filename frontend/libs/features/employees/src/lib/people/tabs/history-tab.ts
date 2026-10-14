import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import type { EmploymentHistoryEntry } from '@gggs/shared/models';
import { EmptyState, Skeleton, Timeline, type TimelineEntry } from '@gggs/shared/ui';

const CHANGE_LABELS: Record<string, string> = {
  Hire: 'Hired',
  Transfer: 'Transferred',
  Promotion: 'Promoted',
  ManagerChange: 'Manager changed',
  Redesignation: 'Redesignated',
  EmploymentTermsChange: 'Employment terms changed',
  Rehire: 'Rehired',
};

/** Every employment record, newest first: what changed, from when, and why. */
@Component({
  selector: 'gggs-history-tab',
  imports: [Timeline, Skeleton, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="History could not be loaded" [description]="error()!" />
    } @else if (!history()) {
      <gggs-skeleton [lines]="5" />
    } @else if (entries().length === 0) {
      <gggs-empty-state icon="pi pi-history" title="No history yet" />
    } @else {
      <section class="panel">
        <gggs-timeline [entries]="entries()" />
      </section>
    }
  `,
})
export class HistoryTab {
  private readonly api = inject(EmployeeDirectoryApi);

  readonly employeeId = input.required<string>();

  protected readonly history = signal<readonly EmploymentHistoryEntry[] | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly entries = computed<readonly TimelineEntry[]>(() =>
    (this.history() ?? []).map((entry) => ({
      date: entry.effectiveFrom,
      title: CHANGE_LABELS[entry.changeType] ?? entry.changeType,
      description: [entry.designation, entry.departmentName, entry.positionName, entry.gradeName, entry.locationName, entry.managerName ? `reports to ${entry.managerName}` : null, entry.reason]
        .filter(Boolean)
        .join(' · '),
      tone: entry.changeType === 'Promotion' ? 'success' : entry.effectiveTo ? 'neutral' : 'primary',
    })),
  );

  constructor() {
    effect(() => {
      const id = this.employeeId();
      this.history.set(null);
      this.error.set(null);
      this.api.history(id).then(
        (history) => this.history.set(history),
        (error: unknown) => this.error.set(error instanceof ApiError ? error.message : 'Try again in a moment.'),
      );
    });
  }
}
