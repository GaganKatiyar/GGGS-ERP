import { ApiError } from '@gggs/core/http';
import { describe, expect, it } from 'vitest';
import { describeError } from './role-detail-page';

describe('describeError (role permissions)', () => {
  it('explains an unknown permission code in plain language, matching Role.Grant\'s own guard', () => {
    expect(describeError(new ApiError(422, { errorCode: 'PERMISSION_UNKNOWN' }))).toBe(
      'That is not a permission this platform defines.',
    );
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeError(new Error('network down'))).toBe(
      'That permission could not be granted. Please try again.',
    );
  });
});
