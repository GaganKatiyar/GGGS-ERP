import { ApiError } from '@gggs/core/http';
import { describe, expect, it } from 'vitest';
import { describeError } from './exit-employee-page';

describe('describeError (exit)', () => {
  it('explains that this employee has already left', () => {
    expect(describeError(new ApiError(422, { code: 'EMPLOYEE_ALREADY_LEFT' }))).toBe(
      'This employee has already left.',
    );
  });

  it('explains an exit date before joining', () => {
    expect(
      describeError(new ApiError(422, { code: 'EMPLOYEE_EXIT_BEFORE_JOINING' })),
    ).toBe('The exit date cannot be before the employee joined.');
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeError(new Error('network down'))).toBe(
      'The exit could not be recorded. Please try again.',
    );
  });
});
