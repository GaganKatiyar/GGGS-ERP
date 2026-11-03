import { Injectable, computed, effect, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

/** The colour palettes the header's picker offers; `brand` is the default in styles/_tokens.scss. */
export type Palette = 'brand' | 'brand-light-menu' | 'classic';

export interface PaletteOption {
  readonly key: Palette;
  readonly label: string;
  readonly description: string;
  /** Menu rail and action colour, for the picker's little preview. */
  readonly swatch: readonly [string, string];
}

export const PALETTES: readonly PaletteOption[] = [
  { key: 'brand', label: 'GGGS brand', description: 'Charcoal menu, brand red', swatch: ['#16202b', '#d81324'] },
  { key: 'brand-light-menu', label: 'GGGS brand, light menu', description: 'White menu, brand red', swatch: ['#ffffff', '#d81324'] },
  { key: 'classic', label: 'Classic blue', description: 'Navy menu, electric blue', swatch: ['#142b4a', '#2563eb'] },
];

const STORAGE_KEY = 'gggs-theme';
const PALETTE_KEY = 'gggs-palette';
const DARK_CLASS = 'gggs-dark';
const PALETTE_CLASS_PREFIX = 'gggs-palette-';

/**
 * Light/dark theme and colour palette, persisted per browser.
 *
 * `.gggs-dark` and the `gggs-palette-*` classes (styles/_tokens.scss) and the PrimeNG preset's
 * `darkModeSelector` (app.config.ts) all key off classes on <html>; this sets them. Both choices
 * are also applied before the app boots by public/theme-boot.js, so a reload never flashes the
 * default first - keep the storage keys and class names there in step with these.
 */
@Injectable({ providedIn: 'root' })
export class ThemeStore {
  private readonly theme = signal<Theme>(readInitialTheme());
  private readonly paletteState = signal<Palette>(readInitialPalette());
  readonly isDark = computed(() => this.theme() === 'dark');
  readonly palette = this.paletteState.asReadonly();

  constructor() {
    effect(() => {
      document.documentElement.classList.toggle(DARK_CLASS, this.theme() === 'dark');
      persist(STORAGE_KEY, this.theme());
    });

    effect(() => {
      const palette = this.paletteState();
      for (const option of PALETTES) {
        document.documentElement.classList.toggle(PALETTE_CLASS_PREFIX + option.key, option.key === palette && palette !== 'brand');
      }
      persist(PALETTE_KEY, palette);
    });
  }

  toggle(): void {
    this.theme.update((current) => (current === 'dark' ? 'light' : 'dark'));
  }

  setPalette(palette: Palette): void {
    this.paletteState.set(palette);
  }
}

function persist(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Private browsing / blocked storage: the class still applies for this session, only
    // persistence across reloads is lost.
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

function readInitialPalette(): Palette {
  try {
    const stored = localStorage.getItem(PALETTE_KEY);
    if (PALETTES.some((option) => option.key === stored)) {
      return stored as Palette;
    }
  } catch {
    // Blocked storage: the default palette.
  }

  return 'brand';
}
