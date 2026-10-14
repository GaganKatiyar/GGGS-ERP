import { ApiError } from '@gggs/core/http';
import { describe, expect, it } from 'vitest';
import { describeError } from './change-manager-page';

describe('describeError (change manager)', () => {
  it('explains reporting-to-self in plain language', () => {
    expect(
      describeError(
        new ApiError(422, { code: 'EMPLOYMENT_RECORD_CANNOT_REPORT_TO_SELF' }),
      ),
    ).toBe('An employee cannot be their own reporting manager.');
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeError(new Error('network down'))).toBe(
      'The reporting manager could not be changed. Please try again.',
    );
  });
});
