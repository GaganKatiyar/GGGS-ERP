import { ALL_MODULES, moduleName, statusTone } from './module-names';

describe('module names', () => {
  it('lists every plan feature code the backend defines (Features.All), once', () => {
    expect(ALL_MODULES).toHaveLength(25);
    expect(new Set(ALL_MODULES).size).toBe(ALL_MODULES.length);
    expect(ALL_MODULES).toContain('CoreHr');
    expect(ALL_MODULES).toContain('AI');
  });

  it('names a known module and falls back to the code for an unknown one', () => {
    expect(moduleName('Time')).toBe('Time & attendance');
    expect(moduleName('SomethingNew')).toBe('SomethingNew');
  });

  it('colours an organisation by how usable it is', () => {
    expect(statusTone('Active')).toBe('success');
    expect(statusTone('Suspended')).toBe('danger');
    expect(statusTone('Offboarding')).toBe('warning');
    expect(statusTone('Archived')).toBe('neutral');
  });
});
