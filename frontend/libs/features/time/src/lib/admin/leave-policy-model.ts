import type { LeavePlanAssignment, LeavePlanItemInput, LeavePlanRuleItem, LeavePlanRules } from '@gggs/shared/models';

export const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'] as const;

/** "Jan–Dec", "Apr–Mar": the leave year a start month gives. */
export function leaveYearLabel(startMonth: number): string {
  const last = startMonth === 1 ? 12 : startMonth - 1;
  return `${MONTHS[startMonth - 1]?.slice(0, 3)}–${MONTHS[last - 1]?.slice(0, 3)}`;
}

/** The rules a leave type starts with when added to a policy: generous, then HR tightens them. */
export function defaultRules(): LeavePlanRules {
  return {
    annualEntitlement: 12,
    accrual: 'Monthly',
    carryForwardLimit: 0,
    maxNegativeBalance: 0,
    countSandwichedDays: false,
    eligibleGenderCodes: [],
    minServiceMonths: 0,
    allowDuringProbation: true,
    allowDuringNotice: true,
    minNoticeDays: 0,
    creditTiming: 'StartOfPeriod',
    joiningCutoffDay: 15,
    prorateOnJoining: true,
    encashAtYearEnd: false,
  };
}

/** What a save sends for one leave type: its id and rules, nothing descriptive. */
export function toInput(item: LeavePlanRuleItem | LeavePlanItemInput): LeavePlanItemInput {
  return {
    leaveTypeId: item.leaveTypeId,
    annualEntitlement: item.annualEntitlement,
    accrual: item.accrual,
    carryForwardLimit: item.carryForwardLimit,
    maxNegativeBalance: item.maxNegativeBalance,
    countSandwichedDays: item.countSandwichedDays,
    eligibleGenderCodes: [...item.eligibleGenderCodes],
    minServiceMonths: item.minServiceMonths,
    allowDuringProbation: item.allowDuringProbation,
    allowDuringNotice: item.allowDuringNotice,
    minDaysPerRequest: item.minDaysPerRequest,
    maxConsecutiveDays: item.maxConsecutiveDays,
    maxRequestsPerYear: item.maxRequestsPerYear,
    minNoticeDays: item.minNoticeDays,
    maxBackdatedDays: item.maxBackdatedDays,
    maxAdvanceDays: item.maxAdvanceDays,
    documentRequiredAboveDays: item.documentRequiredAboveDays,
    creditTiming: item.creditTiming,
    joiningCutoffDay: item.joiningCutoffDay,
    prorateOnJoining: item.prorateOnJoining,
    carryForwardExpiryMonths: item.carryForwardExpiryMonths,
    encashAtYearEnd: item.encashAtYearEnd,
    maxEncashDays: item.encashAtYearEnd ? item.maxEncashDays : undefined,
    grantPerOccurrence: item.grantPerOccurrence,
    maxOccurrencesInEmployment: item.maxOccurrencesInEmployment,
  };
}

/** Whether the edited leave types differ from the saved ones (order does not matter). */
export function itemsChanged(saved: readonly (LeavePlanRuleItem | LeavePlanItemInput)[], edited: readonly LeavePlanItemInput[]): boolean {
  const key = (items: readonly (LeavePlanRuleItem | LeavePlanItemInput)[]) =>
    JSON.stringify([...items].map(toInput).sort((a, b) => a.leaveTypeId.localeCompare(b.leaveTypeId)), (_, value) => (value === null ? undefined : value));
  return key(saved) !== key(edited);
}

const days = (value: number) => `${value} ${value === 1 ? 'day' : 'days'}`;

/**
 * The rules in one line, as HR would say them: "12 days a year, 1 credited monthly · up to 3 days
 * at a time · 7 days' notice". Gender names come from the caller's lookup labels.
 */
export function ruleSummary(rules: LeavePlanRules, genderLabels: ReadonlyMap<string, string> = new Map()): string {
  const parts: string[] = [];

  const timing = rules.creditTiming === 'EndOfPeriod' ? 'at the end of each' : 'each';
  if (rules.grantPerOccurrence) {
    const limit = rules.maxOccurrencesInEmployment ? `, up to ${rules.maxOccurrencesInEmployment}× in employment` : '';
    parts.push(`${days(rules.grantPerOccurrence)} each time${limit}`);
  } else if (rules.accrual === 'None') {
    parts.push(rules.maxNegativeBalance >= 366 ? 'Unpaid, no balance needed' : 'Granted by HR');
  } else if (rules.accrual === 'Yearly') {
    parts.push(`${days(rules.annualEntitlement)} a year, credited ${rules.creditTiming === 'EndOfPeriod' ? 'at the year end' : 'at the start'}`);
  } else {
    const [count, period] = rules.accrual === 'Monthly' ? [12, 'month'] : rules.accrual === 'Quarterly' ? [4, 'quarter'] : [2, 'half-year'];
    const share = Math.round((rules.annualEntitlement / count) * 100) / 100;
    parts.push(`${days(rules.annualEntitlement)} a year, ${share} credited ${timing} ${period}`);
  }

  if (rules.eligibleGenderCodes.length) {
    parts.push(`only ${rules.eligibleGenderCodes.map((code) => genderLabels.get(code) ?? code).join(' or ')}`);
  }
  if (rules.minServiceMonths) {
    parts.push(`after ${rules.minServiceMonths} months' service`);
  }
  if (!rules.allowDuringProbation) {
    parts.push('not in probation');
  }
  if (!rules.allowDuringNotice) {
    parts.push('not in notice period');
  }
  if (rules.minDaysPerRequest) {
    parts.push(`at least ${days(rules.minDaysPerRequest)}`);
  }
  if (rules.maxConsecutiveDays) {
    parts.push(`up to ${days(rules.maxConsecutiveDays)} at a time`);
  }
  if (rules.maxRequestsPerYear) {
    parts.push(`${rules.maxRequestsPerYear}× a year`);
  }
  if (rules.minNoticeDays) {
    parts.push(`${days(rules.minNoticeDays)}' notice`);
  }
  if (rules.documentRequiredAboveDays !== undefined && rules.documentRequiredAboveDays !== null) {
    parts.push(`document over ${days(rules.documentRequiredAboveDays)}`);
  }
  if (rules.carryForwardLimit !== undefined && rules.carryForwardLimit !== null && rules.accrual !== 'None') {
    parts.push(rules.carryForwardLimit === 0 ? 'no carry forward' : `carry ${days(rules.carryForwardLimit)} forward`);
  }
  if (rules.carryForwardExpiryMonths) {
    parts.push(`carried days expire after ${rules.carryForwardExpiryMonths} months`);
  }
  if (rules.encashAtYearEnd) {
    parts.push(rules.maxEncashDays ? `encash up to ${days(rules.maxEncashDays)}` : 'excess encashed');
  }
  if (rules.countSandwichedDays) {
    parts.push('sandwich rule');
  }
  return parts.join(' · ');
}

/** The assignments that differ from what is saved, as the save sends them. */
export function assignmentChanges(
  saved: readonly LeavePlanAssignment[],
  edited: ReadonlyMap<string, string | null>,
): { employmentTypeCode: string; planId: string | null }[] {
  return saved
    .filter((row) => edited.has(row.employmentTypeCode) && (edited.get(row.employmentTypeCode) ?? null) !== (row.planId ?? null))
    .map((row) => ({ employmentTypeCode: row.employmentTypeCode, planId: edited.get(row.employmentTypeCode) ?? null }));
}
