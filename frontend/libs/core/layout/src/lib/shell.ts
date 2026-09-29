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
import { NAVIGATION, filterNavigation, type NavItem } from './navigation';

/** How often the header bell's unread count refreshes. There is no push yet (architecture.md
 * §14 puts SignalR in a later phase), so this is the whole story until then. */
const UNREAD_COUNT_REFRESH_MS = 60_000;

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

  protected readonly isSidebarCollapsed = signal(false);
  protected readonly unreadCount = signal(0);
  protected readonly canSeeNotifications = computed(() =>
    this.permissions.hasAny(['notification.inbox.view']),
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
