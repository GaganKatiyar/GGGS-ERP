import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  CalibrationView,
  CareerPathItem,
  CareerStepInput,
  CareerTrack,
  CatalogEntry,
  CourseInput,
  CourseItem,
  FeedbackItem,
  FeedbackRelationship,
  GoalDetailsInput,
  GoalItem,
  GoalLevel,
  GoalStatus,
  GoalView,
  KeyResultInput,
  LearningDashboard,
  LearningEnrollment,
  LearningEnrollmentStatus,
  LearningPathItem,
  MyCareer,
  MyLearning,
  NineBoxView,
  PipDetailsInput,
  PipItem,
  PipObjective,
  PipStatus,
  PromotionDetailsInput,
  PromotionItem,
  PromotionStatus,
  ReviewCycleInput,
  ReviewCycleItem,
  ReviewInput,
  ReviewItem,
  ReviewStatus,
  ReviewView,
  SuccessionInput,
  SuccessionItem,
  TalentLevel,
  Confidence,
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

/** Goals (`/api/v1/goals`) and one's own (`/api/v1/me/goals`). */
@Injectable({ providedIn: 'root' })
export class GoalsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/goals';

  goals(query: { ownerId?: string | null; level?: GoalLevel | null; cycleId?: string | null; status?: GoalStatus | null; departmentId?: string | null } = {}): Promise<readonly GoalItem[]> {
    return firstValueFrom(this.http.get<readonly GoalItem[]>(this.base, { params: params(query) }));
  }

  mine(): Promise<readonly GoalItem[]> {
    return firstValueFrom(this.http.get<readonly GoalItem[]>('/api/v1/me/goals'));
  }

  goal(id: string): Promise<GoalView> {
    return firstValueFrom(this.http.get<GoalView>(`${this.base}/${id}`));
  }

  create(details: GoalDetailsInput, keyResults: readonly KeyResultInput[], activate: boolean): Promise<GoalView> {
    return firstValueFrom(this.http.post<GoalView>(this.base, { details, keyResults, activate }));
  }

  update(id: string, details: GoalDetailsInput): Promise<GoalView> {
    return firstValueFrom(this.http.put<GoalView>(`${this.base}/${id}`, details));
  }

  saveKeyResults(id: string, keyResults: readonly KeyResultInput[]): Promise<GoalView> {
    return firstValueFrom(this.http.put<GoalView>(`${this.base}/${id}/key-results`, keyResults));
  }

  checkIn(id: string, body: { progress?: number | null; confidence: Confidence; note?: string | null; values?: readonly { keyResultId: string; value: number }[] }): Promise<GoalView> {
    return firstValueFrom(this.http.post<GoalView>(`${this.base}/${id}/check-ins`, body));
  }

  act(id: string, action: 'activate' | 'complete' | 'cancel' | 'reopen'): Promise<GoalView> {
    return firstValueFrom(this.http.post<GoalView>(`${this.base}/${id}/${action}`, {}));
  }
}

/** Review cycles, reviews, calibration and improvement plans (`/api/v1/performance`), and one's own (`/api/v1/me`). */
@Injectable({ providedIn: 'root' })
export class PerformanceApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/performance';

  cycles(): Promise<readonly ReviewCycleItem[]> {
    return firstValueFrom(this.http.get<readonly ReviewCycleItem[]>(`${this.base}/cycles`));
  }

  cycle(id: string): Promise<ReviewCycleItem> {
    return firstValueFrom(this.http.get<ReviewCycleItem>(`${this.base}/cycles/${id}`));
  }

  saveCycle(id: string | null, body: ReviewCycleInput): Promise<ReviewCycleItem> {
    return firstValueFrom(id ? this.http.put<ReviewCycleItem>(`${this.base}/cycles/${id}`, body) : this.http.post<ReviewCycleItem>(`${this.base}/cycles`, body));
  }

  cycleAction(id: string, action: 'launch' | 'calibrate' | 'release' | 'close'): Promise<ReviewCycleItem> {
    return firstValueFrom(this.http.post<ReviewCycleItem>(`${this.base}/cycles/${id}/${action}`, {}));
  }

  calibration(id: string, departmentId?: string | null): Promise<CalibrationView> {
    return firstValueFrom(this.http.get<CalibrationView>(`${this.base}/cycles/${id}/calibration`, { params: params({ departmentId }) }));
  }

  reviews(cycleId: string, query: { status?: ReviewStatus | null; departmentId?: string | null } = {}): Promise<readonly ReviewItem[]> {
    return firstValueFrom(this.http.get<readonly ReviewItem[]>(`${this.base}/cycles/${cycleId}/reviews`, { params: params(query) }));
  }

  review(id: string): Promise<ReviewView> {
    return firstValueFrom(this.http.get<ReviewView>(`${this.base}/reviews/${id}`));
  }

  saveSelf(id: string, input: ReviewInput, submit: boolean): Promise<ReviewView> {
    return firstValueFrom(this.http.put<ReviewView>(`${this.base}/reviews/${id}/self`, { input, submit }));
  }

  saveManager(id: string, input: ReviewInput, submit: boolean): Promise<ReviewView> {
    return firstValueFrom(this.http.put<ReviewView>(`${this.base}/reviews/${id}/manager`, { input, submit }));
  }

  returnReview(id: string): Promise<ReviewView> {
    return firstValueFrom(this.http.post<ReviewView>(`${this.base}/reviews/${id}/return`, {}));
  }

  calibrate(id: string, rating: number, note: string | null): Promise<ReviewView> {
    return firstValueFrom(this.http.post<ReviewView>(`${this.base}/reviews/${id}/calibrate`, { rating, note }));
  }

  acknowledge(id: string, comment: string | null): Promise<ReviewView> {
    return firstValueFrom(this.http.post<ReviewView>(`${this.base}/reviews/${id}/acknowledge`, { comment }));
  }

  askFeedback(id: string, reviewerIds: readonly string[], relationship: FeedbackRelationship): Promise<ReviewView> {
    return firstValueFrom(this.http.post<ReviewView>(`${this.base}/reviews/${id}/feedback`, { reviewerIds, relationship }));
  }

  pips(status?: PipStatus | null): Promise<readonly PipItem[]> {
    return firstValueFrom(this.http.get<readonly PipItem[]>(`${this.base}/pips`, { params: params({ status }) }));
  }

  pip(id: string): Promise<PipItem> {
    return firstValueFrom(this.http.get<PipItem>(`${this.base}/pips/${id}`));
  }

  createPip(employeeId: string, details: PipDetailsInput): Promise<PipItem> {
    return firstValueFrom(this.http.post<PipItem>(`${this.base}/pips`, { employeeId, details }));
  }

  updatePip(id: string, details: PipDetailsInput): Promise<PipItem> {
    return firstValueFrom(this.http.put<PipItem>(`${this.base}/pips/${id}`, details));
  }

  pipAction(id: string, action: 'start' | 'cancel'): Promise<PipItem> {
    return firstValueFrom(this.http.post<PipItem>(`${this.base}/pips/${id}/${action}`, {}));
  }

  pipNote(id: string, text: string): Promise<PipItem> {
    return firstValueFrom(this.http.post<PipItem>(`${this.base}/pips/${id}/notes`, { text }));
  }

  extendPip(id: string, endDate: string): Promise<PipItem> {
    return firstValueFrom(this.http.post<PipItem>(`${this.base}/pips/${id}/extend`, { endDate }));
  }

  closePip(id: string, succeeded: boolean, outcome: string, objectives: readonly PipObjective[]): Promise<PipItem> {
    return firstValueFrom(this.http.post<PipItem>(`${this.base}/pips/${id}/close`, { succeeded, outcome, objectives }));
  }

  // Self-service
  myReviews(): Promise<readonly ReviewItem[]> {
    return firstValueFrom(this.http.get<readonly ReviewItem[]>('/api/v1/me/reviews'));
  }

  teamReviews(): Promise<readonly ReviewItem[]> {
    return firstValueFrom(this.http.get<readonly ReviewItem[]>('/api/v1/me/reviews/team'));
  }

  myPips(): Promise<readonly PipItem[]> {
    return firstValueFrom(this.http.get<readonly PipItem[]>('/api/v1/me/pips'));
  }

  myFeedback(): Promise<readonly FeedbackItem[]> {
    return firstValueFrom(this.http.get<readonly FeedbackItem[]>('/api/v1/me/feedback'));
  }

  giveFeedback(id: string, body: { ratings: Readonly<Record<string, number>>; strengths?: string | null; improvements?: string | null }): Promise<readonly FeedbackItem[]> {
    return firstValueFrom(this.http.put<readonly FeedbackItem[]>(`/api/v1/me/feedback/${id}`, body));
  }

  declineFeedback(id: string): Promise<readonly FeedbackItem[]> {
    return firstValueFrom(this.http.post<readonly FeedbackItem[]>(`/api/v1/me/feedback/${id}/decline`, {}));
  }
}

/** Learning (`/api/v1/learning`) and one's own (`/api/v1/me/learning`). */
@Injectable({ providedIn: 'root' })
export class LearningApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/learning';

  dashboard(): Promise<LearningDashboard> {
    return firstValueFrom(this.http.get<LearningDashboard>(`${this.base}/dashboard`));
  }

  courses(includeInactive = false): Promise<readonly CourseItem[]> {
    return firstValueFrom(this.http.get<readonly CourseItem[]>(`${this.base}/courses`, { params: { includeInactive } }));
  }

  saveCourse(id: string | null, details: CourseInput, isActive: boolean): Promise<readonly CourseItem[]> {
    const body = { details, isActive };
    return firstValueFrom(id ? this.http.put<readonly CourseItem[]>(`${this.base}/courses/${id}`, body) : this.http.post<readonly CourseItem[]>(`${this.base}/courses`, body));
  }

  paths(): Promise<readonly LearningPathItem[]> {
    return firstValueFrom(this.http.get<readonly LearningPathItem[]>(`${this.base}/paths`));
  }

  savePath(id: string | null, body: { code: string; title: string; description?: string | null; courseIds: readonly string[]; isActive: boolean }): Promise<readonly LearningPathItem[]> {
    return firstValueFrom(id ? this.http.put<readonly LearningPathItem[]>(`${this.base}/paths/${id}`, body) : this.http.post<readonly LearningPathItem[]>(`${this.base}/paths`, body));
  }

  enrollments(query: { courseId?: string | null; employeeId?: string | null; status?: LearningEnrollmentStatus | null; overdue?: boolean | null } = {}): Promise<readonly LearningEnrollment[]> {
    return firstValueFrom(this.http.get<readonly LearningEnrollment[]>(`${this.base}/enrollments`, { params: params(query) }));
  }

  assign(body: { employeeIds: readonly string[]; courseId?: string | null; pathId?: string | null; dueDate?: string | null }): Promise<{ assigned: number; skipped: number }> {
    return firstValueFrom(this.http.post<{ assigned: number; skipped: number }>(`${this.base}/enrollments/assign`, body));
  }

  certificates(expiring = false): Promise<readonly LearningEnrollment[]> {
    return firstValueFrom(this.http.get<readonly LearningEnrollment[]>(`${this.base}/certificates`, { params: { expiring } }));
  }

  start(id: string): Promise<LearningEnrollment> {
    return firstValueFrom(this.http.post<LearningEnrollment>(`${this.base}/enrollments/${id}/start`, {}));
  }

  progress(id: string, progress: number): Promise<LearningEnrollment> {
    return firstValueFrom(this.http.post<LearningEnrollment>(`${this.base}/enrollments/${id}/progress`, { progress }));
  }

  complete(id: string, body: { completedOn?: string | null; score?: number | null }): Promise<LearningEnrollment> {
    return firstValueFrom(this.http.post<LearningEnrollment>(`${this.base}/enrollments/${id}/complete`, body));
  }

  rate(id: string, rating: number, feedback: string | null): Promise<LearningEnrollment> {
    return firstValueFrom(this.http.post<LearningEnrollment>(`${this.base}/enrollments/${id}/rate`, { rating, feedback }));
  }

  cancel(id: string): Promise<LearningEnrollment> {
    return firstValueFrom(this.http.post<LearningEnrollment>(`${this.base}/enrollments/${id}/cancel`, {}));
  }

  mine(): Promise<MyLearning> {
    return firstValueFrom(this.http.get<MyLearning>('/api/v1/me/learning'));
  }

  catalog(): Promise<readonly CatalogEntry[]> {
    return firstValueFrom(this.http.get<readonly CatalogEntry[]>('/api/v1/me/learning/catalog'));
  }

  enroll(courseId: string): Promise<LearningEnrollment> {
    return firstValueFrom(this.http.post<LearningEnrollment>(`/api/v1/me/learning/courses/${courseId}/enroll`, {}));
  }
}

/** Career paths, promotions, succession and the talent review (`/api/v1/career`), and one's own career (`/api/v1/me/career`). */
@Injectable({ providedIn: 'root' })
export class CareerApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/career';

  paths(): Promise<readonly CareerPathItem[]> {
    return firstValueFrom(this.http.get<readonly CareerPathItem[]>(`${this.base}/paths`));
  }

  savePath(id: string | null, body: { name: string; description?: string | null; track: CareerTrack; departmentId?: string | null; steps: readonly CareerStepInput[]; isActive: boolean }): Promise<readonly CareerPathItem[]> {
    return firstValueFrom(id ? this.http.put<readonly CareerPathItem[]>(`${this.base}/paths/${id}`, body) : this.http.post<readonly CareerPathItem[]>(`${this.base}/paths`, body));
  }

  promotions(status?: PromotionStatus | null): Promise<readonly PromotionItem[]> {
    return firstValueFrom(this.http.get<readonly PromotionItem[]>(`${this.base}/promotions`, { params: params({ status }) }));
  }

  promotion(id: string): Promise<PromotionItem> {
    return firstValueFrom(this.http.get<PromotionItem>(`${this.base}/promotions/${id}`));
  }

  nominate(employeeId: string, details: PromotionDetailsInput, submit: boolean): Promise<PromotionItem> {
    return firstValueFrom(this.http.post<PromotionItem>(`${this.base}/promotions`, { employeeId, details, submit }));
  }

  updatePromotion(id: string, details: PromotionDetailsInput): Promise<PromotionItem> {
    return firstValueFrom(this.http.put<PromotionItem>(`${this.base}/promotions/${id}`, details));
  }

  promotionAction(id: string, action: 'submit' | 'withdraw'): Promise<PromotionItem> {
    return firstValueFrom(this.http.post<PromotionItem>(`${this.base}/promotions/${id}/${action}`, {}));
  }

  succession(): Promise<readonly SuccessionItem[]> {
    return firstValueFrom(this.http.get<readonly SuccessionItem[]>(`${this.base}/succession`));
  }

  saveSuccession(id: string | null, body: SuccessionInput): Promise<readonly SuccessionItem[]> {
    return firstValueFrom(id ? this.http.put<readonly SuccessionItem[]>(`${this.base}/succession/${id}`, body) : this.http.post<readonly SuccessionItem[]>(`${this.base}/succession`, body));
  }

  talentReview(departmentId?: string | null): Promise<NineBoxView> {
    return firstValueFrom(this.http.get<NineBoxView>(`${this.base}/talent-review`, { params: params({ departmentId }) }));
  }

  assess(body: { employeeId: string; potential: TalentLevel; flightRisk: TalentLevel; notes?: string | null }): Promise<NineBoxView> {
    return firstValueFrom(this.http.post<NineBoxView>(`${this.base}/talent-review/assessments`, body));
  }

  mine(): Promise<MyCareer> {
    return firstValueFrom(this.http.get<MyCareer>('/api/v1/me/career'));
  }
}
