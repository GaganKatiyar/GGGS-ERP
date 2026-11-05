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
import { MyPhotoStore, SessionStore, StepUpPrompt } from '@gggs/core/auth';
import { PermissionStore } from '@gggs/core/permissions';
import { TenantStore } from '@gggs/core/tenant';
import { NotificationsApi } from '@gggs/shared/notifications';
import { BrandMark } from '@gggs/shared/ui';
import { WorkflowApi } from '@gggs/shared/workflow';
import { filter } from 'rxjs';
import { SETTINGS_SECTIONS, entryRoute, filterMenu, locate, moduleTabs, searchNavigation, sectionsOf, type NavItem } from './navigation';
import { VisibleNavigation } from './visible-navigation';
import { PALETTES, ThemeStore, type Palette } from './theme';
import { EdgeScroll } from './edge-scroll';
import { GlobalSearch } from './global-search';
import { refreshSortableTables, sortableHeader, toggleSort } from './table-sort';

/** How often the header bell's unread count refreshes. There is no push yet (architecture.md
 * §14 puts SignalR in a later phase), so this is the whole story until then. */
const UNREAD_COUNT_REFRESH_MS = 60_000;

interface Crumb {
  readonly label: string;
  readonly route?: string;
  readonly query?: Record<string, string>;
}

/**
 * The application shell: a navy rail with an icon per module, the app launcher (every module as a
 * tile, and a search over their pages), a header with breadcrumbs, search, notifications and the
 * user menu, the current module's pages as tabs, and the routed outlet.
 *
 * Everything is built from the navigation manifest filtered by what the user can actually do, so
 * nobody is shown a menu of pages that will refuse them, and the active module, panel, breadcrumbs
 * and tabs all follow the URL - deep links and the back button included.
 */
@Component({
  selector: 'gggs-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, GlobalSearch, EdgeScroll, BrandMark],
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
  private readonly paletteMenu = viewChild<ElementRef<HTMLElement>>('paletteMenu');
  private readonly launcherElement = viewChild<ElementRef<HTMLElement>>('launcher');
  private readonly launcherSearch = viewChild<ElementRef<HTMLInputElement>>('launcherSearch');
  private readonly navigationElement = viewChild<ElementRef<HTMLElement>>('shellNavigation');
  private readonly menuToggle = viewChild<ElementRef<HTMLElement>>('menuToggle');
  /** What opened the launcher, so focus goes back there when it closes. */
  private launcherTrigger: HTMLElement | null = null;
  private stepUpLoaded = false;
  protected readonly tenant = inject(TenantStore);
  protected readonly theme = inject(ThemeStore);

  protected readonly isUserMenuOpen = signal(false);
  /** The header's colour theme picker; the choice applies at once and is kept per browser (ThemeStore). */
  protected readonly isPaletteMenuOpen = signal(false);
  protected readonly palettes = PALETTES;
  /** The app launcher: opened from the rail's grid button or the header logo (the only menu on a phone). */
  protected readonly isLauncherOpen = signal(false);
  protected readonly launcherQuery = signal('');

  protected readonly unreadCount = signal(0);
  /** Tasks waiting in the approvals inbox, shown on the Approvals entry. */
  protected readonly approvalsCount = signal(0);

  protected readonly canSearch = computed(() => this.permissions.hasAny(['self.profile.view', 'employee.profile.view']));
  protected readonly canSeeNotifications = computed(() => this.permissions.hasAny(['notification.inbox.view']));
  private readonly canSeeApprovals = computed(() => this.permissions.hasAny(['workflow.task.view']));

  /** Navigation the signed-in user can actually reach. */
  protected readonly navigation = inject(VisibleNavigation).items;
  protected readonly launcherResults = computed(() => searchNavigation(this.navigation(), this.launcherQuery()));
  protected readonly entryRoute = entryRoute;

  private readonly url = signal(this.router.url);
  private readonly pageTitle = signal<string | null>(null);

  private readonly location = computed(() => locate(this.navigation(), this.url()));
  protected readonly activeGroup = computed(() => this.location()?.group ?? null);
  /** The page the URL is on, by longest match - so an overview is not lit up beside its sub-pages. */
  protected readonly activeEntry = computed(() => this.location()?.entry ?? null);
  /** The current module's pages as tabs above the content, for moving within a module in one click. */
  protected readonly tabs = computed(() => moduleTabs(this.activeGroup(), this.activeEntry()));

  /**
   * The module's set-up pages now live in Settings: a gear at the end of its tabs leads to that
   * category, when the user can open at least one page in it.
   */
  protected readonly settingsLink = computed(() => {
    const key = this.activeGroup()?.settingsSection;
    const reachable = key && this.navigation().some((group) => group.children?.some((child) => child.section === key));
    return reachable ? { key, label: SETTINGS_SECTIONS.find((section) => section.key === key)?.label ?? 'Settings' } : null;
  });

  protected readonly breadcrumbs = computed<readonly Crumb[]>(() => {
    const location = this.location();
    if (!location) {
      return this.pageTitle() ? [{ label: this.pageTitle()! }] : [];
    }

    const crumbs: Crumb[] = [];
    if (location.entry !== location.group) {
      crumbs.push({ label: location.group.label, route: location.group.route ?? location.group.children?.[0]?.route });
    }

    // Settings / Organization / Departments: the category sits between the hub and the page.
    const section = SETTINGS_SECTIONS.find((candidate) => candidate.key === location.entry.section);
    if (section) {
      crumbs.push({ label: section.label, route: location.group.route, query: { section: section.key } });
    }

    crumbs.push({ label: location.entry.label, route: location.entry.route });
    const title = this.pageTitle();
    if (title && title !== location.entry.label && !this.url().split(/[?#]/)[0].endsWith(location.entry.route ?? '')) {
      crumbs.push({ label: title });
    }

    return crumbs;
  });

  protected readonly photo = inject(MyPhotoStore);

  // ----- Menu: the icon rail with a pop-out of a module's pages, or the menu floating over the page -----

  /** The floating menu: it starts closed on every load and closes again once a page is chosen. */
  protected readonly isMenuOpen = signal(false);
  protected readonly sectionsOf = sectionsOf;
  /** The sidebar's search. */
  protected readonly menuQuery = signal('');
  protected readonly menuItems = computed(() => filterMenu(this.navigation(), this.menuQuery()));
  /** Modules opened in the sidebar, by label; the current module opens by itself. */
  private readonly openModules = signal<ReadonlySet<string>>(new Set());

  /** The module whose pages pop out beside the rail, and how far down the window it sits. */
  protected readonly flyout = signal<NavItem | null>(null);
  protected readonly flyoutTop = signal(0);
  private flyoutTimer: ReturnType<typeof setTimeout> | undefined;
  private flyoutAnchor: HTMLElement | null = null;
  /** Set while focus is handed back to an icon on close, so that focus does not reopen the list. */
  private returningFocus = false;
  private readonly hoverPointer = typeof window !== 'undefined' && window.matchMedia?.('(hover: hover) and (pointer: fine)').matches;

  protected readonly initials = computed(() =>
    (this.session.user()?.displayName ?? '')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join(''),
  );

  constructor() {
    void this.photo.load();

    // Opening a page opens its module in the sidebar; modules opened by hand stay open.
    effect(() => {
      const group = this.activeGroup();
      if (group?.children?.length && !this.openModules().has(group.label)) {
        this.openModules.set(new Set(this.openModules()).add(group.label));
      }
    });

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
        this.isLauncherOpen.set(false);
        this.isUserMenuOpen.set(false);
        this.isPaletteMenuOpen.set(false);
        this.closeFlyout();
        this.closeMenu();
        // In a long tab bar (My Space has two dozen pages) bring the current page's tab into view.
        setTimeout(() => document.querySelector('.module-tabs__tab--active')?.scrollIntoView({ block: 'nearest', inline: 'center' }));
      });
    this.pageTitle.set(deepestTitle(this.router.routerState.snapshot.root));

    // Plain tables in the page get two things the shared data table has built in: a wide one
    // (scrolling sideways inside itself on a phone, styles/_table.scss) becomes a tab stop so it can
    // be scrolled from the keyboard, and their column headers sort the rows (table-sort.ts).
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
        refreshSortableTables(content);
      };
      const sortFrom = (event: Event) => {
        const header = sortableHeader(event.target);
        if (header && (event.type === 'click' || (event as KeyboardEvent).key === 'Enter' || (event as KeyboardEvent).key === ' ')) {
          event.preventDefault();
          toggleSort(header);
        }
      };
      content.addEventListener('click', sortFrom);
      content.addEventListener('keydown', sortFrom);
      destroyRef.onDestroy(() => {
        content.removeEventListener('click', sortFrom);
        content.removeEventListener('keydown', sortFrom);
      });
      const schedule = () => (pending ||= window.setTimeout(mark, 250));
      const observer = new MutationObserver(schedule);
      observer.observe(content, { childList: true, subtree: true });
      // On a full page load the routed page can already be in place by now, with nothing left to
      // change it, so check once without waiting for a mutation.
      schedule();
      window.addEventListener('resize', schedule);
      destroyRef.onDestroy(() => window.removeEventListener('resize', schedule));
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

  /** Opens the app launcher with an empty search, which takes the focus. */
  protected openLauncher(event: Event): void {
    this.launcherTrigger = event.currentTarget as HTMLElement | null;
    if (this.isMenuOpen()) {
      // The grid button goes with the menu, so focus comes back to the menu's edge instead.
      this.closeMenu();
      this.launcherTrigger = this.menuToggle()?.nativeElement ?? null;
    }
    this.launcherQuery.set('');
    this.isUserMenuOpen.set(false);
    this.isPaletteMenuOpen.set(false);
    this.isLauncherOpen.set(true);
    setTimeout(() => this.launcherSearch()?.nativeElement.focus());
  }

  /** Closes the launcher; focus returns to what opened it unless a choice is taking the person elsewhere. */
  protected closeLauncher(restoreFocus = true): void {
    if (!this.isLauncherOpen()) {
      return;
    }

    this.isLauncherOpen.set(false);
    if (restoreFocus) {
      this.launcherTrigger?.focus();
    }
  }

  protected clearLauncherSearch(): void {
    this.launcherQuery.set('');
    this.launcherSearch()?.nativeElement.focus();
  }

  /**
   * Enter in the search opens the first result (a module, else a page), and Tab stays inside the
   * launcher while it is open - it is a modal dialog over the page.
   */
  protected onLauncherKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && event.target === this.launcherSearch()?.nativeElement) {
      const results = this.launcherResults();
      const route = results.apps[0] ? entryRoute(results.apps[0]) : results.pages[0]?.page.route;
      if (route) {
        event.preventDefault();
        this.closeLauncher(false);
        void this.router.navigateByUrl(route);
      }
      return;
    }

    if (event.key === 'Tab') {
      const focusable = Array.from(this.launcherElement()?.nativeElement.querySelectorAll<HTMLElement>('a[href], button:not([disabled]), input') ?? []);
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

  protected toggleUserMenu(): void {
    this.isPaletteMenuOpen.set(false);
    this.isUserMenuOpen.update((open) => !open);
  }

  protected togglePaletteMenu(): void {
    this.isUserMenuOpen.set(false);
    this.isPaletteMenuOpen.update((open) => !open);
  }

  protected choosePalette(palette: Palette): void {
    this.theme.setPalette(palette);
    this.isPaletteMenuOpen.set(false);
  }

  protected toggleMenu(): void {
    if (this.isMenuOpen()) {
      this.closeMenu();
      return;
    }

    this.closeFlyout();
    this.isMenuOpen.set(true);
    // A long menu opens scrolled to where the person is.
    setTimeout(() => document.querySelector('.sidebar__page--active, .sidebar__item--active')?.scrollIntoView({ block: 'nearest' }));
  }

  protected closeMenu(restoreFocus = false): void {
    if (!this.isMenuOpen()) {
      return;
    }

    this.isMenuOpen.set(false);
    this.menuQuery.set('');
    if (restoreFocus) {
      this.menuToggle()?.nativeElement.focus();
    }
  }

  /** Tabbing out of the open menu closes it, as a click outside does. */
  protected onNavigationFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget as Node | null;
    if (next && !this.navigationElement()?.nativeElement.contains(next)) {
      this.closeMenu();
    }
  }

  /** Arrow keys move between the rail's icons, Home and End to the ends. */
  protected isModuleOpen(item: NavItem): boolean {
    return this.menuQuery().trim().length > 0 || this.openModules().has(item.label);
  }

  protected toggleModule(item: NavItem): void {
    const next = new Set(this.openModules());
    if (next.has(item.label)) {
      next.delete(item.label);
    } else {
      next.add(item.label);
    }
    this.openModules.set(next);
  }

  /**
   * Keys in the sidebar, a tree in all but name: Up and Down walk the visible modules and pages,
   * Right opens a module (or steps into it), Left closes it (or steps back up to it), Home and End
   * jump to the ends.
   */
  private onSidebarKeydown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement;
    const list = target.closest('.sidebar__list');
    if (!list || !['ArrowDown', 'ArrowUp', 'ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(event.key)) {
      return;
    }

    const items = Array.from(list.querySelectorAll<HTMLElement>('.sidebar__item, .sidebar__page'));
    const index = items.indexOf(target);
    if (index < 0) {
      return;
    }

    const moduleLabel = target.dataset['module'];
    const module = this.menuItems().find((item) => item.label === moduleLabel);
    if (event.key === 'ArrowRight' && target.classList.contains('sidebar__item') && module?.children?.length) {
      if (this.isModuleOpen(module)) {
        items[index + 1]?.focus();
      } else {
        this.toggleModule(module);
      }
    } else if (event.key === 'ArrowLeft') {
      if (target.classList.contains('sidebar__page')) {
        items.slice(0, index).reverse().find((item) => item.classList.contains('sidebar__item'))?.focus();
      } else if (module && this.openModules().has(module.label)) {
        this.toggleModule(module);
      }
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'Home' || event.key === 'End') {
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1 : index + (event.key === 'ArrowDown' ? 1 : -1);
      items[(next + items.length) % items.length]?.focus();
    }
    event.preventDefault();
  }

  /** A pointer resting on a rail icon: its module's pages pop out after a moment. */
  protected hoverRailItem(item: NavItem, event: MouseEvent): void {
    if (!this.hoverPointer || !item.children?.length) {
      return;
    }
    const anchor = event.currentTarget as HTMLElement;
    clearTimeout(this.flyoutTimer);
    this.flyoutTimer = setTimeout(() => this.openFlyout(item, anchor), 150);
  }

  /** Keyboard focus on a rail icon shows the same list at once; Right steps into it. */
  protected focusRailItem(item: NavItem, event: FocusEvent): void {
    clearTimeout(this.flyoutTimer);
    if (this.returningFocus) {
      this.returningFocus = false;
      return;
    }
    if (item.children?.length) {
      this.openFlyout(item, event.currentTarget as HTMLElement);
    } else {
      this.flyout.set(null);
    }
  }

  protected leaveFlyout(): void {
    clearTimeout(this.flyoutTimer);
    this.flyoutTimer = setTimeout(() => this.flyout.set(null), 250);
  }

  protected keepFlyout(): void {
    clearTimeout(this.flyoutTimer);
  }

  protected closeFlyout(restoreFocus = false): void {
    clearTimeout(this.flyoutTimer);
    this.flyout.set(null);
    if (restoreFocus && this.flyoutAnchor) {
      this.returningFocus = true;
      this.flyoutAnchor.focus();
      this.returningFocus = false;
    }
  }

  protected onFlyoutKeydown(event: KeyboardEvent): void {
    const links = Array.from((event.currentTarget as HTMLElement).querySelectorAll<HTMLElement>('a'));
    const index = links.indexOf(event.target as HTMLElement);
    if (event.key === 'ArrowLeft' || event.key === 'Escape') {
      this.closeFlyout(true);
      event.preventDefault();
      event.stopPropagation();
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      links[(index + (event.key === 'ArrowDown' ? 1 : -1) + links.length) % links.length]?.focus();
      event.preventDefault();
    }
  }

  private openFlyout(item: NavItem, anchor: HTMLElement): void {
    this.flyoutAnchor = anchor;
    // Level with the icon, but never running off the bottom of the window.
    const rect = anchor.getBoundingClientRect();
    const rows = sectionsOf(item).reduce((count, section) => count + section.pages.length + (section.label ? 1 : 0), 1);
    const height = Math.min(rows * 40 + 24, window.innerHeight - 16);
    this.flyoutTop.set(Math.max(8, Math.min(rect.top, window.innerHeight - height - 8)));
    this.flyout.set(item);
  }

  protected onNavigationKeydown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement;
    if (target.closest('.sidebar__list')) {
      this.onSidebarKeydown(event);
      return;
    }

    const list = target.closest('.rail__list');
    if (list && ['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
      const items = Array.from(list.querySelectorAll<HTMLElement>('.rail__item'));
      const index = items.indexOf(target);
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1 : index + (event.key === 'ArrowDown' ? 1 : -1);
      items[(next + items.length) % items.length]?.focus();
      event.preventDefault();
    } else if (list && event.key === 'ArrowRight' && this.flyout()) {
      // Into the pop-out's first page.
      document.querySelector<HTMLElement>('.rail-flyout a')?.focus();
      event.preventDefault();
    }
  }

  protected onEscape(): void {
    if (this.flyout()) {
      this.closeFlyout(true);
    }
    this.closeMenu(true);
    this.isUserMenuOpen.set(false);
    this.isPaletteMenuOpen.set(false);
    this.closeLauncher();
  }

  protected onDocumentClick(event: MouseEvent): void {
    const target = event.target as Node;
    if (!this.navigationElement()?.nativeElement.contains(target)) {
      this.closeMenu();
    }

    const menu = this.userMenu()?.nativeElement;
    if (this.isUserMenuOpen() && menu && !menu.contains(target)) {
      this.isUserMenuOpen.set(false);
    }

    const paletteMenu = this.paletteMenu()?.nativeElement;
    if (this.isPaletteMenuOpen() && paletteMenu && !paletteMenu.contains(target)) {
      this.isPaletteMenuOpen.set(false);
    }
  }

  protected focusContent(event: Event): void {
    event.preventDefault();
    document.getElementById('main-content')?.focus();
  }

  protected signOut(): void {
    void this.session.signOut();
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
