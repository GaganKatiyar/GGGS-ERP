import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { AppArt, ICON_ART } from './app-art';

/** The test run starts in the workspace or in this library, so look from both. */
function readSprite(): string {
  for (const start of [process.cwd(), resolve(process.cwd(), '../../..')]) {
    try {
      return readFileSync(resolve(start, 'apps/hrms-web/public/app-art.svg'), 'utf8');
    } catch {
      // Try the next place.
    }
  }

  throw new Error('app-art.svg not found');
}

const drawings = new DOMParser().parseFromString(readSprite(), 'image/svg+xml');
const symbols = [...drawings.querySelectorAll('symbol')];

@Component({
  imports: [AppArt],
  template: `<gggs-app-art [icon]="icon()" />`,
})
class Host {
  readonly icon = signal('pi pi-wallet');
}

describe('the drawings', () => {
  it('are valid, with no name used twice', () => {
    expect(drawings.querySelector('parsererror')).toBeNull();
    const names = symbols.map((symbol) => symbol.id);
    expect(new Set(names).size).toBe(names.length);
  });

  it('are all on the same grid', () => {
    expect(symbols.filter((symbol) => symbol.getAttribute('viewBox') !== '0 0 48 48').map((symbol) => symbol.id)).toEqual([]);
  });

  it('exist for every icon that names one, and none is left unused', () => {
    const drawn = new Set(symbols.map((symbol) => symbol.id));
    const named = new Set(Object.values(ICON_ART));
    expect([...named].filter((name) => !drawn.has(name))).toEqual([]);
    expect([...drawn].filter((name) => !named.has(name))).toEqual([]);
  });
});

describe('AppArt', () => {
  const render = (icon: string) => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.icon.set(icon);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('shows the drawing for a known icon, hidden from screen readers', () => {
    const element = render('pi pi-wallet');
    expect(element.querySelector('use')?.getAttribute('href')).toMatch(/^app-art\.svg\?v=\d+#wallet$/);
    expect(element.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(element.querySelector('i')).toBeNull();
  });

  it('falls back to the line icon for one with no drawing, so a tile is never empty', () => {
    const element = render('pi pi-calendar-plus');
    expect(element.querySelector('svg')).toBeNull();
    expect(element.querySelector('i')?.className).toBe('pi pi-calendar-plus');
  });
});
