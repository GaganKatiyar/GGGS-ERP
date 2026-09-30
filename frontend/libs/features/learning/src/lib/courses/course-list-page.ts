import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { CourseListItem } from '@gggs/shared/models';
import { LearningApi } from '../data/learning-api';

/** Every course this tenant has defined, plus a form to add a new one and per-row
 * activate/deactivate actions. */
@Component({
  selector: 'gggs-course-list-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Courses" subtitle="Employees are enrolled against a course." />

    <gggs-card title="Course catalog">
      @if (courses(); as items) {
        @if (items.length > 0) {
          <table class="courses-table">
            <thead>
              <tr>
                <th scope="col">Code</th>
                <th scope="col">Title</th>
                <th scope="col">Duration (hrs)</th>
                <th scope="col">Status</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              @for (course of items; track course.id) {
                <tr>
                  <td>{{ course.code }}</td>
                  <td>{{ course.title }}</td>
                  <td>{{ course.durationHours }}</td>
                  <td>{{ course.isActive ? 'Active' : 'Inactive' }}</td>
                  <td class="course-actions" *gggsHasPermission="'learning.course.manage'">
                    @if (course.isActive) {
                      <gggs-button size="sm" (click)="deactivate(course.id)" [disabled]="isActing()">Deactivate</gggs-button>
                    } @else {
                      <gggs-button size="sm" (click)="reactivate(course.id)" [disabled]="isActing()">Reactivate</gggs-button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No courses yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }

      <form
        *gggsHasPermission="'learning.course.manage'"
        [formGroup]="form"
        (ngSubmit)="submit()"
        novalidate
        class="create-form"
      >
        <label class="field">
          <span class="field__label">Code</span>
          <input type="text" formControlName="code" maxlength="30" required />
        </label>

        <label class="field">
          <span class="field__label">Title</span>
          <input type="text" formControlName="title" maxlength="200" required />
        </label>

        <label class="field">
          <span class="field__label">Duration (hours)</span>
          <input type="number" formControlName="durationHours" min="0.25" step="0.25" required />
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Creating…' : 'Create course' }}
        </gggs-button>
      </form>
    </gggs-card>
  `,
  styles: `
    .courses-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .courses-table th,
    .courses-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .course-actions {
      display: flex;
      gap: 0.5rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 1rem;
    }

    .create-form {
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
  `,
})
export class CourseListPage {
  private readonly api = inject(LearningApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly courses = signal<readonly CourseListItem[] | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly isActing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    code: ['', [Validators.required, Validators.maxLength(30)]],
    title: ['', [Validators.required, Validators.maxLength(200)]],
    durationHours: [1, [Validators.required, Validators.min(0.25)]],
  });

  constructor() {
    void this.load();
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      await this.api.createCourse({
        code: value.code ?? '',
        title: value.title ?? '',
        durationHours: value.durationHours ?? 1,
      });

      this.form.reset({ durationHours: 1 });
      await this.load();
    } catch {
      this.errorMessage.set('The course could not be created. A code may already be in use.');
    } finally {
      this.isSaving.set(false);
    }
  }

  protected async deactivate(id: string): Promise<void> {
    await this.act(() => this.api.deactivateCourse(id));
  }

  protected async reactivate(id: string): Promise<void> {
    await this.act(() => this.api.reactivateCourse(id));
  }

  private async act(run: () => Promise<unknown>): Promise<void> {
    if (this.isActing()) {
      return;
    }

    this.isActing.set(true);

    try {
      await run();
      await this.load();
    } finally {
      this.isActing.set(false);
    }
  }

  private async load(): Promise<void> {
    this.courses.set(await this.api.listCourses());
  }
}
