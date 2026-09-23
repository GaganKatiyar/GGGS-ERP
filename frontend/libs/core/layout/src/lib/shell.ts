import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SessionStore } from '@gggs/core/auth';
import { PermissionStore } from '@gggs/core/permissions';
import { TenantStore } from '@gggs/core/tenant';
import { NAVIGATION, filterNavigation, type NavItem } from './navigation';

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
  protected readonly session = inject(SessionStore);
  protected readonly tenant = inject(TenantStore);

  protected readonly isSidebarCollapsed = signal(false);

  /** Navigation the signed-in user can actually reach. */
  protected readonly navigation = computed(() =>
    filterNavigation(NAVIGATION, (item) => this.isVisible(item)),
  );

  protected toggleSidebar(): void {
    this.isSidebarCollapsed.update((collapsed) => !collapsed);
  }

  protected signOut(): void {
    this.session.signOut();
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
