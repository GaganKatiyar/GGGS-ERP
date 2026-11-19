import { ChangeDetectionStrategy, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HelpText } from './help-article';
import { type CompanyAnswer, HelpCenter } from './help-center';
import { answerFor, plainText } from './help-engine';
import type { HelpTopic } from './help-model';

/** One message in the conversation. */
interface AssistantMessage {
  readonly id: number;
  readonly from: 'me' | 'bot';
  /** Paragraphs; [[...]] marks names on the screen. */
  readonly paragraphs: readonly string[];
  readonly question?: string;
  readonly topic?: HelpTopic;
  readonly company?: readonly CompanyAnswer[];
  readonly dependsOnPolicy?: boolean;
  readonly unanswered?: boolean;
  rated?: boolean;
}

/**
 * The help assistant: ready questions and typed ones, answered from the Help & Wiki and, where the
 * person may read them, the organisation's knowledge base and policies. No AI and nothing personal:
 * it matches words against help that is the same for everyone. When it has no answer it hands over
 * to a person (a helpdesk ticket with the question filled in) and tells us, so we can write that guide.
 */
@Component({
  selector: 'gggs-help-assistant',
  imports: [RouterLink, HelpText],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'center.assistantOpen.set(false)' },
  template: `
    @if (center.assistantOpen()) {
      <section class="assistant" role="dialog" aria-label="Help assistant" aria-modal="false">
        <header class="assistant__head">
          <span class="assistant__mark" aria-hidden="true"><i class="pi pi-question"></i></span>
          <div>
            <strong>Help assistant</strong>
            <span>Answers from the Help & Wiki</span>
          </div>
          <button type="button" class="assistant__close" aria-label="Close the assistant" title="Close" (click)="center.assistantOpen.set(false)">
            <i class="pi pi-times" aria-hidden="true"></i>
          </button>
        </header>

        <div class="assistant__log" #log aria-live="polite">
          @for (message of messages(); track message.id) {
            <div class="msg" [class.msg--me]="message.from === 'me'">
              @for (paragraph of message.paragraphs; track $index) {
                <p><gggs-help-text [text]="paragraph" /></p>
              }
              @if (message.dependsOnPolicy) {
                <p class="muted text-sm">This also depends on your company's own rules.</p>
              }
              @if (message.company?.length) {
                <div class="msg__company">
                  <p class="text-sm"><strong>From your company</strong></p>
                  @for (hit of message.company; track hit.title) {
                    <a [routerLink]="hit.link" (click)="center.assistantOpen.set(false)">
                      <i class="pi" [class.pi-book]="hit.kind === 'article'" [class.pi-file]="hit.kind === 'policy'" aria-hidden="true"></i>
                      {{ hit.title }}
                    </a>
                  }
                </div>
              }
              @if (message.unanswered) {
                <div class="msg__actions">
                  @if (center.canRaiseTicket()) {
                    <button type="button" class="btn btn--primary btn--sm" (click)="center.raiseTicket(message.question ?? '')">Raise a helpdesk ticket</button>
                  }
                  <a class="btn btn--sm" routerLink="/help" [queryParams]="{ q: message.question }" (click)="center.assistantOpen.set(false)">Search Help & Wiki</a>
                </div>
              }
              @if (message.topic) {
                <div class="msg__source">
                  <span>From:</span>
                  <a routerLink="/help" [queryParams]="{ topic: message.topic.id }" (click)="center.assistantOpen.set(false)">{{ message.topic.title }}</a>
                  @if (message.question) {
                    <span class="msg__rate">
                      @if (message.rated === undefined) {
                        <button type="button" class="btn btn--sm" (click)="rate(message, true)" aria-label="This answer helped">Helpful</button>
                        <button type="button" class="btn btn--sm" (click)="rate(message, false)" aria-label="This answer did not help">Not helpful</button>
                      } @else {
                        <span class="muted">Thanks</span>
                      }
                    </span>
                  }
                </div>
              }
            </div>
          }
          @if (thinking()) {
            <div class="msg"><span class="typing" aria-label="Looking for an answer"><i></i><i></i><i></i></span></div>
          }
        </div>

        @if (center.questions().length) {
          <div class="assistant__suggest" aria-label="Ready questions">
            @for (q of center.questions(); track q.question) {
              <button type="button" class="assistant__chip" (click)="ask(q.question)">{{ q.question }}</button>
            }
          </div>
        }

        <form class="assistant__form" (submit)="submit($event)">
          <label class="sr-only" for="help-assistant-input">Type your question</label>
          <input id="help-assistant-input" #input type="text" maxlength="300" autocomplete="off" placeholder="Type your question..." [value]="draft()" (input)="draft.set($any($event.target).value)" />
          <button type="submit" class="btn btn--primary" [disabled]="!draft().trim()">Ask</button>
        </form>
        <p class="assistant__note">Answers come from the help pages and your company's knowledge base. The assistant never reads your pay, leave or other personal records.</p>
      </section>
    } @else {
      <button type="button" class="assistant-fab" (click)="open()" aria-haspopup="dialog">
        <i class="pi pi-comments" aria-hidden="true"></i><span class="assistant-fab__label">Ask for help</span>
      </button>
    }
  `,
  styles: `
    .assistant-fab {
      position: fixed;
      inset-inline-end: max(1.25rem, calc(env(safe-area-inset-right, 0px) + 1rem));
      inset-block-end: max(1.25rem, calc(env(safe-area-inset-bottom, 0px) + 1rem));
      z-index: var(--gggs-z-sticky);
      display: inline-flex;
      align-items: center;
      gap: var(--gggs-space-2);
      padding: 0.65rem 1rem;
      border: 0;
      border-radius: var(--gggs-radius-pill);
      background: var(--gggs-color-primary);
      color: var(--gggs-color-primary-contrast);
      font-weight: var(--gggs-font-weight-semibold);
      box-shadow: var(--gggs-shadow-lg);
      cursor: pointer;
    }
    .assistant-fab:hover { background: var(--gggs-color-primary-hover); }
    .assistant {
      position: fixed;
      inset-inline-end: max(1.25rem, calc(env(safe-area-inset-right, 0px) + 1rem));
      inset-block-end: max(1.25rem, calc(env(safe-area-inset-bottom, 0px) + 1rem));
      z-index: var(--gggs-z-modal);
      width: min(24rem, calc(100vw - 2rem));
      height: min(36rem, calc(100vh - 6rem));
      display: flex;
      flex-direction: column;
      overflow: hidden;
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-xl);
      background: var(--gggs-color-surface-raised);
      box-shadow: var(--gggs-shadow-lg);
    }
    .assistant__head { display: flex; align-items: center; gap: var(--gggs-space-3); padding: var(--gggs-space-3) var(--gggs-space-4); background: var(--gggs-color-nav-bg); color: var(--gggs-color-nav-text-active); }
    .assistant__head div { display: grid; }
    .assistant__head span { color: var(--gggs-color-nav-text); font-size: var(--gggs-font-size-xs); }
    .assistant__mark { width: 2rem; height: 2rem; border-radius: var(--gggs-radius-md); display: grid; place-items: center; background: var(--gggs-color-nav-active); }
    .assistant__close { margin-inline-start: auto; width: 2rem; height: 2rem; border: 0; border-radius: var(--gggs-radius-md); background: rgb(255 255 255 / 12%); color: inherit; cursor: pointer; }
    .assistant__log { flex: 1; overflow-y: auto; display: flex; flex-direction: column; gap: var(--gggs-space-3); padding: var(--gggs-space-4); background: var(--gggs-color-surface); }
    .msg { align-self: flex-start; max-width: 90%; display: grid; gap: var(--gggs-space-2); padding: var(--gggs-space-3); border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-lg); background: var(--gggs-color-surface-raised); font-size: var(--gggs-font-size-sm); }
    .msg p { margin: 0; }
    .msg--me { align-self: flex-end; border-color: transparent; background: var(--gggs-color-brand); color: #fff; }
    .msg__company, .msg__source { display: flex; flex-wrap: wrap; gap: var(--gggs-space-1) var(--gggs-space-2); align-items: center; padding-top: var(--gggs-space-2); border-top: 1px solid var(--gggs-color-border); }
    .msg__company { flex-direction: column; align-items: flex-start; }
    .msg a { color: var(--gggs-color-accent); }
    .msg__actions { display: flex; flex-wrap: wrap; gap: var(--gggs-space-2); }
    .msg__source { color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    .msg__rate { margin-inline-start: auto; display: inline-flex; gap: var(--gggs-space-1); }
    .typing { display: inline-flex; gap: 4px; }
    .typing i { width: 6px; height: 6px; border-radius: 50%; background: var(--gggs-color-text-muted); animation: help-blink 1s infinite; }
    .typing i:nth-child(2) { animation-delay: 0.15s; }
    .typing i:nth-child(3) { animation-delay: 0.3s; }
    @keyframes help-blink { 0%, 80%, 100% { opacity: 0.25; } 40% { opacity: 1; } }
    @media (prefers-reduced-motion: reduce) { .typing i { animation: none; opacity: 0.6; } }
    .assistant__suggest { display: flex; gap: var(--gggs-space-2); overflow-x: auto; padding: var(--gggs-space-2) var(--gggs-space-3) 0; border-top: 1px solid var(--gggs-color-border); }
    .assistant__chip { flex: none; padding: 0.25rem 0.7rem; border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-pill); background: var(--gggs-color-surface-raised); color: var(--gggs-color-text); font-size: var(--gggs-font-size-xs); white-space: nowrap; cursor: pointer; }
    .assistant__chip:hover { background: var(--gggs-color-surface-hover); }
    .assistant__form { display: flex; gap: var(--gggs-space-2); padding: var(--gggs-space-2) var(--gggs-space-3); }
    .assistant__form input { flex: 1; min-width: 0; }
    .assistant__note { margin: 0; padding: 0 var(--gggs-space-3) var(--gggs-space-2); color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    @media (max-width: 40rem) { .assistant-fab__label { display: none; } }
  `,
})
export class HelpAssistant {
  protected readonly center = inject(HelpCenter);
  private readonly log = viewChild<ElementRef<HTMLElement>>('log');
  private readonly input = viewChild<ElementRef<HTMLInputElement>>('input');

  private nextId = 1;
  protected readonly messages = signal<readonly AssistantMessage[]>([]);
  protected readonly draft = signal('');
  protected readonly thinking = signal(false);

  protected async open(): Promise<void> {
    await this.center.openAssistant();
    if (!this.messages().length) {
      this.push({ from: 'bot', paragraphs: ['Hello. I can help you use Nuts and Bolts.', 'Pick a question below, or type your own, for example "how do I apply for leave".'] });
    }
    this.scroll();
    setTimeout(() => this.input()?.nativeElement.focus({ preventScroll: true }));
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const text = this.draft().trim();
    this.draft.set('');
    void this.ask(text);
  }

  protected async ask(text: string): Promise<void> {
    if (!text) {
      return;
    }
    if (!this.messages().length) {
      await this.open();
    }
    this.push({ from: 'me', paragraphs: [text] });
    this.thinking.set(true);
    this.scroll();

    try {
      await this.center.load();
      const answer = answerFor(text, this.center.content()?.questions ?? [], this.center.topics());
      const needsCompany = answer.kind !== 'question' || answer.question.dependsOnPolicy === true;
      const company = needsCompany ? await this.center.companyAnswers(text) : [];

      if (answer.kind === 'question') {
        this.push({ from: 'bot', paragraphs: answer.question.answer, topic: answer.topic, question: text, company, dependsOnPolicy: answer.question.dependsOnPolicy });
      } else if (answer.kind === 'topic') {
        const steps = answer.topic.guide ? answer.topic.guide.steps.slice(0, 3) : [];
        this.push({
          from: 'bot',
          paragraphs: [`This help page looks right: [[${answer.topic.title}]]. ${plainText(answer.topic.summary)}`, ...steps],
          topic: answer.topic,
          question: text,
          company,
        });
      } else if (company.length) {
        this.push({ from: 'bot', paragraphs: ['I have no guide for that, but your company has written about it:'], company, question: text });
      } else {
        this.center.feedback('Unanswered', null, text, null);
        this.push({
          from: 'bot',
          paragraphs: [
            'Sorry, I could not find an answer to that.',
            this.center.canRaiseTicket() ? 'Try a ready question below, or raise a helpdesk ticket and a person will help. Your question is filled in for you.' : 'Try a ready question below, or ask your HR team.',
          ],
          question: text,
          unanswered: true,
        });
      }
    } finally {
      this.thinking.set(false);
      this.scroll();
    }
  }

  protected rate(message: AssistantMessage, helpful: boolean): void {
    this.messages.update((messages) => messages.map((candidate) => (candidate.id === message.id ? { ...candidate, rated: helpful } : candidate)));
    this.center.feedback('AnswerRated', message.topic?.id ?? null, message.question ?? null, helpful);
  }

  private push(message: Omit<AssistantMessage, 'id'>): void {
    this.messages.update((messages) => [...messages, { ...message, id: this.nextId++ }]);
  }

  private scroll(): void {
    setTimeout(() => {
      const log = this.log()?.nativeElement;
      if (log) {
        log.scrollTop = log.scrollHeight;
      }
    });
  }
}
