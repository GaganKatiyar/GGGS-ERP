import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import type { WorkflowRequestDetail } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton } from '@gggs/shared/ui';
import { WorkflowApi } from '@gggs/shared/workflow';
import { RequestView } from './request-view';

/** One request, with what its requester can still do: withdraw it, or resubmit it after a send-back. */
@Component({
  selector: 'gggs-request-detail-page',
  imports: [RouterLink, PageHeader, Skeleton, EmptyState, FormDialog, RequestView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="This request could not be shown" [description]="error()!" />
    } @else if (!detail()) {
      <gggs-skeleton [lines]="8" />
    } @else {
      <gggs-page-header [title]="detail()!.request.title" [subtitle]="detail()!.request.subjectName">
        <div class="toolbar">
          @if (detail()!.canResubmit) {
            <button type="button" class="btn btn--primary" (click)="openAction('resubmit')">Resubmit</button>
          }
          @if (detail()!.canWithdraw) {
            <button type="button" class="btn btn--danger" (click)="openAction('withdraw')">Withdraw</button>
          }
          <a class="btn" routerLink="/approvals/requests">All my requests</a>
        </div>
      </gggs-page-header>

      <section class="panel">
        <gggs-request-view [detail]="detail()!" />
      </section>
    }

    <gggs-form-dialog
      [(visible)]="actionOpen"
      [title]="action() === 'resubmit' ? 'Resubmit this request' : 'Withdraw this request'"
      [saveLabel]="action() === 'resubmit' ? 'Resubmit' : 'Withdraw'"
      [busy]="busy()"
      [error]="actionError()"
      (save)="confirm()"
    >
      <label class="field">
        <span class="field__label">{{ action() === 'resubmit' ? 'What changed' : 'Why' }}</span>
        <textarea rows="3" maxlength="1000" [value]="comment()" (input)="comment.set($any($event.target).value)"></textarea>
      </label>
    </gggs-form-dialog>
  `,
})
export class RequestDetailPage {
  private readonly api = inject(WorkflowApi);

  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  protected readonly detail = signal<WorkflowRequestDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly action = signal<'resubmit' | 'withdraw'>('resubmit');
  protected readonly actionOpen = signal(false);
  protected readonly comment = signal('');
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  constructor() {
    effect(() => void this.load(this.id()));
  }

  protected openAction(action: 'resubmit' | 'withdraw'): void {
    this.action.set(action);
    this.comment.set('');
    this.actionError.set(null);
    this.actionOpen.set(true);
  }

  protected async confirm(): Promise<void> {
    this.busy.set(true);
    this.actionError.set(null);
    try {
      const comment = this.comment().trim() || null;
      if (this.action() === 'resubmit') {
        await this.api.resubmit(this.id(), comment);
      } else {
        await this.api.withdraw(this.id(), comment);
      }
      this.actionOpen.set(false);
      await this.load(this.id());
    } catch (error) {
      this.actionError.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    this.error.set(null);
    try {
      this.detail.set(await this.api.request(id));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Try again in a moment.');
    }
  }
}
