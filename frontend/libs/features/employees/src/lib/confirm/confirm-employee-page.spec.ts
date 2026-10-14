import { ApiError } from '@gggs/core/http';
import { describe, expect, it } from 'vitest';
import { describeError } from './confirm-employee-page';

describe('describeError (confirm)', () => {
  it('explains an already-confirmed employee in plain language', () => {
    expect(describeError(new ApiError(422, { code: 'EMPLOYEE_NOT_ON_PROBATION' }))).toBe(
      'This employee has already been confirmed.',
    );
  });

  it('explains a confirmation date before joining', () => {
    expect(
      describeError(new ApiError(422, { code: 'EMPLOYEE_CONFIRMATION_BEFORE_JOINING' })),
    ).toBe('The confirmation date cannot be before the employee joined.');
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeError(new Error('network down'))).toBe(
      'The employee could not be confirmed. Please try again.',
    );
  });
});
