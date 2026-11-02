import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** One kind of notification on the preferences page, with the person's choice for it. */
export interface NotificationPreference {
  readonly code: string;
  readonly name: string;
  /** The event's area: the code's first part ("workflow", "payroll"). */
  readonly module: string;
  readonly templateSendsEmail: boolean;
  readonly templateSendsInApp: boolean;
  /** False unless the organisation has both chosen text for this notification and set up a way to send it. */
  readonly templateSendsSms: boolean;
  readonly emailEnabled: boolean;
  readonly inAppEnabled: boolean;
  readonly smsEnabled: boolean;
  /** Always sent, whatever is chosen: account and legal notices. */
  readonly isMandatory: boolean;
}

export interface NotificationPreferenceChoice {
  readonly code: string;
  readonly emailEnabled: boolean;
  readonly inAppEnabled: boolean;
  readonly smsEnabled: boolean;
}

export interface MyPreferences {
  readonly timeZone?: string;
}

/** The signed-in person's own settings: notification channels (`/me/notification-preferences`) and time zone (`/me/preferences`). */
@Injectable({ providedIn: 'root' })
export class PreferencesApi {
  private readonly http = inject(HttpClient);

  notifications(): Promise<readonly NotificationPreference[]> {
    return firstValueFrom(this.http.get<readonly NotificationPreference[]>('/api/v1/me/notification-preferences'));
  }

  async saveNotifications(choices: readonly NotificationPreferenceChoice[]): Promise<void> {
    await firstValueFrom(this.http.put('/api/v1/me/notification-preferences', choices));
  }

  mine(): Promise<MyPreferences> {
    return firstValueFrom(this.http.get<MyPreferences>('/api/v1/me/preferences'));
  }

  save(preferences: MyPreferences): Promise<MyPreferences> {
    return firstValueFrom(this.http.put<MyPreferences>('/api/v1/me/preferences', preferences));
  }
}
