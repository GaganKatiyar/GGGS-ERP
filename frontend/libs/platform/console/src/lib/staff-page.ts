import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SessionStore } from '@gggs/core/auth';
import { PermissionStore } from '@gggs/core/permissions';
import { PlatformApi, type InviteStaffResult, type PlatformRoleRow, type PlatformStaffRow } from '@gggs/platform/data-access';
import { FormDialog, PageHeader, Skeleton, dateTime } from '@gggs/shared/ui';
import { PLATFORM, messageOf } from './platform-permissions';

/**
 * GGGS's own platform team: who has a console account, with which roles. An invitation gives a one-time
 * link shown here once, to pass on in person or over chat - the console never emails staff.
 */
@Component({
  selector: 'gggs-console-staff-page',
  imports: [FormsModule, PageHeader, Skeleton, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Platform staff" subtitle="Who can use this console, and what each role allows." />

    @if (notice()) {
      <p class="alert alert--success" role="status"><i class="pi pi-check" aria-hidden="true"></i><span>{{ notice() }}</span></p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    @if (link(); as l) {
      <section class="panel stack invite-link">
        <h2 class="panel__title">Invitation link for {{ l.name }}</h2>
        <p class="muted text-sm">Send this to them yourself. It works once, until {{ when(l.expires) }}, and is not shown again.</p>
        <div class="row">
          <input class="invite-link__url" readonly [value]="l.url" aria-label="Invitation link" (focus)="$any($event.target).select()" />
          <button type="button" class="btn btn--secondary" (click)="copy(l.url)">{{ copied() ? 'Copied' : 'Copy' }}</button>
          <button type="button" class="btn btn--ghost" (click)="link.set(null)">Done</button>
        </div>
      </section>
    }

    <section class="panel panel--flush">
      <header class="panel__header">
        <h2 class="panel__title">Staff</h2>
        @if (canInvite) {
          <button type="button" class="btn btn--primary btn--sm" (click)="openInvite()"><i class="pi pi-user-plus" aria-hidden="true"></i> Invite</button>
        }
      </header>
      @if (staff(); as list) {
        <div class="table-wrap">
          <table class="table">
            <thead><tr><th>Name</th><th>Status</th><th>Roles</th><th>Last sign-in</th><th></th></tr></thead>
            <tbody>
              @for (s of list; track s.id) {
                <tr>
                  <td><strong>{{ s.displayName }}</strong><div class="subtle text-xs">{{ s.email }}</div></td>
                  <td>
                    <span class="badge" [class.badge--success]="s.status === 'Active'" [class.badge--warning]="s.status === 'Invited'" [class.badge--danger]="s.status === 'Disabled'">{{ s.status }}</span>
                    @if (s.lockedOut) { <span class="badge badge--danger">Locked</span> }
                  </td>
                  <td>{{ roleNames(s) }}</td>
                  <td>{{ s.lastLoginDate ? when(s.lastLoginDate) : 'Never' }}</td>
                  <td class="actions">
                    @if (canManage && s.status !== 'Disabled') {
                      <button type="button" class="btn btn--ghost btn--sm" (click)="openRoles(s)">Roles</button>
                      @if (s.lockedOut) {
                        <button type="button" class="btn btn--ghost btn--sm" (click)="act(api.unlockStaff(s.id), s.displayName + ' unlocked.')">Unlock</button>
                      }
                      @if (s.id !== myId()) {
                        <button type="button" class="btn btn--ghost btn--sm" (click)="disable(s)">Disable</button>
                      }
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      } @else {
        <gggs-skeleton [lines]="4" />
      }
    </section>

    <section class="panel">
      <header class="panel__header"><h2 class="panel__title">Roles</h2></header>
      <div class="grid-2">
        @for (role of roles(); track role.id) {
          <div class="role">
            <strong>{{ role.name }}</strong> <span class="subtle text-sm">· {{ role.holders }} {{ role.holders === 1 ? 'person' : 'people' }}</span>
            <p class="muted text-sm">{{ role.description }}</p>
            <p class="text-xs subtle">{{ role.permissions.join(', ') }}</p>
          </div>
        }
      </div>
    </section>

    <gggs-form-dialog [(visible)]="inviteOpen" title="Invite to the platform team" saveLabel="Create invitation" [busy]="busy()" (save)="invite()">
      <div class="stack">
        <label class="field"><span class="field__label">Name *</span><input [(ngModel)]="inviteName" maxlength="200" /></label>
        <label class="field"><span class="field__label">Email *</span><input type="email" [(ngModel)]="inviteEmail" maxlength="256" /></label>
        <fieldset class="checks">
          <legend class="field__label">Roles</legend>
          @for (role of roles(); track role.id) {
            <label><input type="checkbox" [checked]="chosen().includes(role.id)" (change)="toggle(role.id)" /> {{ role.name }}</label>
          }
        </fieldset>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="rolesOpen" [title]="'Roles for ' + (editing()?.displayName ?? '')" saveLabel="Save roles" [busy]="busy()" (save)="saveRoles()">
      <fieldset class="checks">
        @for (role of roles(); track role.id) {
          <label><input type="checkbox" [checked]="chosen().includes(role.id)" (change)="toggle(role.id)" /> {{ role.name }}</label>
        }
      </fieldset>
    </gggs-form-dialog>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .invite-link__url { flex: 1; min-width: 0; font-family: var(--gggs-font-mono, monospace); }
    .role p { margin: var(--gggs-space-1) 0 0; }
    .checks { display: flex; flex-direction: column; gap: var(--gggs-space-2); border: 0; padding: 0; margin: 0; }
  `,
})
export class ConsoleStaffPage {
  protected readonly api = inject(PlatformApi);
  private readonly permissions = inject(PermissionStore);
  private readonly session = inject(SessionStore);

  protected readonly canInvite = this.permissions.has(PLATFORM.staffInvite);
  protected readonly canManage = this.permissions.has(PLATFORM.staffManage);
  protected readonly myId = () => this.session.user()?.userId;

  protected readonly staff = signal<PlatformStaffRow[] | null>(null);
  protected readonly roles = signal<PlatformRoleRow[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly link = signal<{ name: string; url: string; expires: string } | null>(null);
  protected readonly copied = signal(false);
  protected readonly inviteOpen = signal(false);
  protected readonly rolesOpen = signal(false);
  protected readonly editing = signal<PlatformStaffRow | null>(null);
  protected readonly chosen = signal<string[]>([]);
  protected inviteName = '';
  protected inviteEmail = '';

  constructor() {
    void this.load();
  }

  protected when(value: string): string {
    return dateTime(value);
  }

  protected roleNames(row: PlatformStaffRow): string {
    return row.roles.length ? row.roles.map((role) => role.name).join(', ') : 'No role';
  }

  protected toggle(id: string): void {
    this.chosen.update((ids) => (ids.includes(id) ? ids.filter((existing) => existing !== id) : [...ids, id]));
  }

  protected openInvite(): void {
    this.inviteName = '';
    this.inviteEmail = '';
    this.chosen.set([]);
    this.inviteOpen.set(true);
  }

  protected openRoles(row: PlatformStaffRow): void {
    this.editing.set(row);
    this.chosen.set(row.roles.map((role) => role.id));
    this.rolesOpen.set(true);
  }

  protected async invite(): Promise<void> {
    if (!this.inviteName.trim() || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.inviteEmail.trim())) {
      this.error.set('Enter a name and a valid email address.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const result: InviteStaffResult = await this.api.inviteStaff({ email: this.inviteEmail.trim(), displayName: this.inviteName.trim(), roleIds: this.chosen() });
      const query = new URLSearchParams({ id: result.platformStaffId, token: result.invitationToken });
      this.link.set({ name: this.inviteName.trim(), url: `${window.location.origin}/accept-invitation?${query.toString()}`, expires: result.expiresDate });
      this.copied.set(false);
      this.inviteOpen.set(false);
      await this.load();
    } catch (error) {
      this.error.set(messageOf(error, 'The invitation could not be created.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async saveRoles(): Promise<void> {
    const row = this.editing();
    if (!row) return;
    this.rolesOpen.set(false);
    await this.act(this.api.setStaffRoles(row.id, this.chosen()), `Roles for ${row.displayName} saved.`);
  }

  protected async disable(row: PlatformStaffRow): Promise<void> {
    if (!window.confirm(`Disable ${row.displayName}? They will be signed out and cannot sign in again.`)) return;
    await this.act(this.api.disableStaff(row.id), `${row.displayName} disabled.`);
  }

  protected async act(work: Promise<void>, message: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await work;
      this.notice.set(message);
      await this.load();
    } catch (error) {
      this.error.set(messageOf(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async copy(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.copied.set(true);
    } catch {
      this.copied.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const [staff, roles] = await Promise.all([this.api.staff(), this.api.roles()]);
      this.staff.set(staff);
      this.roles.set(roles);
    } catch (error) {
      this.staff.set([]);
      this.error.set(messageOf(error, 'The platform staff could not be loaded.'));
    }
  }
}
