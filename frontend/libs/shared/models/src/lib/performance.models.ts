/**
 * Performance, mirroring the DTOs in `Gggs.Talent.Api.Endpoints.PerformanceCycleEndpoints`,
 * `GoalEndpoints` and `ReviewEndpoints`.
 */

export type PerformanceCycleStatus = 'Draft' | 'Open' | 'Closed';

export interface PerformanceCycleListItem {
  readonly id: string;
  readonly name: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly status: PerformanceCycleStatus;
}

export interface PerformanceCycle {
  readonly id: string;
  readonly name: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly status: PerformanceCycleStatus;
}

export interface CreatePerformanceCycleRequest {
  readonly name: string;
  readonly startDate: string;
  readonly endDate: string;
}

export type GoalStatus = 'Active' | 'Completed' | 'Cancelled';

export interface GoalListItem {
  readonly id: string;
  readonly title: string;
  readonly targetValue: number;
  readonly currentValue: number;
  readonly unit: string;
  readonly status: GoalStatus;
}

export interface Goal {
  readonly id: string;
  readonly cycleId: string;
  readonly employeeId: string;
  readonly title: string;
  readonly description?: string;
  readonly targetValue: number;
  readonly currentValue: number;
  readonly unit: string;
  readonly dueDate: string;
  readonly status: GoalStatus;
}

export interface CreateGoalRequest {
  readonly cycleId: string;
  readonly employeeId: string;
  readonly title: string;
  readonly description?: string;
  readonly targetValue: number;
  readonly unit: string;
  readonly dueDate: string;
}

export interface CheckInGoalRequest {
  readonly checkInDate: string;
  readonly value: number;
  readonly comment?: string;
}

export interface GoalCheckIn {
  readonly id: string;
  readonly checkInDate: string;
  readonly value: number;
  readonly comment?: string;
}

export type ReviewStatus = 'Draft' | 'SelfSubmitted' | 'ManagerSubmitted' | 'Released';

export interface Review {
  readonly id: string;
  readonly cycleId: string;
  readonly employeeId: string;
  readonly managerEmployeeId: string;
  readonly selfRating?: number;
  readonly selfComments?: string;
  readonly managerRating?: number;
  readonly managerComments?: string;
  readonly status: ReviewStatus;
  readonly releasedDate?: string;
}

export interface CreateReviewRequest {
  readonly cycleId: string;
  readonly employeeId: string;
  readonly managerEmployeeId: string;
}

export interface SubmitReviewRatingRequest {
  readonly rating: number;
  readonly comments?: string;
}
