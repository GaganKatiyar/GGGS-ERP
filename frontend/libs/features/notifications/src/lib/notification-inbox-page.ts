import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import type { InAppNotificationItem, PagedResult } from '@gggs/shared/models';
import { NotificationsApi } from '@gggs/shared/notifications';
import { EmptyState, PageHeader, Skeleton } from '@gggs/shared/ui';

/** Everything sent to me: approvals waiting, decisions on my requests, policies to read, documents to sign. */
@Component({
  selector: 'gggs-notification-inbox-page',
  imports: [DatePipe, PageHeader, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Notifications" subtitle="What needs your attention.">
      <div class="toolbar">
        <label class="inbox__filter"><input type="checkbox" [checked]="unreadOnly()" (change)="toggleUnread()" /> Unread only</label>
        <button type="button" class="btn" [disabled]="busy()" (click)="markAllRead()">Mark all as read</button>
      </div>
    </gggs-page-header>

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else if (!page() || page()!.items.length === 0) {
      <gggs-empty-state icon="pi pi-bell" [title]="unreadOnly() ? 'All caught up' : 'No notifications yet'" />
    } @else {
      <ul class="inbox">
        @for (item of page()!.items; track item.id) {
          <li>
            <button type="button" class="inbox__item" [class.inbox__item--unread]="!item.isRead" (click)="open(item)">
              <span class="inbox__dot" aria-hidden="true"></span>
              <span class="inbox__text">
                <strong>{{ item.title }}</strong>
                <span class="muted">{{ item.body }}</span>
              </span>
              <span class="muted inbox__date">{{ item.createdDate | date: 'short' }}</span>
              @if (!item.isRead) {
                <span class="inbox__hidden">Unread</span>
              }
            </button>
          </li>
        }
      </ul>
      @if (page()!.hasNextPage) {
        <div class="toolbar inbox__more">
          <button type="button" class="btn" (click)="more()">Show older</button>
        </div>
      }
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styles: `
    .inbox {
      margin: 0;
      padding: 0;
      list-style: none;
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-raised);
    }

    .inbox__filter {
      font-size: var(--gggs-font-size-sm);
    }

    .inbox__item {
      display: flex;
      align-items: flex-start;
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

    .inbox__dot {
      flex: none;
      width: 0.5rem;
      height: 0.5rem;
      margin-block-start: 0.45rem;
      border-radius: 50%;
      background: transparent;
    }

    .inbox__item--unread .inbox__dot {
      background: var(--gggs-color-primary);
    }

    .inbox__text {
      display: flex;
      flex: 1;
      flex-direction: column;
      gap: 0.125rem;
    }

    .inbox__date {
      white-space: nowrap;
      font-size: var(--gggs-font-size-xs);
    }

    .inbox__more {
      justify-content: center;
      margin-block-start: var(--gggs-space-3);
    }

    .inbox__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class NotificationInboxPage {
  private readonly api = inject(NotificationsApi);
  private readonly router = inject(Router);

  protected readonly page = signal<PagedResult<InAppNotificationItem> | null>(null);
  protected readonly unreadOnly = signal(false);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private pageSize = 25;

  constructor() {
    void this.load();
  }

  protected toggleUnread(): void {
    this.unreadOnly.update((value) => !value);
    this.pageSize = 25;
    void this.load();
  }

  protected more(): void {
    this.pageSize += 25;
    void this.load();
  }

  protected async open(item: InAppNotificationItem): Promise<void> {
    try {
      if (!item.isRead) {
        await this.api.markRead(item.id);
      }
    } catch {
      // Opening the link matters more than the read mark; the next load shows the true state.
    }

    if (item.link) {
      void this.router.navigateByUrl(item.link);
    } else {
      void this.load();
    }
  }

  protected async markAllRead(): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.markAllRead();
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    this.error.set(null);
    try {
      this.page.set(await this.api.list({ page: 1, pageSize: this.pageSize, unreadOnly: this.unreadOnly() }));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Notifications could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
