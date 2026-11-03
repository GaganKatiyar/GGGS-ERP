import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { navigationFor } from '@gggs/core/layout';
import { PermissionStore } from '@gggs/core/permissions';
import { AdminApi } from '@gggs/shared/identity';
import type { PermissionCatalogItem, Role, UserAccessSummary } from '@gggs/shared/models';
import { EmptyState, PageHeader, Skeleton, StatusBadge, Tabs, type TabItem } from '@gggs/shared/ui';
import { codeSet, groupByModule, menuPages, permissionLabel, reachLabel } from './access-model';

interface MatrixRow {
  readonly key: string;
  readonly label: string;
  readonly code?: string;
  readonly isModule: boolean;
  /** One cell per role, in column order: a reach label, a count for a module row, or empty. */
  readonly cells: readonly string[];
}

/**
 * Who can do what, three ways round: each user with their roles, every role against every
 * permission, and every menu page against every role. Read-only - a role's column header opens
 * its permission grid, a user opens their access page.
 */
@Component({
  selector: 'gggs-access-overview-page',
  imports: [RouterLink, PageHeader, EmptyState, Skeleton, StatusBadge, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Access overview" subtitle="Who has which role, what each role may do, and which menu pages that opens." />
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />

    @if (loading()) {
      <gggs-skeleton [lines]="8" />
    } @else {
      @switch (tab()) {
        @case ('users') {
          <div class="tools">
            <label><span class="sr-only">Search users</span><input type="search" placeholder="Search name, email, employee" [value]="query()" (input)="query.set($any($event.target).value)" /></label>
            <label class="tools__field">
              <span class="sr-only">Role</span>
              <select (change)="roleFilter.set($any($event.target).value)">
                <option value="">All roles</option>
                @for (role of roles(); track role.id) {
                  <option [value]="role.id">{{ role.name }}</option>
                }
              </select>
            </label>
            <span class="muted">{{ shownUsers().length }} of {{ users().length }} users</span>
          </div>
          <div class="scroll">
            <table class="table">
              <thead>
                <tr><th scope="col">User</th><th scope="col">Employee</th><th scope="col">Status</th><th scope="col">Roles</th><th scope="col" class="num">Permissions</th><th scope="col" class="num">Menu pages</th></tr>
              </thead>
              <tbody>
                @for (user of shownUsers(); track user.id) {
                  <tr>
                    <td><a [routerLink]="['/admin/users', user.id, 'access']">{{ user.displayName }}</a><span class="sub">{{ user.email }}</span></td>
                    <td>{{ user.employeeName ?? '—' }}</td>
                    <td><gggs-status-badge [status]="user.status" /></td>
                    <td>
                      <span class="chips">
                        @for (grant of user.roles; track grant.roleId + (grant.scopeId ?? '')) {
                          <span class="chip" [class.chip--auto]="grant.isAutomatic" [title]="grant.isAutomatic ? 'Given automatically from the org chart' : 'Given by hand'">
                            {{ grant.roleName }}{{ grant.scopeName ? ' · ' + grant.scopeName : '' }}{{ grant.isAutomatic ? ' (auto)' : '' }}
                          </span>
                        } @empty {
                          <span class="muted">No roles</span>
                        }
                      </span>
                    </td>
                    <td class="num">{{ user.permissionCodes.length }}</td>
                    <td class="num">{{ menuCount(user) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
        @case ('matrix') {
          <div class="tools">
            <label><span class="sr-only">Search permissions</span><input type="search" placeholder="Search permissions" [value]="matrixQuery()" (input)="matrixQuery.set($any($event.target).value)" /></label>
            <label class="tools__check"><input type="checkbox" [checked]="differencesOnly()" (change)="differencesOnly.set($any($event.target).checked)" /> Only rows where roles differ</label>
          </div>
          <div class="scroll">
            <table class="table matrix">
              <thead>
                <tr>
                  <th scope="col" class="matrix__first">Permission</th>
                  @for (role of roles(); track role.id) {
                    <th scope="col"><a [routerLink]="['/admin/roles', role.id]" [queryParams]="{ tab: 'permissions' }" title="Edit this role's permissions">{{ role.name }}</a></th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (row of matrixRows(); track row.key) {
                  <tr [class.matrix__module]="row.isModule">
                    <th scope="row" class="matrix__first">
                      @if (row.isModule) {
                        <button type="button" class="matrix__toggle" [attr.aria-expanded]="isOpen(row.key)" (click)="toggle(row.key)">
                          <i class="pi" [class.pi-chevron-down]="isOpen(row.key)" [class.pi-chevron-right]="!isOpen(row.key)" aria-hidden="true"></i> {{ row.label }}
                        </button>
                      } @else {
                        {{ row.label }}<code class="sub">{{ row.code }}</code>
                      }
                    </th>
                    @for (cell of row.cells; track $index) {
                      <td class="matrix__cell" [class.matrix__cell--on]="!!cell">{{ cell }}</td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <p class="muted">A module row counts the role's permissions in it; open it for each permission's reach (Own, Reports, Dept, Branch, Entity, BU, All).</p>
        }
        @case ('menu') {
          <div class="scroll">
            <table class="table matrix">
              <thead>
                <tr>
                  <th scope="col" class="matrix__first">Menu page</th>
                  @for (role of roles(); track role.id) {
                    <th scope="col"><a [routerLink]="['/admin/roles', role.id]" [queryParams]="{ tab: 'menu' }">{{ role.name }}</a></th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (row of menuRows(); track row.key) {
                  <tr [class.matrix__module]="row.isModule">
                    <th scope="row" class="matrix__first">{{ row.label }}</th>
                    @for (cell of row.cells; track $index) {
                      <td class="matrix__cell" [class.matrix__cell--on]="!!cell" [title]="cell">{{ cell ? '✓' : '' }}<span class="sr-only">{{ cell ? 'Opens' : 'Not opened' }}</span></td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <p class="muted">A tick means the role on its own puts the page in the menu (hover for the permission that does it). Pages marked * show only to people linked to an employee record.</p>
        }
      }
    }
    @if (failed()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="The access data could not be loaded" description="Try again in a moment." />
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-3); }
    .tools { display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-3); }
    .tools input[type='search'] { min-width: min(20rem, 100%); }
    .tools__check { display: flex; align-items: center; gap: var(--gggs-space-2); }
    .scroll { overflow-x: auto; border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-lg); background: var(--gggs-color-surface-raised); }
    .num { text-align: end; }
    .sub { display: block; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    .chips { display: flex; flex-wrap: wrap; gap: var(--gggs-space-1); }
    .chip { padding: 0 var(--gggs-space-2); border-radius: var(--gggs-radius-pill); background: var(--gggs-color-surface-sunken); box-shadow: inset 0 0 0 1px var(--gggs-color-border); font-size: var(--gggs-font-size-sm); white-space: nowrap; }
    .chip--auto { background: var(--gggs-color-primary-subtle); color: var(--gggs-color-primary); }
    .matrix th, .matrix td { white-space: nowrap; }
    .matrix__first { position: sticky; inset-inline-start: 0; z-index: 1; min-width: 16rem; background: var(--gggs-color-surface-raised); text-align: start; }
    .matrix__module th, .matrix__module td { background: var(--gggs-color-surface-sunken); font-weight: var(--gggs-font-weight-semibold); }
    .matrix__toggle { display: flex; align-items: center; gap: var(--gggs-space-2); padding: 0; border: 0; background: none; color: inherit; font: inherit; cursor: pointer; }
    .matrix__cell { text-align: center; color: var(--gggs-color-text-muted); }
    .matrix__cell--on { color: var(--gggs-color-text); font-weight: var(--gggs-font-weight-medium); }
  `,
})
export class AccessOverviewPage {
  private readonly api = inject(AdminApi);
  private readonly permissions = inject(PermissionStore);

  protected readonly tabs: readonly TabItem[] = [
    { key: 'users', label: 'Users' },
    { key: 'matrix', label: 'Roles × permissions' },
    { key: 'menu', label: 'Menu access' },
  ];
  protected readonly tab = signal('users');
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  protected readonly users = signal<readonly UserAccessSummary[]>([]);
  protected readonly roles = signal<readonly Role[]>([]);
  private readonly catalog = signal<readonly PermissionCatalogItem[]>([]);

  protected readonly query = signal('');
  protected readonly roleFilter = signal('');
  protected readonly matrixQuery = signal('');
  protected readonly differencesOnly = signal(false);
  private readonly open = signal<ReadonlySet<string>>(new Set());

  private readonly hasFeature = (feature: string) => this.permissions.hasFeature(feature);

  protected readonly shownUsers = computed(() => {
    const term = this.query().trim().toLowerCase();
    const role = this.roleFilter();
    return this.users().filter(
      (user) =>
        (!role || user.roles.some((grant) => grant.roleId === role)) &&
        (!term || [user.displayName, user.email, user.employeeName ?? ''].some((text) => text.toLowerCase().includes(term))),
    );
  });

  protected readonly matrixRows = computed<readonly MatrixRow[]>(() => {
    const roles = this.roles();
    const reaches = roles.map((role) => new Map(role.permissions.map((permission) => [permission.permissionCode.toLowerCase(), permission.scope])));
    const term = this.matrixQuery().trim().toLowerCase();
    const rows: MatrixRow[] = [];

    for (const group of groupByModule(this.catalog())) {
      const items = group.items.filter((permission) => {
        const cells = reaches.map((reach) => reach.get(permission.code.toLowerCase()) ?? '');
        const differs = new Set(cells).size > 1;
        return (!term || permission.code.includes(term) || group.label.toLowerCase().includes(term) || permissionLabel(permission.code).label.toLowerCase().includes(term)) && (!this.differencesOnly() || differs);
      });
      if (items.length === 0) {
        continue;
      }

      rows.push({
        key: group.module,
        label: `${group.label} (${items.length})`,
        isModule: true,
        cells: reaches.map((reach) => {
          const held = items.filter((permission) => reach.has(permission.code.toLowerCase())).length;
          return held ? String(held) : '';
        }),
      });

      if (term || this.open().has(group.module)) {
        for (const permission of items) {
          rows.push({
            key: permission.code,
            label: permissionLabel(permission.code).label,
            code: permission.code,
            isModule: false,
            cells: reaches.map((reach) => {
              const scope = reach.get(permission.code.toLowerCase());
              return scope ? reachLabel(scope, 'short') : '';
            }),
          });
        }
      }
    }
    return rows;
  });

  /** Every page the tenant's plan offers, against each role on its own. */
  protected readonly menuRows = computed<readonly MatrixRow[]>(() => {
    const everything = navigationFor({ hasPermission: () => true, hasFeature: this.hasFeature, hasEmployee: true });
    const byRole = this.roles().map((role) => {
      const pages = menuPages(codeSet(role.permissions.map((permission) => permission.permissionCode)), this.hasFeature, true);
      return new Map(pages.map((entry) => [entry.page.route ?? entry.page.label, entry.unlockedBy ?? 'Open to everyone signed in']));
    });

    return everything.flatMap((group) => {
      const pages = group.children?.length ? group.children : [group];
      return [
        { key: `group:${group.label}`, label: group.label, isModule: true, cells: byRole.map((map) => (pages.some((page) => map.has(page.route ?? page.label)) ? 'Some pages in this module' : '')) },
        ...pages.map((page) => ({
          key: page.route ?? page.label,
          label: page.label + (page.needsEmployee ? ' *' : ''),
          isModule: false,
          cells: byRole.map((map) => map.get(page.route ?? page.label) ?? ''),
        })),
      ];
    });
  });

  constructor() {
    void this.load();
  }

  protected menuCount(user: UserAccessSummary): number {
    return menuPages(codeSet(user.permissionCodes), this.hasFeature, !!user.employeeId).length;
  }

  protected isOpen(module: string): boolean {
    return this.matrixQuery().trim().length > 0 || this.open().has(module);
  }

  protected toggle(module: string): void {
    const next = new Set(this.open());
    if (next.has(module)) {
      next.delete(module);
    } else {
      next.add(module);
    }
    this.open.set(next);
  }

  private async load(): Promise<void> {
    try {
      const [users, roles, catalog] = await Promise.all([this.api.accessUsers(), this.api.listRoles(), this.api.listPermissionCatalog()]);
      this.users.set(users);
      this.roles.set([...roles].sort((a, b) => a.name.localeCompare(b.name)));
      this.catalog.set(catalog);
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
