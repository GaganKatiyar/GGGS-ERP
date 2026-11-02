import { describe, expect, it } from 'vitest';
import { NAV_STYLE_KEY, readNavStyle } from './nav-style';

describe('readNavStyle', () => {
  it('is the expanded menu unless the rail was chosen', () => {
    expect(readNavStyle({ getItem: () => null })).toBe('expanded');
    expect(readNavStyle({ getItem: (key) => (key === NAV_STYLE_KEY ? 'compact' : null) })).toBe('compact');
    expect(readNavStyle({ getItem: () => 'something else' })).toBe('expanded');
  });

  it('falls back to expanded when storage is missing or throws', () => {
    expect(readNavStyle(undefined)).toBe('expanded');
    expect(readNavStyle({ getItem: () => { throw new Error('blocked'); } })).toBe('expanded');
  });
});
