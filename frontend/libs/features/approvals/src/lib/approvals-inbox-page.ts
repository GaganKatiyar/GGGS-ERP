import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import type { PagedResult, WorkflowRequestDetail, WorkflowTask } from '@gggs/shared/models';
import { Drawer, EmptyState, PageHeader, Skeleton, StatusBadge, Tabs, type TabItem } from '@gggs/shared/ui';
import { WorkflowApi, type TaskDecision } from '@gggs/shared/workflow';
import { RequestView } from './request-view';

/**
 * The approvals inbox (spec §109): what is waiting for me, oldest deadline first, and what I have
 * already decided. Opening a task shows the whole request beside the decision.
 */
@Component({
  selector: 'gggs-approvals-inbox-page',
  imports: [DatePipe, RouterLink, PageHeader, Tabs, Skeleton, EmptyState, StatusBadge, Drawer, RequestView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Approvals" subtitle="Requests waiting for your decision.">
      <div class="toolbar">
        <a class="btn" routerLink="requests"><i class="pi pi-send" aria-hidden="true"></i> My requests</a>
        <a class="btn" routerLink="delegations"><i class="pi pi-arrow-right-arrow-left" aria-hidden="true"></i> Delegations</a>
      </div>
    </gggs-page-header>

    <gggs-tabs [tabs]="tabs" [selected]="view()" (selectedChange)="switchTo($event)" />

    <div class="inbox__body">
      @if (loading()) {
        <gggs-skeleton [lines]="5" />
      } @else if (!tasks() || tasks()!.items.length === 0) {
        <gggs-empty-state
          icon="pi pi-check-circle"
          [title]="view() === 'pending' ? 'Nothing waiting for you' : 'No decisions yet'"
          [description]="view() === 'pending' ? 'New requests appear here and in your notifications.' : undefined"
        />
      } @else {
        <ul class="inbox__list">
          @for (task of tasks()!.items; track task.id) {
            <li>
              <button type="button" class="inbox__item" (click)="open(task)">
                <span class="inbox__main">
                  <strong>{{ task.title }}</strong>
                  <span class="muted">
                    {{ task.subjectName }} · {{ task.stageName }} · from {{ task.requesterName ?? 'someone' }}
                    @if (task.delegatedFromName) {
                      · on behalf of {{ task.delegatedFromName }}
                    }
                  </span>
                </span>
                <span class="inbox__meta">
                  @if (view() === 'pending') {
                    @if (task.isOverdue) {
                      <gggs-status-badge status="Overdue" />
                    } @else if (task.dueDate) {
                      <span class="muted">due {{ task.dueDate | date: 'short' }}</span>
                    }
                  } @else {
                    <gggs-status-badge [status]="task.status" />
                    <span class="muted">{{ task.actedDate | date: 'short' }}</span>
                  }
                </span>
              </button>
            </li>
          }
        </ul>
      }
      @if (listError()) {
        <p class="field__error" role="alert">{{ listError() }}</p>
      }
    </div>

    <gggs-drawer [(visible)]="drawerOpen" [title]="selected()?.title ?? 'Request'">
      @if (detail(); as request) {
        <div class="stack">
          <gggs-request-view [detail]="request" />

          @if (selected()?.status === 'Pending') {
            <section class="panel stack">
              <label class="field">
                <span class="field__label">Comment <span class="muted">(required to reject or send back)</span></span>
                <textarea rows="3" maxlength="1000" [value]="comment()" (input)="comment.set($any($event.target).value)"></textarea>
              </label>
              @if (decisionError()) {
                <p class="field__error" role="alert">{{ decisionError() }}</p>
              }
              <div class="toolbar">
                <button type="button" class="btn btn--primary" [disabled]="deciding()" (click)="decide('approve')">Approve</button>
                <button type="button" class="btn" [disabled]="deciding()" (click)="decide('send-back')">Send back</button>
                <button type="button" class="btn btn--danger" [disabled]="deciding()" (click)="decide('reject')">Reject</button>
              </div>
            </section>
          }
        </div>
      } @else {
        <gggs-skeleton [lines]="6" />
      }
    </gggs-drawer>
  `,
  styles: `
    .inbox__body {
      margin-block-start: var(--gggs-space-4);
    }

    .inbox__list {
      margin: 0;
      padding: 0;
      list-style: none;
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-raised);
    }

    .inbox__item {
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

    .inbox__item:hover {
      background: var(--gggs-color-surface-hover);
    }

    .inbox__main {
      display: flex;
      flex: 1;
      flex-direction: column;
      gap: 0.125rem;
    }

    .inbox__meta {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-2);
      white-space: nowrap;
    }
  `,
})
export class ApprovalsInboxPage {
  private readonly api = inject(WorkflowApi);

  protected readonly tabs: readonly TabItem[] = [
    { key: 'pending', label: 'Waiting for me' },
    { key: 'done', label: 'Decided' },
  ];

  protected readonly view = signal<'pending' | 'done'>('pending');
  protected readonly tasks = signal<PagedResult<WorkflowTask> | null>(null);
  protected readonly loading = signal(true);
  protected readonly listError = signal<string | null>(null);

  protected readonly drawerOpen = signal(false);
  protected readonly selected = signal<WorkflowTask | null>(null);
  protected readonly detail = signal<WorkflowRequestDetail | null>(null);
  protected readonly comment = signal('');
  protected readonly deciding = signal(false);
  protected readonly decisionError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected switchTo(key: string): void {
    this.view.set(key === 'done' ? 'done' : 'pending');
    void this.load();
  }

  protected async open(task: WorkflowTask): Promise<void> {
    this.selected.set(task);
    this.detail.set(null);
    this.comment.set('');
    this.decisionError.set(null);
    this.drawerOpen.set(true);
    try {
      this.detail.set(await this.api.request(task.instanceId));
    } catch (error) {
      this.decisionError.set(error instanceof ApiError ? error.message : 'The request could not be loaded.');
    }
  }

  protected async decide(decision: TaskDecision): Promise<void> {
    const task = this.selected();
    if (!task) {
      return;
    }

    const comment = this.comment().trim();
    if (decision !== 'approve' && !comment) {
      this.decisionError.set('Say why, so the requester knows what to change.');
      return;
    }

    this.deciding.set(true);
    this.decisionError.set(null);
    try {
      await this.api.decide(task.id, decision, comment || null);
      this.drawerOpen.set(false);
      await this.load();
    } catch (error) {
      this.decisionError.set(error instanceof ApiError ? error.message : 'The decision could not be saved. Try again.');
    } finally {
      this.deciding.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.listError.set(null);
    try {
      this.tasks.set(await this.api.tasks(this.view()));
    } catch (error) {
      this.tasks.set(null);
      this.listError.set(error instanceof ApiError ? error.message : 'Your approvals could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
