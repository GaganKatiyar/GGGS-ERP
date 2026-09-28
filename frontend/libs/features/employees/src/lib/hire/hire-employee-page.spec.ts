import { ApiError } from '@gggs/core/http';
import { describe, expect, it } from 'vitest';
import { describeError } from './hire-employee-page';

describe('describeError (hire)', () => {
  it('names a duplicate employee code plainly', () => {
    expect(describeError(new ApiError(422, { errorCode: 'EMPLOYEE_CODE_IN_USE' }))).toBe(
      'That employee code is already in use.',
    );
  });

  it('explains a bad organizational reference without exposing the raw 404', () => {
    expect(describeError(new ApiError(404, {}))).toBe(
      'One of the chosen organizational references could not be found.',
    );
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeError(new Error('network down'))).toBe(
      'The employee could not be hired. Please try again.',
    );
  });
});
