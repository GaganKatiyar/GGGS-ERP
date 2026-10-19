import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { RecruitmentApi } from '@gggs/shared/hiring';
import type { ApplicationStage, ApplicationView, BackgroundCheckItem, BackgroundCheckStatus, InterviewItem, InterviewMode, OfferItem, OfferTermsInput } from '@gggs/shared/models';
import { BACKGROUND_CHECKS, INTERVIEW_MODES, PIPELINE_STAGES, humanize } from '@gggs/shared/models';
import { MoneyPipe } from '@gggs/shared/money';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge, Timeline } from '@gggs/shared/ui';
import type { TimelineEntry } from '@gggs/shared/ui';
import { errorText } from './recruitment-forms';

type Dialog = 'interview' | 'offer' | 'reject' | 'hire' | 'check' | 'result' | 'respond' | null;

const localInput = (date: Date) => new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);

/**
 * One application (spec §12): the candidate against the opening - match, history, interviews and
 * scorecards, verification, the offer and the hire. Every decision here is a person's; the match score
 * only ranks.
 */
@Component({
  selector: 'gggs-application-page',
  imports: [DatePipe, FormsModule, RouterLink, MoneyPipe, PageHeader, StatusBadge, EmptyState, Skeleton, FormDialog, Timeline, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="v.candidate.fullName" [subtitle]="v.application.requisitionNumber + ' · ' + v.application.requisitionTitle">
        <gggs-status-badge [status]="v.application.stage" />
        @if (v.canManage && open()) {
          <select class="rec__stage" aria-label="Move to stage" [value]="v.application.stage" (change)="move($any($event.target).value)">
            @for (stage of movable; track stage) {
              <option [value]="stage">{{ humanize(stage) }}</option>
            }
            @if (v.application.stage === 'Offer') {
              <option value="Offer">Offer</option>
            }
          </select>
          <button type="button" class="btn" (click)="openInterview()">Schedule interview</button>
          @if (canOffer && !liveOffer()) {
            <button type="button" class="btn btn--primary" (click)="openOffer()">Make an offer</button>
          }
          <button type="button" class="btn btn--link" (click)="openDialog('reject')">Reject or withdraw</button>
        }
      </gggs-page-header>
      <p>
        <a [routerLink]="['/recruitment/requisitions', v.application.requisitionId]">← Pipeline</a> ·
        <a [routerLink]="['/recruitment/candidates', v.candidate.id]">Candidate profile</a>
      </p>

      <div class="rec__split">
        <div class="stack">
          <section class="panel rec__facts">
            <div><span class="field__label">Now</span><p>{{ v.candidate.currentTitle ?? '—' }}{{ v.candidate.currentCompany ? ' at ' + v.candidate.currentCompany : '' }}</p></div>
            <div><span class="field__label">Experience · notice</span><p>{{ v.candidate.experienceYears ?? '—' }} yrs · {{ v.candidate.noticePeriodDays ?? '—' }} days</p></div>
            <div><span class="field__label">CTC now → expected</span><p>{{ v.candidate.currentCtc | gggsMoney }} → {{ v.candidate.expectedCtc | gggsMoney }}</p></div>
            <div><span class="field__label">Contact</span><p>{{ v.candidate.email }}{{ v.candidate.mobile ? ' · ' + v.candidate.mobile : '' }}</p></div>
            <div><span class="field__label">Source</span><p>{{ humanize(v.candidate.source) }}{{ v.candidate.agencyName ? ' · ' + v.candidate.agencyName : '' }}{{ v.candidate.referrerName ? ' · ' + v.candidate.referrerName : '' }}</p></div>
          </section>

          <section class="panel stack">
            <div class="toolbar">
              <h2 class="rec__heading">Interviews</h2>
            </div>
            @for (interview of v.interviews; track interview.id) {
              <div class="rec__interview">
                <div>
                  <strong>{{ interview.round }}</strong> · {{ interview.scheduledAt | date: 'EEE d MMM y, HH:mm' }} · {{ interview.durationMinutes }} min · {{ humanize(interview.mode) }}
                  <div class="muted">{{ interview.panelNames.join(', ') }}{{ interview.location ? ' · ' + interview.location : '' }}</div>
                </div>
                <span class="toolbar">
                  <span class="muted">{{ interview.scorecards }}/{{ interview.panel.length }} feedback</span>
                  <gggs-status-badge [status]="interview.status" />
                  @if (v.canManage && interview.status === 'Scheduled') {
                    <button type="button" class="btn btn--link btn--sm" (click)="openInterview(interview)">Reschedule</button>
                    <button type="button" class="btn btn--link btn--sm" (click)="interviewAction(interview, 'complete')">Done</button>
                    <button type="button" class="btn btn--link btn--sm" (click)="interviewAction(interview, 'no-show')">No-show</button>
                    <button type="button" class="btn btn--link btn--sm" (click)="interviewAction(interview, 'cancel')">Cancel</button>
                  }
                </span>
              </div>
            } @empty {
              <p class="muted">No interviews yet.</p>
            }
          </section>

          <section class="panel stack">
            <h2 class="rec__heading">Scorecards</h2>
            @for (card of v.scorecards; track card.id) {
              <article class="rec__scorecard">
                <div class="toolbar">
                  <strong>{{ card.interviewerName ?? '—' }}</strong>
                  <span class="muted">{{ card.round }} · {{ card.submittedDate | date: 'd MMM' }}</span>
                  <span class="rec__spacer"></span>
                  <span class="rec__rating">{{ card.overallRating }} / 5</span>
                  <gggs-status-badge [status]="humanize(card.recommendation)" [variants]="recommendationVariants" />
                </div>
                <p class="muted">{{ ratingText(card.ratings) }}</p>
                @if (card.comments) {
                  <p>{{ card.comments }}</p>
                }
              </article>
            } @empty {
              <p class="muted">No feedback yet. Panel members give it from My interviews.</p>
            }
          </section>

          <section class="panel stack">
            <h2 class="rec__heading">Offer</h2>
            @for (offer of v.offers; track offer.id) {
              <article class="rec__offer">
                <div class="toolbar">
                  <strong>{{ offer.number }}</strong>
                  <span>{{ offer.designation }} · {{ offer.annualCtc | gggsMoney: offer.currency }}{{ offer.withinBudget ? '' : ' (above range)' }}</span>
                  <span class="muted">joining {{ offer.joiningDate | date: 'd MMM y' }}{{ offer.expiresOn ? ' · valid to ' + (offer.expiresOn | date: 'd MMM') : '' }}</span>
                  <span class="rec__spacer"></span>
                  <gggs-status-badge [status]="offer.status" />
                </div>
                @if (offer.declineReason) {
                  <p class="muted">Declined: {{ offer.declineReason }}</p>
                }
                @if (canOffer) {
                  <div class="toolbar">
                    @if (offer.status === 'Draft' || offer.status === 'Rejected') {
                      <button type="button" class="btn btn--sm" (click)="openOffer(offer)">Edit</button>
                      <button type="button" class="btn btn--primary btn--sm" (click)="offerAction(offer, 'submit')">Submit for approval</button>
                    }
                    @if (offer.status === 'Approved') {
                      <button type="button" class="btn btn--primary btn--sm" (click)="offerAction(offer, 'send')">Mark as sent</button>
                    }
                    @if (offer.status === 'Sent') {
                      <button type="button" class="btn btn--primary btn--sm" (click)="openRespond(offer)">Record response</button>
                    }
                    @if (offer.status === 'Accepted' && !v.candidate.employeeId) {
                      <button type="button" class="btn btn--primary btn--sm" (click)="openHire(offer)"><i class="pi pi-user-plus" aria-hidden="true"></i> Hire</button>
                    }
                    @if (offer.status !== 'Accepted' && offer.status !== 'Declined' && offer.status !== 'Withdrawn') {
                      <button type="button" class="btn btn--link btn--sm" (click)="offerAction(offer, 'withdraw')">Withdraw</button>
                    }
                  </div>
                }
              </article>
            } @empty {
              <p class="muted">No offer yet.</p>
            }
            @if (v.candidate.employeeId) {
              <p class="rec__notice" role="status">Hired. <a [routerLink]="['/employees', v.candidate.employeeId]">Employee record</a> · onboarding has started.</p>
            }
          </section>
        </div>

        <div class="stack">
          <section class="panel stack">
            <h2 class="rec__heading">Match {{ v.match.score }}%</h2>
            <div class="rec__meter" role="meter" [attr.aria-valuenow]="v.match.score" aria-valuemin="0" aria-valuemax="100"><span [style.width.%]="v.match.score"></span></div>
            <p class="muted">Skills {{ v.match.skillsScore }}/60 · experience {{ v.match.experienceScore }}/25 · salary {{ v.match.salaryScore }}/15</p>
            @if (v.match.matchedSkills.length) {
              <p>Has: {{ v.match.matchedSkills.join(', ') }}</p>
            }
            @if (v.match.missingSkills.length) {
              <p class="muted">Missing: {{ v.match.missingSkills.join(', ') }}</p>
            }
            <p class="muted rec__small">A rule-based guide for ranking - hiring decisions stay with people.</p>
          </section>

          <section class="panel stack">
            <div class="toolbar">
              <h2 class="rec__heading">Background verification</h2>
              <span class="rec__spacer"></span>
              @if (v.canManage && !openCheck()) {
                <button type="button" class="btn btn--sm" (click)="openDialog('check')">Initiate</button>
              }
            </div>
            @for (check of v.backgroundChecks; track check.id) {
              <div>
                <div class="toolbar">
                  <span>{{ check.checks.join(', ') }}</span>
                  <span class="rec__spacer"></span>
                  <gggs-status-badge [status]="check.status" />
                </div>
                <p class="muted">{{ check.vendor ?? 'In-house' }} · {{ check.initiatedOn | date: 'd MMM' }}{{ check.completedOn ? ' – ' + (check.completedOn | date: 'd MMM') : '' }}{{ check.remarks ? ' · ' + check.remarks : '' }}</p>
                @if (v.canManage && (check.status === 'Initiated' || check.status === 'InProgress')) {
                  <button type="button" class="btn btn--link btn--sm" (click)="openResult(check)">Record result</button>
                }
              </div>
            } @empty {
              <p class="muted">Not started.</p>
            }
          </section>

          <section class="panel stack">
            <h2 class="rec__heading">History</h2>
            <gggs-timeline [entries]="history()" />
          </section>
        </div>
      </div>

      <gggs-form-dialog [(visible)]="interviewOpen" [title]="editingInterview() ? 'Reschedule interview' : 'Schedule an interview'" [busy]="busy()" [error]="formError()" [saveDisabled]="!round().trim() || !when() || !panel().length" (save)="saveInterview()">
        <div class="rec__grid">
          <label class="field">
            <span class="field__label">Round</span>
            <input type="text" maxlength="100" [value]="round()" (input)="round.set($any($event.target).value)" placeholder="Technical 1" />
          </label>
          <label class="field">
            <span class="field__label">When</span>
            <input type="datetime-local" [value]="when()" (change)="when.set($any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">Minutes</span>
            <input type="number" min="10" max="480" step="5" [value]="duration()" (input)="duration.set(+$any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">Mode</span>
            <select [value]="mode()" (change)="mode.set($any($event.target).value)">
              @for (option of modes; track option) {
                <option [value]="option">{{ humanize(option) }}</option>
              }
            </select>
          </label>
          <label class="field rec__wide">
            <span class="field__label">Room or meeting link</span>
            <input type="text" maxlength="500" [value]="where()" (input)="where.set($any($event.target).value)" />
          </label>
        </div>
        <fieldset class="rec__fieldset">
          <legend class="field__label">Panel</legend>
          @for (member of panel(); track member.id) {
            <div class="toolbar">
              <span>{{ member.name }}</span>
              <button type="button" class="btn btn--link btn--sm" (click)="removePanel(member.id)">Remove</button>
            </div>
          }
          <gggs-employee-select ariaLabel="Add a panel member" [ngModel]="null" (ngModelChange)="addPanel($event)" />
          <p class="muted rec__small">Each panel member is notified and gives feedback from My interviews.</p>
        </fieldset>
      </gggs-form-dialog>

      <gggs-form-dialog [(visible)]="offerOpen" [title]="editingOffer() ? 'Edit offer' : 'Make an offer'" [saveLabel]="editingOffer() ? 'Save' : submitOffer() ? 'Create and submit' : 'Save draft'" [busy]="busy()" [error]="formError()" [saveDisabled]="!terms().annualCtc || !terms().designation.trim() || !terms().joiningDate" (save)="saveOffer()">
        <div class="rec__grid">
          <label class="field">
            <span class="field__label">Designation</span>
            <input type="text" maxlength="200" [value]="terms().designation" (input)="patchTerms({ designation: $any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">Annual CTC</span>
            <input type="number" min="0" [value]="terms().annualCtc || ''" (input)="patchTerms({ annualCtc: +$any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">Joining date</span>
            <input type="date" [value]="terms().joiningDate" (change)="patchTerms({ joiningDate: $any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">Valid until</span>
            <input type="date" [value]="terms().expiresOn ?? ''" (change)="patchTerms({ expiresOn: $any($event.target).value || null })" />
          </label>
          <label class="field rec__wide">
            <span class="field__label">Notes</span>
            <textarea rows="3" maxlength="2000" [value]="terms().notes ?? ''" (input)="patchTerms({ notes: $any($event.target).value || null })"></textarea>
          </label>
        </div>
        @if (!editingOffer()) {
          <label class="rec__flag"><input type="checkbox" [checked]="submitOffer()" (change)="submitOffer.set($any($event.target).checked)" /> <span>Submit for approval now</span></label>
        }
      </gggs-form-dialog>

      <gggs-form-dialog [(visible)]="rejectOpen" title="Close this application" [saveLabel]="withdrawn() ? 'Withdraw' : 'Reject'" [busy]="busy()" [error]="formError()" [saveDisabled]="!withdrawn() && !reason().trim()" (save)="close()">
        <label class="rec__flag"><input type="checkbox" [checked]="withdrawn()" (change)="withdrawn.set($any($event.target).checked)" /> <span>The candidate withdrew</span></label>
        <label class="field">
          <span class="field__label">{{ withdrawn() ? 'Note' : 'Reason' }}</span>
          <textarea rows="3" maxlength="500" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea>
        </label>
      </gggs-form-dialog>

      <gggs-form-dialog [(visible)]="respondOpen" title="Candidate's response" [busy]="busy()" [error]="formError()" (save)="respond()">
        <label class="rec__flag"><input type="radio" name="response" [checked]="accepted()" (change)="accepted.set(true)" /> <span>Accepted</span></label>
        <label class="rec__flag"><input type="radio" name="response" [checked]="!accepted()" (change)="accepted.set(false)" /> <span>Declined</span></label>
        @if (!accepted()) {
          <label class="field">
            <span class="field__label">Reason</span>
            <input type="text" maxlength="500" [value]="reason()" (input)="reason.set($any($event.target).value)" />
          </label>
        }
      </gggs-form-dialog>

      <gggs-form-dialog [(visible)]="hireOpen" title="Hire" saveLabel="Create employee" [busy]="busy()" [error]="formError()" (save)="hire()">
        <p>Creates {{ v.candidate.fullName }}'s employee record joining {{ hiringOffer()?.joiningDate | date: 'd MMM y' }} at {{ hiringOffer()?.annualCtc ?? 0 | gggsMoney }}, records the salary in payroll and starts onboarding.</p>
        <label class="field">
          <span class="field__label">Reports to (defaults to the hiring manager)</span>
          <gggs-employee-select ariaLabel="Manager" [ngModel]="managerId()" (ngModelChange)="managerId.set($event)" />
        </label>
      </gggs-form-dialog>

      <gggs-form-dialog [(visible)]="checkOpen" title="Initiate background verification" saveLabel="Initiate" [busy]="busy()" [error]="formError()" [saveDisabled]="!checks().length" (save)="initiateCheck()">
        <label class="field">
          <span class="field__label">Vendor</span>
          <input type="text" maxlength="200" [value]="vendor()" (input)="vendor.set($any($event.target).value)" placeholder="In-house" />
        </label>
        <fieldset class="rec__fieldset">
          <legend class="field__label">Checks</legend>
          @for (check of checkTypes; track check) {
            <label class="rec__flag"><input type="checkbox" [checked]="checks().includes(check)" (change)="toggleCheck(check, $any($event.target).checked)" /> <span>{{ check }}</span></label>
          }
        </fieldset>
      </gggs-form-dialog>

      <gggs-form-dialog [(visible)]="resultOpen" title="Verification result" [busy]="busy()" [error]="formError()" [saveDisabled]="(result() === 'Discrepancy' || result() === 'Failed') && !reason().trim()" (save)="recordCheck()">
        <label class="field">
          <span class="field__label">Result</span>
          <select [value]="result()" (change)="result.set($any($event.target).value)">
            <option value="InProgress">In progress</option>
            <option value="Clear">Clear</option>
            <option value="Discrepancy">Discrepancy</option>
            <option value="Failed">Failed</option>
          </select>
        </label>
        <label class="field">
          <span class="field__label">Remarks</span>
          <textarea rows="3" maxlength="2000" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea>
        </label>
      </gggs-form-dialog>
    } @else if (!error()) {
      <gggs-skeleton [lines]="12" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './recruitment.css',
})
export class ApplicationPage {
  private readonly api = inject(RecruitmentApi);

  readonly id = input.required<string>();

  protected readonly canOffer = inject(PermissionStore).has('recruitment.offer.manage');
  protected readonly humanize = humanize;
  protected readonly movable: readonly ApplicationStage[] = PIPELINE_STAGES.filter((stage) => stage !== 'Offer');
  protected readonly modes = INTERVIEW_MODES;
  protected readonly checkTypes = BACKGROUND_CHECKS;
  protected readonly recommendationVariants = { 'Strong yes': 'success', Yes: 'success', No: 'danger', 'Strong no': 'danger' } as const;

  protected readonly view = signal<ApplicationView | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  protected readonly interviewOpen = signal(false);
  protected readonly offerOpen = signal(false);
  protected readonly rejectOpen = signal(false);
  protected readonly respondOpen = signal(false);
  protected readonly hireOpen = signal(false);
  protected readonly checkOpen = signal(false);
  protected readonly resultOpen = signal(false);

  protected readonly editingInterview = signal<InterviewItem | null>(null);
  protected readonly round = signal('');
  protected readonly when = signal('');
  protected readonly duration = signal(60);
  protected readonly mode = signal<InterviewMode>('Video');
  protected readonly where = signal('');
  protected readonly panel = signal<readonly { id: string; name: string }[]>([]);

  protected readonly editingOffer = signal<OfferItem | null>(null);
  protected readonly terms = signal<OfferTermsInput>({ annualCtc: 0, currency: '', joiningDate: '', designation: '' });
  protected readonly submitOffer = signal(true);
  protected readonly hiringOffer = signal<OfferItem | null>(null);
  protected readonly managerId = signal<string | null>(null);
  protected readonly accepted = signal(true);

  protected readonly reason = signal('');
  protected readonly withdrawn = signal(false);
  protected readonly vendor = signal('');
  protected readonly checks = signal<readonly string[]>(['Identity', 'Education', 'Employment']);
  protected readonly checking = signal<BackgroundCheckItem | null>(null);
  protected readonly result = signal<BackgroundCheckStatus>('Clear');

  protected readonly open = computed(() => {
    const stage = this.view()?.application.stage;
    return stage !== undefined && PIPELINE_STAGES.includes(stage);
  });
  protected readonly liveOffer = computed(() => (this.view()?.offers ?? []).some((offer) => !['Declined', 'Withdrawn', 'Rejected'].includes(offer.status)));
  protected readonly openCheck = computed(() => (this.view()?.backgroundChecks ?? []).some((check) => check.status === 'Initiated' || check.status === 'InProgress'));
  protected readonly history = computed<readonly TimelineEntry[]>(() =>
    [...(this.view()?.history ?? [])].reverse().map((change) => ({
      date: change.at,
      title: humanize(change.stage),
      description: change.note,
      tone: change.stage === 'Hired' ? 'success' : change.stage === 'Rejected' || change.stage === 'Withdrawn' ? 'danger' : 'primary',
    })),
  );

  constructor() {
    effect(() => {
      this.api.application(this.id()).then(
        (view) => this.view.set(view),
        (error: unknown) => this.error.set(errorText(error, 'The application could not be loaded.')),
      );
    });
  }

  protected ratingText(ratings: Readonly<Record<string, number>>): string {
    return Object.entries(ratings)
      .map(([criterion, rating]) => `${criterion} ${rating}`)
      .join(' · ');
  }

  protected openDialog(dialog: Dialog): void {
    this.formError.set(null);
    this.reason.set('');
    this.withdrawn.set(false);
    this.rejectOpen.set(dialog === 'reject');
    this.checkOpen.set(dialog === 'check');
  }

  protected async move(stage: ApplicationStage): Promise<void> {
    await this.run(() => this.api.move(this.id(), stage, null));
  }

  protected openInterview(interview?: InterviewItem): void {
    this.editingInterview.set(interview ?? null);
    this.round.set(interview?.round ?? `Round ${(this.view()?.interviews.length ?? 0) + 1}`);
    this.when.set(localInput(interview ? new Date(interview.scheduledAt) : new Date(Date.now() + 86_400_000)));
    this.duration.set(interview?.durationMinutes ?? 60);
    this.mode.set(interview?.mode ?? 'Video');
    this.where.set(interview?.location ?? '');
    this.panel.set(interview ? interview.panel.map((id, index) => ({ id, name: interview.panelNames[index] ?? id })) : []);
    this.formError.set(null);
    this.interviewOpen.set(true);
  }

  protected addPanel(employeeId: string | null): void {
    if (employeeId && !this.panel().some((member) => member.id === employeeId)) {
      this.panel.update((panel) => [...panel, { id: employeeId, name: 'Panel member ' + (panel.length + 1) }]);
    }
  }

  protected removePanel(id: string): void {
    this.panel.update((panel) => panel.filter((member) => member.id !== id));
  }

  protected async saveInterview(): Promise<void> {
    const body = {
      round: this.round(),
      scheduledAt: new Date(this.when()).toISOString(),
      durationMinutes: this.duration(),
      mode: this.mode(),
      location: this.where() || null,
      panel: this.panel().map((member) => member.id),
    };
    const editing = this.editingInterview();
    if (await this.run(() => (editing ? this.api.reschedule(editing.id, body) : this.api.schedule(this.id(), body)), true)) {
      this.interviewOpen.set(false);
    }
  }

  protected async interviewAction(interview: InterviewItem, action: 'complete' | 'cancel' | 'no-show'): Promise<void> {
    await this.run(() => this.api.interviewAction(interview.id, action));
  }

  protected openOffer(offer?: OfferItem): void {
    const view = this.view();
    this.editingOffer.set(offer ?? null);
    this.terms.set(
      offer
        ? { annualCtc: offer.annualCtc, currency: offer.currency, joiningDate: offer.joiningDate, designation: offer.designation, gradeId: offer.gradeId ?? null, expiresOn: offer.expiresOn ?? null, notes: offer.notes ?? null }
        : {
            annualCtc: view?.candidate.expectedCtc ?? 0,
            currency: '',
            joiningDate: new Date(Date.now() + 30 * 86_400_000).toISOString().slice(0, 10),
            designation: view?.application.requisitionTitle ?? '',
            expiresOn: null,
            notes: null,
          },
    );
    this.submitOffer.set(true);
    this.formError.set(null);
    this.offerOpen.set(true);
  }

  protected patchTerms(change: Partial<OfferTermsInput>): void {
    this.terms.update((terms) => ({ ...terms, ...change }));
  }

  protected async saveOffer(): Promise<void> {
    const editing = this.editingOffer();
    this.busy.set(true);
    this.formError.set(null);
    try {
      if (editing) {
        await this.api.updateOffer(editing.id, this.terms());
      } else {
        await this.api.draftOffer(this.id(), this.terms(), this.submitOffer());
      }

      this.offerOpen.set(false);
      await this.reload();
    } catch (error) {
      this.formError.set(errorText(error, 'The offer could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async offerAction(offer: OfferItem, action: 'submit' | 'send' | 'withdraw'): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.offerAction(offer.id, action);
      await this.reload();
    } catch (error) {
      this.error.set(errorText(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected openRespond(offer: OfferItem): void {
    this.hiringOffer.set(offer);
    this.accepted.set(true);
    this.reason.set('');
    this.formError.set(null);
    this.respondOpen.set(true);
  }

  protected async respond(): Promise<void> {
    const offer = this.hiringOffer();
    if (!offer) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.respond(offer.id, this.accepted(), this.reason() || null);
      this.respondOpen.set(false);
      await this.reload();
    } catch (error) {
      this.formError.set(errorText(error, 'The response could not be recorded.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected openHire(offer: OfferItem): void {
    this.hiringOffer.set(offer);
    this.managerId.set(null);
    this.formError.set(null);
    this.hireOpen.set(true);
  }

  protected async hire(): Promise<void> {
    const offer = this.hiringOffer();
    if (!offer) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.hire(offer.id, { managerEmployeeId: this.managerId() });
      this.hireOpen.set(false);
      await this.reload();
    } catch (error) {
      this.formError.set(errorText(error, 'The hire could not be completed.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async close(): Promise<void> {
    if (await this.run(() => this.api.close(this.id(), this.withdrawn() ? 'withdraw' : 'reject', this.reason() || null), true)) {
      this.rejectOpen.set(false);
    }
  }

  protected toggleCheck(check: string, on: boolean): void {
    this.checks.update((checks) => (on ? [...checks, check] : checks.filter((item) => item !== check)));
  }

  protected async initiateCheck(): Promise<void> {
    const view = this.view();
    if (!view) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.initiateCheck({ candidateId: view.candidate.id, applicationId: view.application.id, vendor: this.vendor() || null, checks: this.checks() });
      this.checkOpen.set(false);
      await this.reload();
    } catch (error) {
      this.formError.set(errorText(error, 'The verification could not be started.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected openResult(check: BackgroundCheckItem): void {
    this.checking.set(check);
    this.result.set('Clear');
    this.reason.set('');
    this.formError.set(null);
    this.resultOpen.set(true);
  }

  protected async recordCheck(): Promise<void> {
    const check = this.checking();
    if (!check) {
      return;
    }

    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.recordCheck(check.id, this.result(), this.reason() || null);
      this.resultOpen.set(false);
      await this.reload();
    } catch (error) {
      this.formError.set(errorText(error, 'The result could not be recorded.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    this.view.set(await this.api.application(this.id()));
  }

  /** Runs an action that returns the refreshed view. Returns whether it worked; errors go to the dialog when one is open. */
  private async run(action: () => Promise<ApplicationView>, inDialog = false): Promise<boolean> {
    this.busy.set(true);
    (inDialog ? this.formError : this.error).set(null);
    try {
      this.view.set(await action());
      return true;
    } catch (error) {
      (inDialog ? this.formError : this.error).set(errorText(error, 'That did not work.'));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
