import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SessionStore } from '@gggs/core/auth';
import { PermissionStore } from '@gggs/core/permissions';
import { TenantStore } from '@gggs/core/tenant';
import { NotificationsApi } from '@gggs/shared/notifications';
import { NAVIGATION, filterNavigation, isBuilt, type NavItem } from './navigation';
import { ThemeStore } from './theme';

/** How often the header bell's unread count refreshes. There is no push yet (architecture.md
 * §14 puts SignalR in a later phase), so this is the whole story until then. */
const UNREAD_COUNT_REFRESH_MS = 60_000;

/** Flipped when the Notification module ships (docs/roadmap.md, M3). */
const NOTIFICATIONS_AVAILABLE = false;

/** Below this, the sidebar stops being a column and becomes an overlay drawer (shell.scss). */
const MOBILE_BREAKPOINT_QUERY = '(max-width: 48rem)';

/**
 * The application shell: sidebar, header and the routed outlet.
 *
 * The sidebar is built from the navigation manifest filtered by what the user can actually do, so
 * nobody is shown a menu of pages that will refuse them. Groups whose children are all hidden
 * disappear rather than sitting there empty.
 */
@Component({
  selector: 'gggs-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  private readonly permissions = inject(PermissionStore);
  private readonly notificationsApi = inject(NotificationsApi);
  protected readonly session = inject(SessionStore);
  protected readonly tenant = inject(TenantStore);
  protected readonly theme = inject(ThemeStore);

  // "Collapsed" means minimal sidebar presence either way: a narrow icon rail on desktop, or fully
  // hidden off-screen on mobile - so the drawer starts closed on a small screen rather than
  // covering the page on first paint, and the same signal/toggle drives both behaviors.
  protected readonly isSidebarCollapsed = signal(
    typeof window !== 'undefined' && window.matchMedia(MOBILE_BREAKPOINT_QUERY).matches,
  );
  protected readonly unreadCount = signal(0);
  /**
   * The bell needs both the permission and the Notification module, which ships in M3. Until then
   * there is no inbox endpoint to poll, so the bell stays hidden rather than failing every minute.
   */
  protected readonly canSeeNotifications = computed(
    () => NOTIFICATIONS_AVAILABLE && this.permissions.hasAny(['notification.inbox.view']),
  );

  /** Navigation the signed-in user can actually reach. */
  protected readonly navigation = computed(() =>
    filterNavigation(NAVIGATION, (item) => this.isVisible(item)),
  );

  constructor() {
    if (this.canSeeNotifications()) {
      void this.refreshUnreadCount();

      const interval = setInterval(
        () => void this.refreshUnreadCount(),
        UNREAD_COUNT_REFRESH_MS,
      );
      inject(DestroyRef).onDestroy(() => clearInterval(interval));
    }
  }

  protected toggleSidebar(): void {
    this.isSidebarCollapsed.update((collapsed) => !collapsed);
  }

  /** Navigating (or dismissing the backdrop) closes the drawer on mobile; harmless on desktop,
   * where it just re-collapses to the icon rail. */
  protected closeSidebarOnMobile(): void {
    if (window.matchMedia(MOBILE_BREAKPOINT_QUERY).matches) {
      this.isSidebarCollapsed.set(true);
    }
  }

  private async refreshUnreadCount(): Promise<void> {
    try {
      this.unreadCount.set(await this.notificationsApi.unreadCount());
    } catch {
      // A failed refresh leaves the last known count showing rather than resetting to zero,
      // which would read as "everything is read" when it might just be the network.
    }
  }

  protected signOut(): void {
    void this.session.signOut();
  }

  private isVisible(item: NavItem): boolean {
    if (!isBuilt(item)) {
      return false;
    }

    if (
      item.feature !== undefined &&
      !this.permissions.hasFeature(item.feature)
    ) {
      return false;
    }

    return (
      item.permissions === undefined ||
      this.permissions.hasAny(item.permissions)
    );
  }
}
