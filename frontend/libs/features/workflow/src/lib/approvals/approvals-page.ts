import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import { WORKFLOW_TASK_STATUS_LABELS, type WorkflowTaskItem } from '@gggs/shared/models';
import { ConfirmationService, MessageService } from 'primeng/api';
import { WorkflowApi } from '../workflow-api';

/**
 * The unified approvals inbox - architecture.md §12.1: everything pending for the caller, across
 * every module, one place. `entityType` names the source record kind (`LEAVE_REQUEST`,
 * `ATTENDANCE_REGULARIZATION`, …); v1 shows it as a readable label rather than linking through to
 * each module's own detail view, since that routing table only grows as more modules adopt
 * Workflow.
 */
@Component({
  selector: 'gggs-approvals-page',
  imports: [PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Approvals" subtitle="Everything waiting on your decision." />

    <gggs-card>
      @if (tasks().length === 0) {
        <p class="muted">
          @if (isLoading()) {
            Loading…
          } @else {
            Nothing waiting on you right now.
          }
        </p>
      } @else {
        <table class="tasks-table">
          <thead>
            <tr>
              <th scope="col">Type</th>
              <th scope="col">Step</th>
              <th scope="col">Due</th>
              <th scope="col">Status</th>
              <th scope="col"></th>
            </tr>
          </thead>
          <tbody>
            @for (task of tasks(); track task.id) {
              <tr>
                <td>{{ entityTypeLabel(task.entityType) }}</td>
                <td>{{ task.stepKey }}</td>
                <td>{{ task.dueDate ?? '—' }}</td>
                <td>{{ statusLabel(task) }}</td>
                <td class="tasks-table__actions">
                  @if (task.status === 'Pending') {
                    <gggs-button
                      size="sm"
                      [loading]="actingTaskId() === task.id"
                      (click)="approve(task)"
                    >
                      Approve
                    </gggs-button>
                    <gggs-button
                      size="sm"
                      variant="danger"
                      [loading]="actingTaskId() === task.id"
                      (click)="confirmReject(task)"
                    >
                      Reject
                    </gggs-button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
    </gggs-card>
  `,
  styles: `
    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0;
    }

    .tasks-table {
      width: 100%;
      border-collapse: collapse;
    }

    .tasks-table th,
    .tasks-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .tasks-table__actions {
      display: flex;
      gap: 0.5rem;
    }
  `,
})
export class ApprovalsPage {
  private readonly api = inject(WorkflowApi);
  private readonly confirmation = inject(ConfirmationService);
  private readonly messages = inject(MessageService);

  protected readonly tasks = signal<readonly WorkflowTaskItem[]>([]);
  protected readonly isLoading = signal(false);
  protected readonly actingTaskId = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected entityTypeLabel(entityType: string): string {
    // "LEAVE_REQUEST" -> "Leave request"
    const lower = entityType.toLowerCase().replace(/_/g, ' ');
    return lower.charAt(0).toUpperCase() + lower.slice(1);
  }

  protected statusLabel(task: WorkflowTaskItem): string {
    return WORKFLOW_TASK_STATUS_LABELS[task.status];
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      const result = await this.api.listMyTasks({ page: 1, pageSize: 50, status: 'Pending' });
      this.tasks.set(result.items);
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async approve(task: WorkflowTaskItem): Promise<void> {
    this.actingTaskId.set(task.id);

    try {
      await this.api.approveTask(task.id, {});
      this.messages.add({ severity: 'success', summary: 'Approved' });
      await this.load();
    } catch (error) {
      this.messages.add({
        severity: 'error',
        summary: 'Could not approve',
        detail: error instanceof ApiError ? error.message : undefined,
      });
    } finally {
      this.actingTaskId.set(null);
    }
  }

  protected confirmReject(task: WorkflowTaskItem): void {
    this.confirmation.confirm({
      header: 'Reject this request?',
      message: 'The requester will be notified and the request will not proceed.',
      acceptLabel: 'Reject',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: 'danger' },
      accept: () => void this.reject(task),
    });
  }

  private async reject(task: WorkflowTaskItem): Promise<void> {
    this.actingTaskId.set(task.id);

    try {
      await this.api.rejectTask(task.id, {});
      this.messages.add({ severity: 'success', summary: 'Rejected' });
      await this.load();
    } catch (error) {
      this.messages.add({
        severity: 'error',
        summary: 'Could not reject',
        detail: error instanceof ApiError ? error.message : undefined,
      });
    } finally {
      this.actingTaskId.set(null);
    }
  }
}
