import { describe, expect, it } from 'vitest';
import { parseCsv, toImportRows } from './employee-import-page';

describe('parseCsv', () => {
  it('splits rows and cells, keeping quoted commas, quotes and newlines', () => {
    const text = 'firstName,designation\r\nAsha,"Engineer, Platform"\n"Ravi ""RK""","Line one\nline two"\n';

    expect(parseCsv(text)).toEqual([
      ['firstName', 'designation'],
      ['Asha', 'Engineer, Platform'],
      ['Ravi "RK"', 'Line one\nline two'],
    ]);
  });

  it('drops blank lines and keeps a last row without a trailing newline', () => {
    expect(parseCsv('a,b\n\n1,2')).toEqual([
      ['a', 'b'],
      ['1', '2'],
    ]);
  });
});

describe('toImportRows', () => {
  it('maps headers case-insensitively, ignores unknown columns and empty cells', () => {
    const { rows, unknown } = toImportRows([
      ['﻿FirstName', 'JOININGDATE', 'shoeSize', 'departmentCode'],
      ['Asha', '2026-11-02', '7', ''],
    ]);

    expect(rows).toEqual([{ firstName: 'Asha', joiningDate: '2026-11-02' }]);
    expect(unknown).toEqual(['shoeSize']);
  });
});
