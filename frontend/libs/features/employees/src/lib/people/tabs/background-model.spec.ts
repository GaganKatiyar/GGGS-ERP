import { describe, expect, it } from 'vitest';
import { durationLabel, educationYears, experienceSpan, fromMonthInput, monthsSpanned, toMonthInput, totalExperience } from './background-model';

describe('background', () => {
  it('shows education years however many are known', () => {
    expect(educationYears({ startYear: 2012, endYear: 2016 })).toBe('2012 – 2016');
    expect(educationYears({ endYear: 2016 })).toBe('Finished 2016');
    expect(educationYears({ startYear: 2024 })).toBe('Started 2024');
    expect(educationYears({})).toBe('');
  });

  it('counts both the first and the last month of a job', () => {
    expect(monthsSpanned('2016-07-01', '2019-03-31')).toBe(33);
    expect(monthsSpanned('2020-01-01', '2020-01-15')).toBe(1);
    expect(durationLabel(33)).toBe('2 yrs 9 mos');
    expect(durationLabel(12)).toBe('1 yr');
    expect(durationLabel(1)).toBe('1 mo');
  });

  it('runs an open job to today', () => {
    expect(experienceSpan({ fromDate: '2024-01-01' }, '2024-06-10')).toBe('Jan 2024 – Present · 6 mos');
  });

  it('counts overlapping jobs once in the total', () => {
    const jobs = [
      { fromDate: '2016-01-01', toDate: '2016-12-31' },
      { fromDate: '2016-07-01', toDate: '2017-06-30' },
    ];
    expect(totalExperience(jobs, '2026-01-01')).toBe(18);
  });

  it('round-trips a month input', () => {
    expect(toMonthInput('2019-04-01')).toBe('2019-04');
    expect(fromMonthInput('2019-04')).toBe('2019-04-01');
    expect(fromMonthInput('')).toBeUndefined();
  });
});
