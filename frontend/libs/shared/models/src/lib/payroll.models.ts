/**
 * Payroll, mirroring the DTOs in `Gggs.Payroll.Api.Contracts.PayrollContracts`.
 */

export type SalaryComponentKind = 'Earning' | 'Deduction';

export type SalaryComponentCalcType = 'Fixed' | 'PercentOfBasic';

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
  readonly displayOrder: number;
}

export interface AddSalaryStructureComponentRequest {
  readonly componentId: string;
  readonly value: number;
  readonly displayOrder: number;
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
