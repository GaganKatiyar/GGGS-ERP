import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import type {
  CheckInGoalRequest,
  CreateGoalRequest,
  CreatePerformanceCycleRequest,
  CreateReviewRequest,
  Goal,
  GoalCheckIn,
  GoalListItem,
  PerformanceCycle,
  PerformanceCycleListItem,
  Review,
  SubmitReviewRatingRequest,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/** Performance cycles, goals and reviews, over the gateway's `/api/v1/performance/*` routes. */
@Injectable({ providedIn: 'root' })
export class PerformanceApi {
  private readonly http = inject(HttpClient);

  // ---- Cycles ----

  listCycles(): Promise<readonly PerformanceCycleListItem[]> {
    return firstValueFrom(this.http.get<readonly PerformanceCycleListItem[]>('/api/v1/performance/cycles'));
  }

  getCycle(id: string): Promise<PerformanceCycle> {
    return firstValueFrom(this.http.get<PerformanceCycle>(`/api/v1/performance/cycles/${id}`));
  }

  createCycle(request: CreatePerformanceCycleRequest): Promise<PerformanceCycle> {
    return firstValueFrom(this.http.post<PerformanceCycle>('/api/v1/performance/cycles', request));
  }

  openCycle(id: string): Promise<PerformanceCycle> {
    return firstValueFrom(this.http.post<PerformanceCycle>(`/api/v1/performance/cycles/${id}/open`, {}));
  }

  closeCycle(id: string): Promise<PerformanceCycle> {
    return firstValueFrom(this.http.post<PerformanceCycle>(`/api/v1/performance/cycles/${id}/close`, {}));
  }

  // ---- Goals ----

  listGoalsForEmployee(employeeId: string): Promise<readonly GoalListItem[]> {
    return firstValueFrom(
      this.http.get<readonly GoalListItem[]>(`/api/v1/performance/employees/${employeeId}/goals`),
    );
  }

  getGoal(id: string): Promise<Goal> {
    return firstValueFrom(this.http.get<Goal>(`/api/v1/performance/goals/${id}`));
  }

  createGoal(request: CreateGoalRequest): Promise<Goal> {
    return firstValueFrom(this.http.post<Goal>('/api/v1/performance/goals', request));
  }

  checkInGoal(id: string, request: CheckInGoalRequest): Promise<Goal> {
    return firstValueFrom(this.http.post<Goal>(`/api/v1/performance/goals/${id}/check-ins`, request));
  }

  listGoalCheckIns(id: string): Promise<readonly GoalCheckIn[]> {
    return firstValueFrom(this.http.get<readonly GoalCheckIn[]>(`/api/v1/performance/goals/${id}/check-ins`));
  }

  cancelGoal(id: string): Promise<Goal> {
    return firstValueFrom(this.http.post<Goal>(`/api/v1/performance/goals/${id}/cancel`, {}));
  }

  // ---- Reviews ----

  getReview(id: string): Promise<Review> {
    return firstValueFrom(this.http.get<Review>(`/api/v1/performance/reviews/${id}`));
  }

  /** Null when no review has been raised yet for this employee and cycle - a 404 here is a normal,
   * expected state, not an error (see RecruitmentApi.getOffer's own remarks on the same shape). */
  async getReviewForEmployeeAndCycle(employeeId: string, cycleId: string): Promise<Review | null> {
    try {
      return await firstValueFrom(
        this.http.get<Review>(`/api/v1/performance/employees/${employeeId}/cycles/${cycleId}/review`),
      );
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        return null;
      }

      throw error;
    }
  }

  createReview(request: CreateReviewRequest): Promise<Review> {
    return firstValueFrom(this.http.post<Review>('/api/v1/performance/reviews', request));
  }

  submitSelfReview(id: string, request: SubmitReviewRatingRequest): Promise<Review> {
    return firstValueFrom(this.http.post<Review>(`/api/v1/performance/reviews/${id}/self`, request));
  }

  submitManagerReview(id: string, request: SubmitReviewRatingRequest): Promise<Review> {
    return firstValueFrom(this.http.post<Review>(`/api/v1/performance/reviews/${id}/manager`, request));
  }

  releaseReview(id: string): Promise<Review> {
    return firstValueFrom(this.http.post<Review>(`/api/v1/performance/reviews/${id}/release`, {}));
  }
}
