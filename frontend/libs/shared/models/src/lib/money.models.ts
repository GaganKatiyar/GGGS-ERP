/** Money contracts (M7): payroll, compliance, compensation, benefits and project financials, as the API returns them. */

// ----- Compliance ----------------------------------------------------------------------------

export type StatutoryRuleType = 'ProvidentFund' | 'StateInsurance' | 'ProfessionalTax' | 'IncomeTax' | 'Gratuity' | 'LabourWelfareFund';
export type RuleStatus = 'Draft' | 'Active' | 'Retired';

export const STATUTORY_RULE_TYPES: readonly StatutoryRuleType[] = ['ProvidentFund', 'StateInsurance', 'ProfessionalTax', 'IncomeTax', 'Gratuity', 'LabourWelfareFund'];

export interface RuleItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly type: StatutoryRuleType;
  readonly countryCode: string;
  readonly stateCode?: string;
  readonly version: number;
  readonly effectiveFrom: string;
  readonly effectiveTo?: string;
  readonly status: RuleStatus;
  readonly configuration: Readonly<Record<string, unknown>>;
  readonly eligibility?: string;
  readonly notes?: string;
  readonly publishedDate?: string;
  readonly inForce: boolean;
}

export interface RuleSummary {
  readonly code: string;
  readonly name: string;
  readonly type: StatutoryRuleType;
  readonly countryCode: string;
  readonly stateCode?: string;
  readonly current?: RuleItem;
  readonly draft?: RuleItem;
  readonly upcoming?: RuleItem;
  readonly versions: number;
}

export type FilingKind = 'ProvidentFundReturn' | 'StateInsuranceReturn' | 'ProfessionalTaxReturn' | 'TaxDeposit' | 'TaxReturn' | 'TaxCertificate' | 'LabourWelfareReturn' | 'Other';
export type FilingFrequency = 'Monthly' | 'Quarterly' | 'HalfYearly' | 'Annual';
export type FilingStatus = 'Pending' | 'Filed' | 'Waived';

export const FILING_KINDS: readonly FilingKind[] = ['ProvidentFundReturn', 'StateInsuranceReturn', 'ProfessionalTaxReturn', 'TaxDeposit', 'TaxReturn', 'TaxCertificate', 'LabourWelfareReturn', 'Other'];
export const FILING_FREQUENCIES: readonly FilingFrequency[] = ['Monthly', 'Quarterly', 'HalfYearly', 'Annual'];

export interface ObligationItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly kind: FilingKind;
  readonly frequency: FilingFrequency;
  readonly dueDay: number;
  readonly dueMonthOffset: number;
  readonly stateCode?: string;
  readonly ownerEmployeeId?: string;
  readonly ownerName?: string;
  readonly description?: string;
  readonly startsFrom: string;
  readonly isActive: boolean;
  readonly nextDue?: string;
}

export interface FilingItem {
  readonly id: string;
  readonly obligationId: string;
  readonly name: string;
  readonly kind: FilingKind;
  readonly periodLabel: string;
  readonly periodStart: string;
  readonly periodEnd: string;
  readonly dueDate: string;
  readonly status: FilingStatus;
  readonly isOverdue: boolean;
  readonly daysToDue: number;
  readonly filedOn?: string;
  readonly filedByName?: string;
  readonly reference?: string;
  readonly amount?: number;
  readonly notes?: string;
  readonly documentId?: string;
  readonly ownerName?: string;
}

export type RegistrationKind = 'License' | 'Registration' | 'Certificate' | 'Audit' | 'Inspection';
export const REGISTRATION_KINDS: readonly RegistrationKind[] = ['License', 'Registration', 'Certificate', 'Audit', 'Inspection'];

export interface RegistrationItem {
  readonly id: string;
  readonly kind: RegistrationKind;
  readonly name: string;
  readonly number?: string;
  readonly authority?: string;
  readonly stateCode?: string;
  readonly legalEntityId?: string;
  readonly issuedOn?: string;
  readonly expiresOn?: string;
  readonly renewalLeadDays: number;
  readonly ownerEmployeeId?: string;
  readonly ownerName?: string;
  readonly notes?: string;
  readonly documentId?: string;
  readonly validity: 'Valid' | 'ExpiringSoon' | 'Expired' | 'NoExpiry';
  readonly daysToExpiry?: number;
}

export interface RegistrationInput {
  readonly kind: RegistrationKind;
  readonly name: string;
  readonly number?: string | null;
  readonly authority?: string | null;
  readonly stateCode?: string | null;
  readonly legalEntityId?: string | null;
  readonly issuedOn?: string | null;
  readonly expiresOn?: string | null;
  readonly renewalLeadDays: number;
  readonly ownerEmployeeId?: string | null;
  readonly notes?: string | null;
  readonly documentId?: string | null;
}

export interface ComplianceDashboard {
  readonly rulesInForce: number;
  readonly draftRules: number;
  readonly dueIn30Days: number;
  readonly overdue: number;
  readonly filedThisMonth: number;
  readonly expiringRegistrations: number;
  readonly expiredRegistrations: number;
  readonly upcoming: readonly FilingItem[];
  readonly expiring: readonly RegistrationItem[];
}

// ----- Payroll -------------------------------------------------------------------------------

export type ComponentKind = 'Earning' | 'Deduction' | 'EmployerContribution' | 'Reimbursement';
export type CalculationType = 'Fixed' | 'Percentage' | 'Formula' | 'Balancing' | 'Input' | 'Statutory';
export type PayrollInputKind = 'Bonus' | 'Incentive' | 'Commission' | 'Overtime' | 'Arrears' | 'Reimbursement' | 'Deduction';
export type TaxRegime = 'New' | 'Old';
export type RunStatus = 'Draft' | 'Calculated' | 'PendingApproval' | 'Approved' | 'Finalized' | 'Paid' | 'Cancelled';
export type ItemStatus = 'Ok' | 'Warning' | 'Error';
export type DeclarationStatus = 'Draft' | 'Submitted' | 'Verified';
export type InputStatus = 'Pending' | 'Included' | 'Paid' | 'Cancelled';
export type SalaryReason = 'Hire' | 'Revision' | 'Promotion' | 'Correction' | 'Other';

export const COMPONENT_KINDS: readonly ComponentKind[] = ['Earning', 'Deduction', 'EmployerContribution', 'Reimbursement'];
export const CALCULATION_TYPES: readonly CalculationType[] = ['Fixed', 'Percentage', 'Formula', 'Balancing', 'Input'];
export const INPUT_KINDS: readonly PayrollInputKind[] = ['Bonus', 'Incentive', 'Commission', 'Overtime', 'Arrears', 'Reimbursement', 'Deduction'];
export const SALARY_REASONS: readonly SalaryReason[] = ['Hire', 'Revision', 'Promotion', 'Correction', 'Other'];

export interface ComponentItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly kind: ComponentKind;
  readonly calculationType: CalculationType;
  readonly value?: number;
  readonly percentOf?: string;
  readonly formula?: string;
  readonly inputKind?: PayrollInputKind;
  readonly isTaxable: boolean;
  readonly pfApplicable: boolean;
  readonly esiApplicable: boolean;
  readonly ptApplicable: boolean;
  readonly isProrated: boolean;
  readonly showOnPayslip: boolean;
  readonly displayOrder: number;
  readonly isSystem: boolean;
  readonly isActive: boolean;
  readonly description?: string;
}

export interface ComponentInput {
  readonly code?: string | null;
  readonly name: string;
  readonly kind: ComponentKind;
  readonly calculationType: CalculationType;
  readonly value?: number | null;
  readonly percentOf?: string | null;
  readonly formula?: string | null;
  readonly inputKind?: PayrollInputKind | null;
  readonly isTaxable: boolean;
  readonly pfApplicable: boolean;
  readonly esiApplicable: boolean;
  readonly ptApplicable: boolean;
  readonly isProrated: boolean;
  readonly showOnPayslip: boolean;
  readonly displayOrder: number;
  readonly description?: string | null;
}

export interface StructureLineItem {
  readonly componentId: string;
  readonly code: string;
  readonly name: string;
  readonly kind: ComponentKind;
  readonly calculationType: CalculationType;
  readonly value?: number;
  readonly percentOf?: string;
  readonly formula?: string;
  readonly overridden: boolean;
}

export interface StructureItem {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly description?: string;
  readonly isActive: boolean;
  readonly lines: readonly StructureLineItem[];
  readonly employeeCount: number;
}

export interface StructureLineInput {
  readonly componentId: string;
  readonly calculationType?: CalculationType | null;
  readonly value?: number | null;
  readonly percentOf?: string | null;
  readonly formula?: string | null;
}

export interface PreviewLine {
  readonly code: string;
  readonly name: string;
  readonly kind: ComponentKind;
  readonly monthly: number;
  readonly annual: number;
}

export interface TaxComputation {
  readonly regime: string;
  readonly monthsRemaining: number;
  readonly grossTaxableYear: number;
  readonly standardDeduction: number;
  readonly hraExemption: number;
  readonly professionalTax: number;
  readonly chapterVIA: number;
  readonly taxableIncome: number;
  readonly taxBeforeRebate: number;
  readonly rebate: number;
  readonly surcharge: number;
  readonly cess: number;
  readonly annualTax: number;
  readonly alreadyDeducted: number;
  readonly monthlyTds: number;
}

export interface SalaryPreview {
  readonly annualCtc: number;
  readonly monthlyCtc: number;
  readonly monthlyGross: number;
  readonly monthlyDeductions: number;
  readonly monthlyTakeHome: number;
  readonly monthlyEmployerContributions: number;
  readonly lines: readonly PreviewLine[];
  readonly tax?: TaxComputation;
  readonly issues: readonly string[];
}

export interface SalaryHistoryItem {
  readonly id: string;
  readonly structureId: string;
  readonly structureName: string;
  readonly annualCtc: number;
  readonly currency: string;
  readonly effectiveFrom: string;
  readonly effectiveTo?: string;
  readonly reason: SalaryReason;
  readonly source: string;
  readonly pfEnabled: boolean;
  readonly notes?: string;
  readonly changePercent?: number;
}

export interface EmployeeSalaryView {
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly current?: SalaryHistoryItem;
  readonly breakdown?: SalaryPreview;
  readonly history: readonly SalaryHistoryItem[];
  readonly gratuity?: { readonly yearsOfService: number; readonly amount: number; readonly eligible: boolean; readonly monthlyBasic: number };
  readonly canManage: boolean;
}

export interface SalaryRow {
  readonly employeeId: string;
  readonly employeeName: string;
  readonly employeeCode: string;
  readonly designation?: string;
  readonly departmentId?: string;
  readonly annualCtc?: number;
  readonly structureName?: string;
  readonly effectiveFrom?: string;
}

export interface PayrollSettings {
  readonly payDay: number;
  readonly prorationBasis: 'CalendarDays' | 'ThirtyDays';
  readonly lopFromAttendance: boolean;
  readonly overtimeFromTimesheets: boolean;
  readonly overtimeMultiplier: number;
  readonly standardHoursPerDay: number;
  readonly defaultRegime: TaxRegime;
  readonly defaultStateCode?: string;
  readonly defaultCurrency?: string;
  readonly employerName?: string;
  readonly employerAddress?: string;
  readonly employerPan?: string;
  readonly employerTan?: string;
  readonly pfEstablishmentCode?: string;
  readonly esiEmployerCode?: string;
}

export interface InputItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly period: string;
  readonly componentId: string;
  readonly componentCode: string;
  readonly componentName: string;
  readonly kind: PayrollInputKind;
  readonly amount: number;
  readonly quantity?: number;
  readonly description: string;
  readonly source: string;
  readonly status: InputStatus;
  readonly runId?: string;
}

export interface DeclarationItem {
  readonly section: string;
  readonly description: string;
  readonly declared: number;
  readonly verified?: number;
}

export interface DeclarationView {
  readonly id?: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly financialYear: number;
  readonly financialYearLabel: string;
  readonly regime: TaxRegime;
  readonly status: DeclarationStatus;
  readonly items: readonly DeclarationItem[];
  readonly monthlyRent?: number;
  readonly metroCity: boolean;
  readonly landlordPan?: string;
  readonly otherIncome?: number;
  readonly previousEmployerIncome?: number;
  readonly previousEmployerTds?: number;
  readonly submittedDate?: string;
  readonly verifiedDate?: string;
  readonly verifierNotes?: string;
  readonly sectionLimits: Readonly<Record<string, number>>;
  readonly comparison?: { readonly newRegimeTax: number; readonly oldRegimeTax: number; readonly better: TaxRegime };
}

export interface DeclarationInput {
  readonly regime: TaxRegime;
  readonly items: readonly DeclarationItem[];
  readonly monthlyRent?: number | null;
  readonly metroCity: boolean;
  readonly landlordPan?: string | null;
  readonly otherIncome?: number | null;
  readonly previousEmployerIncome?: number | null;
  readonly previousEmployerTds?: number | null;
}

export interface DeclarationRow {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly regime: TaxRegime;
  readonly status: DeclarationStatus;
  readonly declared: number;
  readonly submittedDate?: string;
}

export interface RunItem {
  readonly id: string;
  readonly period: string;
  readonly periodEnd: string;
  readonly label: string;
  readonly payDate: string;
  readonly legalEntityId?: string;
  readonly status: RunStatus;
  readonly currency: string;
  readonly employeeCount: number;
  readonly gross: number;
  readonly deductions: number;
  readonly net: number;
  readonly employerCost: number;
  readonly warnings: number;
  readonly errors: number;
  readonly calculatedDate?: string;
  readonly approvedDate?: string;
  readonly finalizedDate?: string;
  readonly paidDate?: string;
  readonly paymentReference?: string;
  readonly notes?: string;
  readonly workflowInstanceId?: string;
}

export interface StatutoryTotals {
  readonly pfEmployee: number;
  readonly pfEmployer: number;
  readonly esiEmployee: number;
  readonly esiEmployer: number;
  readonly professionalTax: number;
  readonly tds: number;
  readonly labourWelfare: number;
}

export interface RunDetail {
  readonly run: RunItem;
  readonly readiness: readonly { readonly code: string; readonly level: 'Info' | 'Warning'; readonly message: string; readonly count: number }[];
  readonly statutory: StatutoryTotals;
  readonly previous?: RunItem;
  readonly netVariancePercent?: number;
  readonly held: number;
}

export interface RunItemRow {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeCode: string;
  readonly employeeName: string;
  readonly designation?: string;
  readonly status: ItemStatus;
  readonly paidDays: number;
  readonly lopDays: number;
  readonly gross: number;
  readonly deductions: number;
  readonly net: number;
  readonly onHold: boolean;
  readonly holdReason?: string;
  readonly issueCount: number;
  readonly firstIssue?: string;
}

export interface PayLine {
  readonly code: string;
  readonly name: string;
  readonly kind: ComponentKind;
  readonly amount: number;
  readonly taxable: boolean;
  readonly fullAmount?: number;
  readonly note?: string;
}

export interface RunItemDetail {
  readonly id: string;
  readonly runId: string;
  readonly employeeId: string;
  readonly employeeCode: string;
  readonly employeeName: string;
  readonly designation?: string;
  readonly status: ItemStatus;
  readonly periodDays: number;
  readonly paidDays: number;
  readonly lopDays: number;
  readonly annualCtc: number;
  readonly gross: number;
  readonly reimbursements: number;
  readonly deductions: number;
  readonly net: number;
  readonly employerContributions: number;
  readonly lines: readonly PayLine[];
  readonly rules: readonly { readonly code: string; readonly ruleId: string; readonly version: number; readonly type: string }[];
  readonly tax?: TaxComputation;
  readonly issues: readonly string[];
  readonly stateCode?: string;
  readonly onHold: boolean;
  readonly holdReason?: string;
}

export interface PayslipItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly period: string;
  readonly periodLabel: string;
  readonly payDate: string;
  readonly currency: string;
  readonly gross: number;
  readonly deductions: number;
  readonly net: number;
  readonly publishedDate: string;
}

export interface PayslipLine {
  readonly code: string;
  readonly name: string;
  readonly amount: number;
  readonly fullAmount?: number;
  readonly note?: string;
}

export interface PayslipDocument {
  readonly employerName: string;
  readonly employerAddress?: string;
  readonly periodLabel: string;
  readonly period: string;
  readonly payDate: string;
  readonly currency: string;
  readonly employee: {
    readonly name: string;
    readonly code: string;
    readonly designation?: string;
    readonly department?: string;
    readonly joiningDate?: string;
    readonly pan?: string;
    readonly uan?: string;
    readonly bankName?: string;
    readonly bankAccount?: string;
    readonly ifsc?: string;
    readonly annualCtc: number;
  };
  readonly periodDays: number;
  readonly paidDays: number;
  readonly lopDays: number;
  readonly earnings: readonly PayslipLine[];
  readonly reimbursements: readonly PayslipLine[];
  readonly deductions: readonly PayslipLine[];
  readonly employerContributions: readonly PayslipLine[];
  readonly gross: number;
  readonly totalDeductions: number;
  readonly net: number;
  readonly netInWords: string;
  readonly yearToDate: { readonly gross: number; readonly deductions: number; readonly net: number; readonly tds: number; readonly providentFund: number };
  readonly tax?: { readonly regime: string; readonly taxableIncome: number; readonly annualTax: number; readonly deductedBefore: number; readonly thisMonth: number };
}

export interface PayslipView {
  readonly id: string;
  readonly employeeId: string;
  readonly runId: string;
  readonly document: PayslipDocument;
}

export interface TaxStatement {
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly pan?: string;
  readonly financialYear: number;
  readonly financialYearLabel: string;
  readonly employerName: string;
  readonly employerTan?: string;
  readonly employerPan?: string;
  readonly grossSalary: number;
  readonly exemptions: number;
  readonly standardDeduction: number;
  readonly professionalTax: number;
  readonly chapterVIA: number;
  readonly taxableIncome: number;
  readonly taxOnIncome: number;
  readonly rebate: number;
  readonly cess: number;
  readonly annualTax: number;
  readonly tdsDeducted: number;
  readonly regime: string;
  readonly months: readonly { readonly period: string; readonly periodLabel: string; readonly gross: number; readonly taxable: number; readonly tds: number }[];
}

export interface PayrollDashboard {
  readonly latestRun?: RunItem;
  readonly lastFinalized?: RunItem;
  readonly trend: readonly { readonly period: string; readonly label: string; readonly employees: number; readonly gross: number; readonly deductions: number; readonly net: number; readonly employerCost: number }[];
  readonly statutory?: StatutoryTotals;
  readonly departments: readonly { readonly name: string; readonly employees: number; readonly gross: number; readonly employerCost: number }[];
  readonly employeesWithSalary: number;
  readonly employeesWithoutSalary: number;
  readonly pendingDeclarations: number;
  readonly pendingInputs: number;
}

// ----- Benefits ------------------------------------------------------------------------------

export type BenefitType = 'Health' | 'Life' | 'Accident' | 'Meal' | 'Transport' | 'Phone' | 'Internet' | 'Wellness' | 'Retirement';
export type EnrollmentStatus = 'Pending' | 'Active' | 'Rejected' | 'Cancelled';
export const BENEFIT_TYPES: readonly BenefitType[] = ['Health', 'Life', 'Accident', 'Meal', 'Transport', 'Phone', 'Internet', 'Wellness', 'Retirement'];

export interface PlanDetails {
  readonly name: string;
  readonly type: BenefitType;
  readonly provider?: string | null;
  readonly description?: string | null;
  readonly coverageAmount?: number | null;
  readonly currency?: string | null;
  readonly employeeContribution: number;
  readonly employerContribution: number;
  readonly dependentContribution: number;
  readonly allowsDependents: boolean;
  readonly maxDependents: number;
  readonly eligibleEmploymentTypes?: readonly string[] | null;
  readonly minServiceMonths: number;
  readonly autoEnroll: boolean;
  readonly mandatory: boolean;
  readonly requiresApproval: boolean;
  readonly enrollmentOpen: boolean;
  readonly employeeShareTaxDeductible: boolean;
}

export interface PlanItem extends PlanDetails {
  readonly id: string;
  readonly code: string;
  readonly currency: string;
  readonly eligibleEmploymentTypes: readonly string[];
  readonly isActive: boolean;
  readonly enrolled: number;
  readonly monthlyEmployeeCost: number;
  readonly monthlyEmployerCost: number;
}

export interface DependentItem {
  readonly id: string;
  readonly fullName: string;
  readonly relationshipCode: string;
  readonly dateOfBirth?: string;
}

export interface EnrollmentItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly planId: string;
  readonly planCode: string;
  readonly planName: string;
  readonly planType: BenefitType;
  readonly status: EnrollmentStatus;
  readonly effectiveFrom: string;
  readonly effectiveTo?: string;
  readonly employeeMonthly: number;
  readonly employerMonthly: number;
  readonly dependents: readonly DependentItem[];
  readonly notes?: string;
  readonly mandatory: boolean;
}

export interface MyBenefits {
  readonly plans: readonly { readonly plan: PlanItem; readonly ineligibility?: string; readonly enrollment?: EnrollmentItem }[];
  readonly dependents: readonly DependentItem[];
  readonly monthlyEmployeeCost: number;
  readonly monthlyEmployerCost: number;
}

export interface BenefitCharge {
  readonly employeeId: string;
  readonly enrollmentId: string;
  readonly planCode: string;
  readonly planName: string;
  readonly planType: string;
  readonly employeeAmount: number;
  readonly employerAmount: number;
  readonly employeeShareTaxDeductible: boolean;
}

// ----- Compensation --------------------------------------------------------------------------

export type RevisionType = 'Merit' | 'Promotion' | 'MarketAdjustment' | 'Correction' | 'Retention';
export type RevisionStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Applied' | 'Rejected' | 'Cancelled';
export type CycleStatus = 'Planning' | 'Open' | 'Closed';
export type AwardType = 'Bonus' | 'Commission' | 'Incentive' | 'PerformanceBonus' | 'ProjectIncentive' | 'Referral' | 'Retention' | 'SpotAward';
export type AwardStatus = 'PendingApproval' | 'Approved' | 'Paid' | 'Rejected' | 'Cancelled';
export type GrantStatus = 'Active' | 'Cancelled' | 'Exercised';

export const REVISION_TYPES: readonly RevisionType[] = ['Merit', 'Promotion', 'MarketAdjustment', 'Correction', 'Retention'];
export const AWARD_TYPES: readonly AwardType[] = ['Bonus', 'Commission', 'Incentive', 'PerformanceBonus', 'ProjectIncentive', 'Referral', 'Retention', 'SpotAward'];

export interface BandItem {
  readonly id: string;
  readonly gradeId: string;
  readonly gradeName?: string;
  readonly currency: string;
  readonly minimum: number;
  readonly midpoint: number;
  readonly maximum: number;
  readonly effectiveFrom: string;
  readonly notes?: string;
  readonly current: boolean;
  readonly employees: number;
  readonly averageCompaRatio?: number;
  readonly belowRange: number;
  readonly aboveRange: number;
}

export interface CycleItem {
  readonly id: string;
  readonly name: string;
  readonly effectiveDate: string;
  readonly budgetPercent: number;
  readonly status: CycleStatus;
  readonly notes?: string;
  readonly budget: number;
  readonly committed: number;
  readonly proposed: number;
  readonly revisions: number;
  readonly averageIncreasePercent: number;
}

export interface RevisionItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly cycleId?: string;
  readonly cycleName?: string;
  readonly type: RevisionType;
  readonly currentCtc: number;
  readonly proposedCtc: number;
  readonly increasePercent: number;
  readonly currency: string;
  readonly effectiveDate: string;
  readonly newGradeId?: string;
  readonly newGradeName?: string;
  readonly newDesignation?: string;
  readonly justification: string;
  readonly performanceRating?: string;
  readonly status: RevisionStatus;
  readonly proposedByName?: string;
  readonly decidedDate?: string;
  readonly appliedDate?: string;
  readonly applyError?: string;
  readonly compaRatioBefore?: number;
  readonly compaRatioAfter?: number;
}

export interface RevisionDetailsInput {
  readonly cycleId?: string | null;
  readonly type: RevisionType;
  readonly proposedCtc: number;
  readonly effectiveDate: string;
  readonly newGradeId?: string | null;
  readonly newStructureId?: string | null;
  readonly newDesignation?: string | null;
  readonly justification: string;
  readonly performanceRating?: string | null;
}

export interface AwardItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly employeeCode?: string;
  readonly type: AwardType;
  readonly amount: number;
  readonly currency: string;
  readonly payoutMonth: string;
  readonly reason: string;
  readonly projectId?: string;
  readonly status: AwardStatus;
  readonly proposedByName?: string;
  readonly decidedDate?: string;
}

export interface VestingEvent {
  readonly date: string;
  readonly units: number;
  readonly cumulative: number;
}

export interface GrantItem {
  readonly id: string;
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly planName: string;
  readonly grantDate: string;
  readonly units: number;
  readonly strikePrice: number;
  readonly fairValuePerUnit?: number;
  readonly currency: string;
  readonly vestingStart: string;
  readonly vestingMonths: number;
  readonly cliffMonths: number;
  readonly vestingFrequencyMonths: number;
  readonly vestedUnits: number;
  readonly exercisedUnits: number;
  readonly vestedValue?: number;
  readonly status: GrantStatus;
  readonly nextVestingDate?: string;
  readonly schedule: readonly VestingEvent[];
  readonly notes?: string;
}

export interface GrantDetailsInput {
  readonly planName: string;
  readonly grantDate: string;
  readonly units: number;
  readonly strikePrice: number;
  readonly fairValuePerUnit?: number | null;
  readonly currency?: string | null;
  readonly vestingStart?: string | null;
  readonly vestingMonths: number;
  readonly cliffMonths: number;
  readonly vestingFrequencyMonths: number;
  readonly notes?: string | null;
}

export interface SalaryLine {
  readonly code: string;
  readonly name: string;
  readonly kind: string;
  readonly monthly: number;
  readonly annual: number;
}

export interface TotalRewards {
  readonly employeeId: string;
  readonly employeeName?: string;
  readonly currency: string;
  readonly annualCtc: number;
  readonly salaryLines: readonly SalaryLine[];
  readonly band?: { readonly gradeName?: string; readonly minimum: number; readonly midpoint: number; readonly maximum: number; readonly compaRatio: number; readonly penetration: number; readonly position: 'Below' | 'Within' | 'Above' };
  readonly variablePayLast12Months: number;
  readonly benefitsEmployerAnnual: number;
  readonly benefits: readonly BenefitCharge[];
  readonly equityVestedValue: number;
  readonly totalAnnualRewards: number;
  readonly revisions: readonly RevisionItem[];
  readonly awards: readonly AwardItem[];
  readonly grants: readonly GrantItem[];
}

// ----- Project financials --------------------------------------------------------------------

export type CostCategory = 'Labour' | 'Vendor' | 'Expense' | 'Infrastructure' | 'Other';
export type InvoiceStatus = 'Draft' | 'Sent' | 'Paid' | 'Cancelled';
export const COST_CATEGORIES: readonly CostCategory[] = ['Labour', 'Vendor', 'Expense', 'Infrastructure', 'Other'];

export interface FinanceSummary {
  readonly contractValue: number;
  readonly revenue: number;
  readonly billed: number;
  readonly collected: number;
  readonly unbilled: number;
  readonly budget: number;
  readonly labourCost: number;
  readonly otherCost: number;
  readonly actualCost: number;
  readonly forecastCost: number;
  readonly forecastRevenue: number;
  readonly grossMargin: number;
  readonly marginPercent?: number;
  readonly forecastMargin: number;
  readonly forecastMarginPercent?: number;
  readonly costVariance: number;
  readonly revenueVariance: number;
  readonly budgetUsedPercent?: number;
  readonly hours: number;
  readonly billableHours: number;
  readonly unpricedHours: number;
  readonly months: readonly { readonly month: string; readonly hours: number; readonly labourCost: number; readonly otherCost: number; readonly revenue: number; readonly billed: number }[];
  readonly categories: readonly { readonly category: CostCategory; readonly budget: number; readonly actual: number; readonly planned: number }[];
}

export interface ProjectFinanceRow {
  readonly projectId: string;
  readonly code: string;
  readonly name: string;
  readonly billingType: string;
  readonly status: string;
  readonly currency: string;
  readonly contractValue: number;
  readonly revenue: number;
  readonly actualCost: number;
  readonly budget: number;
  readonly grossMargin: number;
  readonly marginPercent?: number;
  readonly budgetUsedPercent?: number;
  readonly billed: number;
  readonly unbilled: number;
  readonly forecastMargin: number;
  readonly forecastMarginPercent?: number;
}

export interface PortfolioFinance {
  readonly contractValue: number;
  readonly revenue: number;
  readonly actualCost: number;
  readonly grossMargin: number;
  readonly marginPercent?: number;
  readonly billed: number;
  readonly unbilled: number;
  readonly forecastMargin: number;
  readonly overBudget: number;
  readonly lossMaking: number;
  readonly projects: readonly ProjectFinanceRow[];
}

export interface FinanceRate {
  readonly id: string;
  readonly projectRoleId?: string;
  readonly roleName?: string;
  readonly employeeId?: string;
  readonly employeeName?: string;
  readonly billingRate?: number;
  readonly costRate?: number;
  readonly effectiveFrom: string;
}

export interface FinanceCost {
  readonly id: string;
  readonly category: CostCategory;
  readonly date: string;
  readonly amount: number;
  readonly vendor?: string;
  readonly description: string;
  readonly reference?: string;
  readonly planned: boolean;
}

export interface FinanceInvoice {
  readonly id: string;
  readonly number: string;
  readonly invoiceDate: string;
  readonly dueDate?: string;
  readonly periodFrom?: string;
  readonly periodTo?: string;
  readonly amount: number;
  readonly taxAmount: number;
  readonly status: InvoiceStatus;
  readonly paidOn?: string;
  readonly milestoneId?: string;
  readonly milestoneName?: string;
  readonly notes?: string;
  readonly isOverdue: boolean;
}

export interface ProjectFinance {
  readonly projectId: string;
  readonly code: string;
  readonly name: string;
  readonly billingType: string;
  readonly status: string;
  readonly currency: string;
  readonly progressPercent: number;
  readonly contractValueFromContract?: number;
  readonly summary: FinanceSummary;
  readonly people: readonly { readonly employeeId: string; readonly employeeName?: string; readonly hours: number; readonly billableHours: number; readonly cost: number; readonly revenue: number; readonly costRate?: number; readonly billingRate?: number }[];
  readonly budget: readonly { readonly category: CostCategory; readonly amount: number; readonly notes?: string }[];
  readonly rates: readonly FinanceRate[];
  readonly costs: readonly FinanceCost[];
  readonly invoices: readonly FinanceInvoice[];
  readonly contractValueOverride?: number;
  readonly retainerMonthlyFee?: number;
  readonly milestones: readonly { readonly milestoneId: string; readonly name: string; readonly plannedDate: string; readonly actualDate?: string; readonly status: string; readonly isBillable: boolean; readonly amount?: number }[];
  readonly trend: readonly { readonly month: string; readonly revenue: number; readonly cost: number; readonly billed: number; readonly collected: number; readonly forecastRevenue: number; readonly forecastCost: number }[];
  readonly canManage: boolean;
}
