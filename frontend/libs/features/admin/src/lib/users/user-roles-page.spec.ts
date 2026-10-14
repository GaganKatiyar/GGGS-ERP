import { ApiError } from '@gggs/core/http';
import { describe, expect, it } from 'vitest';
import { describeError } from './user-roles-page';

describe('describeError (user roles)', () => {
  it('explains the last-administrator guard in plain language', () => {
    expect(describeError(new ApiError(422, { code: 'LAST_ADMINISTRATOR' }))).toBe(
      'This is the only account that can manage users. Give someone else that permission first.',
    );
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeError(new Error('network down'))).toBe(
      'The roles could not be saved. Please try again.',
    );
  });
});
