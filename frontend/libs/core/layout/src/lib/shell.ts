import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ViewContainerRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SessionStore, StepUpPrompt } from '@gggs/core/auth';
import { PermissionStore } from '@gggs/core/permissions';
import { TenantStore } from '@gggs/core/tenant';
import { NotificationsApi } from '@gggs/shared/notifications';
import { WorkflowApi } from '@gggs/shared/workflow';
import { NAVIGATION, filterNavigation, isBuilt, type NavItem } from './navigation';
import { ThemeStore } from './theme';
import { GlobalSearch } from './global-search';

/** How often the header bell's unread count refreshes. There is no push yet (architecture.md
 * §14 puts SignalR in a later phase), so this is the whole story until then. */
const UNREAD_COUNT_REFRESH_MS = 60_000;

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
  imports: [RouterOutlet, RouterLink, RouterLinkActive, GlobalSearch],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  private readonly permissions = inject(PermissionStore);
  private readonly notificationsApi = inject(NotificationsApi);
  private readonly workflowApi = inject(WorkflowApi);
  protected readonly session = inject(SessionStore);
  private readonly stepUpOpen = inject(StepUpPrompt).open;
  private readonly stepUpHost = viewChild('stepUpHost', { read: ViewContainerRef });
  private stepUpLoaded = false;
  protected readonly tenant = inject(TenantStore);
  protected readonly theme = inject(ThemeStore);

  // "Collapsed" means minimal sidebar presence either way: a narrow icon rail on desktop, or fully
  // hidden off-screen on mobile - so the drawer starts closed on a small screen rather than
  // covering the page on first paint, and the same signal/toggle drives both behaviors.
  protected readonly isSidebarCollapsed = signal(
    typeof window !== 'undefined' && window.matchMedia(MOBILE_BREAKPOINT_QUERY).matches,
  );
  protected readonly unreadCount = signal(0);
  /** Tasks waiting in the approvals inbox, shown on the Approvals entry. */
  protected readonly approvalsCount = signal(0);

  protected readonly canSearch = computed(() => this.permissions.hasAny(['self.profile.view', 'employee.profile.view']));
  protected readonly canSeeNotifications = computed(() => this.permissions.hasAny(['notification.inbox.view']));
  private readonly canSeeApprovals = computed(() => this.permissions.hasAny(['workflow.task.view']));

  /** Navigation the signed-in user can actually reach. */
  protected readonly navigation = computed(() =>
    filterNavigation(NAVIGATION, (item) => this.isVisible(item)),
  );

  constructor() {
    // The "confirm it's you" dialog is fetched the first time it is needed, not with the shell.
    effect(() => {
      const host = this.stepUpHost();
      if (this.stepUpOpen() && host && !this.stepUpLoaded) {
        this.stepUpLoaded = true;
        void import('./step-up-dialog').then(({ StepUpDialog }) => host.createComponent(StepUpDialog));
      }
    });

    if (this.canSeeNotifications() || this.canSeeApprovals()) {
      void this.refreshCounts();

      const interval = setInterval(
        () => void this.refreshCounts(),
        UNREAD_COUNT_REFRESH_MS,
      );
      inject(DestroyRef).onDestroy(() => clearInterval(interval));
    }
  }

  /** The counter a navigation entry shows, if any. */
  protected badgeFor(item: NavItem): number {
    return item.badge === 'approvals' ? this.approvalsCount() : item.badge === 'notifications' ? this.unreadCount() : 0;
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

  private async refreshCounts(): Promise<void> {
    // A failed refresh leaves the last known count showing rather than resetting to zero, which
    // would read as "everything is read" when it might just be the network.
    if (this.canSeeNotifications()) {
      try {
        this.unreadCount.set(await this.notificationsApi.unreadCount());
      } catch {
        // Keep the last count.
      }
    }

    if (this.canSeeApprovals()) {
      try {
        this.approvalsCount.set(await this.workflowApi.pendingCount());
      } catch {
        // Keep the last count.
      }
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
