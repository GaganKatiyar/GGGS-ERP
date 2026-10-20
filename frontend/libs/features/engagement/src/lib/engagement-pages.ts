import { DatePipe, KeyValuePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import type { WritableSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EngagementApi } from '@gggs/shared/experience';
import type { EngagementDashboard, MySurveyItem, OrgUnit, QuestionType, SurveyInput, SurveyItem, SurveyQuestion, SurveyResults, SurveyView } from '@gggs/shared/models';
import { QUESTION_TYPES } from '@gggs/shared/models';
import { OrgStructureApi } from '@gggs/shared/org-reference';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const today = () => new Date().toISOString().slice(0, 10);
const inDays = (days: number) => new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10);

const blankSurvey = (): SurveyInput => ({ title: '', description: null, isAnonymous: true, questions: [], audienceDepartmentIds: [], opensOn: today(), closesOn: inDays(14) });

/** The survey builder, shared by the list and the survey page. */
@Component({
  selector: 'gggs-survey-builder',
  imports: [],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="xp__grid">
      <label class="field xp__wide">
        <span class="field__label">Title</span>
        <input type="text" maxlength="200" [value]="form()().title" (input)="patch({ title: $any($event.target).value })" />
      </label>
      <label class="field xp__wide">
        <span class="field__label">Introduction</span>
        <textarea rows="2" maxlength="2000" [value]="form()().description ?? ''" (input)="patch({ description: $any($event.target).value || null })"></textarea>
      </label>
      <label class="field">
        <span class="field__label">Opens</span>
        <input type="date" [value]="form()().opensOn" (change)="patch({ opensOn: $any($event.target).value })" />
      </label>
      <label class="field">
        <span class="field__label">Closes</span>
        <input type="date" [value]="form()().closesOn" (change)="patch({ closesOn: $any($event.target).value })" />
      </label>
      <label class="xp__flag"><input type="checkbox" [checked]="form()().isAnonymous" (change)="patch({ isAnonymous: $any($event.target).checked })" /> Anonymous (results only for groups of 3 or more)</label>
      <fieldset class="xp__card xp__wide">
        <legend>Audience (none ticked means everyone)</legend>
        <div class="xp__grid">
          @for (department of departments(); track department.id) {
            <label class="xp__flag"><input type="checkbox" [checked]="form()().audienceDepartmentIds.includes(department.id)" (change)="toggleDepartment(department.id, $any($event.target).checked)" /> {{ department.name }}</label>
          }
        </div>
      </fieldset>
      <div class="xp__wide stack">
        <div class="toolbar">
          <strong>Questions</strong>
          <button type="button" class="btn btn--sm" (click)="usePulse()">Use the pulse template</button>
          <button type="button" class="btn btn--sm" (click)="addQuestion()">Add question</button>
        </div>
        @for (question of form()().questions; track $index) {
          <div class="xp__line">
            <label class="field">
              <span class="field__label">Type</span>
              <select [value]="question.type" (change)="patchQuestion($index, { type: $any($event.target).value })">
                @for (type of types; track type) {
                  <option [value]="type">{{ type === 'Enps' ? 'eNPS (0-10)' : type === 'Rating' ? 'Agreement (1-5)' : type }}</option>
                }
              </select>
            </label>
            <label class="field">
              <span class="field__label">Key</span>
              <input type="text" maxlength="40" [value]="question.key" (input)="patchQuestion($index, { key: $any($event.target).value })" />
            </label>
            <label class="field">
              <span class="field__label">Question</span>
              <input type="text" maxlength="500" [value]="question.text" (input)="patchQuestion($index, { text: $any($event.target).value })" />
            </label>
            <label class="field">
              <span class="field__label">Options</span>
              <input type="text" [disabled]="question.type !== 'Choice'" placeholder="Yes, No" [value]="(question.options ?? []).join(', ')" (change)="patchQuestion($index, { options: split($any($event.target).value) })" />
            </label>
            <label class="xp__flag"><input type="checkbox" [checked]="question.required" (change)="patchQuestion($index, { required: $any($event.target).checked })" /> Required</label>
            <button type="button" class="btn btn--link btn--sm" aria-label="Remove question" (click)="removeQuestion($index)"><i class="pi pi-trash" aria-hidden="true"></i></button>
          </div>
        } @empty {
          <p class="muted">No questions yet.</p>
        }
      </div>
    </div>
  `,
})
export class SurveyBuilder {
  private readonly api = inject(EngagementApi);

  readonly form = input.required<WritableSignal<SurveyInput>>();

  protected readonly types = QUESTION_TYPES;
  protected readonly departments = signal<readonly OrgUnit[]>([]);

  constructor() {
    inject(OrgStructureApi)
      .active('departments')
      .then((departments) => this.departments.set(departments), () => undefined);
  }

  protected split(value: string): string[] {
    return value
      .split(',')
      .map((part) => part.trim())
      .filter(Boolean);
  }

  protected patch(change: Partial<SurveyInput>): void {
    this.form().update((form) => ({ ...form, ...change }));
  }

  protected toggleDepartment(id: string, on: boolean): void {
    this.form().update((form) => ({ ...form, audienceDepartmentIds: on ? [...form.audienceDepartmentIds, id] : form.audienceDepartmentIds.filter((existing) => existing !== id) }));
  }

  protected async usePulse(): Promise<void> {
    const questions = await this.api.pulseTemplate();
    this.patch({ questions });
  }

  protected addQuestion(): void {
    this.form().update((form) => ({ ...form, questions: [...form.questions, { key: `q${form.questions.length + 1}`, text: '', type: 'Rating' as QuestionType, options: null, required: true }] }));
  }

  protected patchQuestion(index: number, change: Partial<SurveyQuestion>): void {
    this.form().update((form) => ({ ...form, questions: form.questions.map((question, at) => (at === index ? { ...question, ...change } : question)) }));
  }

  protected removeQuestion(index: number): void {
    this.form().update((form) => ({ ...form, questions: form.questions.filter((_, at) => at !== index) }));
  }
}

/** Engagement overview: eNPS over time, response rates and every survey. */
@Component({
  selector: 'gggs-engagement-page',
  imports: [DatePipe, RouterLink, PageHeader, KpiCard, StatusBadge, EmptyState, FormDialog, Skeleton, SurveyBuilder],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Engagement" subtitle="Pulse and engagement surveys, eNPS and what people say - safely anonymous.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="create()"><i class="pi pi-plus" aria-hidden="true"></i> New survey</button>
      }
    </gggs-page-header>
    @if (dashboard(); as d) {
      <div class="xp__kpis">
        <gggs-kpi-card label="Open surveys" [value]="d.openSurveys" />
        <gggs-kpi-card label="Latest eNPS" [value]="d.latestEnps !== undefined ? d.latestEnps : '—'" hint="-100 to 100" />
        <gggs-kpi-card label="Average response rate" [value]="d.averageResponseRate + '%'" />
      </div>
      @if (d.enpsTrend.length) {
        <section class="panel stack">
          <h2 class="xp__heading">eNPS trend</h2>
          @for (point of d.enpsTrend; track point.surveyId) {
            <div class="xp__row">
              <span><strong>{{ point.title }}</strong></span>
              <span class="muted">closes {{ point.closesOn | date: 'd MMM y' }}</span>
              <span>{{ point.responses }} responses</span>
              <span class="xp__bar"><span [style.width.%]="point.enps !== undefined ? (point.enps + 100) / 2 : 0"></span></span>
              <strong>{{ point.enps !== undefined ? point.enps : 'too few' }}</strong>
            </div>
          }
        </section>
      }
    } @else if (canSeeResults && !error()) {
      <gggs-skeleton [lines]="3" />
    }
    <section class="panel stack">
      @for (survey of surveys(); track survey.id) {
        <a class="xp__row" [routerLink]="['/engagement/surveys', survey.id]">
          <span><strong>{{ survey.title }}</strong> <span class="muted">{{ survey.isAnonymous ? 'anonymous' : 'named' }} · {{ survey.questions }} questions</span></span>
          <span>{{ survey.opensOn | date: 'd MMM' }} – {{ survey.closesOn | date: 'd MMM y' }}</span>
          <span>{{ survey.responseCount }} of {{ survey.invitedCount }} answered</span>
          <span class="xp__bar"><span [style.width.%]="survey.responseRate"></span></span>
          <gggs-status-badge [status]="survey.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-heart" title="No surveys yet" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>

    <gggs-form-dialog [(visible)]="builderOpen" title="New survey" saveLabel="Save draft" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().title.trim() || !form().questions.length" (save)="save()" width="min(64rem, 96vw)">
      <gggs-survey-builder [form]="form" />
    </gggs-form-dialog>
  `,
})
export class EngagementPage {
  private readonly api = inject(EngagementApi);
  private readonly permissions = inject(PermissionStore);

  protected readonly canManage = this.permissions.has('engagement.survey.manage');
  protected readonly canSeeResults = this.permissions.has('engagement.results.view');
  protected readonly dashboard = signal<EngagementDashboard | null>(null);
  protected readonly surveys = signal<readonly SurveyItem[]>([]);
  protected readonly builderOpen = signal(false);
  protected readonly form = signal<SurveyInput>(blankSurvey());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    if (this.canSeeResults) {
      this.api.dashboard().then((d) => this.dashboard.set(d), () => undefined);
    }

    this.load();
  }

  protected load(): void {
    this.api.surveys().then((surveys) => this.surveys.set(surveys), (error: unknown) => this.error.set(message(error, 'Surveys could not be loaded.')));
  }

  protected create(): void {
    this.form.set(blankSurvey());
    this.formError.set(null);
    this.builderOpen.set(true);
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.save(null, this.form());
      this.builderOpen.set(false);
      this.load();
    } catch (error) {
      this.formError.set(message(error, 'The survey could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** One survey: its questions, publishing and closing, and its results once enough people answer. */
@Component({
  selector: 'gggs-survey-page',
  imports: [DatePipe, KeyValuePipe, RouterLink, PageHeader, KpiCard, StatusBadge, FormDialog, Skeleton, SurveyBuilder],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="v.survey.title" [subtitle]="(v.survey.opensOn | date: 'd MMM') + ' – ' + (v.survey.closesOn | date: 'd MMM y') + (v.survey.isAnonymous ? ' · anonymous' : ' · named')">
        <a class="btn" routerLink="/engagement">All surveys</a>
        @if (canManage) {
          @if (v.survey.status === 'Draft') {
            <button type="button" class="btn" (click)="edit(v)">Edit</button>
            <button type="button" class="btn btn--primary" (click)="act('publish')">Publish</button>
          }
          @if (v.survey.status === 'Open' || v.survey.status === 'Scheduled') {
            <button type="button" class="btn" (click)="act('close')">Close now</button>
          }
        }
      </gggs-page-header>
      <div class="xp__kpis">
        <gggs-kpi-card label="Status" [value]="v.survey.status" />
        <gggs-kpi-card label="Invited" [value]="v.survey.invitedCount" />
        <gggs-kpi-card label="Answered" [value]="v.survey.responseCount" [hint]="v.survey.responseRate + '% response rate'" />
        @if (results(); as r) {
          <gggs-kpi-card label="eNPS" [value]="r.enps !== undefined ? r.enps : '—'" [hint]="r.promoters + ' promoters · ' + r.passives + ' passives · ' + r.detractors + ' detractors'" />
        }
      </div>
      @if (results(); as r) {
        @if (r.suppressed) {
          <section class="panel"><p class="muted">Results show once at least {{ r.minimumGroupSize }} people have answered, so no one can be singled out.</p></section>
        } @else {
          <section class="panel stack">
            @for (question of r.questions; track question.key) {
              <div class="xp__card">
                <strong>{{ question.text }}</strong>
                <span class="muted">{{ question.answers }} answers{{ question.average !== undefined ? ' · average ' + question.average : '' }}{{ question.enpsScore !== undefined ? ' · eNPS ' + question.enpsScore : '' }}</span>
                @for (entry of question.distribution | keyvalue; track entry.key) {
                  <div class="xp__row">
                    <span>{{ entry.key }}</span>
                    <span class="xp__bar"><span [style.width.%]="question.answers ? (entry.value * 100) / question.answers : 0"></span></span>
                    <span>{{ entry.value }}</span>
                  </div>
                }
                @for (comment of question.comments; track $index) {
                  <p class="xp__comment">“{{ comment }}”</p>
                }
              </div>
            }
          </section>
          <section class="panel stack">
            <h2 class="xp__heading">By department</h2>
            @for (group of r.byDepartment; track group.department) {
              <div class="xp__row">
                <span><strong>{{ group.department }}</strong></span>
                <span>{{ group.responses }} answers</span>
                <span>eNPS {{ group.enps !== undefined ? group.enps : '—' }}</span>
                <span>average agreement {{ group.averageRating !== undefined ? group.averageRating : '—' }}</span>
              </div>
            }
            @if (r.suppressedGroups) {
              <p class="muted">{{ r.suppressedGroups }} smaller group(s) are not shown.</p>
            }
          </section>
        }
      } @else {
        <section class="panel stack">
          <h2 class="xp__heading">Questions</h2>
          @for (question of v.questions; track question.key) {
            <div class="xp__row">
              <span><strong>{{ question.text }}</strong></span>
              <span class="muted">{{ question.type }}{{ question.required ? '' : ' · optional' }}</span>
              <span class="muted">{{ (question.options ?? []).join(', ') }}</span>
            </div>
          }
        </section>
      }
      <p><gggs-status-badge [status]="v.survey.status" /></p>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="builderOpen" title="Edit survey" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().title.trim() || !form().questions.length" (save)="save()" width="min(64rem, 96vw)">
      <gggs-survey-builder [form]="form" />
    </gggs-form-dialog>
  `,
})
export class SurveyPage {
  private readonly api = inject(EngagementApi);
  private readonly permissions = inject(PermissionStore);

  readonly id = input.required<string>();

  protected readonly canManage = this.permissions.has('engagement.survey.manage');
  private readonly canSeeResults = this.permissions.has('engagement.results.view');
  protected readonly view = signal<SurveyView | null>(null);
  protected readonly results = signal<SurveyResults | null>(null);
  protected readonly builderOpen = signal(false);
  protected readonly form = signal<SurveyInput>(blankSurvey());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    effect(() => {
      const id = this.id();
      this.api.survey(id).then(
        (view) => {
          this.view.set(view);
          if (this.canSeeResults && view.survey.status !== 'Draft') {
            this.api.results(id).then((results) => this.results.set(results), () => undefined);
          }
        },
        (error: unknown) => this.error.set(message(error, 'The survey could not be loaded.')),
      );
    });
  }

  protected edit(view: SurveyView): void {
    const s = view.survey;
    this.form.set({ title: s.title, description: s.description ?? null, isAnonymous: s.isAnonymous, questions: view.questions, audienceDepartmentIds: s.audienceDepartmentIds, opensOn: s.opensOn, closesOn: s.closesOn });
    this.formError.set(null);
    this.builderOpen.set(true);
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    try {
      this.view.set(await this.api.save(this.id(), this.form()));
      this.builderOpen.set(false);
    } catch (error) {
      this.formError.set(message(error, 'The survey could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async act(action: 'publish' | 'close'): Promise<void> {
    try {
      const view = await this.api.act(this.id(), action);
      this.view.set(view);
      if (this.canSeeResults && view.survey.status !== 'Draft') {
        this.results.set(await this.api.results(this.id()));
      }
    } catch (error) {
      this.error.set(message(error, 'The survey could not be changed.'));
    }
  }
}

/** Surveys waiting for me, answered in place. */
@Component({
  selector: 'gggs-my-surveys-page',
  imports: [DatePipe, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My surveys" subtitle="Tell us how it is. Anonymous surveys never record who answered what." />
    @for (survey of surveys(); track survey.id) {
      <section class="panel stack">
        <h2 class="xp__heading">{{ survey.title }}</h2>
        <p class="muted">Closes {{ survey.closesOn | date: 'd MMM y' }} · {{ survey.isAnonymous ? 'anonymous' : 'your name is recorded' }}</p>
        @if (survey.description) {
          <p>{{ survey.description }}</p>
        }
        @if (survey.responded || done().includes(survey.id)) {
          <p class="xp__notice">Thank you - your answers are in.</p>
        } @else {
          @for (question of survey.questions; track question.key) {
            <div class="field">
              <span class="field__label">{{ question.text }}{{ question.required ? '' : ' (optional)' }}</span>
              @switch (question.type) {
                @case ('Enps') {
                  <div class="toolbar">
                    @for (score of enpsScale; track score) {
                      <button type="button" class="btn btn--sm" [class.btn--primary]="answer(survey.id, question.key) === '' + score" (click)="setAnswer(survey.id, question.key, '' + score)">{{ score }}</button>
                    }
                  </div>
                }
                @case ('Rating') {
                  <div class="toolbar">
                    @for (score of ratingScale; track score) {
                      <button type="button" class="btn btn--sm" [class.btn--primary]="answer(survey.id, question.key) === '' + score" (click)="setAnswer(survey.id, question.key, '' + score)">{{ ratingLabels[score - 1] }}</button>
                    }
                  </div>
                }
                @case ('Choice') {
                  <div class="toolbar">
                    @for (option of question.options ?? []; track option) {
                      <button type="button" class="btn btn--sm" [class.btn--primary]="answer(survey.id, question.key) === option" (click)="setAnswer(survey.id, question.key, option)">{{ option }}</button>
                    }
                  </div>
                }
                @default {
                  <textarea rows="3" maxlength="2000" [value]="answer(survey.id, question.key)" (input)="setAnswer(survey.id, question.key, $any($event.target).value)"></textarea>
                }
              }
            </div>
          }
          <div class="toolbar">
            <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="submit(survey)">Submit</button>
          </div>
        }
      </section>
    } @empty {
      <section class="panel"><gggs-empty-state icon="pi pi-heart" title="No surveys right now" /></section>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
})
export class MySurveysPage {
  private readonly api = inject(EngagementApi);

  protected readonly enpsScale = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
  protected readonly ratingScale = [1, 2, 3, 4, 5];
  protected readonly ratingLabels = ['Strongly disagree', 'Disagree', 'Neutral', 'Agree', 'Strongly agree'];
  protected readonly surveys = signal<readonly MySurveyItem[]>([]);
  protected readonly answers = signal<Readonly<Record<string, Readonly<Record<string, string>>>>>({});
  protected readonly done = signal<readonly string[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.api.mine().then((surveys) => this.surveys.set(surveys), (error: unknown) => this.error.set(message(error, 'Your surveys could not be loaded.')));
  }

  protected answer(surveyId: string, key: string): string {
    return this.answers()[surveyId]?.[key] ?? '';
  }

  protected setAnswer(surveyId: string, key: string, value: string): void {
    this.answers.update((all) => ({ ...all, [surveyId]: { ...(all[surveyId] ?? {}), [key]: value } }));
  }

  protected async submit(survey: MySurveyItem): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.respond(survey.id, this.answers()[survey.id] ?? {});
      this.done.update((done) => [...done, survey.id]);
    } catch (error) {
      this.error.set(message(error, 'Your answers could not be sent.'));
    } finally {
      this.busy.set(false);
    }
  }
}
