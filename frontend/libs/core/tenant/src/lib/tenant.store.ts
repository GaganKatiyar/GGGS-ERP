import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import type { TenantBranding, TenantContext } from '@gggs/shared/models';

/**
 * The tenant the current session belongs to, plus their branding.
 *
 * Note what is missing: nothing here is ever sent to the API. The server takes the tenant from the
 * token, so this exists purely so the UI can show the right name, logo, currency and time zone.
 */
@Injectable({ providedIn: 'root' })
export class TenantStore {
  private readonly document = inject(DOCUMENT);
  private readonly tenant = signal<TenantContext | null>(null);

  readonly current = this.tenant.asReadonly();
  readonly name = computed(() => this.tenant()?.name ?? '');
  readonly currency = computed(() => this.tenant()?.defaultCurrency ?? 'USD');
  readonly timeZone = computed(() => this.tenant()?.defaultTimeZone ?? 'UTC');

  setTenant(tenant: TenantContext): void {
    this.tenant.set(tenant);
    this.applyBranding(tenant.branding);
  }

  clear(): void {
    this.tenant.set(null);
  }

  /**
   * Applies tenant branding as CSS custom properties.
   *
   * Guarded rather than applied blindly: a customer who picks a pale yellow as their brand colour
   * should not end up with unreadable buttons, so a colour that fails contrast against white is
   * ignored and the default is kept.
   */
  private applyBranding(branding: TenantBranding | undefined): void {
    const root = this.document.documentElement;

    if (!branding?.primaryColor) {
      root.style.removeProperty('--gggs-color-primary');
      return;
    }

    if (TenantStore.hasSufficientContrast(branding.primaryColor)) {
      root.style.setProperty('--gggs-color-primary', branding.primaryColor);
    } else {
      console.warn(
        `Tenant brand colour ${branding.primaryColor} fails contrast against white text; keeping the default.`,
      );
    }
  }

  /** WCAG contrast of white text on the given background; 4.5:1 is the AA threshold for body text. */
  private static hasSufficientContrast(hexColor: string): boolean {
    const match = /^#?([0-9a-f]{6})$/i.exec(hexColor.trim());
    if (!match) {
      return false;
    }

    const value = Number.parseInt(match[1], 16);
    const channels = [(value >> 16) & 255, (value >> 8) & 255, value & 255].map(
      (channel) => {
        const normalized = channel / 255;
        return normalized <= 0.03928
          ? normalized / 12.92
          : ((normalized + 0.055) / 1.055) ** 2.4;
      },
    );

    const luminance =
      0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
    const contrastWithWhite = 1.05 / (luminance + 0.05);

    return contrastWithWhite >= 4.5;
  }
}
