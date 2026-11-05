import { ICON_ART } from '@gggs/shared/ui';
import { describe, expect, it } from 'vitest';
import { NAVIGATION, type NavItem } from './navigation';

const everyItem = (items: readonly NavItem[]): NavItem[] => items.flatMap((item) => [item, ...everyItem(item.children ?? [])]);

describe('the pictures on app tiles', () => {
  // Any page can become a recent link, so a page with no drawing would show a plain line icon there.
  it('cover every icon in the menu', () => {
    const missing = everyItem(NAVIGATION)
      .filter((item) => !ICON_ART[item.icon])
      .map((item) => `${item.label} (${item.icon})`);

    expect(missing).toEqual([]);
  });
});
