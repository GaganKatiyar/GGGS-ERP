import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi, isoToday } from '@gggs/shared/delivery';
import type { ChangeRequest, ChangeRequestStatus, WorkPriority } from '@gggs/shared/models';
import { EmptyState, FormDialog, StatusBadge } from '@gggs/shared/ui';

/** What comes after each status (spec §50): implement, test, close. */
const NEXT: Partial<Record<ChangeRequestStatus, readonly ChangeRequestStatus[]>> = {
  Approved: ['Implementing', 'Closed'],
  Implementing: ['Testing'],
  Testing: ['Closed', 'Implementing'],
};

/**
 * Change requests (spec §50): drafted with their impact, approved through the tenant's workflow
 * (impact analysis, project manager, client), then implemented, tested and closed.
 */
@Component({
  selector: 'gggs-changes-tab',
  imports: [DatePipe, DecimalPipe, FormDialog, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel stack">
      <div class="toolbar">
        <h2 class="cr__heading">Change requests</h2>
        @if (canEdit()) {
          <button type="button" class="btn btn--primary btn--sm" (click)="edit(null)">Raise a change</button>
        }
      </div>
      @for (request of requests(); track request.id) {
        <article class="cr__item">
          <div class="cr__row">
            <strong>{{ request.code }}</strong>
            <span class="cr__title">{{ request.title }}</span>
            <gggs-status-badge [status]="request.status" />
          </div>
          <p class="muted cr__meta">
            Raised {{ request.createdDate | date: 'd MMM y' }}{{ request.requestedByName ? ' by ' + request.requestedByName : '' }} ·
            {{ request.estimatedHours ?? 0 | number: '1.0-0' }} h · {{ request.estimatedCost ?? 0 | number: '1.0-0' }} cost ·
            {{ request.scheduleImpactDays ?? 0 }} day(s) on the schedule · {{ request.priority }} priority
            @if (request.clientApprovedOn) {
              · client approved {{ request.clientApprovedOn | date: 'd MMM' }}{{ request.clientApprovalReference ? ' (' + request.clientApprovalReference + ')' : '' }}
            }
          </p>
          <p class="cr__description">{{ request.description }}</p>
          @if (request.impact) {
            <p class="cr__description"><strong>Impact:</strong> {{ request.impact }}</p>
          }
          @if (canEdit()) {
            <div class="toolbar cr__actions">
              @if (request.status === 'Draft') {
                <button type="button" class="btn btn--sm" (click)="edit(request)">Edit</button>
                <button type="button" class="btn btn--primary btn--sm" (click)="submit(request)">Submit for approval</button>
              }
              @if (request.status === 'Draft' || request.status === 'PendingApproval') {
                <button type="button" class="btn btn--link btn--sm" (click)="withdraw(request)">Withdraw</button>
              }
              @if (!request.clientApprovedOn && (request.status === 'Approved' || request.status === 'Implementing' || request.status === 'Testing')) {
                <button type="button" class="btn btn--sm" (click)="openClientApproval(request)">Record client approval</button>
              }
              @for (next of nextOf(request); track next) {
                <button type="button" class="btn btn--sm" (click)="advance(request, next)">{{ next === 'Closed' ? 'Close' : 'Move to ' + next.toLowerCase() }}</button>
              }
            </div>
          }
        </article>
      } @empty {
        <gggs-empty-state icon="pi pi-arrow-right-arrow-left" title="No change requests" description="Scope changes go through here, with their cost and schedule impact." />
      }
      @if (message()) {
        <p class="cr__message" role="status">{{ message() }}</p>
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>

    <gggs-form-dialog [(visible)]="formOpen" [title]="editing() ? 'Edit ' + editing()?.code : 'Raise a change request'" size="lg" [busy]="busy()" [error]="dialogError()" (save)="save()">
      <div class="form-grid">
        <label class="field form-grid__full">
          <span class="field__label">Title *</span>
          <input type="text" maxlength="300" [value]="title()" (input)="title.set($any($event.target).value)" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">What is asked for *</span>
          <textarea rows="3" maxlength="8000" [value]="description()" (input)="description.set($any($event.target).value)"></textarea>
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Why</span>
          <input type="text" maxlength="2000" [value]="reason()" (input)="reason.set($any($event.target).value)" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Impact analysis</span>
          <textarea rows="3" maxlength="4000" [value]="impact()" (input)="impact.set($any($event.target).value)"></textarea>
        </label>
        <label class="field">
          <span class="field__label">Estimated hours</span>
          <input type="number" min="0" [value]="hours() ?? ''" (input)="hours.set(num($any($event.target).value))" />
        </label>
        <label class="field">
          <span class="field__label">Estimated cost</span>
          <input type="number" min="0" step="1000" [value]="cost() ?? ''" (input)="cost.set(num($any($event.target).value))" />
        </label>
        <label class="field">
          <span class="field__label">Schedule impact (days)</span>
          <input type="number" [value]="days() ?? ''" (input)="days.set(num($any($event.target).value))" />
        </label>
        <label class="field">
          <span class="field__label">Priority</span>
          <select [value]="priority()" (change)="priority.set($any($event.target).value)">
            <option value="Low">Low</option>
            <option value="Medium">Medium</option>
            <option value="High">High</option>
            <option value="Critical">Critical</option>
          </select>
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Requested by <span class="muted">(someone at the client; empty means you)</span></span>
          <input type="text" maxlength="200" [value]="requestedBy()" (input)="requestedBy.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="clientOpen" title="Client approval" [busy]="busy()" [error]="dialogError()" (save)="recordClientApproval()">
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Approved on</span>
          <input type="date" [value]="clientOn()" (input)="clientOn.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Reference</span>
          <input type="text" maxlength="200" placeholder="Email subject, PO amendment…" [value]="clientReference()" (input)="clientReference.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .cr__heading {
      margin-inline-end: auto;
      margin-block: 0;
      font-size: var(--gggs-font-size-md);
    }

    .cr__item {
      padding-block: var(--gggs-space-3);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .cr__row {
      display: flex;
      gap: var(--gggs-space-3);
      align-items: center;
    }

    .cr__title {
      margin-inline-end: auto;}

    .cr__meta {
      margin: var(--gggs-space-1) 0;
      font-size: var(--gggs-font-size-sm);
    }

    .cr__description {
      margin: var(--gggs-space-1) 0;
      white-space: pre-wrap;
    }

    .cr__actions {
      flex-wrap: wrap;
    }

    .cr__message {
      color: var(--gggs-color-success);
    }
  `,
})
export class ChangesTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();
  readonly canEdit = input(false);

  protected readonly requests = signal<readonly ChangeRequest[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly formOpen = signal(false);
  protected readonly editing = signal<ChangeRequest | null>(null);
  protected readonly title = signal('');
  protected readonly description = signal('');
  protected readonly reason = signal('');
  protected readonly impact = signal('');
  protected readonly hours = signal<number | null>(null);
  protected readonly cost = signal<number | null>(null);
  protected readonly days = signal<number | null>(null);
  protected readonly priority = signal<WorkPriority>('Medium');
  protected readonly requestedBy = signal('');
  protected readonly clientOpen = signal(false);
  protected readonly clientTarget = signal<ChangeRequest | null>(null);
  protected readonly clientOn = signal(isoToday());
  protected readonly clientReference = signal('');
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  constructor() {
    effect(() => {
      void this.load(this.projectId());
    });
  }

  protected num(value: string): number | null {
    return value === '' ? null : Number(value);
  }

  protected nextOf(request: ChangeRequest): readonly ChangeRequestStatus[] {
    return NEXT[request.status] ?? [];
  }

  protected edit(request: ChangeRequest | null): void {
    this.editing.set(request);
    this.title.set(request?.title ?? '');
    this.description.set(request?.description ?? '');
    this.reason.set(request?.reason ?? '');
    this.impact.set(request?.impact ?? '');
    this.hours.set(request?.estimatedHours ?? null);
    this.cost.set(request?.estimatedCost ?? null);
    this.days.set(request?.scheduleImpactDays ?? null);
    this.priority.set(request?.priority ?? 'Medium');
    this.requestedBy.set(request?.requestedByEmployeeId ? '' : (request?.requestedByName ?? ''));
    this.dialogError.set(null);
    this.formOpen.set(true);
  }

  protected async save(): Promise<void> {
    if (!this.title().trim() || !this.description().trim()) {
      this.dialogError.set('Say what is asked for, with a title.');
      return;
    }

    await this.dialog(async () => {
      await this.api.saveChangeRequest(this.projectId(), this.editing()?.id ?? null, {
        title: this.title().trim(),
        description: this.description().trim(),
        reason: this.reason() || null,
        impact: this.impact() || null,
        estimatedHours: this.hours(),
        estimatedCost: this.cost(),
        scheduleImpactDays: this.days(),
        priority: this.priority(),
        requestedByName: this.requestedBy().trim() || null,
      });
      this.formOpen.set(false);
    });
  }

  protected async submit(request: ChangeRequest): Promise<void> {
    await this.act(async () => {
      const result = await this.api.submitChangeRequest(this.projectId(), request.id);
      this.message.set(result.status === 'PendingApproval' ? `${request.code} is with the approvers.` : `${request.code} is approved.`);
    });
  }

  protected async withdraw(request: ChangeRequest): Promise<void> {
    await this.act(() => this.api.withdrawChangeRequest(this.projectId(), request.id));
  }

  protected async advance(request: ChangeRequest, status: ChangeRequestStatus): Promise<void> {
    await this.act(() => this.api.advanceChangeRequest(this.projectId(), request.id, status));
  }

  protected openClientApproval(request: ChangeRequest): void {
    this.clientTarget.set(request);
    this.clientOn.set(isoToday());
    this.clientReference.set('');
    this.dialogError.set(null);
    this.clientOpen.set(true);
  }

  protected async recordClientApproval(): Promise<void> {
    const request = this.clientTarget();
    if (request) {
      await this.dialog(async () => {
        await this.api.recordClientApproval(this.projectId(), request.id, this.clientOn(), this.clientReference() || null);
        this.clientOpen.set(false);
      });
    }
  }

  private async act(action: () => Promise<unknown>): Promise<void> {
    this.error.set(null);
    this.message.set(null);
    try {
      await action();
      await this.load(this.projectId());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    }
  }

  private async dialog(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await action();
      await this.load(this.projectId());
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(projectId: string): Promise<void> {
    try {
      this.requests.set(await this.api.changeRequests(projectId));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Change requests could not be loaded.');
    }
  }
}
