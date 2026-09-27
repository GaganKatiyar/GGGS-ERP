import { describe, expect, it } from 'vitest';
import { safeReturnUrl } from './return-url';

describe('safeReturnUrl', () => {
  it('keeps a same-site path, including its query string', () => {
    expect(safeReturnUrl('/employees?status=Active')).toBe(
      '/employees?status=Active',
    );
  });

  it('falls back when there is nothing to return to', () => {
    expect(safeReturnUrl(null)).toBe('/dashboard');
    expect(safeReturnUrl(undefined)).toBe('/dashboard');
    expect(safeReturnUrl('')).toBe('/dashboard');
  });

  it('refuses another origin', () => {
    // The dangerous one: a link to our own sign-in page that sends the user somewhere else the
    // moment they authenticate.
    expect(safeReturnUrl('https://evil.example/harvest')).toBe('/dashboard');
    expect(safeReturnUrl('//evil.example/harvest')).toBe('/dashboard');
    expect(safeReturnUrl('/\\evil.example/harvest')).toBe('/dashboard');
  });

  it('refuses a javascript URL', () => {
    expect(safeReturnUrl('javascript:alert(1)')).toBe('/dashboard');
  });

  it('refuses a path carrying control characters', () => {
    expect(safeReturnUrl('/employees\n/../admin')).toBe('/dashboard');
  });

  it('refuses the sign-in page itself, which would bounce the user back', () => {
    expect(safeReturnUrl('/sign-in')).toBe('/dashboard');
    expect(safeReturnUrl('/sign-in?returnUrl=%2Fdashboard')).toBe('/dashboard');
  });
});
