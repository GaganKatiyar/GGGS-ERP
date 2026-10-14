import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { InAppNotificationItem, PagedResult } from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/**
 * A signed-in user's own notification inbox, over the API's `/api/v1/notifications` routes.
 *
 * Every read and write here is implicitly scoped to the caller - there is no id or user parameter
 * anywhere in this API, because the server never accepts one either (see
 * `Gggs.Notification.Api.Endpoints.InboxEndpoints`'s own remarks): a user's inbox is whichever
 * rows the token identifies as theirs, and nothing else.
 */
@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly http = inject(HttpClient);

  list(query: { page: number; pageSize: number }): Promise<PagedResult<InAppNotificationItem>> {
    return firstValueFrom(
      this.http.get<PagedResult<InAppNotificationItem>>('/api/v1/notifications', {
        params: { page: String(query.page), pageSize: String(query.pageSize) },
      }),
    );
  }

  async unreadCount(): Promise<number> {
    const response = await firstValueFrom(
      this.http.get<{ unreadCount: number }>('/api/v1/notifications/unread-count'),
    );
    return response.unreadCount;
  }

  markRead(id: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/v1/notifications/${id}/read`, {}));
  }

  markAllRead(): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/notifications/read-all', {}));
  }
}
