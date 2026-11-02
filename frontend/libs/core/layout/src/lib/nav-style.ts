import { Injectable, computed, signal } from '@angular/core';

/** The two menus: the labelled sidebar with its pages, or the slim rail of icons. */
export type NavStyle = 'expanded' | 'compact';

export const NAV_STYLE_KEY = 'gggs-nav-style';

/** Narrower than this, the sidebar would crowd the page, so the rail is shown whatever was chosen. */
export const EXPANDED_MIN_WIDTH_QUERY = '(min-width: 64.0625rem)';

/** The saved menu style; expanded unless someone chose the rail. Storage that throws (private mode) is no choice. */
export function readNavStyle(storage: Pick<Storage, 'getItem'> | undefined): NavStyle {
  try {
    return storage?.getItem(NAV_STYLE_KEY) === 'compact' ? 'compact' : 'expanded';
  } catch {
    return 'expanded';
  }
}

/**
 * Which menu the shell shows. The choice is the user's and kept per browser, like the colour
 * theme; the window decides whether the sidebar fits.
 */
@Injectable({ providedIn: 'root' })
export class NavStyleStore {
  private readonly preference = signal<NavStyle>(readNavStyle(storageOrUndefined()));
  private readonly wide = signal(matches(EXPANDED_MIN_WIDTH_QUERY));

  /** What the user chose. */
  readonly style = this.preference.asReadonly();
  /** Whether the window has room for the sidebar at all. */
  readonly canExpand = this.wide.asReadonly();
  /** The menu actually shown. */
  readonly isExpanded = computed(() => this.preference() === 'expanded' && this.wide());

  constructor() {
    try {
      window.matchMedia(EXPANDED_MIN_WIDTH_QUERY).addEventListener('change', (event) => this.wide.set(event.matches));
    } catch {
      // No matchMedia (tests): stay with what was read.
    }
  }

  toggle(): void {
    this.set(this.preference() === 'expanded' ? 'compact' : 'expanded');
  }

  set(style: NavStyle): void {
    this.preference.set(style);
    try {
      localStorage.setItem(NAV_STYLE_KEY, style);
    } catch {
      // Not remembered, but still applied for this visit.
    }
  }
}

function storageOrUndefined(): Storage | undefined {
  try {
    return localStorage;
  } catch {
    return undefined;
  }
}

function matches(query: string): boolean {
  try {
    return window.matchMedia(query).matches;
  } catch {
    return true;
  }
}
