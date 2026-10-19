import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { CatalogEntry, CourseInput, CourseItem, LearningDashboard, LearningEnrollment, LearningEnrollmentStatus, LearningPathItem, MyLearning } from '@gggs/shared/models';
import { COURSE_FORMATS, humanize } from '@gggs/shared/models';
import { LearningApi, ProgressBar } from '@gggs/shared/talent';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const split = (value: string) =>
  value
    .split(',')
    .map((part) => part.trim())
    .filter(Boolean);
const num = (value: string): number | null => (value === '' ? null : +value);

const blankCourse = (): CourseInput => ({
  code: '',
  title: '',
  description: null,
  category: null,
  format: 'Online',
  provider: null,
  durationHours: 1,
  contentUrl: null,
  mandatory: false,
  assignToNewJoiners: false,
  completionDays: 30,
  certificateValidityMonths: null,
  skills: [],
});

/** Learning overview: what is assigned, in progress and overdue, mandatory compliance, and certificates lapsing. */
@Component({
  selector: 'gggs-learning-dashboard-page',
  imports: [RouterLink, PageHeader, KpiCard, Skeleton, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Learning" subtitle="Assignments, completion, mandatory compliance and certificates.">
      <a class="btn" routerLink="/learning/enrollments">Enrollments</a>
      <a class="btn" routerLink="/learning/certificates">Certificates</a>
      <a class="btn btn--primary" routerLink="/learning/courses">Courses</a>
    </gggs-page-header>
    @if (dashboard(); as d) {
      <div class="lms__kpis">
        <gggs-kpi-card label="Assigned, not started" [value]="d.assigned" />
        <gggs-kpi-card label="In progress" [value]="d.inProgress" />
        <gggs-kpi-card label="Completed (90 days)" [value]="d.completedLast90Days" />
        <gggs-kpi-card label="Overdue" [value]="d.overdue" />
        <gggs-kpi-card label="Mandatory compliance" [value]="d.mandatoryCompliancePercent !== undefined ? d.mandatoryCompliancePercent + '%' : '—'" hint="People with every mandatory course done" />
        <gggs-kpi-card label="Certificates expiring (30 days)" [value]="d.certificatesExpiringIn30Days" [hint]="d.certificatesExpired + ' already lapsed'" />
      </div>
      <section class="panel stack">
        <h2 class="lms__heading">Most taken</h2>
        @for (course of d.topCourses; track course.id) {
          <div class="lms__row">
            <span><strong>{{ course.title }}</strong> <span class="muted">{{ course.code }}</span></span>
            <span>{{ course.enrolled }} enrolled · {{ course.completed }} completed</span>
            <span class="muted">{{ course.averageRating ? course.averageRating + ' ★' : '' }}</span>
            <span class="muted">{{ course.mandatory ? 'Mandatory' : '' }}</span>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-book" title="No learning yet" />
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './learning.css',
})
export class LearningDashboardPage {
  protected readonly dashboard = signal<LearningDashboard | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    inject(LearningApi)
      .dashboard()
      .then(
        (dashboard) => this.dashboard.set(dashboard),
        (error: unknown) => this.error.set(message(error, 'The overview could not be loaded.')),
      );
  }
}

/** The course catalogue and learning paths, with assignment to people. */
@Component({
  selector: 'gggs-courses-page',
  imports: [FormsModule, PageHeader, StatusBadge, EmptyState, FormDialog, EmployeeSelect, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Courses" subtitle="The catalogue and learning paths. Mandatory courses for joiners are assigned automatically on hire.">
      @if (canManage) {
        <button type="button" class="btn" (click)="editPath(null)">New path</button>
        <button type="button" class="btn btn--primary" (click)="edit(null)"><i class="pi pi-plus" aria-hidden="true"></i> New course</button>
      }
    </gggs-page-header>
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />
    <section class="panel stack">
      @if (tab() === 'courses') {
        @for (course of courses(); track course.id) {
          <div class="lms__row">
            <span><strong>{{ course.title }}</strong> <span class="muted">{{ course.code }}{{ course.category ? ' · ' + course.category : '' }}</span></span>
            <span>{{ humanize(course.format) }} · {{ course.durationHours }} h{{ course.certificateValidityMonths ? ' · certificate ' + course.certificateValidityMonths + ' months' : '' }}</span>
            <span class="muted">{{ course.mandatory ? 'Mandatory' : 'Optional' }}{{ course.assignToNewJoiners ? ' · joiners' : '' }} · {{ course.enrolled }} enrolled</span>
            <span class="toolbar">
              @if (canAssign) {
                <button type="button" class="btn btn--sm" (click)="openAssign(course.id, null)">Assign</button>
              }
              @if (canManage) {
                <button type="button" class="btn btn--link btn--sm" (click)="edit(course)">Edit</button>
              }
              <gggs-status-badge [status]="course.isActive ? 'Active' : 'Inactive'" />
            </span>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-book" title="No courses" />
        }
      } @else {
        @for (path of paths(); track path.id) {
          <div class="lms__row">
            <span><strong>{{ path.title }}</strong> <span class="muted">{{ path.code }}</span></span>
            <span>{{ path.courseTitles.join(' → ') }}</span>
            <span class="muted">{{ path.durationHours }} h in all</span>
            <span class="toolbar">
              @if (canAssign) {
                <button type="button" class="btn btn--sm" (click)="openAssign(null, path.id)">Assign</button>
              }
              @if (canManage) {
                <button type="button" class="btn btn--link btn--sm" (click)="editPath(path)">Edit</button>
              }
            </span>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-sitemap" title="No learning paths" />
        }
      }
      @if (notice()) {
        <p class="lms__notice" role="status">{{ notice() }}</p>
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="courseOpen" [title]="editing() ? 'Edit course' : 'New course'" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().code.trim() || !form().title.trim()" (save)="saveCourse()" width="min(52rem, 96vw)">
      <div class="lms__grid">
        <label class="field">
          <span class="field__label">Code</span>
          <input type="text" maxlength="30" [value]="form().code" (input)="patch({ code: $any($event.target).value.toUpperCase() })" />
        </label>
        <label class="field">
          <span class="field__label">Title</span>
          <input type="text" maxlength="200" [value]="form().title" (input)="patch({ title: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Category</span>
          <input type="text" maxlength="80" [value]="form().category ?? ''" (input)="patch({ category: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Format</span>
          <select [value]="form().format" (change)="patch({ format: $any($event.target).value })">
            @for (format of formats; track format) {
              <option [value]="format">{{ format }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Hours</span>
          <input type="number" min="0" step="0.5" [value]="form().durationHours" (input)="patch({ durationHours: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Provider</span>
          <input type="text" maxlength="200" [value]="form().provider ?? ''" (input)="patch({ provider: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Complete within (days)</span>
          <input type="number" min="1" max="365" [value]="form().completionDays ?? ''" (input)="patch({ completionDays: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Certificate valid (months, empty for none)</span>
          <input type="number" min="1" max="120" [value]="form().certificateValidityMonths ?? ''" (input)="patch({ certificateValidityMonths: num($any($event.target).value) })" />
        </label>
        <label class="field lms__wide">
          <span class="field__label">Link to the content</span>
          <input type="url" maxlength="500" [value]="form().contentUrl ?? ''" (input)="patch({ contentUrl: $any($event.target).value || null })" />
        </label>
        <label class="field lms__wide">
          <span class="field__label">Skills it builds (comma-separated)</span>
          <input type="text" [value]="form().skills.join(', ')" (change)="patch({ skills: split($any($event.target).value) })" />
        </label>
        <label class="field lms__wide">
          <span class="field__label">Description</span>
          <textarea rows="3" maxlength="4000" [value]="form().description ?? ''" (input)="patch({ description: $any($event.target).value || null })"></textarea>
        </label>
        <label class="lms__flag"><input type="checkbox" [checked]="form().mandatory" (change)="patch({ mandatory: $any($event.target).checked })" /> <span>Mandatory (renewed when its certificate lapses)</span></label>
        <label class="lms__flag"><input type="checkbox" [checked]="form().assignToNewJoiners" (change)="patch({ assignToNewJoiners: $any($event.target).checked })" /> <span>Assign to every joiner</span></label>
        <label class="lms__flag"><input type="checkbox" [checked]="active()" (change)="active.set($any($event.target).checked)" /> <span>Active</span></label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="pathOpen" [title]="editingPath() ? 'Edit path' : 'New learning path'" [busy]="busy()" [error]="formError()" [saveDisabled]="!pathCode().trim() || !pathTitle().trim() || !pathCourses().length" (save)="savePath()">
      <div class="lms__grid">
        <label class="field">
          <span class="field__label">Code</span>
          <input type="text" maxlength="30" [value]="pathCode()" (input)="pathCode.set($any($event.target).value.toUpperCase())" />
        </label>
        <label class="field">
          <span class="field__label">Title</span>
          <input type="text" maxlength="200" [value]="pathTitle()" (input)="pathTitle.set($any($event.target).value)" />
        </label>
      </div>
      <fieldset class="lms__fieldset">
        <legend class="field__label">Courses, in order</legend>
        @for (course of courses(); track course.id) {
          <label class="lms__flag"><input type="checkbox" [checked]="pathCourses().includes(course.id)" (change)="togglePathCourse(course.id, $any($event.target).checked)" /> <span>{{ course.title }}</span></label>
        }
      </fieldset>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="assignOpen" title="Assign learning" saveLabel="Assign" [busy]="busy()" [error]="formError()" [saveDisabled]="!assignees().length" (save)="assign()">
      @for (person of assignees(); track person) {
        <div class="toolbar"><span class="muted">Person {{ $index + 1 }}</span> <button type="button" class="btn btn--link btn--sm" (click)="removeAssignee(person)">Remove</button></div>
      }
      <gggs-employee-select ariaLabel="Add a person" [ngModel]="null" (ngModelChange)="addAssignee($event)" />
      <label class="field">
        <span class="field__label">Due (empty for the course's default)</span>
        <input type="date" [value]="dueDate()" (change)="dueDate.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './learning.css',
})
export class CoursesPage {
  private readonly api = inject(LearningApi);
  private readonly permissions = inject(PermissionStore);

  protected readonly canManage = this.permissions.has('learning.course.manage');
  protected readonly canAssign = this.permissions.has('learning.enrollment.manage');
  protected readonly humanize = humanize;
  protected readonly split = split;
  protected readonly num = num;
  protected readonly formats = COURSE_FORMATS;
  protected readonly tabs: readonly TabItem[] = [
    { key: 'courses', label: 'Courses' },
    { key: 'paths', label: 'Learning paths' },
  ];
  protected readonly tab = signal('courses');
  protected readonly courses = signal<readonly CourseItem[]>([]);
  protected readonly paths = signal<readonly LearningPathItem[]>([]);
  protected readonly courseOpen = signal(false);
  protected readonly editing = signal<CourseItem | null>(null);
  protected readonly form = signal<CourseInput>(blankCourse());
  protected readonly active = signal(true);
  protected readonly pathOpen = signal(false);
  protected readonly editingPath = signal<LearningPathItem | null>(null);
  protected readonly pathCode = signal('');
  protected readonly pathTitle = signal('');
  protected readonly pathCourses = signal<readonly string[]>([]);
  protected readonly assignOpen = signal(false);
  protected readonly assignCourse = signal<string | null>(null);
  protected readonly assignPath = signal<string | null>(null);
  protected readonly assignees = signal<readonly string[]>([]);
  protected readonly dueDate = signal('');
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    Promise.all([this.api.courses(true), this.api.paths()]).then(
      ([courses, paths]) => {
        this.courses.set(courses);
        this.paths.set(paths);
      },
      (error: unknown) => this.error.set(message(error, 'The catalogue could not be loaded.')),
    );
  }

  protected edit(course: CourseItem | null): void {
    this.editing.set(course);
    this.form.set(course ? { ...course, description: course.description ?? null } : blankCourse());
    this.active.set(course?.isActive ?? true);
    this.formError.set(null);
    this.courseOpen.set(true);
  }

  protected patch(change: Partial<CourseInput>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected async saveCourse(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      this.courses.set(await this.api.saveCourse(this.editing()?.id ?? null, this.form(), this.active()));
      this.courseOpen.set(false);
    } catch (error) {
      this.formError.set(message(error, 'The course could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected editPath(path: LearningPathItem | null): void {
    this.editingPath.set(path);
    this.pathCode.set(path?.code ?? '');
    this.pathTitle.set(path?.title ?? '');
    this.pathCourses.set(path?.courseIds ?? []);
    this.formError.set(null);
    this.pathOpen.set(true);
  }

  protected togglePathCourse(id: string, on: boolean): void {
    this.pathCourses.update((list) => (on ? [...list, id] : list.filter((item) => item !== id)));
  }

  protected async savePath(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      this.paths.set(await this.api.savePath(this.editingPath()?.id ?? null, { code: this.pathCode(), title: this.pathTitle(), courseIds: this.pathCourses(), isActive: true }));
      this.pathOpen.set(false);
      this.tab.set('paths');
    } catch (error) {
      this.formError.set(message(error, 'The path could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected openAssign(courseId: string | null, pathId: string | null): void {
    this.assignCourse.set(courseId);
    this.assignPath.set(pathId);
    this.assignees.set([]);
    this.dueDate.set('');
    this.formError.set(null);
    this.assignOpen.set(true);
  }

  protected addAssignee(id: string | null): void {
    if (id && !this.assignees().includes(id)) {
      this.assignees.update((list) => [...list, id]);
    }
  }

  protected removeAssignee(id: string): void {
    this.assignees.update((list) => list.filter((item) => item !== id));
  }

  protected async assign(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      const result = await this.api.assign({ employeeIds: this.assignees(), courseId: this.assignCourse(), pathId: this.assignPath(), dueDate: this.dueDate() || null });
      this.assignOpen.set(false);
      this.notice.set(`Assigned ${result.assigned}${result.skipped ? `; ${result.skipped} skipped (already taking or holding it)` : ''}.`);
    } catch (error) {
      this.formError.set(message(error, 'The assignment could not be made.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Enrollments across courses, filterable to what is overdue. */
@Component({
  selector: 'gggs-enrollments-page',
  imports: [DatePipe, PageHeader, StatusBadge, EmptyState, ProgressBar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Enrollments" subtitle="Who is taking what, how far along, and what is overdue." />
    <section class="panel toolbar lms__filters">
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">Any</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ humanize(option) }}</option>
          }
        </select>
      </label>
      <label class="lms__flag"><input type="checkbox" [checked]="overdue()" (change)="overdue.set($any($event.target).checked); load()" /> <span>Only overdue</span></label>
    </section>
    <section class="panel stack">
      @for (row of rows(); track row.id) {
        <div class="lms__row">
          <span><strong>{{ row.employeeName ?? '—' }}</strong> <span class="muted">{{ row.employeeCode ?? '' }}</span></span>
          <span>{{ row.courseTitle }} <span class="muted">{{ humanize(row.source) }}</span></span>
          <span class="lms__progress">{{ row.progress }}% <gggs-progress-bar [value]="row.progress" /></span>
          <span class="muted">{{ row.completedOn ? 'done ' + (row.completedOn | date: 'd MMM y') : row.dueDate ? 'due ' + (row.dueDate | date: 'd MMM y') : '' }}</span>
          <span><gggs-status-badge [status]="row.overdue ? 'Overdue' : row.status" /></span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-list" title="No enrollments here" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
  `,
  styleUrl: './learning.css',
})
export class EnrollmentsPage {
  private readonly api = inject(LearningApi);

  protected readonly humanize = humanize;
  protected readonly statuses: readonly LearningEnrollmentStatus[] = ['Assigned', 'InProgress', 'Completed', 'Expired', 'Cancelled'];
  protected readonly rows = signal<readonly LearningEnrollment[]>([]);
  protected readonly status = signal<LearningEnrollmentStatus | ''>('');
  protected readonly overdue = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.rows.set(await this.api.enrollments({ status: this.status() || null, overdue: this.overdue() || null }));
    } catch (error) {
      this.error.set(message(error, 'Enrollments could not be loaded.'));
    }
  }
}

/** Certificates earned and when they lapse. */
@Component({
  selector: 'gggs-certificates-page',
  imports: [DatePipe, PageHeader, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Certificates" subtitle="Certificates earned from courses. Mandatory courses are assigned again when their certificate lapses.">
      <label class="lms__flag"><input type="checkbox" [checked]="expiring()" (change)="expiring.set($any($event.target).checked); load()" /> <span>Expiring within 60 days</span></label>
    </gggs-page-header>
    <section class="panel stack">
      @for (row of rows(); track row.id) {
        <div class="lms__row">
          <span><strong>{{ row.employeeName ?? '—' }}</strong></span>
          <span>{{ row.courseTitle }}</span>
          <span class="muted">{{ row.certificateNumber }}</span>
          <span>{{ row.certificateExpiresOn ? 'until ' + (row.certificateExpiresOn | date: 'd MMM y') : '' }}</span>
          <span><gggs-status-badge [status]="row.status" /></span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-verified" title="No certificates" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
  `,
  styleUrl: './learning.css',
})
export class CertificatesPage {
  private readonly api = inject(LearningApi);

  protected readonly rows = signal<readonly LearningEnrollment[]>([]);
  protected readonly expiring = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    try {
      this.rows.set(await this.api.certificates(this.expiring()));
    } catch (error) {
      this.error.set(message(error, 'Certificates could not be loaded.'));
    }
  }
}

/** My learning: what is assigned to me, the catalogue to enrol from, and my certificates. */
@Component({
  selector: 'gggs-my-learning-page',
  imports: [DatePipe, PageHeader, StatusBadge, EmptyState, Skeleton, FormDialog, ProgressBar, Tabs, KpiCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My learning" subtitle="Courses assigned to you, the catalogue, and your certificates." />
    @if (mine(); as m) {
      <div class="lms__kpis">
        <gggs-kpi-card label="To do" [value]="m.active.length" [hint]="overdueCount() + ' overdue'" />
        <gggs-kpi-card label="Completed" [value]="m.completed.length" />
        <gggs-kpi-card label="Hours this year" [value]="m.hoursThisYear" />
        <gggs-kpi-card label="Certificates" [value]="m.certificates.length" />
      </div>
      <gggs-tabs [tabs]="tabs" [(selected)]="tab" />
      <section class="panel stack">
        @switch (tab()) {
          @case ('active') {
            @for (row of m.active; track row.id) {
              <article class="lms__course">
                <div class="toolbar">
                  <strong>{{ row.courseTitle }}</strong>
                  <span class="muted">{{ humanize(row.format) }} · {{ row.durationHours }} h{{ row.mandatory ? ' · mandatory' : '' }}</span>
                  <span class="lms__spacer"></span>
                  <gggs-status-badge [status]="row.overdue ? 'Overdue' : row.status" />
                </div>
                <span class="lms__progress">{{ row.progress }}% <gggs-progress-bar [value]="row.progress" /></span>
                <div class="toolbar">
                  <span class="muted">{{ row.dueDate ? 'Due ' + (row.dueDate | date: 'd MMM y') : 'No due date' }}{{ row.assignedByName ? ' · assigned by ' + row.assignedByName : '' }}</span>
                  <span class="lms__spacer"></span>
                  @if (row.contentUrl) {
                    <a class="btn btn--sm" [href]="row.contentUrl" target="_blank" rel="noopener">Open course</a>
                  }
                  @if (row.status === 'Assigned') {
                    <button type="button" class="btn btn--sm" [disabled]="busy()" (click)="start(row)">Start</button>
                  } @else {
                    <button type="button" class="btn btn--sm" [disabled]="busy()" (click)="progress(row)">+25%</button>
                  }
                  <button type="button" class="btn btn--primary btn--sm" (click)="openComplete(row)">Mark complete</button>
                  @if (row.source === 'Self') {
                    <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="cancel(row)">Drop</button>
                  }
                </div>
              </article>
            } @empty {
              <gggs-empty-state icon="pi pi-check" title="Nothing to do" description="Browse the catalogue to pick up something new." />
            }
          }
          @case ('catalog') {
            @for (entry of catalog(); track entry.course.id) {
              <div class="lms__row">
                <span><strong>{{ entry.course.title }}</strong> <span class="muted">{{ entry.course.category ?? '' }}</span></span>
                <span>{{ humanize(entry.course.format) }} · {{ entry.course.durationHours }} h{{ entry.course.averageRating ? ' · ' + entry.course.averageRating + ' ★' : '' }}</span>
                <span class="muted">{{ entry.course.description ?? '' }}</span>
                <span>
                  @if (entry.mine; as mine) {
                    <gggs-status-badge [status]="mine.status" />
                  } @else {
                    <button type="button" class="btn btn--primary btn--sm" [disabled]="busy()" (click)="enroll(entry)">Enrol</button>
                  }
                </span>
              </div>
            }
          }
          @default {
            @for (row of m.certificates; track row.id) {
              <div class="lms__row">
                <span><strong>{{ row.courseTitle }}</strong></span>
                <span class="muted">{{ row.certificateNumber }}</span>
                <span>{{ row.completedOn | date: 'd MMM y' }} – {{ row.certificateExpiresOn | date: 'd MMM y' }}</span>
                <span><gggs-status-badge [status]="row.status" /></span>
              </div>
            } @empty {
              <gggs-empty-state icon="pi pi-verified" title="No certificates yet" />
            }
            @for (row of m.completed; track row.id) {
              @if (!row.rating) {
                <div class="lms__row">
                  <span>{{ row.courseTitle }}</span>
                  <span class="muted">completed {{ row.completedOn | date: 'd MMM y' }}</span>
                  <span class="lms__stars">
                    @for (score of [1, 2, 3, 4, 5]; track score) {
                      <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Rate ' + score" (click)="rate(row, score)">{{ score }}★</button>
                    }
                  </span>
                  <span></span>
                </div>
              }
            }
          }
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
    <gggs-form-dialog [(visible)]="completeOpen" [title]="'Complete · ' + (completing()?.courseTitle ?? '')" saveLabel="Mark complete" [busy]="busy()" [error]="formError()" (save)="complete()">
      <label class="field">
        <span class="field__label">Completed on</span>
        <input type="date" [value]="completedOn()" (change)="completedOn.set($any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Score (optional)</span>
        <input type="number" min="0" max="100" [value]="score() ?? ''" (input)="score.set(num($any($event.target).value))" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './learning.css',
})
export class MyLearningPage {
  private readonly api = inject(LearningApi);

  protected readonly humanize = humanize;
  protected readonly num = num;
  protected readonly tabs: readonly TabItem[] = [
    { key: 'active', label: 'To do' },
    { key: 'catalog', label: 'Catalogue' },
    { key: 'certificates', label: 'Certificates and history' },
  ];
  protected readonly tab = signal('active');
  protected readonly mine = signal<MyLearning | null>(null);
  protected readonly catalog = signal<readonly CatalogEntry[]>([]);
  protected readonly completeOpen = signal(false);
  protected readonly completing = signal<LearningEnrollment | null>(null);
  protected readonly completedOn = signal(new Date().toISOString().slice(0, 10));
  protected readonly score = signal<number | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly overdueCount = computed(() => (this.mine()?.active ?? []).filter((row) => row.overdue).length);

  constructor() {
    void this.load();
  }

  protected async start(row: LearningEnrollment): Promise<void> {
    await this.run(() => this.api.start(row.id));
  }

  protected async progress(row: LearningEnrollment): Promise<void> {
    await this.run(() => this.api.progress(row.id, Math.min(99, row.progress + 25)));
  }

  protected async cancel(row: LearningEnrollment): Promise<void> {
    await this.run(() => this.api.cancel(row.id));
  }

  protected async rate(row: LearningEnrollment, rating: number): Promise<void> {
    await this.run(() => this.api.rate(row.id, rating, null));
  }

  protected async enroll(entry: CatalogEntry): Promise<void> {
    await this.run(() => this.api.enroll(entry.course.id));
    this.tab.set('active');
  }

  protected openComplete(row: LearningEnrollment): void {
    this.completing.set(row);
    this.completedOn.set(new Date().toISOString().slice(0, 10));
    this.score.set(null);
    this.formError.set(null);
    this.completeOpen.set(true);
  }

  protected async complete(): Promise<void> {
    const row = this.completing();
    if (!row) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.complete(row.id, { completedOn: this.completedOn(), score: this.score() });
      this.completeOpen.set(false);
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'The course could not be completed.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const [mine, catalog] = await Promise.all([this.api.mine(), this.api.catalog()]);
      this.mine.set(mine);
      this.catalog.set(catalog);
    } catch (error) {
      this.error.set(error instanceof ApiError && error.status === 422 ? 'Your account is not linked to an employee record.' : message(error, 'Your learning could not be loaded.'));
    }
  }
}
