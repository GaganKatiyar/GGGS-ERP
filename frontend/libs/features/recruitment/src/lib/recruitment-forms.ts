import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { RecruitmentApi } from '@gggs/shared/hiring';
import { LookupSelect } from '@gggs/shared/master-data';
import type { AgencyItem, CandidateDetailsInput, CandidateProfile, CandidateSource, CandidateView, RequisitionInput, RequisitionItem } from '@gggs/shared/models';
import { CANDIDATE_SOURCES, LOOKUP_LISTS, humanize } from '@gggs/shared/models';
import { OrgSelect } from '@gggs/shared/org-reference';
import { FormDialog } from '@gggs/shared/ui';

export const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

export const splitList = (value: string): string[] =>
  value
    .split(',')
    .map((part) => part.trim())
    .filter(Boolean);

const num = (value: string): number | null => (value === '' ? null : +value);

const blankRequisition = (): RequisitionInput => ({
  title: '',
  departmentId: '',
  positionId: null,
  jobId: null,
  locationId: null,
  openings: 1,
  employmentTypeCode: 'FULL_TIME',
  workModeCode: 'HYBRID',
  minExperienceYears: null,
  maxExperienceYears: null,
  salaryMin: null,
  salaryMax: null,
  skills: [],
  description: null,
  hiringManagerEmployeeId: null,
  recruiterEmployeeId: null,
  priority: 'Medium',
  targetDate: null,
});

const requisitionInput = (item: RequisitionItem): RequisitionInput => ({
  title: item.title,
  departmentId: item.departmentId,
  positionId: item.positionId ?? null,
  jobId: item.jobId ?? null,
  locationId: item.locationId ?? null,
  openings: item.openings,
  employmentTypeCode: item.employmentTypeCode,
  workModeCode: item.workModeCode,
  minExperienceYears: item.minExperienceYears ?? null,
  maxExperienceYears: item.maxExperienceYears ?? null,
  salaryMin: item.salaryMin ?? null,
  salaryMax: item.salaryMax ?? null,
  skills: item.skills,
  description: item.description ?? null,
  hiringManagerEmployeeId: item.hiringManagerEmployeeId ?? null,
  recruiterEmployeeId: item.recruiterEmployeeId ?? null,
  priority: item.priority ?? null,
  targetDate: item.targetDate ?? null,
});

export const blankCandidate = (): CandidateDetailsInput => ({
  firstName: '',
  lastName: null,
  email: '',
  mobile: null,
  currentCompany: null,
  currentTitle: null,
  experienceYears: null,
  currentCtc: null,
  expectedCtc: null,
  noticePeriodDays: null,
  location: null,
  skills: [],
  linkedInUrl: null,
  notes: null,
});

const candidateInput = (candidate: CandidateProfile): CandidateDetailsInput => ({
  firstName: candidate.firstName,
  lastName: candidate.lastName ?? null,
  email: candidate.email,
  mobile: candidate.mobile ?? null,
  currentCompany: candidate.currentCompany ?? null,
  currentTitle: candidate.currentTitle ?? null,
  experienceYears: candidate.experienceYears ?? null,
  currentCtc: candidate.currentCtc ?? null,
  expectedCtc: candidate.expectedCtc ?? null,
  noticePeriodDays: candidate.noticePeriodDays ?? null,
  location: candidate.location ?? null,
  skills: candidate.skills,
  linkedInUrl: candidate.linkedInUrl ?? null,
  notes: candidate.notes ?? null,
});

/** Opening or editing a requisition. */
@Component({
  selector: 'gggs-requisition-form',
  imports: [FormsModule, FormDialog, OrgSelect, LookupSelect, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog
      [(visible)]="visible"
      [title]="requisition() ? 'Edit ' + requisition()?.number : 'Open a requisition'"
      [saveLabel]="requisition() ? 'Save' : 'Open'"
      [busy]="busy()"
      [error]="error()"
      [saveDisabled]="!form().title.trim() || !form().departmentId"
      (save)="save()"
      size="lg"
    >
      <div class="rec__grid">
        <label class="field rec__wide">
          <span class="field__label">Title</span>
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
          <span class="field__label">Position</span>
          <gggs-org-select kind="positions" placeholder="None" ariaLabel="Position" [ngModel]="form().positionId" (ngModelChange)="patch({ positionId: $event })" />
        </label>
        <label class="field">
          <span class="field__label">Location</span>
          <gggs-org-select kind="locations" placeholder="Any" ariaLabel="Location" [ngModel]="form().locationId" (ngModelChange)="patch({ locationId: $event })" />
        </label>
        <label class="field">
          <span class="field__label">Openings</span>
          <input type="number" min="1" max="500" [value]="form().openings" (input)="patch({ openings: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Target date</span>
          <input type="date" [value]="form().targetDate ?? ''" (change)="patch({ targetDate: $any($event.target).value || null })" />
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
          <span class="field__label">CTC from</span>
          <input type="number" min="0" [value]="form().salaryMin ?? ''" (input)="patch({ salaryMin: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">CTC to</span>
          <input type="number" min="0" [value]="form().salaryMax ?? ''" (input)="patch({ salaryMax: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Hiring manager</span>
          <gggs-employee-select ariaLabel="Hiring manager" [ngModel]="form().hiringManagerEmployeeId" (ngModelChange)="patch({ hiringManagerEmployeeId: $event })" />
        </label>
        <label class="field">
          <span class="field__label">Recruiter</span>
          <gggs-employee-select ariaLabel="Recruiter" [ngModel]="form().recruiterEmployeeId" (ngModelChange)="patch({ recruiterEmployeeId: $event })" />
        </label>
        <label class="field">
          <span class="field__label">Priority</span>
          <select [value]="form().priority ?? ''" (change)="patch({ priority: $any($event.target).value || null })">
            <option value="Low">Low</option>
            <option value="Medium">Medium</option>
            <option value="High">High</option>
            <option value="Critical">Critical</option>
          </select>
        </label>
        <label class="field rec__wide">
          <span class="field__label">Skills (comma-separated; used for matching)</span>
          <input type="text" [value]="form().skills.join(', ')" (change)="patch({ skills: split($any($event.target).value) })" />
        </label>
        <label class="field rec__wide">
          <span class="field__label">Description</span>
          <textarea rows="4" maxlength="8000" [value]="form().description ?? ''" (input)="patch({ description: $any($event.target).value || null })"></textarea>
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styleUrl: './recruitment.css',
})
export class RequisitionForm {
  private readonly api = inject(RecruitmentApi);

  readonly visible = model(false);
  readonly requisition = input<RequisitionItem | null>(null);
  readonly saved = output<RequisitionItem>();

  protected readonly lists = LOOKUP_LISTS;
  protected readonly num = num;
  protected readonly split = splitList;
  protected readonly form = signal<RequisitionInput>(blankRequisition());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      if (this.visible()) {
        const requisition = this.requisition();
        this.form.set(requisition ? requisitionInput(requisition) : blankRequisition());
        this.error.set(null);
      }
    });
  }

  protected patch(change: Partial<RequisitionInput>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const saved = await this.api.saveRequisition(this.requisition()?.id ?? null, this.form());
      this.visible.set(false);
      this.saved.emit(saved);
    } catch (error) {
      this.error.set(errorText(error, 'The requisition could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** The candidate profile fields, shared by the recruiter's form and an employee's referral. */
@Component({
  selector: 'gggs-candidate-fields',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="rec__grid">
      <label class="field">
        <span class="field__label">First name</span>
        <input type="text" maxlength="100" [value]="value().firstName" (input)="patch({ firstName: $any($event.target).value })" />
      </label>
      <label class="field">
        <span class="field__label">Last name</span>
        <input type="text" maxlength="100" [value]="value().lastName ?? ''" (input)="patch({ lastName: $any($event.target).value || null })" />
      </label>
      <label class="field">
        <span class="field__label">Email</span>
        <input type="email" maxlength="256" [value]="value().email" (input)="patch({ email: $any($event.target).value })" />
      </label>
      <label class="field">
        <span class="field__label">Mobile</span>
        <input type="tel" maxlength="20" [value]="value().mobile ?? ''" (input)="patch({ mobile: $any($event.target).value || null })" />
      </label>
      <label class="field">
        <span class="field__label">Current company</span>
        <input type="text" maxlength="200" [value]="value().currentCompany ?? ''" (input)="patch({ currentCompany: $any($event.target).value || null })" />
      </label>
      <label class="field">
        <span class="field__label">Current title</span>
        <input type="text" maxlength="200" [value]="value().currentTitle ?? ''" (input)="patch({ currentTitle: $any($event.target).value || null })" />
      </label>
      <label class="field">
        <span class="field__label">Experience (years)</span>
        <input type="number" min="0" step="0.5" [value]="value().experienceYears ?? ''" (input)="patch({ experienceYears: num($any($event.target).value) })" />
      </label>
      <label class="field">
        <span class="field__label">Notice period (days)</span>
        <input type="number" min="0" [value]="value().noticePeriodDays ?? ''" (input)="patch({ noticePeriodDays: num($any($event.target).value) })" />
      </label>
      @if (showCtc()) {
        <label class="field">
          <span class="field__label">Current CTC</span>
          <input type="number" min="0" [value]="value().currentCtc ?? ''" (input)="patch({ currentCtc: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Expected CTC</span>
          <input type="number" min="0" [value]="value().expectedCtc ?? ''" (input)="patch({ expectedCtc: num($any($event.target).value) })" />
        </label>
      }
      <label class="field">
        <span class="field__label">Location</span>
        <input type="text" maxlength="200" [value]="value().location ?? ''" (input)="patch({ location: $any($event.target).value || null })" />
      </label>
      <label class="field">
        <span class="field__label">LinkedIn</span>
        <input type="url" maxlength="500" [value]="value().linkedInUrl ?? ''" (input)="patch({ linkedInUrl: $any($event.target).value || null })" />
      </label>
      <label class="field rec__wide">
        <span class="field__label">Skills (comma-separated)</span>
        <input type="text" [value]="value().skills.join(', ')" (change)="patch({ skills: split($any($event.target).value) })" />
      </label>
    </div>
  `,
  styleUrl: './recruitment.css',
})
export class CandidateFields {
  readonly value = model.required<CandidateDetailsInput>();
  readonly showCtc = input(true);

  protected readonly num = num;
  protected readonly split = splitList;

  protected patch(change: Partial<CandidateDetailsInput>): void {
    this.value.update((value) => ({ ...value, ...change }));
  }
}

/** Adding a candidate (found again by email or mobile) or editing one. */
@Component({
  selector: 'gggs-candidate-form',
  imports: [FormsModule, FormDialog, EmployeeSelect, CandidateFields],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog
      [(visible)]="visible"
      [title]="candidate() ? 'Edit ' + candidate()?.fullName : 'Add a candidate'"
      [saveLabel]="candidate() ? 'Save' : requisitionId() ? 'Add to pipeline' : 'Add'"
      [busy]="busy()"
      [error]="error()"
      [saveDisabled]="!details().firstName.trim() || !details().email.trim() || (source() === 'Agency' && !agencyId()) || (source() === 'Referral' && !referrerId())"
      (save)="save()"
      size="lg"
    >
      <gggs-candidate-fields [(value)]="details" />
      @if (!candidate()) {
        <div class="rec__grid">
          <label class="field">
            <span class="field__label">Source</span>
            <select [value]="source()" (change)="source.set($any($event.target).value)">
              @for (option of sources; track option) {
                <option [value]="option">{{ humanize(option) }}</option>
              }
            </select>
          </label>
          @if (source() === 'Agency') {
            <label class="field">
              <span class="field__label">Agency</span>
              <select [value]="agencyId()" (change)="agencyId.set($any($event.target).value)">
                <option value="">Choose</option>
                @for (agency of agencies(); track agency.id) {
                  @if (agency.isActive) {
                    <option [value]="agency.id">{{ agency.name }}</option>
                  }
                }
              </select>
            </label>
          }
          @if (source() === 'Referral') {
            <label class="field">
              <span class="field__label">Referred by</span>
              <gggs-employee-select ariaLabel="Referred by" [ngModel]="referrerId()" (ngModelChange)="referrerId.set($event)" />
            </label>
          }
        </div>
      }
      <label class="field">
        <span class="field__label">Notes</span>
        <textarea rows="2" maxlength="4000" [value]="details().notes ?? ''" (input)="patchNotes($any($event.target).value)"></textarea>
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './recruitment.css',
})
export class CandidateForm {
  private readonly api = inject(RecruitmentApi);

  readonly visible = model(false);
  readonly candidate = input<CandidateProfile | null>(null);
  /** When set, a new candidate is applied to this requisition. */
  readonly requisitionId = input<string | null>(null);
  readonly saved = output<CandidateView>();

  protected readonly sources = CANDIDATE_SOURCES;
  protected readonly humanize = humanize;
  protected readonly details = signal<CandidateDetailsInput>(blankCandidate());
  protected readonly source = signal<CandidateSource>('Direct');
  protected readonly agencyId = signal('');
  protected readonly referrerId = signal<string | null>(null);
  protected readonly agencies = signal<readonly AgencyItem[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      if (this.visible()) {
        const candidate = this.candidate();
        this.details.set(candidate ? candidateInput(candidate) : blankCandidate());
        this.source.set('Direct');
        this.error.set(null);
        if (!candidate && !this.agencies().length) {
          this.api.agencies().then(
            (agencies) => this.agencies.set(agencies),
            () => undefined,
          );
        }
      }
    });
  }

  protected patchNotes(notes: string): void {
    this.details.update((details) => ({ ...details, notes: notes || null }));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const candidate = this.candidate();
      const view = candidate
        ? await this.api.updateCandidate(candidate.id, this.details())
        : await this.api.addCandidate({
            details: this.details(),
            source: this.source(),
            agencyId: this.source() === 'Agency' ? this.agencyId() || null : null,
            referrerEmployeeId: this.source() === 'Referral' ? this.referrerId() : null,
            requisitionId: this.requisitionId(),
          });
      this.visible.set(false);
      this.saved.emit(view);
    } catch (error) {
      this.error.set(errorText(error, 'The candidate could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}
