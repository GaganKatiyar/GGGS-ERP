import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import type { EducationEntry } from '@gggs/shared/models';
import { EmptyState, FormDialog, Skeleton } from '@gggs/shared/ui';
import { educationYears } from './background-model';

const blank = (text: string | null | undefined) => (text?.trim() ? text.trim() : undefined);
const year = (value: number | null | undefined) => (value ? Number(value) : undefined);

/**
 * Degrees and courses as cards, added and edited in a dialog. With no employee id it is the
 * signed-in employee's own (My profile); with one it is HR's view of that employee (Employee 360).
 */
@Component({
  selector: 'gggs-education-tab',
  imports: [ReactiveFormsModule, FormDialog, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (entries(); as list) {
      @if (editable()) {
        <div class="bg-bar">
          <button type="button" class="btn btn--sm" (click)="openAdd()"><i class="pi pi-plus" aria-hidden="true"></i> Add education</button>
        </div>
      }
      <ul class="bg-list" aria-label="Education">
        @for (entry of list; track entry.id) {
          <li class="bg-card">
            <span class="bg-card__icon" aria-hidden="true"><i class="pi pi-book"></i></span>
            <span class="bg-card__body">
              <strong>{{ entry.degree }}{{ entry.fieldOfStudy ? ', ' + entry.fieldOfStudy : '' }}</strong>
              <span>{{ entry.institution }}</span>
              <span class="muted">{{ meta(entry) }}</span>
            </span>
            @if (editable()) {
              <span class="bg-card__actions">
                <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Edit ' + entry.degree" (click)="openEdit(entry)">Edit</button>
                <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Remove ' + entry.degree" (click)="remove(entry)">Remove</button>
              </span>
            }
          </li>
        } @empty {
          <li><gggs-empty-state icon="pi pi-book" title="No education added" [description]="editable() ? 'Add your degrees and courses, starting with the highest.' : 'Nothing has been added yet.'" /></li>
        }
      </ul>
    } @else if (!listError()) {
      <gggs-skeleton [lines]="4" />
    }
    @if (listError()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ listError() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="open" [title]="editing() ? 'Edit education' : 'Add education'" [busy]="busy()" [error]="error()" (save)="save()">
      <div class="form-grid" [formGroup]="form">
        <label class="field">
          <span class="field__label">Degree or course *</span>
          <input type="text" formControlName="degree" maxlength="120" placeholder="B.Tech, MBA, Diploma…" />
        </label>
        <label class="field">
          <span class="field__label">Field of study</span>
          <input type="text" formControlName="fieldOfStudy" maxlength="120" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Institution *</span>
          <input type="text" formControlName="institution" maxlength="200" />
        </label>
        <label class="field">
          <span class="field__label">Year started</span>
          <input type="number" formControlName="startYear" min="1950" max="2100" inputmode="numeric" />
        </label>
        <label class="field">
          <span class="field__label">Year finished</span>
          <input type="number" formControlName="endYear" min="1950" max="2100" inputmode="numeric" />
        </label>
        <label class="field">
          <span class="field__label">Grade</span>
          <input type="text" formControlName="grade" maxlength="32" placeholder="8.4 CGPA, First class, 78%" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
})
export class EducationTab {
  private readonly api = inject(EmployeeDirectoryApi);

  /** Whose education: empty for the signed-in employee's own. */
  readonly employeeId = input<string>();
  readonly editable = input(false);
  /** Opens the add dialog as soon as the tab appears: "Add education" from the completeness card. */
  readonly openOnStart = input(false);
  /** How many entries there are after each load, for the profile's completeness. */
  readonly counted = output<number>();

  protected readonly entries = signal<readonly EducationEntry[] | null>(null);
  protected readonly listError = signal<string | null>(null);
  protected readonly open = signal(false);
  protected readonly editing = signal<EducationEntry | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    degree: new FormControl<string | null>(null, Validators.required),
    fieldOfStudy: new FormControl<string | null>(null),
    institution: new FormControl<string | null>(null, Validators.required),
    startYear: new FormControl<number | null>(null, [Validators.min(1950), Validators.max(2100)]),
    endYear: new FormControl<number | null>(null, [Validators.min(1950), Validators.max(2100)]),
    grade: new FormControl<string | null>(null),
  });

  constructor() {
    effect(() => void this.load(this.employeeId()));
    effect(() => {
      if (this.openOnStart()) {
        untracked(() => this.openAdd());
      }
    });
  }

  protected meta(entry: EducationEntry): string {
    return [educationYears(entry), entry.grade].filter(Boolean).join(' · ');
  }

  openAdd(): void {
    this.form.reset();
    this.editing.set(null);
    this.error.set(null);
    this.open.set(true);
  }

  protected openEdit(entry: EducationEntry): void {
    this.form.reset({
      degree: entry.degree,
      fieldOfStudy: entry.fieldOfStudy ?? null,
      institution: entry.institution,
      startYear: entry.startYear ?? null,
      endYear: entry.endYear ?? null,
      grade: entry.grade ?? null,
    });
    this.editing.set(entry);
    this.error.set(null);
    this.open.set(true);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.error.set('Fill in the degree and institution; years are between 1950 and 2100.');
      return;
    }

    const value = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.saveEducation(
        {
          degree: value.degree!.trim(),
          fieldOfStudy: blank(value.fieldOfStudy),
          institution: value.institution!.trim(),
          startYear: year(value.startYear),
          endYear: year(value.endYear),
          grade: blank(value.grade),
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

  protected async remove(entry: EducationEntry): Promise<void> {
    if (!confirm(`Remove ${entry.degree} from ${entry.institution}?`)) {
      return;
    }

    try {
      await this.api.removeEducation(entry.id, this.employeeId());
      await this.load(this.employeeId());
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'That could not be removed. Try again.');
    }
  }

  private async load(employeeId: string | undefined): Promise<void> {
    this.listError.set(null);
    try {
      const list = await this.api.education(employeeId);
      this.entries.set(list);
      this.counted.emit(list.length);
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'Education could not be loaded.');
    }
  }
}
