import { describe, expect, it } from 'vitest';
import { ApiError } from '@gggs/core/http';
import { describePasswordError } from './change-password-panel';

describe('describePasswordError', () => {
  it('says plainly when the current password is wrong', () => {
    expect(describePasswordError(new ApiError(403, { code: 'CURRENT_PASSWORD_INCORRECT' }))).toBe('Your current password is not correct.');
  });

  it.each(['PASSWORD_REJECTED', 'PASSWORD_REUSED', 'PASSWORD_BREACHED'])('gives the server its own reason for %s', (code) => {
    expect(describePasswordError(new ApiError(422, { code, detail: 'The reason.' }))).toBe('The reason.');
  });

  it('never tells someone to try again when only another password would work', () => {
    expect(describePasswordError(new ApiError(422, { code: 'PASSWORD_REUSED' }))).not.toContain('try again');
  });

  it('asks them to try again for anything else', () => {
    expect(describePasswordError(new ApiError(500, {}))).toContain('try again');
    expect(describePasswordError(new Error('offline'))).toContain('try again');
  });
});
