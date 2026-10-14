import { ApiError } from '@gggs/core/http';
import { describe, expect, it } from 'vitest';
import { describeError } from './branches-page';

describe('describeError (branches)', () => {
  it('names a duplicate code plainly', () => {
    expect(
      describeError(new ApiError(422, { code: 'ORGANIZATION_CODE_IN_USE' })),
    ).toBe('That code is already in use.');
  });

  it('explains a missing legal entity without exposing the raw 404', () => {
    expect(describeError(new ApiError(404, {}))).toBe(
      'The chosen legal entity could not be found.',
    );
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeError(new Error('network down'))).toBe(
      'The branch could not be created. Please try again.',
    );
  });
});
