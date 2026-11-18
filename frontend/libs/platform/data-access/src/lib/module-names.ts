/**
 * The product's modules (plan feature codes, backend Features.All) as the console names them, in the
 * order the HR app's menu shows them.
 */
export const MODULE_NAMES: Readonly<Record<string, string>> = {
  CoreHr: 'Core HR',
  Workforce: 'Workforce planning',
  Recruitment: 'Recruitment',
  Onboarding: 'Onboarding',
  Time: 'Time & attendance',
  Payroll: 'Payroll',
  Compensation: 'Compensation',
  Benefits: 'Benefits',
  Expenses: 'Expenses',
  Performance: 'Performance',
  Learning: 'Learning & development',
  Career: 'Career',
  Rewards: 'Rewards & recognition',
  Engagement: 'Engagement',
  Helpdesk: 'Helpdesk',
  Assets: 'Assets',
  Documents: 'Documents',
  Compliance: 'Compliance',
  Separation: 'Separation',
  Projects: 'Projects',
  Resources: 'Resources',
  Timesheets: 'Timesheets',
  ProjectFinancials: 'Project financials',
  Analytics: 'Analytics',
  AI: 'AI assistant',
};

export const ALL_MODULES: readonly string[] = Object.keys(MODULE_NAMES);

export function moduleName(code: string): string {
  return MODULE_NAMES[code] ?? code;
}

/** The colour a tenant status shows in, matching the shared status badges. */
export function statusTone(status: string): 'success' | 'warning' | 'danger' | 'info' | 'neutral' {
  switch (status) {
    case 'Active':
      return 'success';
    case 'Provisioning':
      return 'info';
    case 'Suspended':
      return 'danger';
    case 'Offboarding':
      return 'warning';
    default:
      return 'neutral';
  }
}
