import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { SessionStore } from '@gggs/core/auth';
import { firstValueFrom } from 'rxjs';
import { locate, type NavItem } from './navigation';
import { VisibleNavigation } from './visible-navigation';

/** How many recent pages the home screen shows. */
export const RECENT_LINKS_SHOWN = 6;

/** The home screen is where these links are shown, so it is never one of them. */
const HOME_ROUTE = '/dashboard';

/** A recent page as a tile: its name and icon are the menu's, so a renamed page shows its new name. */
export interface RecentLink {
  readonly route: string;
  readonly label: string;
  readonly icon: string;
}

/**
 * Turns saved addresses into links, newest first. A page no longer in the person's menu is left out,
 * so losing access or a plan feature takes it off the list without anything being deleted.
 */
export function recentLinks(routes: readonly string[], navigation: readonly NavItem[], limit = RECENT_LINKS_SHOWN): readonly RecentLink[] {
  const links = new Map<string, RecentLink>();
  for (const route of routes) {
    const entry = locate(navigation, route)?.entry;
    // Exact, so a saved page that has left the menu does not turn into its parent.
    if (entry?.route === route && route !== HOME_ROUTE && !links.has(route)) {
      links.set(route, { route, label: entry.label, icon: entry.icon });
    }

    if (links.size === limit) {
      break;
    }
  }

  return [...links.values()];
}

/**
 * The menu pages the signed-in person opened lately, kept on the server so they follow the person to
 * any browser. This only ever hears a menu address, never the record open on the page.
 */
@Injectable({ providedIn: 'root' })
export class RecentPagesStore {
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionStore);
  private readonly navigation = inject(VisibleNavigation);

  private readonly routes = signal<readonly string[]>([]);
  private readonly loadedUser = signal<string | null>(null);
  private loadingFor: string | null = null;

  /** False until this person's list has been asked for and answered, so links do not flash and change. */
  readonly isLoaded = computed(() => {
    const user = this.session.user()?.userId ?? null;
    return user !== null && this.loadedUser() === user;
  });

  readonly links = computed(() => (this.isLoaded() ? recentLinks(this.routes(), this.navigation.items()) : []));

  /** Asks for the signed-in person's list, once per person, so one person never sees another's. */
  async load(): Promise<void> {
    const user = this.session.user()?.userId ?? null;
    if (!user || this.loadedUser() === user || this.loadingFor === user) {
      return;
    }

    this.loadingFor = user;
    this.routes.set([]);
    let routes: readonly string[] = [];
    try {
      routes = await firstValueFrom(this.http.get<readonly string[]>('/api/v1/me/recent-pages'));
    } catch {
      // The home screen falls back to its usual links; there is nothing here worth telling anyone.
    }

    // Whoever is signed in now may not be who this was asked for.
    if (this.session.user()?.userId === user) {
      // Anything opened while this was on its way is newer than the answer.
      this.routes.update((opened) => [...opened, ...routes.filter((route) => !opened.includes(route))]);
      this.loadedUser.set(user);
    }

    this.loadingFor = null;
  }

  /** Called as a menu page opens. Never gets in the way: a failed save only means a shorter list. */
  record(route: string): void {
    if (route === HOME_ROUTE || !this.session.user() || this.routes()[0] === route) {
      return;
    }

    this.routes.update((routes) => [route, ...routes.filter((known) => known !== route)]);
    this.http.post('/api/v1/me/recent-pages', { route }).subscribe({ error: () => undefined });
  }
}
