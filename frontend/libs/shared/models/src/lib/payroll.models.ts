/**
 * Payroll, mirroring the DTOs in `Gggs.Payroll.Api.Contracts.PayrollContracts`.
 */

export type SalaryComponentKind = 'Earning' | 'Deduction';

export type SalaryComponentCalcType = 'Fixed' | 'PercentOfBasic' | 'PercentOfBasicCapped';

export interface SalaryComponent {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly kind: SalaryComponentKind;
  readonly calcType: SalaryComponentCalcType;
  readonly isTaxable: boolean;
  readonly displayOrder: number;
  readonly isActive: boolean;
}

export interface CreateSalaryComponentRequest {
  readonly code: string;
  readonly name: string;
  readonly kind: SalaryComponentKind;
  readonly calcType: SalaryComponentCalcType;
  readonly isTaxable: boolean;
  readonly displayOrder: number;
}

export interface SalaryStructure {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly isActive: boolean;
}

export interface CreateSalaryStructureRequest {
  readonly code: string;
  readonly name: string;
}

export interface SalaryStructureComponent {
  readonly id: string;
  readonly structureId: string;
  readonly componentId: string;
  readonly value: number;
  readonly wageCeiling: number | null;
  readonly displayOrder: number;
}

export interface AddSalaryStructureComponentRequest {
  readonly componentId: string;
  readonly value: number;
  readonly displayOrder: number;
  readonly wageCeiling?: number;
}

export interface EmployeeSalaryComponent {
  readonly id: string;
  readonly componentId: string;
  readonly monthlyAmount: number;
  readonly annualAmount: number;
}

export interface EmployeeSalary {
  readonly id: string;
  readonly employeeId: string;
  readonly structureId: string;
  readonly annualCtc: number;
  readonly currency: string;
  readonly effectiveFrom: string;
  readonly components: readonly EmployeeSalaryComponent[];
}

export interface AssignEmployeeSalaryRequest {
  readonly employeeId: string;
  readonly structureId: string;
  readonly annualCtc: number;
  readonly currency: string;
  readonly effectiveFrom: string;
}

export type PayFrequency = 'Monthly' | 'SemiMonthly' | 'BiWeekly' | 'Weekly';

export interface PayGroup {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly frequency: PayFrequency;
  readonly currency: string;
  readonly isActive: boolean;
}

export interface CreatePayGroupRequest {
  readonly code: string;
  readonly name: string;
  readonly frequency: PayFrequency;
  readonly currency: string;
}

export type PayPeriodStatus = 'Open' | 'Locked' | 'Closed';

export interface PayPeriod {
  readonly id: string;
  readonly payGroupId: string;
  readonly periodCode: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly payDate: string;
  readonly status: PayPeriodStatus;
}

export interface CreatePayPeriodRequest {
  readonly periodCode: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly payDate: string;
}

export type PayrollRunStatus = 'Draft' | 'Calculated' | 'Finalized';

export interface PayrollRun {
  readonly id: string;
  readonly payGroupId: string;
  readonly payPeriodId: string;
  readonly runNo: string;
  readonly status: PayrollRunStatus;
  readonly employeeCount: number;
  readonly grossTotal: number;
  readonly netTotal: number;
  readonly calculatedDate?: string;
  readonly finalizedDate?: string;
}

export interface CreatePayrollRunRequest {
  readonly payGroupId: string;
  readonly payPeriodId: string;
  readonly runNo: string;
}

export interface PayrollRunEmployeeLine {
  readonly componentId: string;
  readonly amount: number;
}

export interface PayrollRunEmployee {
  readonly id: string;
  readonly employeeId: string;
  readonly gross: number;
  readonly netPay: number;
  readonly paidDays: number;
  readonly lopDays: number;
  readonly totalDays: number;
  readonly lines: readonly PayrollRunEmployeeLine[];
}

export interface PayslipLine {
  readonly componentId: string;
  readonly componentName: string;
  readonly kind: SalaryComponentKind;
  readonly amount: number;
}

export interface Payslip {
  readonly id: string;
  readonly employeeId: string;
  readonly payrollRunId: string;
  readonly runNo: string;
  readonly payPeriodId: string;
  readonly periodCode: string;
  readonly payDate: string;
  readonly gross: number;
  readonly netPay: number;
  readonly publishedDate: string;
  readonly lines: readonly PayslipLine[];
}

export interface PayslipListItem {
  readonly id: string;
  readonly payrollRunId: string;
  readonly runNo: string;
  readonly payDate: string;
  readonly netPay: number;
  readonly publishedDate: string;
}
