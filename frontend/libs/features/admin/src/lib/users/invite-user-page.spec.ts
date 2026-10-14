import { ApiError } from '@gggs/core/http';
import { describe, expect, it } from 'vitest';
import { describeError } from './invite-user-page';

describe('describeError (invite user)', () => {
  it('names a duplicate email plainly', () => {
    expect(describeError(new ApiError(422, { code: 'USER_EMAIL_IN_USE' }))).toBe(
      'Someone with that email address already has an account in this organization.',
    );
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeError(new Error('network down'))).toBe(
      'The invitation could not be sent. Please try again.',
    );
  });
});
