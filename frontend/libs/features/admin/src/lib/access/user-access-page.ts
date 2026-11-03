import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal, type OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HasPermissionDirective, PermissionStore } from '@gggs/core/permissions';
import { AdminApi } from '@gggs/shared/identity';
import type { EffectivePermission, UserAccessDetail } from '@gggs/shared/models';
import { EmptyState, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { codeSet, menuPages, permissionLabel, reachLabel, type MenuPage } from './access-model';

/**
 * Everything one person may do and why: their roles (given by hand or by the org chart), every
 * permission those add up to with the roles behind it, and the menu they see - worked out by the
 * same rules the server enforces and the shell draws.
 */
@Component({
  selector: 'gggs-user-access-page',
  imports: [DatePipe, RouterLink, HasPermissionDirective, PageHeader, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error()) {
      <gggs-empty-state icon="pi pi-user" title="User not found" [description]="error()!" />
    } @else if (!access()) {
      <gggs-skeleton [lines]="8" />
    } @else {
      @let user = access()!.user;
      <gggs-page-header [title]="user.displayName" [subtitle]="user.email + (user.employeeName ? ' · ' + user.employeeName : ' · no employee record')">
        <gggs-status-badge [status]="user.status" />
        <a routerLink="/admin/access" class="btn">Access overview</a>
        <a [routerLink]="['/admin/users', user.id, 'roles']" class="btn btn--primary" *gggsHasPermission="'admin.user.manage'">Change roles</a>
      </gggs-page-header>

      <div class="access-stats">
        <div class="elevated stat"><strong>{{ user.roles.length }}</strong><span>roles</span></div>
        <div class="elevated stat"><strong>{{ access()!.permissions.length }}</strong><span>permissions</span></div>
        <div class="elevated stat"><strong>{{ menu().length }}</strong><span>menu pages</span></div>
        <div class="elevated stat"><strong>{{ user.lastLoginDate ? (user.lastLoginDate | date: 'd MMM y') : 'Never' }}</strong><span>last sign-in</span></div>
      </div>

      <section class="elevated access-card" aria-labelledby="roles-heading">
        <h2 id="roles-heading">Roles</h2>
        @if (user.roles.length === 0) {
          <p class="muted">No roles: this account can sign in but sees nothing.</p>
        } @else {
          <table class="table">
            <thead><tr><th scope="col">Role</th><th scope="col">Given</th><th scope="col">For</th><th scope="col">From</th><th scope="col">Until</th></tr></thead>
            <tbody>
              @for (grant of user.roles; track grant.roleId + (grant.scopeId ?? '')) {
                <tr>
                  <td><a [routerLink]="['/admin/roles', grant.roleId]">{{ grant.roleName }}</a></td>
                  <td>
                    @if (grant.isAutomatic) {
                      <span class="tag">Automatic</span>
                    } @else {
                      By hand
                    }
                  </td>
                  <td>{{ grant.scopeName ?? 'Everywhere' }}</td>
                  <td>{{ grant.validFrom | date: 'd MMM y' }}</td>
                  <td>{{ grant.validTo ? (grant.validTo | date: 'd MMM y') : 'Ongoing' }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      </section>

      <section class="elevated access-card" aria-labelledby="permissions-heading">
        <div class="access-card__head">
          <h2 id="permissions-heading">Permissions</h2>
          <label><span class="sr-only">Search permissions</span><input type="search" placeholder="Search" [value]="query()" (input)="query.set($any($event.target).value)" /></label>
        </div>
        @for (group of permissionGroups(); track group.module) {
          <h3>{{ group.module }}</h3>
          <table class="table">
            <thead><tr><th scope="col">Permission</th><th scope="col">Reach</th><th scope="col">Through</th></tr></thead>
            <tbody>
              @for (permission of group.items; track permission.code) {
                <tr>
                  <td>
                    {{ label(permission.code) }}
                    @if (permission.isSensitive) {
                      <span class="tag tag--sensitive">Sensitive</span>
                    }
                    <code class="code">{{ permission.code }}</code>
                  </td>
                  <td>{{ reach(permission) }}</td>
                  <td>{{ permission.via.join(', ') }}</td>
                </tr>
              }
            </tbody>
          </table>
        } @empty {
          <p class="muted">No permissions{{ query() ? ' match' : '' }}.</p>
        }
      </section>

      <section class="elevated access-card" aria-labelledby="menu-heading">
        <h2 id="menu-heading">Menu</h2>
        <p class="muted">What this person sees in the menu, by the same rule the app uses.</p>
        @for (group of menuGroups(); track group.label) {
          <h3>{{ group.label }}</h3>
          <ul class="chips">
            @for (entry of group.pages; track entry.page.route) {
              <li [title]="entry.unlockedBy ? 'Opened by ' + entry.unlockedBy : 'Open to everyone signed in'">{{ entry.page.label }}</li>
            }
          </ul>
        } @empty {
          <p class="muted">Nothing in the menu.</p>
        }
      </section>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .access-stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(10rem, 100%), 1fr)); gap: var(--gggs-space-3); }
    .stat { display: flex; flex-direction: column; padding: var(--gggs-space-4); }
    .stat strong { font-family: var(--gggs-font-family-display); font-size: var(--gggs-font-size-xl); }
    .stat span { color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-sm); }
    .access-card { display: flex; flex-direction: column; gap: var(--gggs-space-2); padding: var(--gggs-space-5); overflow-x: auto; }
    .access-card h2 { margin: 0; font-size: var(--gggs-font-size-lg); }
    .access-card h3 { margin: var(--gggs-space-3) 0 0; font-size: var(--gggs-font-size-md); }
    .access-card__head { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: var(--gggs-space-3); }
    .code { display: block; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    .tag { padding: 0 var(--gggs-space-2); border-radius: var(--gggs-radius-pill); background: var(--gggs-color-primary-subtle); color: var(--gggs-color-primary); font-size: var(--gggs-font-size-xs); font-weight: var(--gggs-font-weight-semibold); }
    .tag--sensitive { margin-inline-start: var(--gggs-space-2); background: var(--gggs-color-warning-subtle); color: var(--gggs-color-text); }
    .chips { display: flex; flex-wrap: wrap; gap: var(--gggs-space-2); margin: 0; padding: 0; list-style: none; }
    .chips li { padding: var(--gggs-space-1) var(--gggs-space-3); border-radius: var(--gggs-radius-pill); background: var(--gggs-color-surface-sunken); box-shadow: inset 0 0 0 1px var(--gggs-color-border); }
  `,
})
export class UserAccessPage implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly permissions = inject(PermissionStore);

  readonly id = input.required<string>();

  protected readonly access = signal<UserAccessDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly query = signal('');
  protected readonly label = (code: string) => permissionLabel(code).label;

  protected readonly permissionGroups = computed(() => {
    const term = this.query().trim().toLowerCase();
    const groups = new Map<string, EffectivePermission[]>();
    for (const permission of this.access()?.permissions ?? []) {
      const { module, label } = permissionLabel(permission.code);
      if (term && !permission.code.includes(term) && !label.toLowerCase().includes(term) && !module.toLowerCase().includes(term)) {
        continue;
      }
      groups.set(module, [...(groups.get(module) ?? []), permission]);
    }
    return [...groups].sort(([a], [b]) => a.localeCompare(b)).map(([module, items]) => ({ module, items }));
  });

  protected readonly menu = computed<readonly MenuPage[]>(() => {
    const access = this.access();
    return access ? menuPages(codeSet(access.user.permissionCodes), (feature) => this.permissions.hasFeature(feature), !!access.user.employeeId) : [];
  });

  protected readonly menuGroups = computed(() => {
    const groups = new Map<string, MenuPage[]>();
    for (const entry of this.menu()) {
      groups.set(entry.group.label, [...(groups.get(entry.group.label) ?? []), entry]);
    }
    return [...groups].map(([label, pages]) => ({ label, pages }));
  });

  ngOnInit(): void {
    void this.api.userAccess(this.id()).then(
      (access) => this.access.set(access),
      () => this.error.set('This account does not exist, or you cannot see it.'),
    );
  }

  /** "All reports", plus where it is anchored when the grant names one place. */
  protected reach(permission: EffectivePermission): string {
    return reachLabel(permission.scope);
  }
}
