import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { RecruitmentApi } from '@gggs/shared/hiring';
import { LookupSelect } from '@gggs/shared/master-data';
import type { AgencyItem, CandidateDetailsInput, CandidateProfile, CandidateSource, CandidateView, CvFields, CvReadResult, OrgUnit, RequisitionInput, RequisitionItem } from '@gggs/shared/models';
import { CANDIDATE_SOURCES, LOOKUP_LISTS, humanize } from '@gggs/shared/models';
import { OrgSelect, OrgStructureApi, rolesFor } from '@gggs/shared/org-reference';
import { FormDialog } from '@gggs/shared/ui';

export const errorText = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

export const splitList = (value: string): string[] =>
  value
    .split(',')
    .map((part) => part.trim())
    .filter(Boolean);

const num = (value: string): number | null => (value === '' ? null : +value);

/** The candidate fields a CV can fill. */
export type CvField = Exclude<keyof CvFields, never>;

const isEmpty = (value: unknown): boolean => value === null || value === undefined || value === '' || (Array.isArray(value) && value.length === 0);

/** Whether a field still holds what the CV gave (skills compared as a list). */
export const sameValue = (a: unknown, b: unknown): boolean =>
  Array.isArray(a) && Array.isArray(b) ? a.length === b.length && a.every((item, index) => item === b[index]) : a === b;

/** What a CV adds to the form: its values for the fields that are still empty, and nothing else. */
export function cvFill(fields: CvFields, current: CandidateDetailsInput): Partial<Record<CvField, unknown>> & Partial<CandidateDetailsInput> {
  const fill: Record<string, unknown> = {};
  const now = current as unknown as Record<string, unknown>;
  for (const [key, value] of Object.entries(fields)) {
    if (!isEmpty(value) && isEmpty(now[key])) {
      fill[key] = value;
    }
  }
  return fill as Partial<CandidateDetailsInput>;
}

const blankRequisition = (): RequisitionInput => ({
  title: '',
  departmentId: '',
  roleId: null,
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
  roleId: item.roleId ?? null,
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

/**
 * Opening or editing a requirement (a requisition). Only what the app keeps for one, in four
 * steps: what the job is, where and when, who fits, and who runs the hiring. Picking a role
 * fills an empty title with its name; skills are typed as tags and drive the match score.
 */
@Component({
  selector: 'gggs-requisition-form',
  imports: [FormsModule, FormDialog, OrgSelect, LookupSelect, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog
      [(visible)]="visible"
      [title]="requisition() ? 'Edit ' + requisition()?.number : 'New requirement'"
      [description]="requisition() ? '' : 'Approved manpower requests become requirements on their own. Open one here when hiring starts without a request.'"
      [saveLabel]="requisition() ? 'Save changes' : 'Create requirement'"
      [busy]="busy()"
      [error]="error()"
      [saveDisabled]="!form().title.trim() || !form().departmentId || form().openings < 1"
      (save)="save()"
      size="lg"
    >
      <section class="rqf__section">
        <header class="rqf__head"><span class="rqf__step">1</span><div><h3>The job</h3><p>What you are hiring for, and how many.</p></div></header>
        <div class="rqf__grid">
          <label class="field">
            <span class="field__label">Department or function *</span>
            <gggs-org-select kind="departments" placeholder="Choose where the hire will sit" ariaLabel="Department" [ngModel]="form().departmentId || null" (ngModelChange)="chooseDepartment($event)" />
          </label>
          <label class="field">
            <span class="field__label">Role</span>
            <gggs-org-select kind="roles" placeholder="Choose a role of the department" ariaLabel="Role" [where]="roleFilter()" [ngModel]="form().roleId" (ngModelChange)="chooseRole($event)" />
          </label>
          <label class="field rqf__wide">
            <span class="field__label">Title *</span>
            <input type="text" maxlength="200" placeholder="e.g. Senior .NET Engineer" [value]="form().title" (input)="patch({ title: $any($event.target).value })" />
            <span class="field__hint">Shown on the job posting. Picking a role fills it in if it is empty.</span>
          </label>
          <label class="field">
            <span class="field__label">Openings *</span>
            <input type="number" min="1" max="500" [value]="form().openings" (input)="patch({ openings: +$any($event.target).value })" />
          </label>
          <div class="field">
            <span class="field__label" id="rqf-priority">Priority</span>
            <div class="rqf__choice" role="radiogroup" aria-labelledby="rqf-priority">
              @for (option of priorities; track option) {
                <button type="button" role="radio" [attr.aria-checked]="form().priority === option" [class]="'rqf__opt rqf__opt--' + option.toLowerCase()" (click)="patch({ priority: option })">{{ option }}</button>
              }
            </div>
          </div>
        </div>
      </section>

      <section class="rqf__section">
        <header class="rqf__head"><span class="rqf__step">2</span><div><h3>Where and when</h3><p>Where they will work and when you need them.</p></div></header>
        <div class="rqf__grid">
          <label class="field">
            <span class="field__label">Location</span>
            <gggs-org-select kind="locations" placeholder="Any office" ariaLabel="Location" [ngModel]="form().locationId" (ngModelChange)="patch({ locationId: $event })" />
          </label>
          <label class="field">
            <span class="field__label">Needed by</span>
            <input type="date" [value]="form().targetDate ?? ''" (change)="patch({ targetDate: $any($event.target).value || null })" />
          </label>
          <label class="field">
            <span class="field__label">Work mode</span>
            <gggs-lookup-select [list]="lists.workMode" ariaLabel="Work mode" [ngModel]="form().workModeCode" (ngModelChange)="patch({ workModeCode: $event ?? '' })" />
          </label>
          <label class="field">
            <span class="field__label">Employment type</span>
            <gggs-lookup-select [list]="lists.employmentType" ariaLabel="Employment type" [ngModel]="form().employmentTypeCode" (ngModelChange)="patch({ employmentTypeCode: $event ?? '' })" />
          </label>
        </div>
      </section>

      <section class="rqf__section">
        <header class="rqf__head"><span class="rqf__step">3</span><div><h3>Who fits</h3><p>Used to score each candidate's match.</p></div></header>
        <div class="rqf__grid">
          <div class="field">
            <span class="field__label">Experience (years)</span>
            <div class="rqf__range">
              <input type="number" min="0" step="0.5" placeholder="From" aria-label="Experience from" [value]="form().minExperienceYears ?? ''" (input)="patch({ minExperienceYears: num($any($event.target).value) })" />
              <span aria-hidden="true">to</span>
              <input type="number" min="0" step="0.5" placeholder="To" aria-label="Experience to" [value]="form().maxExperienceYears ?? ''" (input)="patch({ maxExperienceYears: num($any($event.target).value) })" />
            </div>
          </div>
          <div class="field">
            <span class="field__label">Salary budget (₹ a year)</span>
            <div class="rqf__range">
              <input type="number" min="0" step="50000" placeholder="From" aria-label="Salary from" [value]="form().salaryMin ?? ''" (input)="patch({ salaryMin: num($any($event.target).value) })" />
              <span aria-hidden="true">to</span>
              <input type="number" min="0" step="50000" placeholder="To" aria-label="Salary to" [value]="form().salaryMax ?? ''" (input)="patch({ salaryMax: num($any($event.target).value) })" />
            </div>
            @if (budgetHint(); as hint) {
              <span class="field__hint">{{ hint }}</span>
            }
          </div>
          <div class="field rqf__wide">
            <span class="field__label" id="rqf-skills">Skills</span>
            <div class="rqf__tags">
              @for (skill of form().skills; track skill) {
                <span class="rqf__tag">{{ skill }}<button type="button" [attr.aria-label]="'Remove ' + skill" (click)="removeSkill(skill)">×</button></span>
              }
              <input
                #skillInput
                type="text"
                aria-labelledby="rqf-skills"
                [placeholder]="form().skills.length ? 'Add another' : 'Type a skill and press Enter'"
                (keydown)="skillKey($event, skillInput)"
                (blur)="addSkill(skillInput)"
              />
            </div>
          </div>
        </div>
      </section>

      <section class="rqf__section">
        <header class="rqf__head"><span class="rqf__step">4</span><div><h3>Who runs it</h3><p>They are told about new candidates and decisions.</p></div></header>
        <div class="rqf__grid">
          <label class="field">
            <span class="field__label">Hiring manager</span>
            <gggs-employee-select ariaLabel="Hiring manager" [ngModel]="form().hiringManagerEmployeeId" (ngModelChange)="patch({ hiringManagerEmployeeId: $event })" />
          </label>
          <label class="field">
            <span class="field__label">Recruiter</span>
            <gggs-employee-select ariaLabel="Recruiter" [ngModel]="form().recruiterEmployeeId" (ngModelChange)="patch({ recruiterEmployeeId: $event })" />
          </label>
          <label class="field rqf__wide">
            <span class="field__label">About the job</span>
            <textarea rows="4" maxlength="8000" placeholder="What the person will do and what good looks like. Used for the job posting." [value]="form().description ?? ''" (input)="patch({ description: $any($event.target).value || null })"></textarea>
          </label>
        </div>
      </section>
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

  private readonly org = inject(OrgStructureApi);
  private readonly departments = signal<readonly OrgUnit[]>([]);
  private roles: readonly OrgUnit[] = [];
  /** The chosen department's roles (a function shares its department's). */
  protected readonly roleFilter = computed(() => rolesFor(this.form().departmentId, this.departments()));

  constructor() {
    effect(() => {
      if (this.visible()) {
        const requisition = this.requisition();
        this.form.set(requisition ? requisitionInput(requisition) : blankRequisition());
        this.error.set(null);
        void this.loadReferences();
      }
    });
  }

  private async loadReferences(): Promise<void> {
    if (this.departments().length) {
      return;
    }
    const [departments, roles] = await Promise.all([this.org.active('departments'), this.org.active('roles')]);
    this.roles = roles;
    this.departments.set(departments);
  }

  /** A role that is not one of the new department's is cleared. */
  protected chooseDepartment(departmentId: string | null): void {
    const fits = rolesFor(departmentId, this.departments());
    const role = this.roles.find((row) => row.id === this.form().roleId);
    this.patch({ departmentId: departmentId ?? '', ...(role && fits && !fits(role) ? { roleId: null } : {}) });
  }

  /** A role fills an empty title with its own name. */
  protected chooseRole(roleId: string | null): void {
    const role = this.roles.find((row) => row.id === roleId);
    this.patch({ roleId, ...(role && !this.form().title.trim() ? { title: role.name } : {}) });
  }

  protected readonly priorities = ['Low', 'Medium', 'High', 'Critical'];

  /** "₹18 L to ₹28 L a year", so a missing zero is easy to spot. */
  protected readonly budgetHint = computed(() => {
    const { salaryMin, salaryMax } = this.form();
    const lakh = (n: number) => `₹${(n / 100_000).toLocaleString('en-IN', { maximumFractionDigits: 1 })} L`;
    if (salaryMin == null && salaryMax == null) {
      return null;
    }
    return `${salaryMin != null ? lakh(salaryMin) : '…'} to ${salaryMax != null ? lakh(salaryMax) : '…'} a year`;
  });

  protected skillKey(event: KeyboardEvent, input: HTMLInputElement): void {
    if (event.key === 'Enter' || event.key === ',') {
      event.preventDefault();
      this.addSkill(input);
    } else if (event.key === 'Backspace' && !input.value) {
      const skills = this.form().skills;
      const last = skills[skills.length - 1];
      if (last) {
        this.removeSkill(last);
      }
    }
  }

  protected addSkill(input: HTMLInputElement): void {
    const added = splitList(input.value).filter((skill) => !this.form().skills.some((s) => s.toLowerCase() === skill.toLowerCase()));
    input.value = '';
    if (added.length) {
      this.patch({ skills: [...this.form().skills, ...added] });
    }
  }

  protected removeSkill(skill: string): void {
    this.patch({ skills: this.form().skills.filter((s) => s !== skill) });
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
      this.error.set(errorText(error, 'The requirement could not be saved.'));
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
        <span class="field__label">First name @if (marked().has('firstName')) {<span class="rec__cv-tag">from CV</span>}</span>
        <input type="text" maxlength="100" [class.rec__cv-filled]="marked().has('firstName')" [value]="value().firstName" (input)="patch({ firstName: $any($event.target).value })" />
      </label>
      <label class="field">
        <span class="field__label">Last name @if (marked().has('lastName')) {<span class="rec__cv-tag">from CV</span>}</span>
        <input type="text" maxlength="100" [class.rec__cv-filled]="marked().has('lastName')" [value]="value().lastName ?? ''" (input)="patch({ lastName: $any($event.target).value || null })" />
      </label>
      <label class="field">
        <span class="field__label">Email @if (marked().has('email')) {<span class="rec__cv-tag">from CV</span>}</span>
        <input type="email" maxlength="256" [class.rec__cv-filled]="marked().has('email')" [value]="value().email" (input)="patch({ email: $any($event.target).value })" />
      </label>
      <label class="field">
        <span class="field__label">Mobile @if (marked().has('mobile')) {<span class="rec__cv-tag">from CV</span>}</span>
        <input type="tel" maxlength="20" [class.rec__cv-filled]="marked().has('mobile')" [value]="value().mobile ?? ''" (input)="patch({ mobile: $any($event.target).value || null })" />
      </label>
      <label class="field">
        <span class="field__label">Current company @if (marked().has('currentCompany')) {<span class="rec__cv-tag">from CV</span>}</span>
        <input type="text" maxlength="200" [class.rec__cv-filled]="marked().has('currentCompany')" [value]="value().currentCompany ?? ''" (input)="patch({ currentCompany: $any($event.target).value || null })" />
      </label>
      <label class="field">
        <span class="field__label">Current title @if (marked().has('currentTitle')) {<span class="rec__cv-tag">from CV</span>}</span>
        <input type="text" maxlength="200" [class.rec__cv-filled]="marked().has('currentTitle')" [value]="value().currentTitle ?? ''" (input)="patch({ currentTitle: $any($event.target).value || null })" />
      </label>
      <label class="field">
        <span class="field__label">Experience (years) @if (marked().has('experienceYears')) {<span class="rec__cv-tag">from CV</span>}</span>
        <input type="number" min="0" step="0.5" [class.rec__cv-filled]="marked().has('experienceYears')" [value]="value().experienceYears ?? ''" (input)="patch({ experienceYears: num($any($event.target).value) })" />
      </label>
      <label class="field">
        <span class="field__label">Notice period (days) @if (marked().has('noticePeriodDays')) {<span class="rec__cv-tag">from CV</span>}</span>
        <input type="number" min="0" [class.rec__cv-filled]="marked().has('noticePeriodDays')" [value]="value().noticePeriodDays ?? ''" (input)="patch({ noticePeriodDays: num($any($event.target).value) })" />
      </label>
      @if (showCtc()) {
        <label class="field">
          <span class="field__label">Current CTC @if (marked().has('currentCtc')) {<span class="rec__cv-tag">from CV</span>}</span>
          <input type="number" min="0" [class.rec__cv-filled]="marked().has('currentCtc')" [value]="value().currentCtc ?? ''" (input)="patch({ currentCtc: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Expected CTC @if (marked().has('expectedCtc')) {<span class="rec__cv-tag">from CV</span>}</span>
          <input type="number" min="0" [class.rec__cv-filled]="marked().has('expectedCtc')" [value]="value().expectedCtc ?? ''" (input)="patch({ expectedCtc: num($any($event.target).value) })" />
        </label>
      }
      <label class="field">
        <span class="field__label">Location @if (marked().has('location')) {<span class="rec__cv-tag">from CV</span>}</span>
        <input type="text" maxlength="200" [class.rec__cv-filled]="marked().has('location')" [value]="value().location ?? ''" (input)="patch({ location: $any($event.target).value || null })" />
      </label>
      <label class="field">
        <span class="field__label">LinkedIn @if (marked().has('linkedInUrl')) {<span class="rec__cv-tag">from CV</span>}</span>
        <input type="url" maxlength="500" [class.rec__cv-filled]="marked().has('linkedInUrl')" [value]="value().linkedInUrl ?? ''" (input)="patch({ linkedInUrl: $any($event.target).value || null })" />
      </label>
      <label class="field rec__wide">
        <span class="field__label">Skills (comma-separated) @if (marked().has('skills')) {<span class="rec__cv-tag">from CV</span>}</span>
        <input type="text" [class.rec__cv-filled]="marked().has('skills')" [value]="value().skills.join(', ')" (change)="patch({ skills: split($any($event.target).value) })" />
      </label>
    </div>
  `,
  styleUrls: ['./recruitment.css', './cv.css'],
})
export class CandidateFields {
  readonly value = model.required<CandidateDetailsInput>();
  readonly showCtc = input(true);
  /** Fields still holding what a CV gave: shown in blue with a "from CV" tag until someone changes them. */
  readonly marked = input<ReadonlySet<string>>(new Set());

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
      <div
        class="rec__cv"
        [class.rec__cv--done]="cv()"
        [class.rec__cv--over]="dragOver()"
        (dragover)="$event.preventDefault(); dragOver.set(true)"
        (dragleave)="dragOver.set(false)"
        (drop)="drop($event)"
      >
        <i class="pi pi-file-arrow-up rec__cv-icon" aria-hidden="true"></i>
        <div class="rec__cv-text" aria-live="polite">
          @if (reading()) {
            <strong>Reading {{ reading() }}…</strong>
            <span>Taking the text out of the file.</span>
          } @else if (cv(); as read) {
            <strong>{{ read.fileName }}</strong>
            <span>{{ cvSummary() }}</span>
          } @else {
            <strong>Fill from a CV</strong>
            <span>Drop a PDF or Word (.docx) CV here, or choose one. Empty fields fill in from it; check them before saving.</span>
          }
        </div>
        <label class="btn btn--sm" for="candidate-cv-file">{{ cv() ? 'Choose another' : 'Choose CV' }}</label>
        <input id="candidate-cv-file" class="sr-only" type="file" accept=".pdf,.docx,.txt" [disabled]="!!reading()" (change)="choose($event)" />
        @if (cv()) {
          <button type="button" class="btn btn--link btn--sm" (click)="removeCv()">Remove</button>
        }
      </div>
      @if (cvError()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ cvError() }}</span></p>
      }
      <gggs-candidate-fields [(value)]="details" [marked]="marked()" />
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
  styleUrls: ['./recruitment.css', './cv.css'],
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

  /** The CV read on this form, the values it put in, and how many empty fields it filled. */
  protected readonly cv = signal<CvReadResult | null>(null);
  protected readonly cvValues = signal<Partial<Record<CvField, unknown>>>({});
  protected readonly cvFilled = signal(0);
  protected readonly reading = signal<string | null>(null);
  protected readonly cvError = signal<string | null>(null);
  protected readonly dragOver = signal(false);

  /** Fields still holding the CV's value; editing one takes away its "from CV" mark. */
  protected readonly marked = computed<ReadonlySet<string>>(() => {
    const details = this.details() as unknown as Record<string, unknown>;
    const values = this.cvValues();
    return new Set((Object.keys(values) as CvField[]).filter((key) => sameValue(values[key], details[key])));
  });

  protected readonly cvSummary = computed(() => {
    const read = this.cv();
    if (!read) {
      return '';
    }
    if (!read.hasText) {
      return 'No text could be read; it may be a scanned image. The CV is attached; fill the fields by hand.';
    }
    const filled = this.cvFilled();
    return filled
      ? `Filled ${filled} ${filled === 1 ? 'field' : 'fields'} from the CV and attached it. Check the blue fields before saving.`
      : 'Attached. The fields already had values, so nothing was changed.';
  });

  constructor() {
    effect(() => {
      if (this.visible()) {
        const candidate = this.candidate();
        this.details.set(candidate ? candidateInput(candidate) : blankCandidate());
        this.source.set('Direct');
        this.error.set(null);
        untracked(() => this.removeCv());
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

  protected choose(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) {
      void this.readCv(file);
    }
  }

  protected drop(event: DragEvent): void {
    event.preventDefault();
    this.dragOver.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) {
      void this.readCv(file);
    }
  }

  protected removeCv(): void {
    const values = this.cvValues();
    // Take back only what the CV put in and nobody has changed since.
    if (Object.keys(values).length) {
      const marked = this.marked();
      this.details.update((details) => {
        const next = { ...details } as Record<string, unknown>;
        for (const key of marked) {
          next[key] = key === 'skills' ? [] : key === 'firstName' || key === 'email' ? '' : null;
        }
        return next as unknown as CandidateDetailsInput;
      });
    }
    this.cv.set(null);
    this.cvValues.set({});
    this.cvFilled.set(0);
    this.cvError.set(null);
  }

  /** Reads the CV on the server; its values go into the fields that are still empty. */
  private async readCv(file: File): Promise<void> {
    if (!/\.(pdf|docx|txt)$/i.test(file.name)) {
      this.cvError.set('Choose a PDF, Word (.docx) or text file. Older .doc files can be saved as .docx first.');
      return;
    }
    if (file.size > 5 * 1024 * 1024) {
      this.cvError.set('The CV must be smaller than 5 MB.');
      return;
    }

    this.removeCv();
    this.reading.set(file.name);
    try {
      const read = await this.api.readCv(file);
      const fill = cvFill(read.fields, this.details());
      this.details.update((details) => ({ ...details, ...fill }));
      this.cvValues.set(fill);
      this.cvFilled.set(Object.keys(fill).length);
      this.cv.set(read);
    } catch (error) {
      this.cvError.set(errorText(error, 'The CV could not be read.'));
    } finally {
      this.reading.set(null);
    }
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const candidate = this.candidate();
      const read = this.cv();
      let view = candidate
        ? await this.api.updateCandidate(candidate.id, this.details())
        : await this.api.addCandidate({
            details: this.details(),
            source: this.source(),
            agencyId: this.source() === 'Agency' ? this.agencyId() || null : null,
            referrerEmployeeId: this.source() === 'Referral' ? this.referrerId() : null,
            requisitionId: this.requisitionId(),
            cvKey: read?.cvKey ?? null,
            cvFileName: read?.fileName ?? null,
          });
      if (candidate && read) {
        view = await this.api.attachCv(candidate.id, read.cvKey, read.fileName);
      }
      this.visible.set(false);
      this.saved.emit(view);
    } catch (error) {
      this.error.set(errorText(error, 'The candidate could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}
