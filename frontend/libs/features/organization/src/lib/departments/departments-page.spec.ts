import { ApiError } from '@gggs/core/http';
import { describe, expect, it } from 'vitest';
import { describeCreateError, describeMoveError } from './departments-page';

describe('describeCreateError', () => {
  it('names a duplicate code plainly', () => {
    expect(
      describeCreateError(new ApiError(422, { code: 'ORGANIZATION_CODE_IN_USE' })),
    ).toBe('That code is already in use.');
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeCreateError(new Error('network down'))).toBe(
      'The department could not be created. Please try again.',
    );
  });
});

describe('describeMoveError', () => {
  it('explains a cycle without repeating the server-facing error code verbatim', () => {
    expect(
      describeMoveError(
        new ApiError(422, { code: 'DEPARTMENT_MOVE_WOULD_CREATE_CYCLE' }),
      ),
    ).toBe('That move would make the department a descendant of itself.');
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeMoveError(new Error('network down'))).toBe(
      'The department could not be moved. Please try again.',
    );
  });
});
