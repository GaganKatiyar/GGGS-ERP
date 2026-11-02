import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from './session.store';

/** Where the signed-in employee's photo is served; `v` (the photo id) lets the browser cache it for good. */
export function myPhotoUrl(photoId: string): string {
  return `/api/v1/me/employee/photo?v=${encodeURIComponent(photoId)}`;
}

/**
 * The signed-in employee's profile photo, shared by the header avatar and the profile page. The
 * session does not carry it (Identity knows nothing of employees), so it is asked for once, and
 * the profile page reports a change so the header follows without a reload.
 */
@Injectable({ providedIn: 'root' })
export class MyPhotoStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);

  private readonly photoId = signal<string | null>(null);
  private loadedFor: string | null = null;

  /** The image URL, or null to show initials. */
  readonly url = computed(() => {
    const id = this.photoId();
    return id ? myPhotoUrl(id) : null;
  });

  /** Fetches the photo id for the signed-in employee, once per employee. Accounts without one have no photo. */
  async load(): Promise<void> {
    const employeeId = this.session.user()?.employeeId ?? null;
    if (!employeeId || this.loadedFor === employeeId) {
      return;
    }

    this.loadedFor = employeeId;
    try {
      // The API leaves a null out, so no photo arrives as a missing id.
      const info = await firstValueFrom(this.http.get<{ photoId?: string }>('/api/v1/me/employee/avatar'));
      this.photoId.set(info.photoId ?? null);
    } catch {
      // Initials are a fine fallback; nothing to tell the user.
      this.photoId.set(null);
    }
  }

  /** Called after an upload or removal. */
  set(photoId: string | null): void {
    this.photoId.set(photoId);
  }
}
