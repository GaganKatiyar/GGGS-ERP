import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type {
  CompleteEnrollmentRequest,
  Course,
  CourseListItem,
  CreateCourseRequest,
  CreateEnrollmentRequest,
  Enrollment,
  EnrollmentListItem,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

/** Courses and enrollments, over the gateway's `/api/v1/learning/*` routes. */
@Injectable({ providedIn: 'root' })
export class LearningApi {
  private readonly http = inject(HttpClient);

  // ---- Courses ----

  listCourses(): Promise<readonly CourseListItem[]> {
    return firstValueFrom(this.http.get<readonly CourseListItem[]>('/api/v1/learning/courses'));
  }

  getCourse(id: string): Promise<Course> {
    return firstValueFrom(this.http.get<Course>(`/api/v1/learning/courses/${id}`));
  }

  createCourse(request: CreateCourseRequest): Promise<Course> {
    return firstValueFrom(this.http.post<Course>('/api/v1/learning/courses', request));
  }

  deactivateCourse(id: string): Promise<Course> {
    return firstValueFrom(this.http.post<Course>(`/api/v1/learning/courses/${id}/deactivate`, {}));
  }

  reactivateCourse(id: string): Promise<Course> {
    return firstValueFrom(this.http.post<Course>(`/api/v1/learning/courses/${id}/reactivate`, {}));
  }

  // ---- Enrollments ----

  listEnrollmentsForEmployee(employeeId: string): Promise<readonly EnrollmentListItem[]> {
    return firstValueFrom(
      this.http.get<readonly EnrollmentListItem[]>(`/api/v1/learning/employees/${employeeId}/enrollments`),
    );
  }

  createEnrollment(request: CreateEnrollmentRequest): Promise<Enrollment> {
    return firstValueFrom(this.http.post<Enrollment>('/api/v1/learning/enrollments', request));
  }

  startEnrollment(id: string): Promise<Enrollment> {
    return firstValueFrom(this.http.post<Enrollment>(`/api/v1/learning/enrollments/${id}/start`, {}));
  }

  completeEnrollment(id: string, request: CompleteEnrollmentRequest): Promise<Enrollment> {
    return firstValueFrom(this.http.post<Enrollment>(`/api/v1/learning/enrollments/${id}/complete`, request));
  }

  cancelEnrollment(id: string): Promise<Enrollment> {
    return firstValueFrom(this.http.post<Enrollment>(`/api/v1/learning/enrollments/${id}/cancel`, {}));
  }
}
