import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button } from '@gggs/shared/ui';
import type { CourseListItem, EnrollmentListItem } from '@gggs/shared/models';
import { LearningApi } from '../data/learning-api';

/** One employee's course enrollments - a list with per-row start/complete/cancel actions, and a
 * form to assign a new one from the active course catalog. */
@Component({
  selector: 'gggs-employee-enrollments-panel',
  imports: [ReactiveFormsModule, HasPermissionDirective, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (enrollments(); as items) {
      @if (items.length > 0) {
        <table class="enrollments-table">
          <thead>
            <tr>
              <th scope="col">Course</th>
              <th scope="col">Due</th>
              <th scope="col">Status</th>
              <th scope="col">Score</th>
              <th scope="col"></th>
            </tr>
          </thead>
          <tbody>
            @for (enrollment of items; track enrollment.id) {
              <tr>
                <td>{{ enrollment.courseTitle }}</td>
                <td>{{ enrollment.dueDate || '—' }}</td>
                <td>{{ enrollment.status }}</td>
                <td>{{ enrollment.score ?? '—' }}</td>
                <td class="enrollment-actions">
                  @if (enrollment.status === 'Assigned') {
                    <gggs-button
                      *gggsHasPermission="'learning.enrollment.manage-own'"
                      size="sm"
                      (click)="start(enrollment.id)"
                      [disabled]="isActing()"
                    >
                      Start
                    </gggs-button>
                  }
                  @if (enrollment.status === 'Assigned' || enrollment.status === 'InProgress') {
                    <gggs-button
                      *gggsHasPermission="'learning.enrollment.manage-own'"
                      size="sm"
                      (click)="toggleComplete(enrollment.id)"
                      [disabled]="isActing()"
                    >
                      {{ expandedEnrollmentId() === enrollment.id ? 'Cancel' : 'Complete' }}
                    </gggs-button>
                    <gggs-button
                      *gggsHasPermission="'learning.enrollment.manage'"
                      size="sm"
                      (click)="cancel(enrollment.id)"
                      [disabled]="isActing()"
                    >
                      Withdraw
                    </gggs-button>
                  }
                </td>
              </tr>
              @if (expandedEnrollmentId() === enrollment.id) {
                <tr>
                  <td colspan="5" class="complete-cell">
                    <form [formGroup]="completeForm" (ngSubmit)="submitComplete(enrollment.id)" novalidate class="complete-form">
                      <label class="field">
                        <span class="field__label">Score (optional, 0-100)</span>
                        <input type="number" formControlName="score" min="0" max="100" />
                      </label>
                      <gggs-button type="submit" size="sm" [disabled]="isActing()">
                        {{ isActing() ? 'Saving…' : 'Mark complete' }}
                      </gggs-button>
                    </form>
                  </td>
                </tr>
              }
            }
          </tbody>
        </table>
      } @else {
        <p class="muted">No enrollments yet.</p>
      }
    } @else {
      <p class="muted">Loading…</p>
    }

    <form
      *gggsHasPermission="'learning.enrollment.manage'"
      [formGroup]="assignForm"
      (ngSubmit)="submitAssign()"
      novalidate
      class="assign-form"
    >
      <label class="field">
        <span class="field__label">Course</span>
        <select formControlName="courseId" required>
          <option value="" disabled>Select a course</option>
          @for (course of activeCourses(); track course.id) {
            <option [value]="course.id">{{ course.title }}</option>
          }
        </select>
      </label>

      <label class="field">
        <span class="field__label">Due date (optional)</span>
        <input type="date" formControlName="dueDate" />
      </label>

      @if (errorMessage()) {
        <p class="field__error" role="alert">{{ errorMessage() }}</p>
      }

      <gggs-button type="submit" size="sm" [disabled]="isActing()">
        {{ isActing() ? 'Assigning…' : 'Assign course' }}
      </gggs-button>
    </form>
  `,
  styles: `
    .enrollments-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .enrollments-table th,
    .enrollments-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .enrollment-actions {
      display: flex;
      gap: 0.5rem;
    }

    .complete-cell {
      background: var(--gggs-color-surface-sunken, #f5f6f8);
      padding: 0.75rem 1rem;
    }

    .complete-form,
    .assign-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
      flex-wrap: wrap;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
    }

    .field__label {
      font-weight: 600;
    }

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 1rem;
    }
  `,
})
export class EmployeeEnrollmentsPanel {
  private readonly api = inject(LearningApi);
  private readonly formBuilder = inject(FormBuilder);

  readonly employeeId = input.required<string>();

  protected readonly enrollments = signal<readonly EnrollmentListItem[] | null>(null);
  protected readonly activeCourses = signal<readonly CourseListItem[]>([]);
  protected readonly isActing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly expandedEnrollmentId = signal<string | null>(null);

  protected readonly assignForm = this.formBuilder.group({
    courseId: ['', Validators.required],
    dueDate: [''],
  });

  protected readonly completeForm = this.formBuilder.group({
    score: [null as number | null],
  });

  constructor() {
    void this.load();
    void this.loadCourses();
  }

  protected toggleComplete(enrollmentId: string): void {
    this.expandedEnrollmentId.set(this.expandedEnrollmentId() === enrollmentId ? null : enrollmentId);
    this.completeForm.reset();
  }

  protected async start(enrollmentId: string): Promise<void> {
    await this.act(() => this.api.startEnrollment(enrollmentId));
  }

  protected async submitComplete(enrollmentId: string): Promise<void> {
    const score = this.completeForm.getRawValue().score;
    await this.act(() => this.api.completeEnrollment(enrollmentId, { score: score ?? undefined }));
    this.expandedEnrollmentId.set(null);
  }

  protected async cancel(enrollmentId: string): Promise<void> {
    await this.act(() => this.api.cancelEnrollment(enrollmentId));
  }

  protected async submitAssign(): Promise<void> {
    if (this.assignForm.invalid || this.isActing()) {
      this.assignForm.markAllAsTouched();
      return;
    }

    this.isActing.set(true);
    this.errorMessage.set(null);

    const value = this.assignForm.getRawValue();

    try {
      await this.api.createEnrollment({
        courseId: value.courseId ?? '',
        employeeId: this.employeeId(),
        dueDate: value.dueDate || undefined,
      });

      this.assignForm.reset();
      await this.load();
    } catch {
      this.errorMessage.set('The course could not be assigned. Please try again.');
    } finally {
      this.isActing.set(false);
    }
  }

  private async act(run: () => Promise<unknown>): Promise<void> {
    if (this.isActing()) {
      return;
    }

    this.isActing.set(true);
    this.errorMessage.set(null);

    try {
      await run();
      await this.load();
    } catch {
      this.errorMessage.set('That action could not be completed. Please try again.');
    } finally {
      this.isActing.set(false);
    }
  }

  private async load(): Promise<void> {
    this.enrollments.set(await this.api.listEnrollmentsForEmployee(this.employeeId()));
  }

  private async loadCourses(): Promise<void> {
    const courses = await this.api.listCourses();
    this.activeCourses.set(courses.filter((c) => c.isActive));
  }
}
