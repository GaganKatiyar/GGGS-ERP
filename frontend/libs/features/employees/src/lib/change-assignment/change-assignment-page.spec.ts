import { ApiError } from '@gggs/core/http';
import { describe, expect, it } from 'vitest';
import { describeError } from './change-assignment-page';

describe('describeError (transfer/promote)', () => {
  it('explains reporting-to-self in plain language, matching EmploymentRecord.Create\'s own guard', () => {
    expect(
      describeError(
        new ApiError(422, { errorCode: 'EMPLOYMENT_RECORD_CANNOT_REPORT_TO_SELF' }),
      ),
    ).toBe('An employee cannot be their own reporting manager.');
  });

  it('explains an invalid effective date, matching EmploymentRecord.EndOn\'s own guard', () => {
    expect(
      describeError(new ApiError(422, { errorCode: 'EMPLOYMENT_RECORD_DATES_INVALID' })),
    ).toBe('The effective date must be on or after the current assignment started.');
  });

  it('explains a bad organizational reference without exposing the raw 404', () => {
    expect(describeError(new ApiError(404, {}))).toBe(
      'One of the chosen organizational references could not be found.',
    );
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeError(new Error('network down'))).toBe(
      'The change could not be completed. Please try again.',
    );
  });
});
