import type { EmployeeProfile } from '@gggs/shared/models';
import { describe, expect, it } from 'vitest';
import { centredSquare } from './profile-photo';
import { initialsOf, monthsServed, nextAnniversary, profileCompleteness, tenureLabel } from './profile-summary';

const on = (year: number, month: number, day: number) => new Date(year, month - 1, day, 15, 30);

describe('tenure', () => {
  it('counts whole months, not starting a month until its day comes round', () => {
    expect(monthsServed('2021-03-01', on(2026, 10, 29))).toBe(67);
    expect(monthsServed('2021-03-30', on(2021, 4, 29))).toBe(0);
    expect(monthsServed('2021-03-30', on(2021, 4, 30))).toBe(1);
  });

  it('reads as years and months, short enough for a tile', () => {
    expect(tenureLabel('2021-03-01', on(2026, 10, 29))).toBe('5 yrs 7 mos');
    expect(tenureLabel('2025-10-29', on(2026, 10, 29))).toBe('1 yr');
    expect(tenureLabel('2026-09-01', on(2026, 10, 29))).toBe('1 mo');
    expect(tenureLabel('2026-10-20', on(2026, 10, 29))).toBe('New joiner');
    expect(tenureLabel('2026-11-15', on(2026, 10, 29))).toBe('Not started');
  });
});

describe('nextAnniversary', () => {
  it('finds the next one on or after today, and which year it marks', () => {
    const next = nextAnniversary('2021-03-01', on(2026, 10, 29));
    expect(next.date).toEqual(new Date(2027, 2, 1));
    expect(next.years).toBe(6);
    expect(next.when).toBe('in 4 months');
  });

  it('says today on the day, and counts days when it is close', () => {
    expect(nextAnniversary('2021-10-29', on(2026, 10, 29))).toMatchObject({ years: 5, when: 'today' });
    expect(nextAnniversary('2021-11-10', on(2026, 10, 29)).when).toBe('in 12 days');
  });

  it('makes the first anniversary the first one for someone who joined this year', () => {
    expect(nextAnniversary('2026-02-01', on(2026, 10, 29))).toMatchObject({ years: 1, date: new Date(2027, 1, 1) });
  });
});

describe('profileCompleteness', () => {
  const profile = (extra: Partial<EmployeeProfile> = {}) => ({ displayName: 'Priya Menon', ...extra }) as EmployeeProfile;

  it('lists what is missing of the employee’s own details', () => {
    const result = profileCompleteness(profile({ officialEmail: 'p@acme.example' }));
    expect(result.percent).toBe(0);
    expect(result.gaps.map((gap) => gap.label)).toEqual([
      'Photo', 'Date of birth', 'Gender', 'Blood group', 'Marital status', 'Nationality',
      'Mobile', 'Personal email', 'Current address', 'Permanent address',
    ]);
  });

  it('scores what is there', () => {
    const result = profileCompleteness(profile({ photoId: 'x', dateOfBirth: '1990-01-01', mobilePhone: '98', gender: { code: 'F', label: 'Female' } }));
    expect(result.percent).toBe(40);
    expect(result.gaps.find((gap) => gap.label === 'Photo')).toBeUndefined();
  });

  it('counts education once the number of entries is known', () => {
    const filled = profile({ photoId: 'x', dateOfBirth: '1990-01-01', mobilePhone: '98', gender: { code: 'F', label: 'Female' } });
    expect(profileCompleteness(filled, 0).gaps.at(-1)).toEqual({ label: 'Education', section: 'education' });
    expect(profileCompleteness(filled, 0).percent).toBe(36);
    expect(profileCompleteness(filled, 2).percent).toBe(45);
  });
});

describe('initialsOf', () => {
  it('takes the first letters of the first two names', () => {
    expect(initialsOf('priya menon nair')).toBe('PM');
    expect(initialsOf('Arjun')).toBe('A');
    expect(initialsOf(undefined)).toBe('');
  });
});

describe('centredSquare', () => {
  it('keeps the middle of a portrait or a landscape picture', () => {
    expect(centredSquare(3000, 4000)).toEqual({ x: 0, y: 500, size: 3000 });
    expect(centredSquare(1920, 1080)).toEqual({ x: 420, y: 0, size: 1080 });
    expect(centredSquare(512, 512)).toEqual({ x: 0, y: 0, size: 512 });
  });
});
