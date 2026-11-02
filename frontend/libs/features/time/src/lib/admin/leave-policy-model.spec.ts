import type { LeavePlanAssignment, LeavePlanItemInput } from '@gggs/shared/models';
import { describe, expect, it } from 'vitest';
import { assignmentChanges, defaultRules, itemsChanged, leaveYearLabel, ruleSummary, toInput } from './leave-policy-model';

const item = (leaveTypeId: string, extra: Partial<LeavePlanItemInput> = {}): LeavePlanItemInput => ({ leaveTypeId, ...defaultRules(), ...extra });

describe('leave policies', () => {
  it('names the leave year from its start month', () => {
    expect(leaveYearLabel(1)).toBe('Jan–Dec');
    expect(leaveYearLabel(4)).toBe('Apr–Mar');
  });

  it('summarises the rules as HR would say them', () => {
    expect(ruleSummary(defaultRules())).toBe('12 days a year, 1 credited each month · no carry forward');
    expect(
      ruleSummary(
        { ...defaultRules(), annualEntitlement: 18, maxConsecutiveDays: 15, minNoticeDays: 7, carryForwardLimit: 30, allowDuringProbation: false, eligibleGenderCodes: ['FEMALE'] },
        new Map([['FEMALE', 'Female']]),
      ),
    ).toBe("18 days a year, 1.5 credited each month · only Female · not in probation · up to 15 days at a time · 7 days' notice · carry 30 days forward");
    expect(ruleSummary({ ...defaultRules(), accrual: 'None', annualEntitlement: 0, maxNegativeBalance: 366, carryForwardLimit: undefined })).toBe('Unpaid, no balance needed');
  });

  it('describes quarterly crediting, grants per occurrence and the year end', () => {
    expect(ruleSummary({ ...defaultRules(), accrual: 'Quarterly', creditTiming: 'EndOfPeriod', carryForwardLimit: 5, encashAtYearEnd: true, maxEncashDays: 3, carryForwardExpiryMonths: 3 })).toBe(
      '12 days a year, 3 credited at the end of each quarter · carry 5 days forward · carried days expire after 3 months · encash up to 3 days',
    );
    expect(ruleSummary({ ...defaultRules(), accrual: 'None', annualEntitlement: 0, carryForwardLimit: undefined, grantPerOccurrence: 182, maxOccurrencesInEmployment: 2, documentRequiredAboveDays: 0 })).toBe(
      '182 days each time, up to 2× in employment · document over 0 days',
    );
  });

  it('notices a changed rule but not a reordering', () => {
    const saved = [item('a'), item('b')];
    expect(itemsChanged(saved, [item('b'), item('a')])).toBe(false);
    expect(itemsChanged(saved, [item('a'), item('b', { minNoticeDays: 3 })])).toBe(true);
    expect(itemsChanged(saved, [item('a')])).toBe(true);
  });

  it('sends only what the save needs', () => {
    expect(Object.keys(toInput({ ...item('a'), leaveTypeCode: 'CL', leaveTypeName: 'Casual', color: '#000', isPaid: true, allowHalfDay: true }))).not.toContain('leaveTypeName');
  });

  it('sends only the engagement types whose policy changed', () => {
    const saved: LeavePlanAssignment[] = [
      { employmentTypeCode: 'FULL_TIME', label: 'Full-time', planId: 'std' },
      { employmentTypeCode: 'INTERN', label: 'Intern' },
    ];
    expect(assignmentChanges(saved, new Map([['FULL_TIME', 'std'], ['INTERN', 'int']]))).toEqual([{ employmentTypeCode: 'INTERN', planId: 'int' }]);
    expect(assignmentChanges(saved, new Map([['FULL_TIME', null]]))).toEqual([{ employmentTypeCode: 'FULL_TIME', planId: null }]);
  });
});
