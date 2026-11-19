import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HelpCenter } from './help-center';
import { findTopic, helpText } from './help-engine';
import type { HelpAudience, HelpTopic } from './help-model';

/** Guide text with the names of things on the screen shown as chips. */
@Component({
  selector: 'gggs-help-text',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@for (part of parts(); track $index) {@if (part.ui) {<span class="help-ui">{{ part.text }}</span>} @else {{{ part.text }}}}`,
  styles: `
    .help-ui {
      display: inline-block;
      padding: 0 0.35rem;
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-xs);
      background: var(--gggs-color-surface-sunken);
      font-weight: var(--gggs-font-weight-semibold);
      font-size: 0.95em;
      line-height: 1.5;
    }
  `,
})
export class HelpText {
  readonly text = input.required<string>();
  protected readonly parts = computed(() => helpText(this.text()));
}

const AUDIENCE_LABELS: Record<HelpAudience, string> = {
  everyone: 'Everyone',
  employee: 'Employees',
  manager: 'Managers',
  admin: 'HR & admins',
};

/** One help page: the guide (or the short description), the organisation's note, and feedback. */
@Component({
  selector: 'gggs-help-article',
  imports: [RouterLink, HelpText],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let t = topic();
    <article class="help-article" [class.help-article--compact]="compact()" [attr.aria-labelledby]="'help-title-' + t.id">
      <header class="help-article__head">
        <p class="help-article__group">{{ t.group }}</p>
        <h2 [id]="'help-title-' + t.id">{{ t.title }}</h2>
        <div class="help-article__meta">
          <span class="badge badge--info">{{ audience(t.audience) }}</span>
          @if (t.path) {
            <span class="muted text-sm">Menu: {{ t.path }}</span>
          }
        </div>
      </header>

      @if (t.guide; as guide) {
        <p class="help-article__lead"><gggs-help-text [text]="guide.lead" /></p>

        @if (guide.before?.length) {
          <section class="help-article__section">
            <h3>Before you start</h3>
            <ul>
              @for (item of guide.before; track $index) {
                <li><gggs-help-text [text]="item" /></li>
              }
            </ul>
          </section>
        }

        <section class="help-article__section">
          <h3>Step by step</h3>
          <ol class="help-steps">
            @for (step of guide.steps; track $index) {
              <li><span><gggs-help-text [text]="step" /></span></li>
            }
          </ol>
        </section>

        @if (guide.tip) {
          <p class="help-tip"><strong>Tip</strong> <span><gggs-help-text [text]="guide.tip" /></span></p>
        }

        @if (!compact() && guide.terms?.length) {
          <section class="help-article__section">
            <h3>Words on this screen</h3>
            <dl class="help-terms">
              @for (term of guide.terms; track term[0]) {
                <div><dt>{{ term[0] }}</dt><dd>{{ term[1] }}</dd></div>
              }
            </dl>
          </section>
        }

        @if (!compact() && guide.faqs?.length) {
          <section class="help-article__section">
            <h3>Common questions</h3>
            @for (faq of guide.faqs; track faq[0]) {
              <details class="help-faq">
                <summary>{{ faq[0] }}</summary>
                <p>{{ faq[1] }}</p>
              </details>
            }
          </section>
        }
      } @else {
        <p class="help-article__lead">{{ t.summary }}</p>
        <p class="muted text-sm">This screen has no step-by-step guide yet. If you get stuck, ask the help assistant or your HR team.</p>
      }

      <section class="help-note" aria-label="In our company">
        @if (editing()) {
          <label class="field">
            <span class="field__label">In our company</span>
            <textarea rows="4" maxlength="1000" [value]="draft()" (input)="draft.set($any($event.target).value)"></textarea>
          </label>
          @if (noteError()) {
            <p class="alert alert--danger" role="alert">{{ noteError() }}</p>
          }
          <div class="help-row">
            <button type="button" class="btn btn--primary btn--sm" [disabled]="saving()" (click)="saveNote()">Save note</button>
            <button type="button" class="btn btn--sm" (click)="editing.set(false)">Cancel</button>
          </div>
        } @else if (note()) {
          <p class="help-note__label">In our company</p>
          <p class="help-note__text">{{ note() }}</p>
          @if (center.canEditNotes()) {
            <button type="button" class="btn btn--link btn--sm" (click)="startEdit()">Edit note</button>
          }
        } @else if (center.canEditNotes() && !compact()) {
          <p class="help-note__label">In our company</p>
          <p class="muted text-sm">Add a short note about your company's own rules for this screen. Everyone in your organisation will see it here.</p>
          <button type="button" class="btn btn--sm" (click)="startEdit()">Add a note</button>
        }
      </section>

      @if (!compact()) {
        @if (related().length) {
          <section class="help-article__section">
            <h3>Related</h3>
            <div class="help-row">
              @for (r of related(); track r.id) {
                <a class="help-chip" [routerLink]="['/help']" [queryParams]="{ topic: r.id }">{{ r.title }}</a>
              }
            </div>
          </section>
        }
      }

      <footer class="help-article__foot">
        @if (t.route && t.route !== '/help') {
          <a class="btn btn--primary btn--sm" [routerLink]="t.route" (click)="navigated.emit()">Open this screen</a>
        }
        <span class="help-row muted text-sm" aria-live="polite">
          @if (rated() === null) {
            Was this helpful?
            <button type="button" class="btn btn--sm" (click)="rate(true)">Yes</button>
            <button type="button" class="btn btn--sm" (click)="rate(false)">No</button>
          } @else {
            Thanks for telling us.
          }
        </span>
      </footer>
    </article>
  `,
  styles: `
    .help-article { display: grid; gap: var(--gggs-space-5); min-width: 0; }
    .help-article--compact { gap: var(--gggs-space-4); }
    .help-article__head { display: grid; gap: var(--gggs-space-2); }
    .help-article__group { margin: 0; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); text-transform: uppercase; letter-spacing: 0.06em; font-weight: var(--gggs-font-weight-semibold); }
    .help-article h2 { margin: 0; font-size: var(--gggs-font-size-xl); }
    .help-article--compact h2 { font-size: var(--gggs-font-size-lg); }
    .help-article__meta { display: flex; flex-wrap: wrap; gap: var(--gggs-space-2); align-items: center; }
    .help-article__lead { margin: 0; font-size: var(--gggs-font-size-md); max-width: 65ch; }
    .help-article__section { display: grid; gap: var(--gggs-space-2); max-width: 72ch; }
    .help-article__section h3 { margin: 0; font-size: var(--gggs-font-size-base); }
    .help-article__section ul { margin: 0; padding-inline-start: 1.2rem; display: grid; gap: var(--gggs-space-1); }
    .help-steps { list-style: none; margin: 0; padding: 0; counter-reset: step; display: grid; gap: var(--gggs-space-3); }
    .help-steps li { counter-increment: step; display: grid; grid-template-columns: 1.75rem 1fr; gap: var(--gggs-space-2); align-items: start; }
    .help-steps li::before { content: counter(step); width: 1.6rem; height: 1.6rem; border-radius: 50%; display: grid; place-items: center; background: var(--gggs-color-brand); color: #fff; font-size: var(--gggs-font-size-xs); font-weight: var(--gggs-font-weight-semibold); }
    .help-tip { margin: 0; display: flex; gap: var(--gggs-space-2); padding: var(--gggs-space-3); border-radius: var(--gggs-radius-md); background: var(--gggs-color-success-subtle); }
    .help-tip strong { color: var(--gggs-color-success); }
    .help-terms { margin: 0; display: grid; gap: 0; border-top: 1px solid var(--gggs-color-border); }
    .help-terms div { display: grid; grid-template-columns: minmax(8rem, 12rem) 1fr; gap: var(--gggs-space-3); padding: var(--gggs-space-2) 0; border-bottom: 1px solid var(--gggs-color-border); }
    .help-terms dt { font-weight: var(--gggs-font-weight-semibold); }
    .help-terms dd { margin: 0; }
    .help-faq { border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-md); padding: 0 var(--gggs-space-3); }
    .help-faq summary { cursor: pointer; padding: var(--gggs-space-2) 0; font-weight: var(--gggs-font-weight-medium); }
    .help-faq p { margin: 0 0 var(--gggs-space-3); }
    .help-note { display: grid; gap: var(--gggs-space-2); }
    .help-note:empty { display: none; }
    .help-note__label { margin: 0; font-weight: var(--gggs-font-weight-semibold); font-size: var(--gggs-font-size-sm); }
    .help-note__text { margin: 0; padding: var(--gggs-space-3); border-radius: var(--gggs-radius-md); background: var(--gggs-color-warning-subtle); white-space: pre-line; }
    .help-row { display: flex; flex-wrap: wrap; gap: var(--gggs-space-2); align-items: center; }
    .help-chip { padding: 0.2rem 0.7rem; border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-pill); color: var(--gggs-color-text); text-decoration: none; font-size: var(--gggs-font-size-sm); }
    .help-chip:hover { background: var(--gggs-color-surface-hover); }
    .help-article__foot { display: flex; flex-wrap: wrap; gap: var(--gggs-space-3); justify-content: space-between; align-items: center; padding-top: var(--gggs-space-3); border-top: 1px solid var(--gggs-color-border); }
    @media (max-width: 40rem) { .help-terms div { grid-template-columns: 1fr; gap: 0; } }
  `,
})
export class HelpArticle {
  protected readonly center = inject(HelpCenter);

  readonly topic = input.required<HelpTopic>();
  /** The help panel's shorter version: no glossary, questions or related pages. */
  readonly compact = input(false);
  /** Fired when "Open this screen" is pressed, so a panel can close itself. */
  readonly navigated = output<void>();

  protected readonly editing = signal(false);
  protected readonly draft = signal('');
  protected readonly saving = signal(false);
  protected readonly noteError = signal<string | null>(null);
  private readonly ratedFor = signal<{ id: string; helpful: boolean } | null>(null);

  protected readonly note = computed(() => this.center.notes()[this.topic().id] ?? '');
  protected readonly rated = computed(() => {
    const rated = this.ratedFor();
    return rated && rated.id === this.topic().id ? rated.helpful : null;
  });
  protected readonly related = computed(() =>
    (this.topic().guide?.related ?? [])
      .map((key) => findTopic(key, this.center.topics()))
      .filter((topic): topic is HelpTopic => !!topic),
  );

  protected audience(audience: HelpAudience): string {
    return AUDIENCE_LABELS[audience];
  }

  protected startEdit(): void {
    this.draft.set(this.note());
    this.noteError.set(null);
    this.editing.set(true);
  }

  protected async saveNote(): Promise<void> {
    this.saving.set(true);
    this.noteError.set(null);
    try {
      await this.center.saveNote(this.topic().id, this.draft().trim());
      this.editing.set(false);
    } catch {
      this.noteError.set('The note could not be saved. Try again.');
    } finally {
      this.saving.set(false);
    }
  }

  protected rate(helpful: boolean): void {
    this.ratedFor.set({ id: this.topic().id, helpful });
    this.center.feedback('PageRated', this.topic().id, null, helpful);
  }
}
