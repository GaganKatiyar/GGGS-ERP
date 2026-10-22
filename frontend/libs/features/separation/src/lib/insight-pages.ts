import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { SeparationApi } from '@gggs/shared/experience';
import type { AttritionAnalytics, CountItem, ExitQuestion, ExitQuestionType, ExitSurvey } from '@gggs/shared/models';
import { Chart, EmptyState, KpiCard, PageHeader, Skeleton } from '@gggs/shared/ui';
import type { ChartSeries } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const percent = (value: number | null | undefined) => (value === null || value === undefined ? '-' : `${value}%`);

/** Attrition over a window: rates, regretted losses, reasons, tenure, hot spots, deadlines and survey results. */
@Component({
  selector: 'gggs-attrition-page',
  imports: [PageHeader, KpiCard, Skeleton, EmptyState, Chart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Attrition" subtitle="Who left, why, and what the exit survey says - within the people you can see.">
      <label class="field">
        <span class="field__label">Period</span>
        <select [value]="'' + months()" (change)="setMonths(+$any($event.target).value)">
          <option value="3">Last 3 months</option>
          <option value="6">Last 6 months</option>
          <option value="12">Last 12 months</option>
          <option value="24">Last 24 months</option>
        </select>
      </label>
    </gggs-page-header>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    @if (data(); as d) {
      <div class="xp__kpis">
        <gggs-kpi-card label="Attrition (annualised)" [value]="d.attritionRate + '%'" [hint]="d.exits + ' exits, ' + d.headcount + ' on the books'" />
        <gggs-kpi-card label="Voluntary / involuntary" [value]="d.voluntary + ' / ' + d.involuntary" [hint]="d.natural + ' retirements, contract ends and deaths'" />
        <gggs-kpi-card label="Regretted" [value]="pct(d.regrettedPercent)" [hint]="d.classified ? d.regretted + ' of ' + d.classified + ' classified resignations' : 'no resignations classified yet'" />
        <gggs-kpi-card label="Left within 6 months" [value]="d.earlyExitPercent + '%'" [hint]="d.earlyExits + ' early exits'" />
        <gggs-kpi-card label="Notice served in full" [value]="pct(d.noticeServedPercent)" hint="of resignations" />
        <gggs-kpi-card label="Settlement turnaround" [value]="d.averageSettlementDays !== null && d.averageSettlementDays !== undefined ? d.averageSettlementDays + ' days' : '-'" [hint]="d.settlementsLate + ' past the legal deadline'" />
      </div>
      @if (d.exits) {
        <section class="panel stack">
          <gggs-chart kind="bar" title="Exits by month" [labels]="labels(d.trend)" [series]="series('Exits', d.trend)" height="14rem" />
        </section>
        <div class="xp__grid">
          <section class="panel stack">
            <gggs-chart kind="bar" title="Why people left" [labels]="labels(d.reasons)" [series]="series('Exits', d.reasons)" />
          </section>
          <section class="panel stack">
            <gggs-chart kind="doughnut" title="Tenure at exit" [labels]="labels(d.tenure)" [series]="series('Exits', d.tenure)" />
          </section>
          <section class="panel stack">
            <h2 class="xp__heading">Departments</h2>
            @for (row of d.departments; track row.label) {
              <div class="xp__row"><span>{{ row.label }}</span><span><strong>{{ row.count }}</strong></span></div>
            }
          </section>
          <section class="panel stack">
            <h2 class="xp__heading">Managers</h2>
            @for (row of d.managers; track row.label) {
              <div class="xp__row"><span>{{ row.label }}</span><span><strong>{{ row.count }}</strong></span></div>
            }
          </section>
        </div>
        <section class="panel stack">
          <h2 class="xp__heading">Exit survey</h2>
          @for (score of d.survey; track score.question) {
            <div class="xp__row">
              <span>{{ score.question }}</span>
              <span><strong>{{ score.kind === 'rating' ? score.value + ' / 5' : score.value + '% yes' }}</strong> <span class="muted">{{ score.responses }} answers</span></span>
            </div>
          } @empty {
            <p class="muted">No survey answers from these leavers yet.</p>
          }
        </section>
      } @else {
        <gggs-empty-state icon="pi pi-chart-line" title="No exits in this period" description="Pick a longer period, or come back once separations complete." />
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
  `,
})
export class AttritionPage {
  private readonly api = inject(SeparationApi);

  protected readonly pct = percent;
  protected readonly months = signal(12);
  protected readonly data = signal<AttritionAnalytics | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected setMonths(months: number): void {
    this.months.set(months);
    void this.load();
  }

  protected labels(items: readonly CountItem[]): readonly string[] {
    return items.map((item) => item.label);
  }

  protected series(label: string, items: readonly CountItem[]): readonly ChartSeries[] {
    return [{ label, values: items.map((item) => item.count) }];
  }

  private async load(): Promise<void> {
    this.error.set(null);
    try {
      this.data.set(await this.api.analytics(this.months()));
    } catch (error) {
      this.error.set(message(error, 'Attrition could not be loaded.'));
    }
  }
}

const TYPES: readonly ExitQuestionType[] = ['Rating', 'YesNo', 'Choice', 'Text'];
const typeLabel = (type: ExitQuestionType) => ({ Rating: 'Rating 1-5', YesNo: 'Yes or no', Choice: 'One of a list', Text: 'Free text' })[type];

/** HR's exit survey: the questions leavers answer from My resignation. */
@Component({
  selector: 'gggs-exit-survey-page',
  imports: [PageHeader, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Exit survey" subtitle="What leavers are asked before they go. Answers feed the attrition page; leavers can hide theirs from their manager." />
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    @if (saved()) {
      <p class="alert alert--success" role="status">Survey saved.</p>
    }
    @if (draft(); as d) {
      <form class="panel stack" (submit)="$event.preventDefault(); save()">
        <div class="form-grid">
          <label class="field"><span class="field__label">Title</span><input type="text" maxlength="200" [value]="d.title" (input)="patch({ title: $any($event.target).value })" /></label>
          <label class="field form-grid__full"><span class="field__label">Introduction</span><textarea rows="2" maxlength="1000" [value]="d.introduction ?? ''" (input)="patch({ introduction: $any($event.target).value })"></textarea></label>
        </div>
        <h2 class="xp__heading">Questions</h2>
        @for (question of d.questions; track $index; let i = $index) {
          <div class="toolbar">
            <label class="field" style="flex: 1 1 24rem"><span class="field__label">Question {{ i + 1 }}</span><input type="text" maxlength="300" [value]="question.text" (input)="question$(i, { text: $any($event.target).value })" /></label>
            <label class="field">
              <span class="field__label">Answer</span>
              <select (change)="question$(i, { type: $any($event.target).value })">
                @for (type of types; track type) {
                  <option [value]="type" [selected]="type === question.type">{{ label(type) }}</option>
                }
              </select>
            </label>
            @if (question.type === 'Choice') {
              <label class="field"><span class="field__label">Options (comma-separated)</span><input type="text" [value]="(question.options ?? []).join(', ')" (change)="question$(i, { options: options($any($event.target).value) })" /></label>
            }
            <label class="xp__flag"><input type="checkbox" [checked]="question.required" (change)="question$(i, { required: $any($event.target).checked })" /> Required</label>
            <button type="button" class="btn btn--sm btn--link" [attr.aria-label]="'Remove question ' + (i + 1)" (click)="remove(i)">Remove</button>
          </div>
        }
        <div class="toolbar">
          <button type="button" class="btn btn--sm" (click)="add()">Add question</button>
        </div>
        <div class="form-actions">
          <button type="submit" class="btn btn--primary" [disabled]="busy() || !canSave()">Save survey</button>
        </div>
      </form>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
  `,
})
export class ExitSurveyPage {
  private readonly api = inject(SeparationApi);

  protected readonly types = TYPES;
  protected readonly label = typeLabel;
  protected readonly draft = signal<ExitSurvey | null>(null);
  protected readonly canSave = computed(() => {
    const draft = this.draft();
    return !!draft && !!draft.title.trim() && draft.questions.length > 0 && draft.questions.every((question) => question.text.trim());
  });
  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.api.exitSurvey().then(
      (survey) => this.draft.set(survey),
      (error: unknown) => this.error.set(message(error, 'The survey could not be loaded.')),
    );
  }

  protected options(text: string): readonly string[] {
    return text.split(',').map((option) => option.trim()).filter(Boolean);
  }

  protected patch(change: Partial<ExitSurvey>): void {
    const draft = this.draft();
    if (draft) {
      this.draft.set({ ...draft, ...change });
      this.saved.set(false);
    }
  }

  protected question$(index: number, change: Partial<ExitQuestion>): void {
    const draft = this.draft();
    if (draft) {
      this.patch({ questions: draft.questions.map((question, i) => (i === index ? { ...question, ...change } : question)) });
    }
  }

  protected add(): void {
    const draft = this.draft();
    if (draft) {
      const used = new Set(draft.questions.map((question) => question.key));
      let n = draft.questions.length + 1;
      while (used.has(`q${n}`)) {
        n++;
      }

      this.patch({ questions: [...draft.questions, { key: `q${n}`, text: '', type: 'Rating', options: null, required: false }] });
    }
  }

  protected remove(index: number): void {
    const draft = this.draft();
    if (draft) {
      this.patch({ questions: draft.questions.filter((_, i) => i !== index) });
    }
  }

  protected async save(): Promise<void> {
    const draft = this.draft();
    if (!draft) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      this.draft.set(await this.api.updateExitSurvey(draft));
      this.saved.set(true);
    } catch (error) {
      this.error.set(message(error, 'The survey could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}
