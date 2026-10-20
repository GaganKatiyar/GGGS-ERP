import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RecruitmentApi } from '@gggs/shared/hiring';
import type { CandidateDetailsInput, InterviewItem, MyInterviewView, PostingItem, Recommendation, ReferralItem } from '@gggs/shared/models';
import { RECOMMENDATIONS, humanize } from '@gggs/shared/models';
import { MoneyPipe } from '@gggs/shared/money';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { CandidateFields, blankCandidate, errorText } from './recruitment-forms';

/** The internal job board and my referrals (spec §12 referrals): open roles anyone can apply to or refer for. */
@Component({
  selector: 'gggs-job-board-page',
  imports: [DatePipe, MoneyPipe, PageHeader, StatusBadge, EmptyState, FormDialog, CandidateFields],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Jobs & referrals" subtitle="Open roles across the company. Know someone good? Refer them.">
      <button type="button" class="btn" (click)="refer(null)"><i class="pi pi-share-alt" aria-hidden="true"></i> General referral</button>
    </gggs-page-header>
    <section class="rec__jobs">
      @for (job of jobs(); track job.id) {
        <article class="panel stack">
          <div class="toolbar">
            <h2 class="rec__heading">{{ job.title }}</h2>
            <span class="rec__spacer"></span>
            <span class="muted">{{ job.requisitionNumber }}</span>
          </div>
          <p class="muted">{{ job.departmentName ?? '' }}{{ job.locationName ? ' · ' + job.locationName : '' }} · {{ humanize(job.employmentTypeCode) }} · {{ humanize(job.workModeCode) }}{{ job.minExperienceYears !== undefined ? ' · ' + job.minExperienceYears + '+ years' : '' }}</p>
          <p class="rec__pre rec__clip">{{ job.description }}</p>
          @if (job.skills.length) {
            <p class="muted">Skills: {{ job.skills.join(', ') }}</p>
          }
          <div class="toolbar">
            <button type="button" class="btn btn--primary btn--sm" (click)="refer(job)">Refer someone</button>
            @if (job.expiresOn) {
              <span class="muted">Closes {{ job.expiresOn | date: 'd MMM' }}</span>
            }
          </div>
        </article>
      } @empty {
        <gggs-empty-state icon="pi pi-briefcase" title="No open roles right now" />
      }
    </section>

    <section class="panel stack">
      <h2 class="rec__heading">My referrals</h2>
      @for (referral of referrals(); track referral.id) {
        <div class="rec__row">
          <span><strong>{{ referral.candidateName }}</strong></span>
          <span>{{ referral.requisitionTitle ?? 'General referral' }}</span>
          <span class="muted">{{ referral.createdDate | date: 'd MMM y' }}{{ referral.stage ? ' · ' + humanize(referral.stage) : '' }}</span>
          <span>{{ referral.bonusAmount ? (referral.bonusAmount | gggsMoney) + ' bonus' + (referral.bonusDueOn && referral.status === 'Hired' ? ' due ' + (referral.bonusDueOn | date: 'd MMM y') : '') : '' }}</span>
          <span><gggs-status-badge [status]="referral.status" /></span>
        </div>
      } @empty {
        <p class="muted">You have not referred anyone yet.</p>
      }
    </section>
    @if (notice()) {
      <p class="alert alert--success" role="status">{{ notice() }}</p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="open" [title]="job() ? 'Refer for ' + job()?.title : 'Refer someone'" saveLabel="Refer" [busy]="busy()" [error]="formError()" [saveDisabled]="!details().firstName.trim() || !details().email.trim()" (save)="submit()" size="lg">
      <gggs-candidate-fields [(value)]="details" [showCtc]="false" />
      <label class="field">
        <span class="field__label">Why are they a good fit?</span>
        <textarea rows="3" maxlength="1000" [value]="note()" (input)="note.set($any($event.target).value)"></textarea>
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './recruitment.css',
})
export class JobBoardPage {
  private readonly api = inject(RecruitmentApi);

  protected readonly humanize = humanize;
  protected readonly jobs = signal<readonly PostingItem[]>([]);
  protected readonly referrals = signal<readonly ReferralItem[]>([]);
  protected readonly open = signal(false);
  protected readonly job = signal<PostingItem | null>(null);
  protected readonly details = signal<CandidateDetailsInput>(blankCandidate());
  protected readonly note = signal('');
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    Promise.all([this.api.jobBoard(), this.api.myReferrals()]).then(
      ([jobs, referrals]) => {
        this.jobs.set(jobs);
        this.referrals.set(referrals);
      },
      (error: unknown) => this.error.set(errorText(error, 'The job board could not be loaded.')),
    );
  }

  protected refer(job: PostingItem | null): void {
    this.job.set(job);
    this.details.set(blankCandidate());
    this.note.set('');
    this.formError.set(null);
    this.open.set(true);
  }

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      this.referrals.set(await this.api.refer({ candidate: this.details(), requisitionId: this.job()?.requisitionId ?? null, note: this.note() || null }));
      this.open.set(false);
      this.notice.set('Thanks - the recruiting team has your referral.');
    } catch (error) {
      this.formError.set(errorText(error, 'The referral could not be sent.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Interviews I sit on, and whether I have given feedback. */
@Component({
  selector: 'gggs-my-interviews-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My interviews" subtitle="Interviews you are on the panel for. Give your feedback once you have met the candidate." />
    <section class="panel stack">
      @for (interview of interviews(); track interview.id) {
        <a class="rec__row rec__row--link" [routerLink]="['/me/interviews', interview.id]">
          <span><strong>{{ interview.scheduledAt | date: 'EEE d MMM, HH:mm' }}</strong> <span class="muted">{{ interview.durationMinutes }} min</span></span>
          <span>{{ interview.candidateName }} · {{ interview.round }}</span>
          <span class="muted">{{ interview.requisitionTitle }}</span>
          <span>{{ interview.myScorecardSubmitted ? 'Feedback given' : 'Feedback due' }}</span>
          <span><gggs-status-badge [status]="interview.status" /></span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-comments" title="No interviews" description="You are not on any interview panel." />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
  `,
  styleUrl: './recruitment.css',
})
export class MyInterviewsPage {
  protected readonly interviews = signal<readonly InterviewItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    inject(RecruitmentApi)
      .myInterviews()
      .then(
        (interviews) => this.interviews.set(interviews),
        (error: unknown) => this.error.set(errorText(error, 'Your interviews could not be loaded.')),
      );
  }
}

/** One interview from the panel's side: the candidate (without pay), the role, and my scorecard. */
@Component({
  selector: 'gggs-my-interview-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="v.candidate.fullName + ' · ' + v.interview.round" [subtitle]="v.interview.requisitionTitle + ' · ' + (v.interview.scheduledAt | date: 'EEE d MMM y, HH:mm')">
        <gggs-status-badge [status]="v.interview.status" />
      </gggs-page-header>
      <p><a routerLink="/me/interviews">← My interviews</a></p>
      <div class="rec__split">
        <section class="panel stack">
          <h2 class="rec__heading">Your feedback</h2>
          @for (criterion of v.criteria; track criterion) {
            <fieldset class="rec__fieldset">
              <legend>{{ criterion }}</legend>
              <div class="rec__stars" role="radiogroup" [attr.aria-label]="criterion">
                @for (score of scores; track score) {
                  <label class="rec__star" [class.rec__star--on]="rating(criterion) >= score">
                    <input type="radio" [name]="criterion" [value]="score" [checked]="rating(criterion) === score" (change)="rate(criterion, score)" />
                    <span>{{ score }}</span>
                  </label>
                }
              </div>
            </fieldset>
          }
          <label class="field">
            <span class="field__label">Recommendation</span>
            <select [value]="recommendation()" (change)="recommendation.set($any($event.target).value)">
              <option value="">Choose</option>
              @for (option of recommendations; track option) {
                <option [value]="option">{{ humanize(option) }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">Comments</span>
            <textarea rows="5" maxlength="4000" [value]="comments()" (input)="comments.set($any($event.target).value)"></textarea>
          </label>
          <div class="toolbar">
            <button type="button" class="btn btn--primary" [disabled]="busy() || !recommendation() || !hasRatings() || v.interview.status === 'Cancelled' || v.interview.status === 'NoShow'" (click)="submit()">
              {{ v.myScorecard ? 'Update feedback' : 'Submit feedback' }}
            </button>
            @if (v.myScorecard) {
              <span class="muted">Submitted {{ v.myScorecard.submittedDate | date: 'd MMM, HH:mm' }}</span>
            }
          </div>
          @if (notice()) {
            <p class="alert alert--success" role="status">{{ notice() }}</p>
          }
        </section>
        <div class="stack">
          <section class="panel rec__facts">
            <div><span class="field__label">Now</span><p>{{ v.candidate.currentTitle ?? '—' }}{{ v.candidate.currentCompany ? ' at ' + v.candidate.currentCompany : '' }}</p></div>
            <div><span class="field__label">Experience</span><p>{{ v.candidate.experienceYears ?? '—' }} years</p></div>
            <div><span class="field__label">Skills</span><p>{{ v.candidate.skills.join(', ') || '—' }}</p></div>
            <div><span class="field__label">Where</span><p>{{ humanize(v.interview.mode) }}{{ v.interview.location ? ' · ' + v.interview.location : '' }}</p></div>
            <div><span class="field__label">Panel</span><p>{{ v.interview.panelNames.join(', ') }}</p></div>
          </section>
          @if (v.requisitionDescription) {
            <section class="panel"><span class="field__label">The role</span><p class="rec__pre">{{ v.requisitionDescription }}</p></section>
          }
        </div>
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styleUrl: './recruitment.css',
})
export class MyInterviewPage {
  private readonly api = inject(RecruitmentApi);

  readonly id = input.required<string>();

  protected readonly humanize = humanize;
  protected readonly scores = [1, 2, 3, 4, 5];
  protected readonly recommendations = RECOMMENDATIONS;
  protected readonly view = signal<MyInterviewView | null>(null);
  protected readonly ratings = signal<Readonly<Record<string, number>>>({});
  protected readonly recommendation = signal<Recommendation | ''>('');
  protected readonly comments = signal('');
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.myInterview(this.id()).then(
        (view) => this.show(view),
        (error: unknown) => this.error.set(errorText(error, 'The interview could not be loaded.')),
      );
    });
  }

  protected rating(criterion: string): number {
    return this.ratings()[criterion] ?? 0;
  }

  protected hasRatings(): boolean {
    return Object.keys(this.ratings()).length > 0;
  }

  protected rate(criterion: string, score: number): void {
    this.ratings.update((ratings) => ({ ...ratings, [criterion]: score }));
  }

  protected async submit(): Promise<void> {
    const recommendation = this.recommendation();
    if (!recommendation) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      this.show(await this.api.submitScorecard(this.id(), { ratings: this.ratings(), recommendation, comments: this.comments() || null }));
      this.notice.set('Thanks - your feedback is in.');
    } catch (error) {
      this.error.set(errorText(error, 'Your feedback could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  private show(view: MyInterviewView): void {
    this.view.set(view);
    this.ratings.set(view.myScorecard?.ratings ?? {});
    this.recommendation.set(view.myScorecard?.recommendation ?? '');
    this.comments.set(view.myScorecard?.comments ?? '');
  }
}
