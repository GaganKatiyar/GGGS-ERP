import { ApiError } from '@gggs/core/http';
import { describe, expect, it } from 'vitest';
import { describeBulkImportError } from './bulk-import-employees-page';

describe('describeBulkImportError', () => {
  it('names a bad row count plainly', () => {
    expect(describeBulkImportError(new ApiError(422, {}))).toBe(
      'Too many rows, or no rows at all - a bulk import accepts 1 to 500 rows.',
    );
  });

  it('falls back to a generic message for anything else', () => {
    expect(describeBulkImportError(new Error('network down'))).toBe(
      'The import could not be submitted. Please try again.',
    );
  });
});
