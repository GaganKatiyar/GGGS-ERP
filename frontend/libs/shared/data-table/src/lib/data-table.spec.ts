import { describe, expect, it } from 'vitest';
import { DataTable } from './data-table';

describe('DataTable.format', () => {
  it('shows timestamps as a readable date and time, never as ISO text', () => {
    const shown = DataTable.format('2026-10-20T13:30:20.546', 'datetime');
    expect(shown).not.toContain('T13');
    expect(shown).toMatch(/2026/);
  });

  it('formats an untyped column that happens to hold a timestamp', () => {
    expect(DataTable.format('2026-10-20T13:30:20Z', undefined)).not.toContain('T13:30');
  });

  it('leaves ordinary text alone', () => {
    expect(DataTable.format('EMP00001', undefined)).toBe('EMP00001');
    expect(DataTable.format('2026-10-20', undefined)).toBe('2026-10-20');
  });

  it('spaces out PascalCase statuses and renders blanks as empty', () => {
    expect(DataTable.format('OnNotice', 'status')).toBe('On Notice');
    expect(DataTable.format(null, 'date')).toBe('');
  });
});
