import { ApiError } from '@gggs/core/http';
import { describe, expect, it } from 'vitest';
import { describeError } from './rehire-employee-page';

describe('describeError (rehire)', () => {
  it('explains that only a former employee can be rehired', () => {
    expect(describeError(new ApiError(422, { errorCode: 'EMPLOYEE_NOT_EXITED' }))).toBe(
      'Only a former employee who has exited can be rehired.',
    );
  });

  it('explains a bad organizational reference without exposing the raw 404', () => {
    expect(describeError(new ApiError(404, {}))).toBe(
      'One of the chosen organizational references could not be found.',
    );
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeError(new Error('network down'))).toBe(
      'The employee could not be rehired. Please try again.',
    );
  });
});
