import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal, type OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmptyState, PageHeader, Tabs, type TabItem } from '@gggs/shared/ui';
import type { DataScope, PermissionCatalogItem, Role, RoleAutoAssign, RoleMember } from '@gggs/shared/models';
import { AdminApi } from '@gggs/shared/identity';
import { REACHES, codeSet, diffPermissions, groupByModule, menuPages, permissionLabel, type MenuPage, type ModuleGroup } from '../access/access-model';

/**
 * One role: what it may do (edited as a grid, saved in one go), who holds it, which menu pages it
 * opens, and whether the org chart hands it out. The grid offers the server's own permission
 * catalog (`GET /api/v1/permissions`), so it can never offer a permission nothing enforces.
 */
@Component({
  selector: 'gggs-role-detail-page',
  imports: [DatePipe, RouterLink, PageHeader, EmptyState, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isLoading()) {
      <p>Loading…</p>
    } @else if (role(); as role) {
      <gggs-page-header [title]="role.name" [subtitle]="role.description">
        <a routerLink="/admin/access" class="btn">Access overview</a>
        <a routerLink="/admin/roles" class="btn">Back to roles</a>
      </gggs-page-header>

      <gggs-tabs [tabs]="tabs()" [(selected)]="activeTab" />

      @switch (activeTab()) {
        @case ('permissions') {
          <div class="grid-tools">
            <label class="grid-tools__search">
              <span class="sr-only">Search permissions</span>
              <input type="search" placeholder="Search permissions" [value]="query()" (input)="query.set($any($event.target).value)" />
            </label>
            <label class="grid-tools__check"><input type="checkbox" [checked]="grantedOnly()" (change)="grantedOnly.set($any($event.target).checked)" /> Granted only</label>
            <span class="muted">{{ edited().size }} of {{ catalog().length }} granted</span>
          </div>

          @for (group of shownGroups(); track group.module) {
            <section class="module elevated">
              <div class="module__head">
                <button type="button" class="module__toggle" [attr.aria-expanded]="isOpen(group.module)" (click)="toggle(group.module)">
                  <i class="pi" [class.pi-chevron-down]="isOpen(group.module)" [class.pi-chevron-right]="!isOpen(group.module)" aria-hidden="true"></i>
                  <strong>{{ group.label }}</strong>
                  <span class="muted">{{ grantedIn(group) }} of {{ fullSize(group) }} granted</span>
                </button>
                @if (canManage) {
                  <button type="button" class="btn btn--link btn--sm" (click)="grantViews(group)">Grant all view</button>
                }
              </div>
              @if (isOpen(group.module)) {
                <table class="table module__table">
                  <thead>
                    <tr>
                      <th scope="col">Permission</th>
                      <th scope="col" class="module__granted">Granted</th>
                      <th scope="col">Reach</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (permission of group.items; track permission.code) {
                      @let scope = edited().get(permission.code);
                      <tr [class.module__row--on]="!!scope">
                        <td>
                          {{ label(permission.code) }}
                          @if (permission.isSensitive) {
                            <span class="tag tag--sensitive">Sensitive</span>
                          }
                          <code class="module__code">{{ permission.code }}</code>
                        </td>
                        <td class="module__granted">
                          <input type="checkbox" [checked]="!!scope" [disabled]="!canManage" [attr.aria-label]="'Grant ' + permission.code" (change)="setGranted(permission.code, $any($event.target).checked)" />
                        </td>
                        <td>
                          <select [disabled]="!scope || !canManage" [attr.aria-label]="'Reach of ' + permission.code" (change)="setReach(permission.code, $any($event.target).value)">
                            @for (reach of reaches; track reach.scope) {
                              <option [value]="reach.scope" [selected]="reach.scope === (scope ?? 'Tenant')">{{ reach.long }}</option>
                            }
                          </select>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              }
            </section>
          } @empty {
            <gggs-empty-state icon="pi pi-search" title="No permissions match" />
          }

          @if (canManage && diff().count > 0) {
            <div class="save-bar elevated" role="region" aria-label="Unsaved changes">
              <span><strong>{{ diff().count }}</strong> unsaved {{ diff().count === 1 ? 'change' : 'changes' }}: {{ diff().added.length }} added, {{ diff().removed.length }} removed, {{ diff().changed.length }} reach changed</span>
              <button type="button" class="btn" [disabled]="isActing()" (click)="discard()">Discard</button>
              <button type="button" class="btn btn--primary" [disabled]="isActing()" (click)="save()">{{ isActing() ? 'Saving…' : 'Save' }}</button>
            </div>
          }
        }
        @case ('members') {
          @if (members().length === 0) {
            <gggs-empty-state icon="pi pi-users" title="Nobody holds this role" />
          } @else {
            <table class="table">
              <thead>
                <tr><th scope="col">User</th><th scope="col">Given</th><th scope="col">For</th><th scope="col">Since</th></tr>
              </thead>
              <tbody>
                @for (member of members(); track member.userId + (member.scopeName ?? '')) {
                  <tr>
                    <td><a [routerLink]="['/admin/users', member.userId, 'access']">{{ member.displayName }}</a> <span class="muted">{{ member.email }}</span></td>
                    <td>
                      @if (member.isAutomatic) {
                        <span class="tag">Automatic</span>
                      } @else {
                        By hand
                      }
                    </td>
                    <td>{{ member.scopeName ?? 'Everywhere' }}</td>
                    <td>{{ member.validFrom | date: 'd MMM y' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          }
        }
        @case ('menu') {
          <p class="muted">The menu pages this role opens on its own. Pages marked * show only to people linked to an employee record.</p>
          @for (group of menu(); track group.label) {
            <section class="menu-group">
              <h2>{{ group.label }}</h2>
              <ul>
                @for (entry of group.pages; track entry.page.route) {
                  <li [title]="entry.unlockedBy ? 'Opened by ' + entry.unlockedBy : 'Open to everyone signed in'">{{ entry.page.label }}{{ entry.page.needsEmployee ? ' *' : '' }}</li>
                }
              </ul>
            </section>
          } @empty {
            <gggs-empty-state icon="pi pi-compass" title="No menu pages" description="This role opens no page on its own." />
          }
        }
        @case ('settings') {
          <section class="elevated settings" aria-labelledby="auto-assign-heading">
            <h2 id="auto-assign-heading">Assign automatically</h2>
            <p class="muted">
              The org chart can give this role out: to everyone who manages someone, or to each department's head (for their own department).
              It follows changes in reporting lines and department heads. Roles given by hand on a user's page are never removed.
            </p>
            <div class="settings__row">
              <label class="field">
                <span class="field__label">Assign automatically to</span>
                <select [disabled]="!canManage" (change)="autoAssign.set($any($event.target).value)">
                  <!-- Selected per option: a [value] on the select is applied before the options exist. -->
                  @for (option of autoAssignOptions; track option.value) {
                    <option [value]="option.value" [selected]="option.value === autoAssign()">{{ option.label }}</option>
                  }
                </select>
              </label>
              @if (canManage && autoAssign() !== role.autoAssign) {
                <button type="button" class="btn btn--primary" [disabled]="isActing()" (click)="saveAutoAssign()">Save</button>
              }
            </div>
          </section>
        }
      }

      @if (message()) {
        <p class="alert alert--success" role="status">{{ message() }}</p>
      }
      @if (errorMessage()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ errorMessage() }}</span></p>
      }
    } @else {
      <gggs-empty-state title="Role not found" />
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-3); }
    .grid-tools { display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-4); }
    .grid-tools__search input { min-width: min(22rem, 100%); }
    .grid-tools__check { display: flex; align-items: center; gap: var(--gggs-space-2); }
    .module { padding: var(--gggs-space-2) var(--gggs-space-4); }
    .module__head { display: flex; align-items: center; justify-content: space-between; gap: var(--gggs-space-3); }
    .module__toggle { display: flex; align-items: center; gap: var(--gggs-space-3); min-height: 2.75rem; padding: 0; border: 0; background: none; color: inherit; font: inherit; cursor: pointer; text-align: start; }
    .module__table { margin-block: var(--gggs-space-2); }
    .module__granted { width: 6rem; text-align: center; }
    .module__row--on td:first-child { font-weight: var(--gggs-font-weight-semibold); }
    .module__code { display: block; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    .tag { padding: 0 var(--gggs-space-2); border-radius: var(--gggs-radius-pill); background: var(--gggs-color-primary-subtle); color: var(--gggs-color-primary); font-size: var(--gggs-font-size-xs); font-weight: var(--gggs-font-weight-semibold); }
    .tag--sensitive { margin-inline-start: var(--gggs-space-2); background: var(--gggs-color-warning-subtle); color: var(--gggs-color-text); }
    .save-bar { position: sticky; inset-block-end: var(--gggs-space-4); display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-3); padding: var(--gggs-space-3) var(--gggs-space-4); box-shadow: var(--gggs-shadow-4); }
    .save-bar span { flex: 1; }
    .menu-group h2 { margin: var(--gggs-space-3) 0 var(--gggs-space-2); font-size: var(--gggs-font-size-md); }
    .menu-group ul { display: flex; flex-wrap: wrap; gap: var(--gggs-space-2); margin: 0; padding: 0; list-style: none; }
    .menu-group li { padding: var(--gggs-space-1) var(--gggs-space-3); border-radius: var(--gggs-radius-pill); background: var(--gggs-color-surface-sunken); box-shadow: inset 0 0 0 1px var(--gggs-color-border); }
    .settings { display: flex; flex-direction: column; gap: var(--gggs-space-3); padding: var(--gggs-space-5); }
    .settings h2 { margin: 0; font-size: var(--gggs-font-size-md); }
    .settings p { margin: 0; max-width: 48rem; }
    .settings__row { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--gggs-space-3); }
  `,
})
export class RoleDetailPage implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly permissions = inject(PermissionStore);

  readonly id = input.required<string>();
  /** `?tab=` deep link from the access overview. */
  readonly tab = input<string | undefined>(undefined);

  protected readonly canManage = this.permissions.has('admin.role.manage');
  protected readonly reaches = REACHES;
  protected readonly label = (code: string) => permissionLabel(code).label;

  protected readonly isLoading = signal(true);
  protected readonly isActing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly role = signal<Role | null>(null);
  protected readonly catalog = signal<readonly PermissionCatalogItem[]>([]);
  protected readonly members = signal<readonly RoleMember[]>([]);
  protected readonly activeTab = signal('permissions');

  /** The grid as edited: code → reach. Saved whole. */
  protected readonly edited = signal<ReadonlyMap<string, DataScope>>(new Map());
  protected readonly query = signal('');
  protected readonly grantedOnly = signal(false);
  private readonly open = signal<ReadonlySet<string>>(new Set());

  protected readonly autoAssign = signal<RoleAutoAssign>('None');
  protected readonly autoAssignOptions: readonly { value: RoleAutoAssign; label: string }[] = [
    { value: 'None', label: 'Nobody - only by hand' },
    { value: 'PeopleManagers', label: 'People managers (anyone with direct reports)' },
    { value: 'DepartmentHeads', label: 'Department heads (for the department they head)' },
  ];

  protected readonly tabs = computed<readonly TabItem[]>(() => [
    { key: 'permissions', label: 'Permissions', note: String(this.role()?.permissions.length ?? 0) },
    { key: 'members', label: 'Members', note: String(this.members().length) },
    { key: 'menu', label: 'Menu' },
    { key: 'settings', label: 'Settings' },
  ]);

  protected readonly groups = computed(() => groupByModule(this.catalog()));
  protected readonly diff = computed(() => diffPermissions(this.role()?.permissions ?? [], this.edited()));

  protected readonly shownGroups = computed<readonly ModuleGroup[]>(() => {
    const term = this.query().trim().toLowerCase();
    const edited = this.edited();
    return this.groups()
      .map((group) => ({
        ...group,
        items: group.items.filter(
          (permission) =>
            (!this.grantedOnly() || edited.has(permission.code)) &&
            (!term || permission.code.includes(term) || this.label(permission.code).toLowerCase().includes(term) || group.label.toLowerCase().includes(term)),
        ),
      }))
      .filter((group) => group.items.length > 0);
  });

  /** The pages the role opens, as the shell would show them to someone holding only this role. */
  protected readonly menu = computed(() => {
    const pages = menuPages(codeSet(this.role()?.permissions.map((permission) => permission.permissionCode) ?? []), (feature) => this.permissions.hasFeature(feature), true);
    const groups = new Map<string, MenuPage[]>();
    for (const entry of pages) {
      groups.set(entry.group.label, [...(groups.get(entry.group.label) ?? []), entry]);
    }
    return [...groups].map(([label, entries]) => ({ label, pages: entries }));
  });

  ngOnInit(): void {
    const tab = this.tab();
    if (tab && ['permissions', 'members', 'menu', 'settings'].includes(tab)) {
      this.activeTab.set(tab);
    }
    void this.load();
  }

  /** For the route's leave guard: true while the grid holds edits that are not saved. */
  hasUnsavedChanges(): boolean {
    return this.diff().count > 0;
  }

  protected fullSize(group: ModuleGroup): number {
    return this.groups().find((candidate) => candidate.module === group.module)?.items.length ?? group.items.length;
  }

  protected grantedIn(group: ModuleGroup): number {
    const edited = this.edited();
    return this.groups().find((candidate) => candidate.module === group.module)?.items.filter((permission) => edited.has(permission.code)).length ?? 0;
  }

  protected isOpen(module: string): boolean {
    return this.query().trim().length > 0 || this.open().has(module);
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

  protected setGranted(code: string, granted: boolean): void {
    const next = new Map(this.edited());
    if (granted) {
      next.set(code, this.role()?.permissions.find((permission) => permission.permissionCode === code)?.scope ?? 'Tenant');
    } else {
      next.delete(code);
    }
    this.edited.set(next);
  }

  protected setReach(code: string, scope: DataScope): void {
    this.edited.set(new Map(this.edited()).set(code, scope));
  }

  protected grantViews(group: ModuleGroup): void {
    const next = new Map(this.edited());
    for (const permission of group.items.filter((candidate) => candidate.action === 'view' && !next.has(candidate.code))) {
      next.set(permission.code, 'Tenant');
    }
    this.edited.set(next);
    this.open.set(new Set(this.open()).add(group.module));
  }

  protected discard(): void {
    this.reset(this.role());
  }

  protected async save(): Promise<void> {
    if (this.isActing()) {
      return;
    }

    await this.act(async () => {
      const permissions = [...this.edited()].map(([permissionCode, scope]) => ({ permissionCode, scope }));
      const role = await this.api.replaceRolePermissions(this.id(), permissions);
      this.reset(role);
      this.message.set('Saved. Everyone holding this role has the change now.');
    });
  }

  protected async saveAutoAssign(): Promise<void> {
    await this.act(async () => {
      const role = await this.api.setRoleAutoAssign(this.id(), this.autoAssign());
      this.role.set(role);
      this.members.set(await this.api.roleMembers(this.id()));
      this.message.set(role.autoAssign === 'None' ? 'Saved. Nobody is given this role automatically any more.' : 'Saved. The org chart now gives this role out, starting now.');
    });
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);
    try {
      const [roles, catalog, members] = await Promise.all([this.api.listRoles(), this.api.listPermissionCatalog(), this.api.roleMembers(this.id()).catch(() => [])]);
      const role = roles.find((candidate) => candidate.id === this.id()) ?? null;
      this.catalog.set(catalog);
      this.members.set(members);
      this.reset(role);
      // Open the modules the role already reaches into; the rest stay folded.
      this.open.set(new Set(catalog.filter((permission) => this.edited().has(permission.code)).map((permission) => permission.module)));
    } finally {
      this.isLoading.set(false);
    }
  }

  private reset(role: Role | null): void {
    this.role.set(role);
    this.autoAssign.set(role?.autoAssign ?? 'None');
    this.edited.set(new Map((role?.permissions ?? []).map((permission) => [permission.permissionCode, permission.scope])));
  }

  private async act(call: () => Promise<void>): Promise<void> {
    this.isActing.set(true);
    this.errorMessage.set(null);
    this.message.set(null);
    try {
      await call();
    } catch (error) {
      this.errorMessage.set(describeError(error));
    } finally {
      this.isActing.set(false);
    }
  }
}

export function describeError(error: unknown): string {
  if (error instanceof ApiError && error.errorCode === 'PERMISSION_UNKNOWN') {
    return 'That is not a permission this platform defines.';
  }

  if (error instanceof ApiError && error.errorCode === 'LAST_ADMINISTRATOR') {
    return error.message;
  }

  return 'That permission could not be granted. Please try again.';
}
