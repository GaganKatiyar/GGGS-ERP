import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { WorkforceApi } from '@gggs/shared/hiring';
import { LookupSelect } from '@gggs/shared/master-data';
import type { ManpowerRequestInput, ManpowerRequestItem } from '@gggs/shared/models';
import { LOOKUP_LISTS, MANPOWER_PRIORITIES, MANPOWER_REASONS, humanize } from '@gggs/shared/models';
import { OrgSelect } from '@gggs/shared/org-reference';
import { FormDialog } from '@gggs/shared/ui';

const blank = (): ManpowerRequestInput => ({
  title: '',
  departmentId: '',
  positionId: null,
  jobId: null,
  locationId: null,
  positions: 1,
  skills: [],
  minExperienceYears: null,
  maxExperienceYears: null,
  employmentTypeCode: 'FULL_TIME',
  workModeCode: 'HYBRID',
  salaryMin: null,
  salaryMax: null,
  budget: null,
  priority: 'Medium',
  requiredBy: null,
  reason: 'NewRole',
  replacementForEmployeeId: null,
  justification: null,
});

const fromItem = (request: ManpowerRequestItem): ManpowerRequestInput => ({
  title: request.title,
  departmentId: request.departmentId,
  positionId: request.positionId ?? null,
  jobId: request.jobId ?? null,
  locationId: request.locationId ?? null,
  positions: request.positions,
  skills: request.skills,
  minExperienceYears: request.minExperienceYears ?? null,
  maxExperienceYears: request.maxExperienceYears ?? null,
  employmentTypeCode: request.employmentTypeCode,
  workModeCode: request.workModeCode,
  salaryMin: request.salaryMin ?? null,
  salaryMax: request.salaryMax ?? null,
  budget: request.budget ?? null,
  priority: request.priority,
  requiredBy: request.requiredBy ?? null,
  reason: request.reason,
  replacementForEmployeeId: request.replacementForEmployeeId ?? null,
  justification: request.justification ?? null,
});

/** Raising or editing a manpower request (spec §11 fields). */
@Component({
  selector: 'gggs-manpower-request-form',
  imports: [FormsModule, FormDialog, OrgSelect, LookupSelect, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog
      [(visible)]="visible"
      [title]="request() ? 'Edit ' + request()?.number : 'Raise a manpower request'"
      [saveLabel]="request() ? 'Save' : submit() ? 'Raise and submit' : 'Save draft'"
      [busy]="busy()"
      [error]="error()"
      [saveDisabled]="!form().title.trim() || !form().departmentId"
      (save)="save()"
      width="min(52rem, 96vw)"
    >
      <div class="wfp__grid">
        <label class="field wfp__wide">
          <span class="field__label">Role title</span>
          <input type="text" maxlength="200" [value]="form().title" (input)="patch({ title: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Department</span>
          <gggs-org-select kind="departments" placeholder="Choose" ariaLabel="Department" [ngModel]="form().departmentId || null" (ngModelChange)="patch({ departmentId: $event ?? '' })" />
        </label>
        <label class="field">
          <span class="field__label">Job</span>
          <gggs-org-select kind="jobs" placeholder="Any" ariaLabel="Job" [ngModel]="form().jobId" (ngModelChange)="patch({ jobId: $event })" />
        </label>
        <label class="field">
          <span class="field__label">Location</span>
          <gggs-org-select kind="locations" placeholder="Any" ariaLabel="Location" [ngModel]="form().locationId" (ngModelChange)="patch({ locationId: $event })" />
        </label>
        <label class="field">
          <span class="field__label">Positions</span>
          <input type="number" min="1" max="500" [value]="form().positions" (input)="patch({ positions: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Employment type</span>
          <gggs-lookup-select [list]="lists.employmentType" ariaLabel="Employment type" [ngModel]="form().employmentTypeCode" (ngModelChange)="patch({ employmentTypeCode: $event ?? '' })" />
        </label>
        <label class="field">
          <span class="field__label">Work mode</span>
          <gggs-lookup-select [list]="lists.workMode" ariaLabel="Work mode" [ngModel]="form().workModeCode" (ngModelChange)="patch({ workModeCode: $event ?? '' })" />
        </label>
        <label class="field">
          <span class="field__label">Experience from (years)</span>
          <input type="number" min="0" step="0.5" [value]="form().minExperienceYears ?? ''" (input)="patch({ minExperienceYears: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">to (years)</span>
          <input type="number" min="0" step="0.5" [value]="form().maxExperienceYears ?? ''" (input)="patch({ maxExperienceYears: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Salary from (annual)</span>
          <input type="number" min="0" [value]="form().salaryMin ?? ''" (input)="patch({ salaryMin: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">to (annual)</span>
          <input type="number" min="0" [value]="form().salaryMax ?? ''" (input)="patch({ salaryMax: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Budget</span>
          <input type="number" min="0" [value]="form().budget ?? ''" (input)="patch({ budget: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Needed by</span>
          <input type="date" [value]="form().requiredBy ?? ''" (change)="patch({ requiredBy: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Priority</span>
          <select [value]="form().priority" (change)="patch({ priority: $any($event.target).value })">
            @for (option of priorities; track option) {
              <option [value]="option">{{ option }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Reason</span>
          <select [value]="form().reason" (change)="patch({ reason: $any($event.target).value })">
            @for (option of reasons; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
        @if (form().reason === 'Replacement') {
          <label class="field">
            <span class="field__label">Replacing</span>
            <gggs-employee-select ariaLabel="Replacing" [ngModel]="form().replacementForEmployeeId" (ngModelChange)="patch({ replacementForEmployeeId: $event })" />
          </label>
        }
        <label class="field wfp__wide">
          <span class="field__label">Skills (comma-separated)</span>
          <input type="text" [value]="form().skills.join(', ')" (change)="patch({ skills: split($any($event.target).value) })" />
        </label>
        <label class="field wfp__wide">
          <span class="field__label">Justification</span>
          <textarea rows="3" maxlength="2000" [value]="form().justification ?? ''" (input)="patch({ justification: $any($event.target).value || null })"></textarea>
        </label>
      </div>
      @if (!request()) {
        <label class="wfp__flag">
          <input type="checkbox" [checked]="submit()" (change)="submit.set($any($event.target).checked)" />
          <span>Submit for approval now</span>
        </label>
      }
    </gggs-form-dialog>
  `,
  styleUrl: './workforce.css',
})
export class ManpowerRequestForm {
  private readonly api = inject(WorkforceApi);

  readonly visible = model(false);
  readonly request = input<ManpowerRequestItem | null>(null);
  readonly saved = output<ManpowerRequestItem>();

  protected readonly lists = LOOKUP_LISTS;
  protected readonly priorities = MANPOWER_PRIORITIES;
  protected readonly reasons = MANPOWER_REASONS;
  protected readonly humanize = humanize;
  protected readonly form = signal<ManpowerRequestInput>(blank());
  protected readonly submit = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      if (this.visible()) {
        const request = this.request();
        this.form.set(request ? fromItem(request) : blank());
        this.error.set(null);
      }
    });
  }

  protected num(value: string): number | null {
    return value === '' ? null : +value;
  }

  protected split(value: string): string[] {
    return value
      .split(',')
      .map((part) => part.trim())
      .filter(Boolean);
  }

  protected patch(change: Partial<ManpowerRequestInput>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const request = this.request();
      const result = request ? await this.api.updateRequest(request.id, this.form()) : await this.api.raise(this.form(), this.submit());
      this.visible.set(false);
      this.saved.emit(result);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }
}
