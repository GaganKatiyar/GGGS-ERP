import { describe, expect, it } from 'vitest';
import { isValidJson } from './tenant-settings-page';

describe('isValidJson', () => {
  it('accepts a plain JSON string, matching the server\'s own ISJSON check', () => {
    expect(isValidJson('"hello"')).toBe(true);
  });

  it('accepts a JSON object', () => {
    expect(isValidJson('{"days": 5}')).toBe(true);
  });

  it('rejects an unquoted string - the mistake this check exists to catch before it reaches the server', () => {
    expect(isValidJson('hello')).toBe(false);
  });

  it('rejects an empty value', () => {
    expect(isValidJson('')).toBe(false);
  });
});
