import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  AlumniItem,
  AssetAction,
  AssetCategoryInput,
  AssetCategoryItem,
  AssetCondition,
  AssetDashboard,
  AssetDetailsInput,
  AssetItem,
  AssetRequestItem,
  AssetRequestStatus,
  AssetStatus,
  AssetView,
  CatalogItem,
  CatalogItemInput,
  ClaimInput,
  ClaimItem,
  ClaimLineInput,
  ClaimStatus,
  ClaimView,
  ClearanceArea,
  ClearanceItem,
  ClearanceStatus,
  EngagementDashboard,
  ExitInterviewInput,
  ExpenseCategoryInput,
  ExpenseCategoryItem,
  ExpenseDashboard,
  HelpdeskCategoryInput,
  HelpdeskCategoryItem,
  HelpdeskDashboard,
  KnowledgeArticleInput,
  KnowledgeArticleItem,
  KnowledgeArticleView,
  MySurveyItem,
  PaymentMode,
  PointLedgerItem,
  RecognitionItem,
  RecognitionStatus,
  RecognitionTypeInput,
  RecognitionTypeItem,
  RedemptionItem,
  RedemptionStatus,
  RewardBudgetItem,
  RewardDashboard,
  RewardRuleInput,
  RewardRuleItem,
  RewardSettings,
  RewardWallet,
  SeparationCaseItem,
  SeparationCaseView,
  SeparationDashboard,
  NoticeRule,
  AttritionAnalytics,
  ExitSurvey,
  MySurvey,
  AbsenceAlert,
  RetrenchmentResult,
  ShowCause,
  ShowCauseOutcome,
  OffboardingTask,
  OffboardingTemplate,
  OffboardingTemplateInput,
  SeparationPolicy,
  SeparationReason,
  SeparationStatus,
  SeparationType,
  SurveyInput,
  SurveyItem,
  SurveyQuestion,
  SurveyResults,
  SurveyStatus,
  SurveyView,
  TicketItem,
  TicketPriority,
  TicketStatus,
  TicketView,
  TravelDetailsInput,
  TravelItem,
  TravelStatus,
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

/** The asset register (`/api/v1/assets`) and one's own assets (`/api/v1/me/assets`). */
@Injectable({ providedIn: 'root' })
export class AssetsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/assets';

  dashboard(): Promise<AssetDashboard> {
    return firstValueFrom(this.http.get<AssetDashboard>(`${this.base}/dashboard`));
  }

  categories(): Promise<readonly AssetCategoryItem[]> {
    return firstValueFrom(this.http.get<readonly AssetCategoryItem[]>(`${this.base}/categories`));
  }

  saveCategory(id: string | null, body: AssetCategoryInput): Promise<readonly AssetCategoryItem[]> {
    return firstValueFrom(id ? this.http.put<readonly AssetCategoryItem[]>(`${this.base}/categories/${id}`, body) : this.http.post<readonly AssetCategoryItem[]>(`${this.base}/categories`, body));
  }

  assets(query: { status?: AssetStatus | null; categoryId?: string | null; employeeId?: string | null; search?: string | null } = {}): Promise<readonly AssetItem[]> {
    return firstValueFrom(this.http.get<readonly AssetItem[]>(this.base, { params: params(query) }));
  }

  asset(id: string): Promise<AssetView> {
    return firstValueFrom(this.http.get<AssetView>(`${this.base}/${id}`));
  }

  register(details: AssetDetailsInput, condition: AssetCondition): Promise<AssetView> {
    return firstValueFrom(this.http.post<AssetView>(this.base, { details, condition }));
  }

  update(id: string, details: AssetDetailsInput): Promise<AssetView> {
    return firstValueFrom(this.http.put<AssetView>(`${this.base}/${id}`, details));
  }

  assign(id: string, employeeId: string, requestId: string | null = null): Promise<AssetView> {
    return firstValueFrom(this.http.post<AssetView>(`${this.base}/${id}/assign`, { employeeId, requestId }));
  }

  return(id: string, condition: AssetCondition, note: string | null): Promise<AssetView> {
    return firstValueFrom(this.http.post<AssetView>(`${this.base}/${id}/return`, { condition, note }));
  }

  act(id: string, action: AssetAction, body: { note?: string | null; cost?: number | null; condition?: AssetCondition | null }): Promise<AssetView> {
    return firstValueFrom(this.http.post<AssetView>(`${this.base}/${id}/${action}`, body));
  }

  requests(status: AssetRequestStatus | null = null): Promise<readonly AssetRequestItem[]> {
    return firstValueFrom(this.http.get<readonly AssetRequestItem[]>(`${this.base}/requests`, { params: params({ status }) }));
  }

  reject(id: string, response: string): Promise<readonly AssetRequestItem[]> {
    return firstValueFrom(this.http.post<readonly AssetRequestItem[]>(`${this.base}/requests/${id}/reject`, { response }));
  }

  mine(): Promise<readonly AssetItem[]> {
    return firstValueFrom(this.http.get<readonly AssetItem[]>('/api/v1/me/assets'));
  }

  myRequests(): Promise<readonly AssetRequestItem[]> {
    return firstValueFrom(this.http.get<readonly AssetRequestItem[]>('/api/v1/me/assets/requests'));
  }

  request(categoryId: string, reason: string): Promise<readonly AssetRequestItem[]> {
    return firstValueFrom(this.http.post<readonly AssetRequestItem[]>('/api/v1/me/assets/requests', { categoryId, reason }));
  }

  cancelRequest(id: string): Promise<readonly AssetRequestItem[]> {
    return firstValueFrom(this.http.post<readonly AssetRequestItem[]>(`/api/v1/me/assets/requests/${id}/cancel`, {}));
  }
}

/** Expense claims and travel (`/api/v1/expenses`), and one's own (`/api/v1/me/expenses`, `/api/v1/me/travel`). */
@Injectable({ providedIn: 'root' })
export class ExpensesApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/expenses';

  dashboard(): Promise<ExpenseDashboard> {
    return firstValueFrom(this.http.get<ExpenseDashboard>(`${this.base}/dashboard`));
  }

  categories(): Promise<readonly ExpenseCategoryItem[]> {
    return firstValueFrom(this.http.get<readonly ExpenseCategoryItem[]>(`${this.base}/categories`));
  }

  saveCategory(id: string | null, body: ExpenseCategoryInput): Promise<readonly ExpenseCategoryItem[]> {
    return firstValueFrom(id ? this.http.put<readonly ExpenseCategoryItem[]>(`${this.base}/categories/${id}`, body) : this.http.post<readonly ExpenseCategoryItem[]>(`${this.base}/categories`, body));
  }

  claims(query: { status?: ClaimStatus | null; employeeId?: string | null } = {}): Promise<readonly ClaimItem[]> {
    return firstValueFrom(this.http.get<readonly ClaimItem[]>(`${this.base}/claims`, { params: params(query) }));
  }

  claim(id: string): Promise<ClaimView> {
    return firstValueFrom(this.http.get<ClaimView>(`${this.base}/claims/${id}`));
  }

  pay(id: string, mode: PaymentMode, reference: string | null): Promise<ClaimView> {
    return firstValueFrom(this.http.post<ClaimView>(`${this.base}/claims/${id}/pay`, { mode, reference }));
  }

  trips(status: TravelStatus | null = null): Promise<readonly TravelItem[]> {
    return firstValueFrom(this.http.get<readonly TravelItem[]>(`${this.base}/travel`, { params: params({ status }) }));
  }

  trip(id: string): Promise<TravelItem> {
    return firstValueFrom(this.http.get<TravelItem>(`${this.base}/travel/${id}`));
  }

  payAdvance(id: string, amount: number): Promise<TravelItem> {
    return firstValueFrom(this.http.post<TravelItem>(`${this.base}/travel/${id}/advance`, { amount }));
  }

  settle(id: string): Promise<TravelItem> {
    return firstValueFrom(this.http.post<TravelItem>(`${this.base}/travel/${id}/settle`, {}));
  }

  myClaims(): Promise<readonly ClaimItem[]> {
    return firstValueFrom(this.http.get<readonly ClaimItem[]>('/api/v1/me/expenses'));
  }

  draft(body: ClaimInput): Promise<ClaimView> {
    return firstValueFrom(this.http.post<ClaimView>('/api/v1/me/expenses', body));
  }

  updateClaim(id: string, body: ClaimInput): Promise<ClaimView> {
    return firstValueFrom(this.http.put<ClaimView>(`/api/v1/me/expenses/${id}`, body));
  }

  saveLines(id: string, lines: readonly ClaimLineInput[]): Promise<ClaimView> {
    return firstValueFrom(this.http.put<ClaimView>(`/api/v1/me/expenses/${id}/lines`, lines));
  }

  submit(id: string): Promise<ClaimView> {
    return firstValueFrom(this.http.post<ClaimView>(`/api/v1/me/expenses/${id}/submit`, {}));
  }

  cancelClaim(id: string): Promise<ClaimView> {
    return firstValueFrom(this.http.post<ClaimView>(`/api/v1/me/expenses/${id}/cancel`, {}));
  }

  myTrips(): Promise<readonly TravelItem[]> {
    return firstValueFrom(this.http.get<readonly TravelItem[]>('/api/v1/me/travel'));
  }

  requestTrip(details: TravelDetailsInput, submit: boolean): Promise<TravelItem> {
    return firstValueFrom(this.http.post<TravelItem>('/api/v1/me/travel', { details, submit }));
  }

  updateTrip(id: string, details: TravelDetailsInput): Promise<TravelItem> {
    return firstValueFrom(this.http.put<TravelItem>(`/api/v1/me/travel/${id}`, details));
  }

  tripAction(id: string, action: 'submit' | 'cancel'): Promise<TravelItem> {
    return firstValueFrom(this.http.post<TravelItem>(`/api/v1/me/travel/${id}/${action}`, {}));
  }
}

/** The helpdesk (`/api/v1/helpdesk`) and one's own tickets (`/api/v1/me/tickets`). */
@Injectable({ providedIn: 'root' })
export class HelpdeskApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/helpdesk';

  dashboard(): Promise<HelpdeskDashboard> {
    return firstValueFrom(this.http.get<HelpdeskDashboard>(`${this.base}/dashboard`));
  }

  categories(): Promise<readonly HelpdeskCategoryItem[]> {
    return firstValueFrom(this.http.get<readonly HelpdeskCategoryItem[]>(`${this.base}/categories`));
  }

  saveCategory(id: string | null, body: HelpdeskCategoryInput): Promise<readonly HelpdeskCategoryItem[]> {
    return firstValueFrom(id ? this.http.put<readonly HelpdeskCategoryItem[]>(`${this.base}/categories/${id}`, body) : this.http.post<readonly HelpdeskCategoryItem[]>(`${this.base}/categories`, body));
  }

  tickets(query: { status?: TicketStatus | null; categoryId?: string | null; requesterId?: string | null; mine?: boolean | null; breached?: boolean | null; openOnly?: boolean | null } = {}): Promise<readonly TicketItem[]> {
    return firstValueFrom(this.http.get<readonly TicketItem[]>(`${this.base}/tickets`, { params: params(query) }));
  }

  ticket(id: string): Promise<TicketView> {
    return firstValueFrom(this.http.get<TicketView>(`${this.base}/tickets/${id}`));
  }

  comment(id: string, body: string, isInternal: boolean): Promise<TicketView> {
    return firstValueFrom(this.http.post<TicketView>(`${this.base}/tickets/${id}/comments`, { body, isInternal }));
  }

  assign(id: string, assigneeEmployeeId: string | null): Promise<TicketView> {
    return firstValueFrom(this.http.post<TicketView>(`${this.base}/tickets/${id}/assign`, { assigneeEmployeeId }));
  }

  status(id: string, status: TicketStatus, resolution: string | null): Promise<TicketView> {
    return firstValueFrom(this.http.post<TicketView>(`${this.base}/tickets/${id}/status`, { status, resolution }));
  }

  recategorise(id: string, categoryId: string, priority: TicketPriority): Promise<TicketView> {
    return firstValueFrom(this.http.post<TicketView>(`${this.base}/tickets/${id}/recategorise`, { categoryId, priority }));
  }

  articles(search: string | null = null, categoryId: string | null = null): Promise<readonly KnowledgeArticleItem[]> {
    return firstValueFrom(this.http.get<readonly KnowledgeArticleItem[]>(`${this.base}/knowledge`, { params: params({ search, categoryId }) }));
  }

  article(id: string): Promise<KnowledgeArticleView> {
    return firstValueFrom(this.http.get<KnowledgeArticleView>(`${this.base}/knowledge/${id}`));
  }

  saveArticle(id: string | null, body: KnowledgeArticleInput): Promise<KnowledgeArticleView> {
    return firstValueFrom(id ? this.http.put<KnowledgeArticleView>(`${this.base}/knowledge/${id}`, body) : this.http.post<KnowledgeArticleView>(`${this.base}/knowledge`, body));
  }

  vote(id: string, helpful: boolean): Promise<KnowledgeArticleView> {
    return firstValueFrom(this.http.post<KnowledgeArticleView>(`${this.base}/knowledge/${id}/vote`, { helpful }));
  }

  mine(): Promise<readonly TicketItem[]> {
    return firstValueFrom(this.http.get<readonly TicketItem[]>('/api/v1/me/tickets'));
  }

  raise(body: { categoryId: string; subject: string; description: string; priority: TicketPriority }): Promise<TicketView> {
    return firstValueFrom(this.http.post<TicketView>('/api/v1/me/tickets', body));
  }

  reopen(id: string, reason: string): Promise<TicketView> {
    return firstValueFrom(this.http.post<TicketView>(`/api/v1/me/tickets/${id}/reopen`, { reason }));
  }

  close(id: string): Promise<TicketView> {
    return firstValueFrom(this.http.post<TicketView>(`/api/v1/me/tickets/${id}/close`, {}));
  }

  cancel(id: string): Promise<TicketView> {
    return firstValueFrom(this.http.post<TicketView>(`/api/v1/me/tickets/${id}/cancel`, {}));
  }

  rate(id: string, rating: number, comment: string | null): Promise<TicketView> {
    return firstValueFrom(this.http.post<TicketView>(`/api/v1/me/tickets/${id}/rate`, { rating, comment }));
  }
}

/** Surveys and eNPS (`/api/v1/engagement`) and one's own surveys (`/api/v1/me/surveys`). */
@Injectable({ providedIn: 'root' })
export class EngagementApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/engagement';

  dashboard(): Promise<EngagementDashboard> {
    return firstValueFrom(this.http.get<EngagementDashboard>(`${this.base}/dashboard`));
  }

  pulseTemplate(): Promise<readonly SurveyQuestion[]> {
    return firstValueFrom(this.http.get<readonly SurveyQuestion[]>(`${this.base}/templates/pulse`));
  }

  surveys(status: SurveyStatus | null = null): Promise<readonly SurveyItem[]> {
    return firstValueFrom(this.http.get<readonly SurveyItem[]>(`${this.base}/surveys`, { params: params({ status }) }));
  }

  survey(id: string): Promise<SurveyView> {
    return firstValueFrom(this.http.get<SurveyView>(`${this.base}/surveys/${id}`));
  }

  save(id: string | null, body: SurveyInput): Promise<SurveyView> {
    return firstValueFrom(id ? this.http.put<SurveyView>(`${this.base}/surveys/${id}`, body) : this.http.post<SurveyView>(`${this.base}/surveys`, body));
  }

  act(id: string, action: 'publish' | 'close'): Promise<SurveyView> {
    return firstValueFrom(this.http.post<SurveyView>(`${this.base}/surveys/${id}/${action}`, {}));
  }

  results(id: string): Promise<SurveyResults> {
    return firstValueFrom(this.http.get<SurveyResults>(`${this.base}/surveys/${id}/results`));
  }

  mine(): Promise<readonly MySurveyItem[]> {
    return firstValueFrom(this.http.get<readonly MySurveyItem[]>('/api/v1/me/surveys'));
  }

  respond(id: string, answers: Readonly<Record<string, string>>): Promise<void> {
    return firstValueFrom(this.http.post<void>(`/api/v1/me/surveys/${id}/respond`, { answers }));
  }
}

/** Rewards and recognition (`/api/v1/rewards`) and one's own wallet (`/api/v1/me/rewards`). */
@Injectable({ providedIn: 'root' })
export class RewardsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/rewards';

  dashboard(): Promise<RewardDashboard> {
    return firstValueFrom(this.http.get<RewardDashboard>(`${this.base}/dashboard`));
  }

  settings(): Promise<RewardSettings> {
    return firstValueFrom(this.http.get<RewardSettings>(`${this.base}/settings`));
  }

  saveSettings(body: RewardSettings): Promise<RewardSettings> {
    return firstValueFrom(this.http.put<RewardSettings>(`${this.base}/settings`, body));
  }

  types(): Promise<readonly RecognitionTypeItem[]> {
    return firstValueFrom(this.http.get<readonly RecognitionTypeItem[]>(`${this.base}/types`));
  }

  saveType(id: string | null, body: RecognitionTypeInput): Promise<readonly RecognitionTypeItem[]> {
    return firstValueFrom(id ? this.http.put<readonly RecognitionTypeItem[]>(`${this.base}/types/${id}`, body) : this.http.post<readonly RecognitionTypeItem[]>(`${this.base}/types`, body));
  }

  rules(): Promise<readonly RewardRuleItem[]> {
    return firstValueFrom(this.http.get<readonly RewardRuleItem[]>(`${this.base}/rules`));
  }

  saveRule(id: string | null, body: RewardRuleInput): Promise<readonly RewardRuleItem[]> {
    return firstValueFrom(id ? this.http.put<readonly RewardRuleItem[]>(`${this.base}/rules/${id}`, body) : this.http.post<readonly RewardRuleItem[]>(`${this.base}/rules`, body));
  }

  budgets(year: number | null = null): Promise<readonly RewardBudgetItem[]> {
    return firstValueFrom(this.http.get<readonly RewardBudgetItem[]>(`${this.base}/budgets`, { params: params({ year }) }));
  }

  saveBudget(body: { year: number; departmentId: string | null; points: number }): Promise<readonly RewardBudgetItem[]> {
    return firstValueFrom(this.http.put<readonly RewardBudgetItem[]>(`${this.base}/budgets`, body));
  }

  adjust(employeeId: string, points: number, note: string): Promise<readonly PointLedgerItem[]> {
    return firstValueFrom(this.http.post<readonly PointLedgerItem[]>(`${this.base}/points/adjust`, { employeeId, points, note }));
  }

  feed(): Promise<readonly RecognitionItem[]> {
    return firstValueFrom(this.http.get<readonly RecognitionItem[]>(`${this.base}/feed`));
  }

  recognitions(status: RecognitionStatus | null = null, employeeId: string | null = null): Promise<readonly RecognitionItem[]> {
    return firstValueFrom(this.http.get<readonly RecognitionItem[]>(`${this.base}/recognitions`, { params: params({ status, employeeId }) }));
  }

  received(employeeId: string): Promise<readonly RecognitionItem[]> {
    return firstValueFrom(this.http.get<readonly RecognitionItem[]>(`${this.base}/employees/${employeeId}/recognitions`));
  }

  give(body: { recipientEmployeeId: string; typeId: string; message: string; isPublic: boolean }): Promise<RecognitionItem> {
    return firstValueFrom(this.http.post<RecognitionItem>(`${this.base}/recognitions`, body));
  }

  cheer(id: string): Promise<RecognitionItem> {
    return firstValueFrom(this.http.post<RecognitionItem>(`${this.base}/recognitions/${id}/cheer`, {}));
  }

  catalog(all = false): Promise<readonly CatalogItem[]> {
    return firstValueFrom(this.http.get<readonly CatalogItem[]>(`${this.base}/catalog`, { params: params({ all: all || null }) }));
  }

  saveItem(id: string | null, body: CatalogItemInput): Promise<readonly CatalogItem[]> {
    return firstValueFrom(id ? this.http.put<readonly CatalogItem[]>(`${this.base}/catalog/${id}`, body) : this.http.post<readonly CatalogItem[]>(`${this.base}/catalog`, body));
  }

  redemptions(status: RedemptionStatus | null = null): Promise<readonly RedemptionItem[]> {
    return firstValueFrom(this.http.get<readonly RedemptionItem[]>(`${this.base}/redemptions`, { params: params({ status }) }));
  }

  redemptionAction(id: string, action: 'fulfil' | 'cancel', note: string | null): Promise<RedemptionItem> {
    return firstValueFrom(this.http.post<RedemptionItem>(`${this.base}/redemptions/${id}/${action}`, { note }));
  }

  wallet(): Promise<RewardWallet> {
    return firstValueFrom(this.http.get<RewardWallet>('/api/v1/me/rewards'));
  }

  ledger(): Promise<readonly PointLedgerItem[]> {
    return firstValueFrom(this.http.get<readonly PointLedgerItem[]>('/api/v1/me/rewards/ledger'));
  }

  myRedemptions(): Promise<readonly RedemptionItem[]> {
    return firstValueFrom(this.http.get<readonly RedemptionItem[]>('/api/v1/me/rewards/redemptions'));
  }

  redeem(itemId: string): Promise<RedemptionItem> {
    return firstValueFrom(this.http.post<RedemptionItem>('/api/v1/me/rewards/redeem', { itemId }));
  }

  cancelMine(id: string): Promise<RedemptionItem> {
    return firstValueFrom(this.http.post<RedemptionItem>(`/api/v1/me/rewards/redemptions/${id}/cancel`, {}));
  }
}

/** Separation, clearance, settlement and alumni (`/api/v1/separation`) and one's own (`/api/v1/me/separation`). */
@Injectable({ providedIn: 'root' })
export class SeparationApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/separation';

  dashboard(): Promise<SeparationDashboard> {
    return firstValueFrom(this.http.get<SeparationDashboard>(`${this.base}/dashboard`));
  }

  cases(status: SeparationStatus | null = null): Promise<readonly SeparationCaseItem[]> {
    return firstValueFrom(this.http.get<readonly SeparationCaseItem[]>(`${this.base}/cases`, { params: params({ status }) }));
  }

  case(id: string): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.get<SeparationCaseView>(`${this.base}/cases/${id}`));
  }

  initiate(body: {
    employeeId: string;
    type: SeparationType;
    reason: string;
    noticeDate: string;
    lastWorkingDay: string;
    noticePeriodDays: number | null;
    payInLieuDays?: number | null;
    nomineeName?: string | null;
    nomineeRelation?: string | null;
    nomineeContact?: string | null;
  }): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/cases`, body));
  }

  terms(id: string, body: { lastWorkingDay: string; shortfallWaived: boolean; eligibleForRehire: boolean; gardenLeave?: boolean }): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.put<SeparationCaseView>(`${this.base}/cases/${id}/terms`, body));
  }

  withdraw(id: string): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/cases/${id}/withdraw`, {}));
  }

  addClearance(id: string, body: { area: ClearanceArea; title: string; ownerEmployeeId: string | null; recoveryAmount: number }): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/cases/${id}/clearance`, body));
  }

  interview(id: string, body: ExitInterviewInput): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.put<SeparationCaseView>(`${this.base}/cases/${id}/interview`, body));
  }

  letters(id: string): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/cases/${id}/letters`, {}));
  }

  calculate(id: string): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/cases/${id}/settlement/calculate`, {}));
  }

  addLine(id: string, body: { code: string; label: string; isDeduction: boolean; amount: number }): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/cases/${id}/settlement/lines`, body));
  }

  removeLine(id: string, lineId: string): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.delete<SeparationCaseView>(`${this.base}/cases/${id}/settlement/lines/${lineId}`));
  }

  settlement(id: string, action: 'approve' | 'reopen' | 'pay', reference: string | null = null): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/cases/${id}/settlement/${action}`, { reference }));
  }

  myClearance(): Promise<readonly ClearanceItem[]> {
    return firstValueFrom(this.http.get<readonly ClearanceItem[]>(`${this.base}/clearance`));
  }

  resolve(itemId: string, status: ClearanceStatus, recoveryAmount: number | null, note: string | null): Promise<ClearanceItem> {
    return firstValueFrom(this.http.post<ClearanceItem>(`${this.base}/clearance/${itemId}`, { status, recoveryAmount, note }));
  }

  alumni(search: string | null = null): Promise<readonly AlumniItem[]> {
    return firstValueFrom(this.http.get<readonly AlumniItem[]>(`${this.base}/alumni`, { params: params({ search }) }));
  }

  updateAlumni(id: string, body: { email: string | null; eligibleForRehire: boolean; optedIn: boolean; notes: string | null }): Promise<AlumniItem> {
    return firstValueFrom(this.http.put<AlumniItem>(`${this.base}/alumni/${id}`, body));
  }

  mine(): Promise<SeparationCaseView | null> {
    return firstValueFrom(this.http.get<SeparationCaseView | null>('/api/v1/me/separation'));
  }

  resign(reason: string, requestedLastDay: string | null, reasonCode: string | null = null): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>('/api/v1/me/separation', { reason, requestedLastDay, reasonCode }));
  }

  requestEarlyRelease(releaseDay: string, withBuyout: boolean): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>('/api/v1/me/separation/early-release', { releaseDay, withBuyout }));
  }

  decideEarlyRelease(id: string, grant: boolean): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/cases/${id}/early-release/${grant ? 'grant' : 'decline'}`, {}));
  }

  classify(id: string, body: { reasonCode: string | null; secondaryReasonCode: string | null; regretted: boolean | null; retentionNote: string | null }): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.put<SeparationCaseView>(`${this.base}/cases/${id}/classification`, body));
  }

  reasons(): Promise<readonly SeparationReason[]> {
    return firstValueFrom(this.http.get<readonly SeparationReason[]>(`${this.base}/reasons`));
  }

  policy(): Promise<SeparationPolicy> {
    return firstValueFrom(this.http.get<SeparationPolicy>(`${this.base}/policy`));
  }

  updatePolicy(body: SeparationPolicy): Promise<SeparationPolicy> {
    return firstValueFrom(this.http.put<SeparationPolicy>(`${this.base}/policy`, body));
  }

  noticeRules(): Promise<readonly NoticeRule[]> {
    return firstValueFrom(this.http.get<readonly NoticeRule[]>(`${this.base}/policy/notice-rules`));
  }

  addNoticeRule(body: { gradeId: string | null; employmentTypeCode: string | null; onProbation: boolean | null; noticeDays: number }): Promise<NoticeRule> {
    return firstValueFrom(this.http.post<NoticeRule>(`${this.base}/policy/notice-rules`, body));
  }

  removeNoticeRule(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/policy/notice-rules/${id}`));
  }

  offboardingTasks(): Promise<readonly OffboardingTask[]> {
    return firstValueFrom(this.http.get<readonly OffboardingTask[]>(`${this.base}/offboarding/tasks`));
  }

  finishOffboardingTask(taskId: string, skipped: boolean, notes: string | null): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/offboarding/tasks/${taskId}`, { skipped, notes }));
  }

  setHandover(id: string, successorEmployeeId: string | null, note: string | null): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.put<SeparationCaseView>(`${this.base}/cases/${id}/handover`, { successorEmployeeId, note }));
  }

  refreshHandover(id: string): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/cases/${id}/handover/refresh`, {}));
  }

  actOnHandover(itemId: string, body: { toEmployeeId: string | null; skip: boolean; note: string | null }): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/handover/${itemId}`, body));
  }

  disableAccess(id: string): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/cases/${id}/access/disable`, {}));
  }

  offboardingTemplates(): Promise<readonly OffboardingTemplate[]> {
    return firstValueFrom(this.http.get<readonly OffboardingTemplate[]>(`${this.base}/offboarding/templates`));
  }

  saveOffboardingTemplate(id: string | null, body: OffboardingTemplateInput): Promise<OffboardingTemplate> {
    return firstValueFrom(
      id ? this.http.put<OffboardingTemplate>(`${this.base}/offboarding/templates/${id}`, body) : this.http.post<OffboardingTemplate>(`${this.base}/offboarding/templates`, body),
    );
  }

  removeOffboardingTemplate(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.base}/offboarding/templates/${id}`));
  }

  analytics(months: number): Promise<AttritionAnalytics> {
    return firstValueFrom(this.http.get<AttritionAnalytics>(`${this.base}/analytics`, { params: { months } }));
  }

  exitSurvey(): Promise<ExitSurvey> {
    return firstValueFrom(this.http.get<ExitSurvey>(`${this.base}/exit-survey`));
  }

  updateExitSurvey(body: ExitSurvey): Promise<ExitSurvey> {
    return firstValueFrom(this.http.put<ExitSurvey>(`${this.base}/exit-survey`, body));
  }

  mySurvey(): Promise<MySurvey> {
    return firstValueFrom(this.http.get<MySurvey>('/api/v1/me/separation/survey'));
  }

  answerSurvey(answers: Readonly<Record<string, string>>, hiddenFromManager: boolean): Promise<MySurvey> {
    return firstValueFrom(this.http.put<MySurvey>('/api/v1/me/separation/survey', { answers, hiddenFromManager }));
  }

  addSummary(id: string, summary: string): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/cases/${id}/summary`, { summary }));
  }

  confirm(id: string): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>(`${this.base}/cases/${id}/confirm`, {}));
  }

  runExitProposals(): Promise<{ proposed: number }> {
    return firstValueFrom(this.http.post<{ proposed: number }>(`${this.base}/exit-proposals/run`, {}));
  }

  showCauses(open: boolean | null): Promise<readonly ShowCause[]> {
    return firstValueFrom(this.http.get<readonly ShowCause[]>(`${this.base}/show-causes`, { params: open === null ? {} : { open } }));
  }

  issueShowCause(body: { employeeId: string; allegation: string; replyDays: number | null }): Promise<ShowCause> {
    return firstValueFrom(this.http.post<ShowCause>(`${this.base}/show-causes`, body));
  }

  startEnquiry(id: string, officerEmployeeId: string): Promise<ShowCause> {
    return firstValueFrom(this.http.post<ShowCause>(`${this.base}/show-causes/${id}/enquiry`, { officerEmployeeId }));
  }

  recordFindings(id: string, findings: string): Promise<ShowCause> {
    return firstValueFrom(this.http.post<ShowCause>(`${this.base}/show-causes/${id}/findings`, { findings }));
  }

  decideShowCause(id: string, body: { outcome: ShowCauseOutcome; note: string | null; lastWorkingDay: string | null; payInLieuDays: number | null; disableAccessNow: boolean }): Promise<ShowCause> {
    return firstValueFrom(this.http.post<ShowCause>(`${this.base}/show-causes/${id}/decision`, body));
  }

  myShowCauses(): Promise<readonly ShowCause[]> {
    return firstValueFrom(this.http.get<readonly ShowCause[]>('/api/v1/me/separation/show-causes'));
  }

  replyToShowCause(id: string, reply: string): Promise<ShowCause> {
    return firstValueFrom(this.http.post<ShowCause>(`/api/v1/me/separation/show-causes/${id}/reply`, { reply }));
  }

  retrench(body: { employeeIds: readonly string[]; reason: string; noticeDate: string; lastWorkingDay: string; payInLieuOfNotice: boolean }): Promise<readonly RetrenchmentResult[]> {
    return firstValueFrom(this.http.post<readonly RetrenchmentResult[]>(`${this.base}/retrenchment`, body));
  }

  absences(open: boolean | null): Promise<readonly AbsenceAlert[]> {
    return firstValueFrom(this.http.get<readonly AbsenceAlert[]>(`${this.base}/absences`, { params: open === null ? {} : { open } }));
  }

  detectAbsences(): Promise<{ raised: number }> {
    return firstValueFrom(this.http.post<{ raised: number }>(`${this.base}/absences/detect`, {}));
  }

  sendAbsenceNotice(id: string): Promise<AbsenceAlert> {
    return firstValueFrom(this.http.post<AbsenceAlert>(`${this.base}/absences/${id}/notice`, {}));
  }

  closeAbsence(id: string, returned: boolean, note: string | null): Promise<AbsenceAlert> {
    return firstValueFrom(this.http.post<AbsenceAlert>(`${this.base}/absences/${id}/close`, { returned, note }));
  }

  separateAbsence(id: string): Promise<AbsenceAlert> {
    return firstValueFrom(this.http.post<AbsenceAlert>(`${this.base}/absences/${id}/separate`, {}));
  }

  withdrawMine(): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.post<SeparationCaseView>('/api/v1/me/separation/withdraw', {}));
  }

  myInterview(body: ExitInterviewInput): Promise<SeparationCaseView> {
    return firstValueFrom(this.http.put<SeparationCaseView>('/api/v1/me/separation/interview', body));
  }
}
