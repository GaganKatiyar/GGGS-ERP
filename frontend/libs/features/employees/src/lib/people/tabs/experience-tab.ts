import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import type { WorkExperienceEntry } from '@gggs/shared/models';
import { EmptyState, FormDialog, Skeleton } from '@gggs/shared/ui';
import { durationLabel, experienceSpan, fromMonthInput, toMonthInput, totalExperience } from './background-model';

const blank = (text: string | null | undefined) => (text?.trim() ? text.trim() : undefined);
/** Today's date where the employee is, as an ISO date. */
const today = () => {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
};

/**
 * Jobs held before joining, newest first, with the total. With no employee id it is the signed-in
 * employee's own (My profile); with one it is HR's view of that employee (Employee 360).
 */
@Component({
  selector: 'gggs-experience-tab',
  imports: [ReactiveFormsModule, FormDialog, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (entries(); as list) {
      @if (editable()) {
        <div class="bg-bar">
          <button type="button" class="btn btn--sm" (click)="openAdd()"><i class="pi pi-plus" aria-hidden="true"></i> Add experience</button>
        </div>
      }
      @if (list.length) {
        <p class="bg-total">Experience before joining: <strong>{{ total() }}</strong></p>
      }
      <ul class="bg-list" aria-label="Work experience">
        @for (entry of list; track entry.id) {
          <li class="bg-card">
            <span class="bg-card__icon" aria-hidden="true"><i class="pi pi-briefcase"></i></span>
            <span class="bg-card__body">
              <strong>{{ entry.title }}</strong>
              <span>{{ entry.company }}{{ entry.location ? ' · ' + entry.location : '' }}</span>
              <span class="muted">{{ span(entry) }}</span>
              @if (entry.summary) {
                <span class="bg-card__summary">{{ entry.summary }}</span>
              }
            </span>
            @if (editable()) {
              <span class="bg-card__actions">
                <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Edit ' + entry.title + ' at ' + entry.company" (click)="openEdit(entry)">Edit</button>
                <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Remove ' + entry.title + ' at ' + entry.company" (click)="remove(entry)">Remove</button>
              </span>
            }
          </li>
        } @empty {
          <li><gggs-empty-state icon="pi pi-briefcase" title="No experience added" [description]="editable() ? 'Add the jobs you held before joining, newest first.' : 'Nothing has been added yet.'" /></li>
        }
      </ul>
    } @else if (!listError()) {
      <gggs-skeleton [lines]="4" />
    }
    @if (listError()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ listError() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="open" [title]="editing() ? 'Edit experience' : 'Add experience'" [busy]="busy()" [error]="error()" (save)="save()">
      <div class="form-grid" [formGroup]="form">
        <label class="field">
          <span class="field__label">Job title *</span>
          <input type="text" formControlName="title" maxlength="120" />
        </label>
        <label class="field">
          <span class="field__label">Company *</span>
          <input type="text" formControlName="company" maxlength="200" />
        </label>
        <label class="field">
          <span class="field__label">From *</span>
          <input type="month" formControlName="from" [max]="thisMonth" />
        </label>
        <div class="field">
          <label class="field__label" for="experience-to">To</label>
          <input id="experience-to" type="month" formControlName="to" aria-describedby="experience-to-hint" />
          <span id="experience-to-hint" class="field__hint">Leave empty if you still held it when you joined.</span>
        </div>
        <label class="field form-grid__full">
          <span class="field__label">Location</span>
          <input type="text" formControlName="location" maxlength="120" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">What you did</span>
          <textarea formControlName="summary" rows="3" maxlength="1000"></textarea>
        </label>
      </div>
    </gggs-form-dialog>
  `,
})
export class ExperienceTab {
  private readonly api = inject(EmployeeDirectoryApi);

  /** Whose experience: empty for the signed-in employee's own. */
  readonly employeeId = input<string>();
  readonly editable = input(false);

  protected readonly entries = signal<readonly WorkExperienceEntry[] | null>(null);
  protected readonly listError = signal<string | null>(null);
  protected readonly open = signal(false);
  protected readonly editing = signal<WorkExperienceEntry | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly thisMonth = today().slice(0, 7);

  protected readonly total = computed(() => durationLabel(totalExperience(this.entries() ?? [], today())));

  protected readonly form = new FormGroup({
    title: new FormControl<string | null>(null, Validators.required),
    company: new FormControl<string | null>(null, Validators.required),
    from: new FormControl<string | null>(null, Validators.required),
    to: new FormControl<string | null>(null),
    location: new FormControl<string | null>(null),
    summary: new FormControl<string | null>(null),
  });

  constructor() {
    effect(() => void this.load(this.employeeId()));
  }

  protected span(entry: WorkExperienceEntry): string {
    return experienceSpan(entry, today());
  }

  openAdd(): void {
    this.form.reset();
    this.editing.set(null);
    this.error.set(null);
    this.open.set(true);
  }

  protected openEdit(entry: WorkExperienceEntry): void {
    this.form.reset({
      title: entry.title,
      company: entry.company,
      from: toMonthInput(entry.fromDate),
      to: toMonthInput(entry.toDate),
      location: entry.location ?? null,
      summary: entry.summary ?? null,
    });
    this.editing.set(entry);
    this.error.set(null);
    this.open.set(true);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.error.set('Fill in the job title, company and start month.');
      return;
    }

    const value = this.form.getRawValue();
    if (value.to && value.to < value.from!) {
      this.error.set('The end month cannot be before the start month.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.saveWorkExperience(
        {
          title: value.title!.trim(),
          company: value.company!.trim(),
          location: blank(value.location),
          fromDate: fromMonthInput(value.from)!,
          toDate: fromMonthInput(value.to),
          summary: blank(value.summary),
        },
        this.editing()?.id ?? null,
        this.employeeId(),
      );
      this.open.set(false);
      await this.load(this.employeeId());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That could not be saved. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(entry: WorkExperienceEntry): Promise<void> {
    if (!confirm(`Remove ${entry.title} at ${entry.company}?`)) {
      return;
    }

    try {
      await this.api.removeWorkExperience(entry.id, this.employeeId());
      await this.load(this.employeeId());
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'That could not be removed. Try again.');
    }
  }

  private async load(employeeId: string | undefined): Promise<void> {
    this.listError.set(null);
    try {
      this.entries.set(await this.api.workExperience(employeeId));
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'Experience could not be loaded.');
    }
  }
}
