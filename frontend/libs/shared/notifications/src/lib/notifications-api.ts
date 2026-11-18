import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { ChannelSendOutcome, EmailLogItem, EmailLogResult, EmailLogSettings, EmailSenderBody, EmailSendersView, InAppNotificationItem, NotificationTemplate, PagedResult } from '@gggs/shared/models';
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

  list(query: { page: number; pageSize: number; unreadOnly?: boolean }): Promise<PagedResult<InAppNotificationItem>> {
    const params: Record<string, string> = { page: String(query.page), pageSize: String(query.pageSize) };
    if (query.unreadOnly) {
      params['unreadOnly'] = 'true';
    }
    return firstValueFrom(this.http.get<PagedResult<InAppNotificationItem>>('/api/v1/notifications', { params }));
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

  templates(): Promise<readonly NotificationTemplate[]> {
    return firstValueFrom(this.http.get<readonly NotificationTemplate[]>('/api/v1/notifications/templates'));
  }

  updateTemplate(id: string, body: Pick<NotificationTemplate, 'subject' | 'body' | 'sendInApp' | 'sendEmail' | 'sendSms' | 'isActive'> & { readonly senderKey: string | null }): Promise<NotificationTemplate> {
    return firstValueFrom(this.http.put<NotificationTemplate>(`/api/v1/notifications/templates/${id}`, body));
  }

  // ----- Studio > Email senders and Email log --------------------------------------------------

  senders(): Promise<EmailSendersView> {
    return firstValueFrom(this.http.get<EmailSendersView>('/api/v1/notifications/senders'));
  }

  saveSender(key: string, body: EmailSenderBody): Promise<EmailSendersView> {
    return firstValueFrom(this.http.put<EmailSendersView>(`/api/v1/notifications/senders/${encodeURIComponent(key)}`, body));
  }

  /** The module goes back to the default sender. */
  removeSender(key: string): Promise<EmailSendersView> {
    return firstValueFrom(this.http.delete<EmailSendersView>(`/api/v1/notifications/senders/${encodeURIComponent(key)}`));
  }

  testSender(key: string, to: string): Promise<ChannelSendOutcome> {
    return firstValueFrom(this.http.post<ChannelSendOutcome>(`/api/v1/notifications/senders/${encodeURIComponent(key)}/test`, { to }));
  }

  emailLog(query: { status?: string; module?: string; search?: string; page: number; pageSize: number }): Promise<EmailLogResult> {
    const params: Record<string, string> = { page: String(query.page), pageSize: String(query.pageSize) };
    for (const key of ['status', 'module', 'search'] as const) {
      const value = query[key]?.trim();
      if (value) {
        params[key] = value;
      }
    }
    return firstValueFrom(this.http.get<EmailLogResult>('/api/v1/notifications/emails', { params }));
  }

  resendEmail(id: string): Promise<EmailLogItem> {
    return firstValueFrom(this.http.post<EmailLogItem>(`/api/v1/notifications/emails/${id}/resend`, {}));
  }

  /** Every failed email that can go again (in one module, if given) back in the queue. */
  resendFailedEmails(module?: string): Promise<{ readonly requeued: number; readonly skipped: number }> {
    const params: Record<string, string> = module ? { module } : {};
    return firstValueFrom(this.http.post<{ readonly requeued: number; readonly skipped: number }>('/api/v1/notifications/emails/resend-failed', {}, { params }));
  }

  emailLogSettings(): Promise<EmailLogSettings> {
    return firstValueFrom(this.http.get<EmailLogSettings>('/api/v1/notifications/emails/settings'));
  }

  saveEmailLogSettings(body: Pick<EmailLogSettings, 'keepSentDays' | 'keepProblemDays' | 'alertOnFailure'>): Promise<EmailLogSettings> {
    return firstValueFrom(this.http.put<EmailLogSettings>('/api/v1/notifications/emails/settings', body));
  }
}
