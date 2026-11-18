import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SessionStore } from '@gggs/core/auth';
import { PermissionStore } from '@gggs/core/permissions';
import { PLATFORM } from './platform-permissions';

interface ConsoleNavItem {
  readonly label: string;
  readonly icon: string;
  readonly link: string;
  readonly permission: string;
}

const NAVIGATION: readonly ConsoleNavItem[] = [
  { label: 'Dashboard', icon: 'pi pi-th-large', link: '/dashboard', permission: PLATFORM.tenantView },
  { label: 'Organisations', icon: 'pi pi-building', link: '/organisations', permission: PLATFORM.tenantView },
  { label: 'Plans & modules', icon: 'pi pi-box', link: '/plans', permission: PLATFORM.tenantView },
  { label: 'Platform staff', icon: 'pi pi-users', link: '/staff', permission: PLATFORM.staffView },
  { label: 'System health', icon: 'pi pi-heart', link: '/health', permission: PLATFORM.healthView },
  { label: 'Activity log', icon: 'pi pi-history', link: '/activity', permission: PLATFORM.tenantView },
];

/**
 * The platform console's frame: a side menu of what the signed-in staff member may open, and who
 * they are. Deliberately its own, not the HR app's shell - nothing here is an organisation's menu.
 */
@Component({
  selector: 'gggs-console-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="console" [class.console--open]="menuOpen()">
      <aside class="console__side" aria-label="Platform console">
        <a class="console__brand" routerLink="/dashboard" (click)="menuOpen.set(false)">
          <span class="console__logo" aria-hidden="true"><i class="pi pi-cog"></i></span>
          <span>
            <strong>Nuts and Bolts</strong>
            <small>Platform console</small>
          </span>
        </a>
        <nav class="console__nav">
          @for (item of navigation(); track item.link) {
            <a
              class="console__link"
              [routerLink]="item.link"
              routerLinkActive="console__link--active"
              (click)="menuOpen.set(false)"
            >
              <i [class]="item.icon" aria-hidden="true"></i>
              <span>{{ item.label }}</span>
            </a>
          }
        </nav>
        <div class="console__me">
          <span class="console__avatar" aria-hidden="true">{{ initials() }}</span>
          <span class="console__who">
            <strong class="truncate">{{ session.displayName() }}</strong>
            <small class="truncate">{{ session.user()?.email }}</small>
          </span>
        </div>
        <a class="console__link" routerLink="/account" routerLinkActive="console__link--active" (click)="menuOpen.set(false)">
          <i class="pi pi-key" aria-hidden="true"></i><span>Change password</span>
        </a>
        <button type="button" class="console__link" (click)="session.signOut()">
          <i class="pi pi-sign-out" aria-hidden="true"></i><span>Sign out</span>
        </button>
      </aside>

      <div class="console__main">
        <header class="console__top">
          <button type="button" class="btn btn--ghost console__burger" aria-label="Menu" (click)="menuOpen.set(!menuOpen())">
            <i class="pi pi-bars" aria-hidden="true"></i>
          </button>
          <span class="console__env">GGGS staff only</span>
        </header>
        <main class="console__content">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
  styles: `
    :host { display: block; min-height: 100vh; background: var(--gggs-color-surface); }
    .console { display: grid; grid-template-columns: 15.5rem 1fr; min-height: 100vh; }
    .console__side {
      position: sticky; top: 0; height: 100vh; display: flex; flex-direction: column; gap: var(--gggs-space-1);
      padding: var(--gggs-space-4) var(--gggs-space-3); background: #1e1b4b; color: #e0e7ff; overflow-y: auto;
    }
    .console__brand { display: flex; gap: var(--gggs-space-3); align-items: center; padding: var(--gggs-space-2); margin-bottom: var(--gggs-space-4); color: inherit; text-decoration: none; }
    .console__brand strong { display: block; color: #fff; }
    .console__brand small, .console__who small { display: block; color: #a5b4fc; font-size: var(--gggs-font-size-xs); }
    .console__logo { display: grid; place-items: center; width: 2.25rem; height: 2.25rem; border-radius: var(--gggs-radius-md); background: #4f46e5; color: #fff; }
    .console__nav { display: flex; flex-direction: column; gap: 2px; flex: 1; }
    .console__link {
      display: flex; align-items: center; gap: var(--gggs-space-3); width: 100%; padding: 0.55rem var(--gggs-space-3);
      border: 0; border-radius: var(--gggs-radius-sm); background: none; color: #c7d2fe; font: inherit; text-align: left;
      text-decoration: none; cursor: pointer;
    }
    .console__link:hover { background: rgb(255 255 255 / 8%); color: #fff; }
    .console__link--active { background: #4338ca; color: #fff; }
    .console__me { display: flex; gap: var(--gggs-space-2); align-items: center; padding: var(--gggs-space-3) var(--gggs-space-2); border-top: 1px solid rgb(255 255 255 / 12%); }
    .console__who { min-width: 0; color: #fff; }
    .console__avatar { flex: none; display: grid; place-items: center; width: 2rem; height: 2rem; border-radius: 50%; background: #6366f1; color: #fff; font-size: var(--gggs-font-size-xs); font-weight: 600; }
    .console__main { min-width: 0; display: flex; flex-direction: column; }
    .console__top { display: flex; align-items: center; justify-content: flex-end; gap: var(--gggs-space-3); padding: var(--gggs-space-2) var(--gggs-space-6); border-bottom: 1px solid var(--gggs-color-border); background: var(--gggs-color-surface-raised); }
    .console__env { font-size: var(--gggs-font-size-xs); color: var(--gggs-color-text-muted); }
    .console__burger { display: none; margin-right: auto; }
    .console__content { padding: var(--gggs-space-6); display: flex; flex-direction: column; gap: var(--gggs-space-4); max-width: 84rem; width: 100%; box-sizing: border-box; }
    @media (max-width: 900px) {
      .console { grid-template-columns: 1fr; }
      .console__side { position: fixed; inset: 0 auto 0 0; width: 16rem; z-index: 20; transform: translateX(-100%); transition: transform 0.2s; }
      .console--open .console__side { transform: none; }
      .console__burger { display: inline-flex; }
      .console__content { padding: var(--gggs-space-4); }
    }
  `,
})
export class ConsoleShell {
  protected readonly session = inject(SessionStore);
  private readonly permissions = inject(PermissionStore);

  protected readonly menuOpen = signal(false);
  protected readonly navigation = computed(() => {
    this.session.user();
    return NAVIGATION.filter((item) => this.permissions.has(item.permission));
  });
  protected readonly initials = computed(() =>
    this.session
      .displayName()
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]?.toUpperCase())
      .join(''),
  );
}
