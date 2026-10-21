import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  ViewContainerRef,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { type ActivatedRouteSnapshot, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SessionStore, StepUpPrompt } from '@gggs/core/auth';
import { PermissionStore } from '@gggs/core/permissions';
import { TenantStore } from '@gggs/core/tenant';
import { NotificationsApi } from '@gggs/shared/notifications';
import { WorkflowApi } from '@gggs/shared/workflow';
import { filter } from 'rxjs';
import { NAVIGATION, filterNavigation, isBuilt, locate, moduleTabs, type NavItem } from './navigation';
import { ThemeStore } from './theme';
import { GlobalSearch } from './global-search';

/** How often the header bell's unread count refreshes. There is no push yet (architecture.md
 * §14 puts SignalR in a later phase), so this is the whole story until then. */
const UNREAD_COUNT_REFRESH_MS = 60_000;

/** Below this, the rail and panel stop being columns and become one drawer (shell.scss). */
const MOBILE_BREAKPOINT_QUERY = '(max-width: 48rem)';

const PANEL_COLLAPSED_KEY = 'gggs.shell.panel-collapsed';

interface Crumb {
  readonly label: string;
  readonly route?: string;
}

/**
 * The application shell: a navy rail of modules, a panel listing the active module's pages,
 * a header with breadcrumbs, search, notifications and the user menu, and the routed outlet.
 *
 * Everything is built from the navigation manifest filtered by what the user can actually do, so
 * nobody is shown a menu of pages that will refuse them, and the active module, panel, breadcrumbs
 * and tabs all follow the URL - deep links and the back button included.
 */
@Component({
  selector: 'gggs-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, GlobalSearch],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.html',
  // Styled by apps/hrms-web/src/styles/_shell.scss.
  host: {
    '(document:keydown.escape)': 'onEscape()',
    '(document:click)': 'onDocumentClick($event)',
  },
})
export class Shell {
  private readonly permissions = inject(PermissionStore);
  private readonly notificationsApi = inject(NotificationsApi);
  private readonly workflowApi = inject(WorkflowApi);
  private readonly router = inject(Router);
  protected readonly session = inject(SessionStore);
  private readonly stepUpOpen = inject(StepUpPrompt).open;
  private readonly stepUpHost = viewChild('stepUpHost', { read: ViewContainerRef });
  private readonly userMenu = viewChild<ElementRef<HTMLElement>>('userMenu');
  private readonly navigationElement = viewChild<ElementRef<HTMLElement>>('navElement');
  private stepUpLoaded = false;
  protected readonly tenant = inject(TenantStore);
  protected readonly theme = inject(ThemeStore);

  private readonly isMobile = signal(typeof window !== 'undefined' && window.matchMedia(MOBILE_BREAKPOINT_QUERY).matches);
  /** On a small screen: whether the navigation drawer is open. */
  protected readonly isDrawerOpen = signal(false);
  /** On a large screen: whether the module panel is folded away, leaving the rail and the content. */
  protected readonly isPanelCollapsed = signal(readFlag(PANEL_COLLAPSED_KEY));
  protected readonly isUserMenuOpen = signal(false);

  protected readonly unreadCount = signal(0);
  /** Tasks waiting in the approvals inbox, shown on the Approvals entry. */
  protected readonly approvalsCount = signal(0);

  protected readonly canSearch = computed(() => this.permissions.hasAny(['self.profile.view', 'employee.profile.view']));
  protected readonly canSeeNotifications = computed(() => this.permissions.hasAny(['notification.inbox.view']));
  private readonly canSeeApprovals = computed(() => this.permissions.hasAny(['workflow.task.view']));

  /** Navigation the signed-in user can actually reach. */
  protected readonly navigation = computed(() => filterNavigation(NAVIGATION, (item) => this.isVisible(item)));

  private readonly url = signal(this.router.url);
  private readonly pageTitle = signal<string | null>(null);
  /** A module opened from the rail without navigating yet; cleared by the next navigation. */
  private readonly browsedGroup = signal<NavItem | null>(null);

  private readonly location = computed(() => locate(this.navigation(), this.url()));
  protected readonly activeGroup = computed(() => this.location()?.group ?? null);
  /** The page the URL is on, by longest match - so an overview is not lit up beside its sub-pages. */
  protected readonly activeEntry = computed(() => this.location()?.entry ?? null);
  /** The module whose pages the panel lists: the one being browsed, else the current page's. */
  protected readonly panelGroup = computed(() => {
    const group = this.browsedGroup() ?? this.activeGroup();
    return group?.children?.length ? group : null;
  });
  protected readonly showPanel = computed(() => !!this.panelGroup() && (this.isMobile() || !this.isPanelCollapsed()));
  /** Tabs above the content when the panel is not there to switch pages. */
  protected readonly tabs = computed(() => (this.showPanel() && !this.isMobile() ? [] : moduleTabs(this.activeGroup())));

  protected readonly breadcrumbs = computed<readonly Crumb[]>(() => {
    const location = this.location();
    if (!location) {
      return this.pageTitle() ? [{ label: this.pageTitle()! }] : [];
    }

    const crumbs: Crumb[] = [];
    if (location.entry !== location.group) {
      crumbs.push({ label: location.group.label, route: location.group.route ?? location.group.children?.[0]?.route });
    }

    crumbs.push({ label: location.entry.label, route: location.entry.route });
    const title = this.pageTitle();
    if (title && title !== location.entry.label && !this.url().split(/[?#]/)[0].endsWith(location.entry.route ?? '')) {
      crumbs.push({ label: title });
    }

    return crumbs;
  });

  protected readonly initials = computed(() =>
    (this.session.user()?.displayName ?? '')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join(''),
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

    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe((event) => {
        this.url.set(event.urlAfterRedirects);
        this.pageTitle.set(deepestTitle(this.router.routerState.snapshot.root));
        this.browsedGroup.set(null);
        this.isDrawerOpen.set(false);
        this.isUserMenuOpen.set(false);
      });
    this.pageTitle.set(deepestTitle(this.router.routerState.snapshot.root));

    if (typeof window !== 'undefined') {
      const query = window.matchMedia(MOBILE_BREAKPOINT_QUERY);
      const listener = (event: MediaQueryListEvent) => {
        this.isMobile.set(event.matches);
        this.isDrawerOpen.set(false);
      };
      query.addEventListener('change', listener);
      inject(DestroyRef).onDestroy(() => query.removeEventListener('change', listener));
    }

    // A table wider than a phone screen scrolls sideways inside itself (styles/_table.scss); make
    // each such table a tab stop so it can be scrolled from the keyboard too.
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const content = document.getElementById('main-content');
      if (!content || typeof MutationObserver === 'undefined') {
        return;
      }

      let pending = 0;
      const mark = () => {
        pending = 0;
        content.querySelectorAll<HTMLElement>('table:not(.p-datatable-table):not([tabindex])').forEach((table) => {
          if (table.scrollWidth > table.clientWidth + 1) {
            table.tabIndex = 0;
          }
        });
      };
      const observer = new MutationObserver(() => (pending ||= window.setTimeout(mark, 250)));
      observer.observe(content, { childList: true, subtree: true });
      destroyRef.onDestroy(() => observer.disconnect());
    });

    if (this.canSeeNotifications() || this.canSeeApprovals()) {
      void this.refreshCounts();

      const interval = setInterval(() => void this.refreshCounts(), UNREAD_COUNT_REFRESH_MS);
      inject(DestroyRef).onDestroy(() => clearInterval(interval));
    }
  }

  /** The counter a navigation entry shows, if any. */
  protected badgeFor(item: NavItem): number {
    return item.badge === 'approvals' ? this.approvalsCount() : item.badge === 'notifications' ? this.unreadCount() : 0;
  }

  protected isActiveGroup(item: NavItem): boolean {
    return (this.browsedGroup() ?? this.activeGroup()) === item;
  }

  /** A rail click on a module with pages opens its panel; the page itself is one more click. */
  protected openGroup(item: NavItem): void {
    this.browsedGroup.set(item === this.activeGroup() ? null : item);
    if (!this.isMobile() && this.isPanelCollapsed()) {
      this.setPanelCollapsed(false);
    }
  }

  protected toggleNavigation(): void {
    if (this.isMobile()) {
      this.isDrawerOpen.update((open) => !open);
      // Focus moves into the drawer, so a keyboard user lands on the menu they just opened.
      if (this.isDrawerOpen()) {
        queueMicrotask(() => this.navigationElement()?.nativeElement.querySelector<HTMLElement>('.rail__item')?.focus());
      }
    } else {
      this.setPanelCollapsed(!this.isPanelCollapsed());
    }
  }

  protected closeDrawer(): void {
    this.isDrawerOpen.set(false);
  }

  protected toggleUserMenu(): void {
    this.isUserMenuOpen.update((open) => !open);
  }

  /**
   * Arrow keys move between entries of the rail or the panel (Home/End to the ends), and while the
   * mobile drawer is open Tab cycles inside it rather than escaping to the page underneath.
   */
  protected onNavigationKeydown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement;
    const list = target.closest('.rail__list, .panel-nav__list');
    if (list && ['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
      const items = Array.from(list.querySelectorAll<HTMLElement>('.rail__item, .panel-nav__link'));
      const index = items.indexOf(target);
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1 : index + (event.key === 'ArrowDown' ? 1 : -1);
      items[(next + items.length) % items.length]?.focus();
      event.preventDefault();
      return;
    }

    if (event.key === 'Tab' && this.isDrawerOpen()) {
      const nav = this.navigationElement()?.nativeElement;
      const focusable = (nav ? Array.from(nav.querySelectorAll<HTMLElement>('a[href], button:not([disabled])')) : []).filter((element) => element.offsetParent !== null);
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        last?.focus();
        event.preventDefault();
      } else if (!event.shiftKey && document.activeElement === last) {
        first?.focus();
        event.preventDefault();
      }
    }
  }

  protected onEscape(): void {
    this.isUserMenuOpen.set(false);
    this.isDrawerOpen.set(false);
  }

  protected onDocumentClick(event: MouseEvent): void {
    const menu = this.userMenu()?.nativeElement;
    if (this.isUserMenuOpen() && menu && !menu.contains(event.target as Node)) {
      this.isUserMenuOpen.set(false);
    }
  }

  protected focusContent(event: Event): void {
    event.preventDefault();
    document.getElementById('main-content')?.focus();
  }

  protected signOut(): void {
    void this.session.signOut();
  }

  private setPanelCollapsed(collapsed: boolean): void {
    this.isPanelCollapsed.set(collapsed);
    try {
      localStorage.setItem(PANEL_COLLAPSED_KEY, collapsed ? '1' : '0');
    } catch {
      // Storage blocked: the choice lasts for this page only.
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

  private isVisible(item: NavItem): boolean {
    if (!isBuilt(item)) {
      return false;
    }

    if (item.feature !== undefined && !this.permissions.hasFeature(item.feature)) {
      return false;
    }

    return item.permissions === undefined || this.permissions.hasAny(item.permissions);
  }
}

/** The title of the innermost routed page, as declared on its route. */
function deepestTitle(root: ActivatedRouteSnapshot): string | null {
  let node: ActivatedRouteSnapshot | null = root;
  let title: string | null = null;
  while (node) {
    if (typeof node.title === 'string') {
      title = node.title;
    }
    node = node.firstChild;
  }

  return title;
}

function readFlag(key: string): boolean {
  try {
    return localStorage.getItem(key) === '1';
  } catch {
    return false;
  }
}
