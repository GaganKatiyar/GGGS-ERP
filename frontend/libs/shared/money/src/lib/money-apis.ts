import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  AwardItem,
  AwardStatus,
  AwardType,
  BandItem,
  ComplianceDashboard,
  ComponentInput,
  ComponentItem,
  CostCategory,
  CycleItem,
  DeclarationInput,
  DeclarationRow,
  DeclarationStatus,
  DeclarationView,
  EmployeeSalaryView,
  EnrollmentItem,
  EnrollmentStatus,
  FilingFrequency,
  FilingItem,
  FilingKind,
  FilingStatus,
  FinanceCost,
  FinanceInvoice,
  FinanceRate,
  GrantDetailsInput,
  GrantItem,
  InputItem,
  ItemStatus,
  MyBenefits,
  ObligationItem,
  PagedResult,
  PayrollDashboard,
  PayrollSettings,
  PayslipItem,
  PayslipView,
  PlanDetails,
  PlanItem,
  PortfolioFinance,
  ProjectFinance,
  RegistrationInput,
  RegistrationItem,
  RevisionDetailsInput,
  RevisionItem,
  RevisionStatus,
  RuleItem,
  RuleSummary,
  RunDetail,
  RunItem,
  RunItemDetail,
  RunItemRow,
  RunStatus,
  SalaryPreview,
  SalaryReason,
  SalaryRow,
  StatutoryRuleType,
  StructureItem,
  StructureLineInput,
  TaxRegime,
  TaxStatement,
  TotalRewards,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

type Query = Record<string, string | number | boolean | null | undefined>;

function params(query: Query): Record<string, string | number | boolean> {
  const result: Record<string, string | number | boolean> = {};
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') {
      result[key] = value;
    }
  }
  return result;
}

/** Saves a file the API produced. Fetched through HttpClient so the session cookie and CSRF header go with it. */
async function download(http: HttpClient, url: string, fallbackName: string): Promise<void> {
  const response: HttpResponse<Blob> = await firstValueFrom(http.get(url, { observe: 'response', responseType: 'blob' }));
  const disposition = response.headers.get('content-disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const name = match ? decodeURIComponent(match[1]) : fallbackName;
  const href = URL.createObjectURL(response.body ?? new Blob());
  const link = document.createElement('a');
  link.href = href;
  link.download = name;
  link.click();
  URL.revokeObjectURL(href);
}

export type PayrollReport = 'register' | 'bank-file' | 'ecr' | 'esi' | 'professional-tax' | 'journal';

/** Payroll (`/api/v1/payroll`) and one's own payslips, salary and tax declaration (`/api/v1/me`). */
@Injectable({ providedIn: 'root' })
export class PayrollApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/payroll';

  dashboard(): Promise<PayrollDashboard> {
    return firstValueFrom(this.http.get<PayrollDashboard>(`${this.base}/dashboard`));
  }

  // Set-up
  components(includeInactive = false): Promise<readonly ComponentItem[]> {
    return firstValueFrom(this.http.get<readonly ComponentItem[]>(`${this.base}/components`, { params: { includeInactive } }));
  }

  saveComponent(id: string | null, body: ComponentInput): Promise<ComponentItem> {
    return firstValueFrom(id ? this.http.put<ComponentItem>(`${this.base}/components/${id}`, body) : this.http.post<ComponentItem>(`${this.base}/components`, body));
  }

  async setComponentActive(id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/components/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }

  structures(): Promise<readonly StructureItem[]> {
    return firstValueFrom(this.http.get<readonly StructureItem[]>(`${this.base}/structures`));
  }

  saveStructure(id: string | null, body: { code?: string | null; name: string; description?: string | null; lines: readonly StructureLineInput[] }): Promise<StructureItem> {
    return firstValueFrom(id ? this.http.put<StructureItem>(`${this.base}/structures/${id}`, body) : this.http.post<StructureItem>(`${this.base}/structures`, body));
  }

  async setStructureActive(id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/structures/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }

  preview(structureId: string, body: { annualCtc: number; stateCode?: string | null; regime?: TaxRegime | null; pfEnabled?: boolean }): Promise<SalaryPreview> {
    return firstValueFrom(this.http.post<SalaryPreview>(`${this.base}/structures/${structureId}/preview`, body));
  }

  settings(): Promise<PayrollSettings> {
    return firstValueFrom(this.http.get<PayrollSettings>(`${this.base}/settings`));
  }

  saveSettings(body: PayrollSettings): Promise<PayrollSettings> {
    return firstValueFrom(this.http.put<PayrollSettings>(`${this.base}/settings`, body));
  }

  // Salaries
  salaries(query: { search?: string; withoutSalary?: boolean; page?: number; pageSize?: number } = {}): Promise<PagedResult<SalaryRow>> {
    return firstValueFrom(this.http.get<PagedResult<SalaryRow>>(`${this.base}/salaries`, { params: params({ ...query }) }));
  }

  salary(employeeId: string): Promise<EmployeeSalaryView> {
    return firstValueFrom(this.http.get<EmployeeSalaryView>(`${this.base}/employees/${employeeId}/salary`));
  }

  assignSalary(employeeId: string, body: { structureId: string; annualCtc: number; effectiveFrom: string; reason: SalaryReason; pfEnabled?: boolean | null; notes?: string | null }): Promise<EmployeeSalaryView> {
    return firstValueFrom(this.http.post<EmployeeSalaryView>(`${this.base}/employees/${employeeId}/salary`, body));
  }

  employeePayslips(employeeId: string, year?: number): Promise<readonly PayslipItem[]> {
    return firstValueFrom(this.http.get<readonly PayslipItem[]>(`${this.base}/employees/${employeeId}/payslips`, { params: params({ year }) }));
  }

  // Inputs
  inputs(period: string, employeeId?: string): Promise<readonly InputItem[]> {
    return firstValueFrom(this.http.get<readonly InputItem[]>(`${this.base}/inputs`, { params: params({ period, employeeId }) }));
  }

  addInput(body: { employeeId: string; period: string; componentId: string; amount: number; quantity?: number | null; description: string }): Promise<InputItem> {
    return firstValueFrom(this.http.post<InputItem>(`${this.base}/inputs`, body));
  }

  async cancelInput(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/inputs/${id}`));
  }

  // Tax declarations
  declarations(year?: number, status?: DeclarationStatus | ''): Promise<readonly DeclarationRow[]> {
    return firstValueFrom(this.http.get<readonly DeclarationRow[]>(`${this.base}/tax-declarations`, { params: params({ year, status }) }));
  }

  declaration(id: string): Promise<DeclarationView> {
    return firstValueFrom(this.http.get<DeclarationView>(`${this.base}/tax-declarations/${id}`));
  }

  verifyDeclaration(id: string, accepted: readonly number[], notes: string | null): Promise<DeclarationView> {
    return firstValueFrom(this.http.post<DeclarationView>(`${this.base}/tax-declarations/${id}/verify`, { accepted, notes }));
  }

  reopenDeclaration(id: string): Promise<DeclarationView> {
    return firstValueFrom(this.http.post<DeclarationView>(`${this.base}/tax-declarations/${id}/reopen`, {}));
  }

  // Runs
  runs(year?: number): Promise<readonly RunItem[]> {
    return firstValueFrom(this.http.get<readonly RunItem[]>(`${this.base}/runs`, { params: params({ year }) }));
  }

  createRun(body: { period: string; payDate?: string | null; notes?: string | null }): Promise<RunItem> {
    return firstValueFrom(this.http.post<RunItem>(`${this.base}/runs`, body));
  }

  run(id: string): Promise<RunDetail> {
    return firstValueFrom(this.http.get<RunDetail>(`${this.base}/runs/${id}`));
  }

  calculate(id: string): Promise<RunDetail> {
    return firstValueFrom(this.http.post<RunDetail>(`${this.base}/runs/${id}/calculate`, {}));
  }

  items(id: string, query: { status?: ItemStatus | ''; onHold?: boolean; search?: string; page?: number; pageSize?: number } = {}): Promise<PagedResult<RunItemRow>> {
    return firstValueFrom(this.http.get<PagedResult<RunItemRow>>(`${this.base}/runs/${id}/items`, { params: params({ ...query }) }));
  }

  item(runId: string, itemId: string): Promise<RunItemDetail> {
    return firstValueFrom(this.http.get<RunItemDetail>(`${this.base}/runs/${runId}/items/${itemId}`));
  }

  hold(runId: string, itemId: string, reason: string): Promise<RunItemDetail> {
    return firstValueFrom(this.http.post<RunItemDetail>(`${this.base}/runs/${runId}/items/${itemId}/hold`, { reason }));
  }

  release(runId: string, itemId: string): Promise<RunItemDetail> {
    return firstValueFrom(this.http.post<RunItemDetail>(`${this.base}/runs/${runId}/items/${itemId}/release`, {}));
  }

  submit(id: string): Promise<{ status: RunStatus; workflowInstanceId?: string }> {
    return firstValueFrom(this.http.post<{ status: RunStatus; workflowInstanceId?: string }>(`${this.base}/runs/${id}/submit`, {}));
  }

  finalize(id: string): Promise<RunDetail> {
    return firstValueFrom(this.http.post<RunDetail>(`${this.base}/runs/${id}/finalize`, {}));
  }

  markPaid(id: string, paidOn: string, reference: string | null): Promise<RunItem> {
    return firstValueFrom(this.http.post<RunItem>(`${this.base}/runs/${id}/paid`, { paidOn, reference }));
  }

  cancelRun(id: string): Promise<RunItem> {
    return firstValueFrom(this.http.post<RunItem>(`${this.base}/runs/${id}/cancel`, {}));
  }

  report(runId: string, report: PayrollReport): Promise<void> {
    return download(this.http, `${this.base}/runs/${runId}/reports/${report}`, `${report}.csv`);
  }

  // Payslips
  payslips(period?: string, employeeId?: string): Promise<readonly PayslipItem[]> {
    return firstValueFrom(this.http.get<readonly PayslipItem[]>(`${this.base}/payslips`, { params: params({ period, employeeId }) }));
  }

  payslip(id: string): Promise<PayslipView> {
    return firstValueFrom(this.http.get<PayslipView>(`${this.base}/payslips/${id}`));
  }

  employeeTaxStatement(employeeId: string, year?: number): Promise<TaxStatement> {
    return firstValueFrom(this.http.get<TaxStatement>(`${this.base}/employees/${employeeId}/tax-statement`, { params: params({ year }) }));
  }

  // Self-service
  myPayslips(year?: number): Promise<readonly PayslipItem[]> {
    return firstValueFrom(this.http.get<readonly PayslipItem[]>('/api/v1/me/payslips', { params: params({ year }) }));
  }

  myPayslip(id: string): Promise<PayslipView> {
    return firstValueFrom(this.http.get<PayslipView>(`/api/v1/me/payslips/${id}`));
  }

  myTaxStatement(year?: number): Promise<TaxStatement> {
    return firstValueFrom(this.http.get<TaxStatement>('/api/v1/me/tax-statement', { params: params({ year }) }));
  }

  mySalary(): Promise<EmployeeSalaryView> {
    return firstValueFrom(this.http.get<EmployeeSalaryView>('/api/v1/me/salary'));
  }

  myDeclaration(year?: number): Promise<DeclarationView> {
    return firstValueFrom(this.http.get<DeclarationView>('/api/v1/me/tax-declaration', { params: params({ year }) }));
  }

  saveMyDeclaration(year: number, body: DeclarationInput): Promise<DeclarationView> {
    return firstValueFrom(this.http.put<DeclarationView>(`/api/v1/me/tax-declaration/${year}`, body));
  }

  submitMyDeclaration(year: number): Promise<DeclarationView> {
    return firstValueFrom(this.http.post<DeclarationView>(`/api/v1/me/tax-declaration/${year}/submit`, {}));
  }
}

/** The compliance centre (`/api/v1/compliance`). */
@Injectable({ providedIn: 'root' })
export class ComplianceApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/compliance';

  dashboard(): Promise<ComplianceDashboard> {
    return firstValueFrom(this.http.get<ComplianceDashboard>(`${this.base}/dashboard`));
  }

  rules(type?: StatutoryRuleType | '', state?: string): Promise<readonly RuleSummary[]> {
    return firstValueFrom(this.http.get<readonly RuleSummary[]>(`${this.base}/rules`, { params: params({ type, state }) }));
  }

  versions(code: string): Promise<readonly RuleItem[]> {
    return firstValueFrom(this.http.get<readonly RuleItem[]>(`${this.base}/rules/codes/${encodeURIComponent(code)}`));
  }

  createRule(body: { code: string; name: string; type: StatutoryRuleType; countryCode?: string | null; stateCode?: string | null; effectiveFrom: string; configuration: unknown; eligibility?: string | null; notes?: string | null }): Promise<RuleItem> {
    return firstValueFrom(this.http.post<RuleItem>(`${this.base}/rules`, body));
  }

  draftVersion(id: string, body: { effectiveFrom: string; configuration?: unknown; notes?: string | null }): Promise<RuleItem> {
    return firstValueFrom(this.http.post<RuleItem>(`${this.base}/rules/${id}/versions`, body));
  }

  updateDraft(id: string, body: { name: string; effectiveFrom: string; configuration: unknown; eligibility?: string | null; notes?: string | null }): Promise<RuleItem> {
    return firstValueFrom(this.http.put<RuleItem>(`${this.base}/rules/${id}`, body));
  }

  async discardDraft(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/rules/${id}`));
  }

  publish(id: string): Promise<RuleItem> {
    return firstValueFrom(this.http.post<RuleItem>(`${this.base}/rules/${id}/publish`, {}));
  }

  retire(id: string, lastDay: string): Promise<RuleItem> {
    return firstValueFrom(this.http.post<RuleItem>(`${this.base}/rules/${id}/retire`, { lastDay }));
  }

  obligations(): Promise<readonly ObligationItem[]> {
    return firstValueFrom(this.http.get<readonly ObligationItem[]>(`${this.base}/obligations`));
  }

  saveObligation(id: string | null, body: { code?: string | null; name: string; kind: FilingKind; frequency: FilingFrequency; dueDay: number; dueMonthOffset: number; stateCode?: string | null; ownerEmployeeId?: string | null; description?: string | null }): Promise<ObligationItem> {
    return firstValueFrom(id ? this.http.put<ObligationItem>(`${this.base}/obligations/${id}`, body) : this.http.post<ObligationItem>(`${this.base}/obligations`, body));
  }

  async setObligationActive(id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/obligations/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }

  filings(query: { from?: string; to?: string; status?: FilingStatus | ''; kind?: FilingKind | ''; overdueOnly?: boolean } = {}): Promise<readonly FilingItem[]> {
    return firstValueFrom(this.http.get<readonly FilingItem[]>(`${this.base}/filings`, { params: params({ ...query }) }));
  }

  generate(): Promise<{ generated: number }> {
    return firstValueFrom(this.http.post<{ generated: number }>(`${this.base}/filings/generate`, {}));
  }

  file(id: string, body: { filedOn: string; reference?: string | null; amount?: number | null; notes?: string | null }): Promise<FilingItem> {
    return firstValueFrom(this.http.post<FilingItem>(`${this.base}/filings/${id}/file`, body));
  }

  waive(id: string, reason: string): Promise<FilingItem> {
    return firstValueFrom(this.http.post<FilingItem>(`${this.base}/filings/${id}/waive`, { reason }));
  }

  reopen(id: string): Promise<FilingItem> {
    return firstValueFrom(this.http.post<FilingItem>(`${this.base}/filings/${id}/reopen`, {}));
  }

  reschedule(id: string, dueDate: string): Promise<FilingItem> {
    return firstValueFrom(this.http.post<FilingItem>(`${this.base}/filings/${id}/reschedule`, { dueDate }));
  }

  registrations(): Promise<readonly RegistrationItem[]> {
    return firstValueFrom(this.http.get<readonly RegistrationItem[]>(`${this.base}/registrations`));
  }

  saveRegistration(id: string | null, body: RegistrationInput): Promise<RegistrationItem> {
    return firstValueFrom(id ? this.http.put<RegistrationItem>(`${this.base}/registrations/${id}`, body) : this.http.post<RegistrationItem>(`${this.base}/registrations`, body));
  }

  async deleteRegistration(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/registrations/${id}`));
  }
}

/** Compensation (`/api/v1/compensation`) and one's own total rewards. */
@Injectable({ providedIn: 'root' })
export class CompensationApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/compensation';

  bands(): Promise<readonly BandItem[]> {
    return firstValueFrom(this.http.get<readonly BandItem[]>(`${this.base}/bands`));
  }

  saveBand(id: string | null, body: { gradeId: string; currency?: string | null; minimum: number; midpoint: number; maximum: number; effectiveFrom: string; notes?: string | null }): Promise<BandItem> {
    return firstValueFrom(id ? this.http.put<BandItem>(`${this.base}/bands/${id}`, body) : this.http.post<BandItem>(`${this.base}/bands`, body));
  }

  async deleteBand(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/bands/${id}`));
  }

  cycles(): Promise<readonly CycleItem[]> {
    return firstValueFrom(this.http.get<readonly CycleItem[]>(`${this.base}/cycles`));
  }

  saveCycle(id: string | null, body: { name: string; effectiveDate: string; budgetPercent: number; notes?: string | null }): Promise<CycleItem> {
    return firstValueFrom(id ? this.http.put<CycleItem>(`${this.base}/cycles/${id}`, body) : this.http.post<CycleItem>(`${this.base}/cycles`, body));
  }

  setCycle(id: string, action: 'open' | 'close'): Promise<CycleItem> {
    return firstValueFrom(this.http.post<CycleItem>(`${this.base}/cycles/${id}/${action}`, {}));
  }

  revisions(query: { cycleId?: string; status?: RevisionStatus | ''; employeeId?: string } = {}): Promise<readonly RevisionItem[]> {
    return firstValueFrom(this.http.get<readonly RevisionItem[]>(`${this.base}/revisions`, { params: params({ ...query }) }));
  }

  propose(employeeId: string, details: RevisionDetailsInput, submit: boolean): Promise<RevisionItem> {
    return firstValueFrom(this.http.post<RevisionItem>(`${this.base}/revisions`, { employeeId, details, submit }));
  }

  updateRevision(id: string, details: RevisionDetailsInput): Promise<RevisionItem> {
    return firstValueFrom(this.http.put<RevisionItem>(`${this.base}/revisions/${id}`, details));
  }

  revisionAction(id: string, action: 'submit' | 'cancel'): Promise<RevisionItem> {
    return firstValueFrom(this.http.post<RevisionItem>(`${this.base}/revisions/${id}/${action}`, {}));
  }

  reschedule(id: string, effectiveDate: string): Promise<RevisionItem> {
    return firstValueFrom(this.http.post<RevisionItem>(`${this.base}/revisions/${id}/reschedule`, { effectiveDate }));
  }

  awards(status?: AwardStatus | '', employeeId?: string): Promise<readonly AwardItem[]> {
    return firstValueFrom(this.http.get<readonly AwardItem[]>(`${this.base}/variable-pay`, { params: params({ status, employeeId }) }));
  }

  proposeAward(body: { employeeId: string; type: AwardType; amount: number; currency?: string | null; payoutMonth: string; reason: string; projectId?: string | null }): Promise<AwardItem> {
    return firstValueFrom(this.http.post<AwardItem>(`${this.base}/variable-pay`, body));
  }

  cancelAward(id: string): Promise<AwardItem> {
    return firstValueFrom(this.http.post<AwardItem>(`${this.base}/variable-pay/${id}/cancel`, {}));
  }

  grants(employeeId?: string): Promise<readonly GrantItem[]> {
    return firstValueFrom(this.http.get<readonly GrantItem[]>(`${this.base}/equity`, { params: params({ employeeId }) }));
  }

  saveGrant(id: string | null, employeeId: string, details: GrantDetailsInput): Promise<GrantItem> {
    const body = { employeeId, details };
    return firstValueFrom(id ? this.http.put<GrantItem>(`${this.base}/equity/${id}`, body) : this.http.post<GrantItem>(`${this.base}/equity`, body));
  }

  exercise(id: string, units: number): Promise<GrantItem> {
    return firstValueFrom(this.http.post<GrantItem>(`${this.base}/equity/${id}/exercise`, { units }));
  }

  cancelGrant(id: string): Promise<GrantItem> {
    return firstValueFrom(this.http.post<GrantItem>(`${this.base}/equity/${id}/cancel`, {}));
  }

  rewards(employeeId: string): Promise<TotalRewards> {
    return firstValueFrom(this.http.get<TotalRewards>(`${this.base}/employees/${employeeId}`));
  }

  myRewards(): Promise<TotalRewards> {
    return firstValueFrom(this.http.get<TotalRewards>('/api/v1/me/compensation'));
  }
}

/** Benefits (`/api/v1/benefits`) and one's own enrollments (`/api/v1/me/benefits`). */
@Injectable({ providedIn: 'root' })
export class BenefitsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/benefits';

  plans(includeInactive = false): Promise<readonly PlanItem[]> {
    return firstValueFrom(this.http.get<readonly PlanItem[]>(`${this.base}/plans`, { params: { includeInactive } }));
  }

  savePlan(id: string | null, code: string | null, details: PlanDetails): Promise<PlanItem> {
    const body = { code, details };
    return firstValueFrom(id ? this.http.put<PlanItem>(`${this.base}/plans/${id}`, body) : this.http.post<PlanItem>(`${this.base}/plans`, body));
  }

  async setPlanActive(id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/plans/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }

  enrollments(planId?: string, status?: EnrollmentStatus | ''): Promise<readonly EnrollmentItem[]> {
    return firstValueFrom(this.http.get<readonly EnrollmentItem[]>(`${this.base}/enrollments`, { params: params({ planId, status }) }));
  }

  enroll(body: { employeeId: string; planId: string; dependentIds?: readonly string[]; effectiveFrom?: string | null; notes?: string | null }): Promise<EnrollmentItem> {
    return firstValueFrom(this.http.post<EnrollmentItem>(`${this.base}/enrollments`, body));
  }

  end(id: string, lastDay: string | null): Promise<EnrollmentItem> {
    return firstValueFrom(this.http.post<EnrollmentItem>(`${this.base}/enrollments/${id}/end`, { lastDay }));
  }

  forEmployee(employeeId: string): Promise<readonly EnrollmentItem[]> {
    return firstValueFrom(this.http.get<readonly EnrollmentItem[]>(`${this.base}/employees/${employeeId}`));
  }

  mine(): Promise<MyBenefits> {
    return firstValueFrom(this.http.get<MyBenefits>('/api/v1/me/benefits'));
  }

  enrollMe(planId: string, dependentIds: readonly string[]): Promise<EnrollmentItem> {
    return firstValueFrom(this.http.post<EnrollmentItem>('/api/v1/me/benefits', { planId, dependentIds }));
  }

  updateMyDependents(id: string, dependentIds: readonly string[]): Promise<EnrollmentItem> {
    return firstValueFrom(this.http.put<EnrollmentItem>(`/api/v1/me/benefits/${id}/dependents`, { dependentIds }));
  }

  cancelMine(id: string): Promise<EnrollmentItem> {
    return firstValueFrom(this.http.post<EnrollmentItem>(`/api/v1/me/benefits/${id}/cancel`, {}));
  }
}

/** Project financials (`/api/v1/project-financials`). */
@Injectable({ providedIn: 'root' })
export class ProjectFinancialsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/project-financials';

  portfolio(): Promise<PortfolioFinance> {
    return firstValueFrom(this.http.get<PortfolioFinance>(this.base));
  }

  project(id: string): Promise<ProjectFinance> {
    return firstValueFrom(this.http.get<ProjectFinance>(`${this.base}/${id}`));
  }

  saveBudget(id: string, lines: readonly { category: CostCategory; amount: number; notes?: string | null }[]): Promise<ProjectFinance> {
    return firstValueFrom(this.http.put<ProjectFinance>(`${this.base}/${id}/budget`, { lines }));
  }

  saveProfile(id: string, body: { contractValue?: number | null; retainerMonthlyFee?: number | null; milestoneAmounts?: Record<string, number> | null }): Promise<ProjectFinance> {
    return firstValueFrom(this.http.put<ProjectFinance>(`${this.base}/${id}/profile`, body));
  }

  saveRate(id: string, rateId: string | null, body: { projectRoleId?: string | null; employeeId?: string | null; billingRate?: number | null; costRate?: number | null; effectiveFrom: string }): Promise<FinanceRate> {
    return firstValueFrom(rateId ? this.http.put<FinanceRate>(`${this.base}/${id}/rates/${rateId}`, body) : this.http.post<FinanceRate>(`${this.base}/${id}/rates`, body));
  }

  async deleteRate(id: string, rateId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${id}/rates/${rateId}`));
  }

  saveCost(id: string, costId: string | null, body: { category: CostCategory; date: string; amount: number; vendor?: string | null; description: string; reference?: string | null; planned: boolean }): Promise<FinanceCost> {
    return firstValueFrom(costId ? this.http.put<FinanceCost>(`${this.base}/${id}/costs/${costId}`, body) : this.http.post<FinanceCost>(`${this.base}/${id}/costs`, body));
  }

  async deleteCost(id: string, costId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${id}/costs/${costId}`));
  }

  saveInvoice(id: string, invoiceId: string | null, number: string | null, details: { invoiceDate: string; dueDate?: string | null; periodFrom?: string | null; periodTo?: string | null; amount: number; taxAmount: number; milestoneId?: string | null; notes?: string | null }): Promise<FinanceInvoice> {
    const body = { number, details };
    return firstValueFrom(invoiceId ? this.http.put<FinanceInvoice>(`${this.base}/${id}/invoices/${invoiceId}`, body) : this.http.post<FinanceInvoice>(`${this.base}/${id}/invoices`, body));
  }

  invoiceAction(id: string, invoiceId: string, action: 'send' | 'cancel'): Promise<FinanceInvoice> {
    return firstValueFrom(this.http.post<FinanceInvoice>(`${this.base}/${id}/invoices/${invoiceId}/${action}`, {}));
  }

  invoicePaid(id: string, invoiceId: string, paidOn: string): Promise<FinanceInvoice> {
    return firstValueFrom(this.http.post<FinanceInvoice>(`${this.base}/${id}/invoices/${invoiceId}/paid`, { paidOn }));
  }
}
