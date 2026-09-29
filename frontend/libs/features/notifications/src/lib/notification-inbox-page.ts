import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DataTable, type DataTableColumn, type DataTableQuery } from '@gggs/shared/data-table';
import { NotificationsApi } from '@gggs/shared/notifications';
import { PageHeader } from '@gggs/shared/ui';
import type { InAppNotificationItem, PagedResult } from '@gggs/shared/models';

/**
 * A signed-in user's own notification inbox.
 *
 * Clicking an unread row marks it read and, when the notification carries a link, follows it -
 * the same "act on it, don't just look at it" behaviour a bell icon's dropdown gives you, without
 * this screen needing its own separate click target for "mark read" on every row.
 */
@Component({
  selector: 'gggs-notification-inbox-page',
  imports: [DataTable, PageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Notifications" subtitle="Things that happened, addressed to you.">
      <button type="button" class="btn" [disabled]="isMarkingAllRead()" (click)="markAllRead()">
        {{ isMarkingAllRead() ? 'Marking…' : 'Mark all as read' }}
      </button>
    </gggs-page-header>

    <gggs-data-table
      [result]="result()"
      [columns]="columns"
      [loading]="isLoading()"
      emptyTitle="No notifications yet"
      emptyDescription="Nothing has been sent to you so far."
      (queryChange)="load($event)"
      (rowClick)="open($event)"
    />
  `,
  styles: `
    .btn {
      padding: 0.5rem 1rem;
      font-weight: 600;
      font-family: inherit;
      font-size: inherit;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #ffffff);
      cursor: pointer;
    }

    .btn:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }
  `,
})
export class NotificationInboxPage {
  private readonly api = inject(NotificationsApi);

  protected readonly isLoading = signal(false);
  protected readonly isMarkingAllRead = signal(false);
  protected readonly result = signal<PagedResult<InAppNotificationItem> | null>(null);

  protected readonly columns: readonly DataTableColumn<InAppNotificationItem>[] = [
    { field: 'title', header: 'Title' },
    { field: 'body', header: 'Details' },
    { field: 'createdDate', header: 'Received', width: '11rem', sortable: true },
    { field: 'isRead', header: 'Read', width: '6rem' },
  ];

  private lastQuery: DataTableQuery = { page: 1, pageSize: 25 };

  constructor() {
    void this.load(this.lastQuery);
  }

  protected async load(query: DataTableQuery): Promise<void> {
    this.lastQuery = query;
    this.isLoading.set(true);

    try {
      this.result.set(await this.api.list({ page: query.page, pageSize: query.pageSize }));
    } finally {
      this.isLoading.set(false);
    }
  }

  protected async open(notification: InAppNotificationItem): Promise<void> {
    if (!notification.isRead) {
      await this.api.markRead(notification.id);
      await this.load(this.lastQuery);
    }

    if (notification.link) {
      // A plain navigation, not the Angular router: `link` is whatever full URL the notification
      // was built with (see `UserInvitedConsumer`'s own remarks) - an absolute address the router
      // has no route table entry for, not an internal path.
      window.location.href = notification.link;
    }
  }

  protected async markAllRead(): Promise<void> {
    this.isMarkingAllRead.set(true);

    try {
      await this.api.markAllRead();
      await this.load(this.lastQuery);
    } finally {
      this.isMarkingAllRead.set(false);
    }
  }
}
