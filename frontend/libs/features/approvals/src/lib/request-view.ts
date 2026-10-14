import { DatePipe, KeyValuePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { WorkflowActionKind, WorkflowRequestDetail } from '@gggs/shared/models';
import { StatusBadge, Timeline, type TimelineEntry } from '@gggs/shared/ui';

const ACTION_LABELS: Readonly<Record<WorkflowActionKind, string>> = {
  Submitted: 'Submitted',
  Approved: 'Approved',
  Rejected: 'Rejected',
  SentBack: 'Sent back for changes',
  Resubmitted: 'Resubmitted',
  Withdrawn: 'Withdrawn',
  Escalated: 'Escalated',
  Reassigned: 'Reassigned',
  StageSkipped: 'Stage skipped',
  Completed: 'Approved by every stage',
  AutoApproved: 'Approved automatically',
};

const ACTION_TONES: Partial<Record<WorkflowActionKind, TimelineEntry['tone']>> = {
  Approved: 'success',
  Completed: 'success',
  AutoApproved: 'success',
  Rejected: 'danger',
  SentBack: 'warning',
  Escalated: 'warning',
};

/** A request at a glance: what it is, where it stands stage by stage, and everything that happened to it. */
@Component({
  selector: 'gggs-request-view',
  imports: [DatePipe, KeyValuePipe, RouterLink, StatusBadge, Timeline],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let r = detail().request;
    <div class="stack">
      <dl class="definition-list">
        <dt>Request</dt>
        <dd>
          <strong>{{ r.title }}</strong>
          @if (r.link) {
            · <a [routerLink]="r.link">Open</a>
          }
        </dd>
        <dt>Kind</dt><dd>{{ r.subjectName }}</dd>
        <dt>Status</dt><dd><gggs-status-badge [status]="r.status" /> @if (r.round > 1) { <span class="muted">round {{ r.round }}</span> }</dd>
        <dt>Raised by</dt><dd>{{ r.requesterName ?? '—' }}</dd>
        <dt>Submitted</dt><dd>{{ r.submittedDate | date: 'medium' }}</dd>
        @for (field of detail().fields | keyvalue; track field.key) {
          <dt>{{ field.key }}</dt><dd>{{ field.value }}</dd>
        }
      </dl>

      <section>
        <h3 class="request__heading">Stages</h3>
        <ol class="request__stages">
          @for (stage of detail().stages; track stage.index) {
            <li class="request__stage" [class.request__stage--current]="stage.state === 'InProgress'">
              <span class="request__stage-name">{{ stage.index + 1 }}. {{ stage.name }}</span>
              <span class="muted">{{ stage.mode === 'All' ? 'everyone approves' : 'first answer decides' }}{{ stage.slaHours ? ' · ' + stage.slaHours + 'h SLA' : '' }}</span>
              @if (stage.condition) {
                <span class="muted">· when {{ stage.condition }}</span>
              }
              <gggs-status-badge [status]="stage.state" />
              <ul class="request__approvers">
                @for (task of tasksFor(stage.index); track task.id) {
                  <li>
                    {{ task.assigneeName ?? 'Unknown' }}
                    @if (task.delegatedFromName) {
                      <span class="muted">for {{ task.delegatedFromName }}</span>
                    }
                    — <gggs-status-badge [status]="task.isOverdue ? 'Overdue' : task.status" />
                    @if (task.comment) {
                      <span class="muted">"{{ task.comment }}"</span>
                    }
                  </li>
                }
              </ul>
            </li>
          }
        </ol>
      </section>

      <section>
        <h3 class="request__heading">History</h3>
        <gggs-timeline [entries]="history()" />
      </section>
    </div>
  `,
  styles: `
    .request__heading {
      margin: 0 0 var(--gggs-space-2);
      font-size: var(--gggs-font-size-sm);
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--gggs-color-text-muted);
    }

    .request__stages {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .request__stage {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--gggs-space-2);
      padding: var(--gggs-space-2) var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
    }

    .request__stage--current {
      border-color: var(--gggs-color-primary);
    }

    .request__stage-name {
      font-weight: var(--gggs-font-weight-semibold);
    }

    .request__approvers {
      flex-basis: 100%;
      margin: 0;
      padding-inline-start: 1.25rem;
    }
  `,
})
export class RequestView {
  readonly detail = input.required<WorkflowRequestDetail>();

  /** The current round's tasks for a stage; earlier rounds live in the history. */
  protected tasksFor(index: number) {
    const round = this.detail().request.round;
    return this.detail().tasks.filter((task) => task.stageIndex === index && task.round === round);
  }

  protected readonly history = computed<readonly TimelineEntry[]>(() =>
    [...this.detail().history].reverse().map((entry) => ({
      date: entry.occurredDate,
      title: ACTION_LABELS[entry.kind] + (entry.actorName ? ` by ${entry.actorName}` : ''),
      description: entry.comment,
      tone: ACTION_TONES[entry.kind] ?? 'neutral',
    })),
  );
}
