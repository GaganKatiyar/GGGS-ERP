/**
 * A signed-in user's own notification inbox, mirroring the DTOs in
 * `Gggs.Notification.Api.Contracts.NotificationContracts`.
 */

export interface InAppNotificationItem {
  readonly id: string;
  readonly eventCode: string;
  readonly title: string;
  readonly body: string;
  readonly link?: string;
  readonly isRead: boolean;
  readonly createdDate: string;
}
