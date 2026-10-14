import { ApiError } from '@gggs/core/http';
import { describe, expect, it } from 'vitest';
import { describeError } from './designations-page';

describe('describeError (designations)', () => {
  it('names a duplicate code plainly', () => {
    expect(
      describeError(new ApiError(422, { code: 'ORGANIZATION_CODE_IN_USE' })),
    ).toBe('That code is already in use.');
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeError(new Error('network down'))).toBe(
      'The designation could not be created. Please try again.',
    );
  });
});
