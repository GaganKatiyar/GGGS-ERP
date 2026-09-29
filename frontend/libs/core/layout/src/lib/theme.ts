import { Injectable, computed, effect, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

const STORAGE_KEY = 'gggs-theme';
const DARK_CLASS = 'gggs-dark';

/**
 * Light/dark theme, persisted per browser.
 *
 * `.gggs-dark` (styles.scss) and the PrimeNG preset's `darkModeSelector` (app.config.ts) were both
 * already wired up to this class - nothing ever added or removed it. This is that missing piece,
 * behind a service rather than a raw class toggle in the shell so a future settings-page control
 * can read/set the same state.
 */
@Injectable({ providedIn: 'root' })
export class ThemeStore {
  private readonly theme = signal<Theme>(readInitialTheme());
  readonly isDark = computed(() => this.theme() === 'dark');

  constructor() {
    effect(() => {
      document.documentElement.classList.toggle(DARK_CLASS, this.theme() === 'dark');

      try {
        localStorage.setItem(STORAGE_KEY, this.theme());
      } catch {
        // Private browsing / blocked storage: the class still applies for this session, only
        // persistence across reloads is lost.
      }
    });
  }

  toggle(): void {
    this.theme.update((current) => (current === 'dark' ? 'light' : 'dark'));
  }
}

function readInitialTheme(): Theme {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'light' || stored === 'dark') {
      return stored;
    }
  } catch {
    // Private browsing / blocked storage: fall through to the OS preference.
  }

  return window.matchMedia('(prefers-color-scheme: dark)').matches
    ? 'dark'
    : 'light';
}
