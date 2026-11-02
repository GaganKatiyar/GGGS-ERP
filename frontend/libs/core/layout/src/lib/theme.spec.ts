import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { ThemeStore } from './theme';

describe('ThemeStore palette', () => {
  const classes = () => document.documentElement.classList;

  beforeEach(() => {
    localStorage.clear();
    // jsdom has no matchMedia; a saved theme means the store never asks for it.
    localStorage.setItem('gggs-theme', 'light');
    classes().remove('gggs-palette-classic', 'gggs-palette-brand-light-menu');
    TestBed.resetTestingModule();
  });

  it('starts on the GGGS brand, which needs no class', () => {
    const store = TestBed.inject(ThemeStore);
    TestBed.tick();
    expect(store.palette()).toBe('brand');
    expect([...classes()].some((name) => name.startsWith('gggs-palette-'))).toBe(false);
  });

  it('applies a chosen palette at once, replaces the previous one and remembers it', () => {
    const store = TestBed.inject(ThemeStore);
    store.setPalette('classic');
    TestBed.tick();
    expect(classes().contains('gggs-palette-classic')).toBe(true);
    expect(localStorage.getItem('gggs-palette')).toBe('classic');

    store.setPalette('brand-light-menu');
    TestBed.tick();
    expect(classes().contains('gggs-palette-classic')).toBe(false);
    expect(classes().contains('gggs-palette-brand-light-menu')).toBe(true);
  });

  it('restores the saved palette, and ignores one it does not know', () => {
    localStorage.setItem('gggs-palette', 'classic');
    expect(TestBed.inject(ThemeStore).palette()).toBe('classic');

    TestBed.resetTestingModule();
    localStorage.setItem('gggs-palette', 'neon');
    expect(TestBed.inject(ThemeStore).palette()).toBe('brand');
  });
});
