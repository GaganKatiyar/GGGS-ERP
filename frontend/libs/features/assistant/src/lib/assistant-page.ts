import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmptyState, PageHeader, StatusBadge } from '@gggs/shared/ui';
import { type ActionItem, type AiStatus, AssistantApi, type ConversationItem, type ConversationView } from './assistant-api';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const ACTION_VARIANTS = { Proposed: 'warning', Executed: 'success', Rejected: 'neutral', Failed: 'danger', Expired: 'neutral' } as const;

const SUGGESTIONS: Readonly<Record<string, readonly string[]>> = {
  me: ['How much leave do I have left?', 'Book casual leave next Friday', 'How are my goals going?', 'My laptop is slow - raise a ticket'],
  hr: ['What is our headcount and attrition?', 'Who is on leave this week?', "What's coming up on the HR calendar?"],
  workforce: ['How is hiring going against plan?', 'Where are we losing people?'],
  resources: ['Who is free for a new project next month?', 'How utilised is the delivery team?'],
  projects: ['Which projects are at risk?', 'Give me a status summary of my projects'],
  career: ['What should I learn for my next role?', 'Who are the successors for key roles?'],
  recruitment: ['How is the pipeline looking?', 'Which requisitions are ageing?'],
  performance: ['Where is the review cycle up to?', 'Which goals are behind?'],
  rewards: ['Recognise a teammate for great work', 'How many points do I have?'],
};

/**
 * The assistants (spec §89): pick one, ask in plain language, and see which tools it used. Anything
 * that would change data arrives as a proposal that does nothing until approved here.
 */
@Component({
  selector: 'gggs-assistant-page',
  imports: [DatePipe, RouterLink, PageHeader, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="AI assistants" subtitle="Answers from your own data, with nothing changed until you approve it">
      @if (canSeeUsage) {
        <a class="btn" routerLink="usage"><i class="pi pi-chart-bar" aria-hidden="true"></i> Usage</a>
      }
      <button type="button" class="btn" (click)="newChat()"><i class="pi pi-plus" aria-hidden="true"></i> New chat</button>
    </gggs-page-header>

    @if (status(); as s) {
      @if (!s.configured) {
        <p class="alert" role="status">
          <i class="pi pi-info-circle" aria-hidden="true"></i>
          <span>The assistants are not switched on yet: an administrator needs to configure an Anthropic API key (<code>Gggs:Ai:ApiKey</code>).</span>
        </p>
      }
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <div class="ai">
      <aside class="panel stack ai__side">
        <h2 class="ai__heading">Assistants</h2>
        <div class="ai__assistants" role="radiogroup" aria-label="Assistant">
          @for (assistant of status()?.assistants ?? []; track assistant.key) {
            <button type="button" role="radio" class="ai__assistant" [class.ai__assistant--on]="assistant.key === assistantKey()"
              [attr.aria-checked]="assistant.key === assistantKey()" [disabled]="!!view() && view()!.conversation.assistantKey !== assistant.key"
              (click)="assistantKey.set(assistant.key)">
              <strong>{{ assistant.title }}</strong>
              <span class="muted">{{ assistant.description }}</span>
            </button>
          }
        </div>
        <h2 class="ai__heading">Recent chats</h2>
        @for (item of conversations(); track item.id) {
          <div class="ai__recent" [class.ai__recent--on]="item.id === view()?.conversation?.id">
            <button type="button" class="btn--link ai__recent-open" (click)="open(item)">
              <span>{{ item.title }}</span>
              <span class="muted">{{ titleOf(item.assistantKey) }} · {{ item.lastActivityAt | date: 'd MMM, HH:mm' }}</span>
            </button>
            <button type="button" class="btn btn--sm" [attr.aria-label]="'Delete ' + item.title" (click)="remove(item)"><i class="pi pi-trash" aria-hidden="true"></i></button>
          </div>
        } @empty {
          <p class="muted">No chats yet.</p>
        }
      </aside>

      <section class="panel ai__chat" aria-label="Conversation">
        <div class="ai__thread" #thread aria-live="polite">
          @for (entry of view()?.messages ?? []; track $index) {
            <div class="ai__message" [class.ai__message--user]="entry.role === 'user'">
              <p class="ai__text">{{ entry.text }}</p>
              @if (entry.toolsUsed.length) {
                <div class="ai__tools">
                  @for (tool of entry.toolsUsed; track tool) {
                    <span class="ai__tool"><i class="pi pi-wrench" aria-hidden="true"></i> {{ tool }}</span>
                  }
                </div>
              }
              @for (action of actionsOf(entry.actionIds); track action.id) {
                <div class="ai__proposal">
                  <div class="ai__proposal-head">
                    <strong>Proposed: {{ action.toolName }}</strong>
                    <gggs-status-badge [status]="action.status" [variants]="actionVariants" />
                  </div>
                  <p>{{ action.summary }}</p>
                  @if (action.result) {
                    <p class="muted">{{ action.result }}</p>
                  }
                  @if (action.status === 'Proposed') {
                    <div class="toolbar">
                      <button type="button" class="btn btn--primary btn--sm" [disabled]="deciding()" (click)="decide(action, true)">Approve</button>
                      <button type="button" class="btn btn--sm" [disabled]="deciding()" (click)="decide(action, false)">Reject</button>
                    </div>
                  }
                </div>
              }
            </div>
          } @empty {
            <gggs-empty-state icon="pi pi-sparkles" [title]="'Ask the ' + titleOf(assistantKey())" description="It looks things up as you, so it only sees what you may see. Actions are drafted for you to approve." />
            <div class="ai__suggestions">
              @for (suggestion of suggestions(); track suggestion) {
                <button type="button" class="btn btn--sm" (click)="draft.set(suggestion)">{{ suggestion }}</button>
              }
            </div>
          }
          @if (sending()) {
            <div class="ai__message"><p class="ai__text muted"><i class="pi pi-spin pi-spinner" aria-hidden="true"></i> Thinking…</p></div>
          }
        </div>
        <form class="ai__compose" (submit)="$event.preventDefault(); send()">
          <label class="ai__sr" for="ai-input">Message</label>
          <textarea id="ai-input" rows="2" maxlength="4000" placeholder="Ask a question or describe what you need…" [value]="draft()"
            (input)="draft.set($any($event.target).value)" (keydown.enter)="onEnter($any($event))" [disabled]="sending() || !status()?.configured"></textarea>
          <button type="submit" class="btn btn--primary" [disabled]="sending() || !draft().trim() || !status()?.configured">
            <i class="pi pi-send" aria-hidden="true"></i> Send
          </button>
        </form>
        <p class="muted ai__footnote">Assistants can be wrong. Check anything important, and nothing is decided until you approve it.</p>
      </section>
    </div>
  `,
  styles: `
    .ai {
      display: grid;
      grid-template-columns: minmax(15rem, 20rem) 1fr;
      gap: var(--gggs-space-3);
      align-items: start;
    }
    .ai__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }
    .ai__assistants {
      display: grid;
      gap: var(--gggs-space-2);
    }
    .ai__assistant {
      display: grid;
      gap: 2px;
      text-align: start;
      padding: var(--gggs-space-2);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md, 6px);
      background: transparent;
      color: inherit;
      cursor: pointer;
      font-size: var(--gggs-font-size-sm);
    }
    .ai__assistant:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }
    .ai__assistant--on {
      border-color: var(--gggs-color-primary);
      box-shadow: inset 3px 0 0 var(--gggs-color-primary);
    }
    .ai__recent {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
    }
    .ai__recent--on .ai__recent-open {
      font-weight: 600;
    }
    .ai__recent-open {
      display: grid;
      flex: 1;
      min-width: 0;
      text-align: start;
    }
    .ai__recent-open span {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .ai__chat {
      display: grid;
      grid-template-rows: 1fr auto auto;
      min-height: 32rem;
      gap: var(--gggs-space-2);
    }
    .ai__thread {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-3);
      max-height: 60vh;
      overflow-y: auto;
      padding-inline-end: var(--gggs-space-1);
    }
    .ai__message {
      max-width: 80%;
      display: grid;
      gap: var(--gggs-space-2);
    }
    .ai__message--user {
      align-self: flex-end;
    }
    .ai__text {
      margin: 0;
      white-space: pre-wrap;
      padding: var(--gggs-space-2) var(--gggs-space-3);
      border-radius: 12px;
      background: color-mix(in srgb, var(--gggs-color-border) 35%, transparent);
    }
    .ai__message--user .ai__text {
      background: var(--gggs-color-primary);
      color: var(--gggs-color-primary-contrast);
    }
    .ai__tools {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-1);
    }
    .ai__tool {
      font-size: 0.72rem;
      color: var(--gggs-color-text-muted);
    }
    .ai__proposal {
      border: 1px dashed var(--gggs-color-primary);
      border-radius: 8px;
      padding: var(--gggs-space-2) var(--gggs-space-3);
      display: grid;
      gap: var(--gggs-space-1);
    }
    .ai__proposal p {
      margin: 0;
    }
    .ai__proposal-head {
      display: flex;
      justify-content: space-between;
      gap: var(--gggs-space-2);
      align-items: center;
    }
    .ai__suggestions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
      justify-content: center;
    }
    .ai__compose {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: flex-end;
    }
    .ai__compose textarea {
      flex: 1;
      resize: vertical;
    }
    .ai__footnote {
      margin: 0;
      font-size: 0.75rem;
    }
    .ai__sr {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip: rect(0 0 0 0);
    }
    @media (max-width: 56rem) {
      .ai {
        grid-template-columns: 1fr;
      }
      .ai__message {
        max-width: 100%;
      }
    }
  `,
})
export class AssistantPage {
  private readonly api = inject(AssistantApi);
  private readonly thread = viewChild<ElementRef<HTMLElement>>('thread');
  protected readonly canSeeUsage = inject(PermissionStore).has('ai.usage.view');
  protected readonly actionVariants = ACTION_VARIANTS;
  protected readonly status = signal<AiStatus | null>(null);
  protected readonly conversations = signal<readonly ConversationItem[]>([]);
  protected readonly view = signal<ConversationView | null>(null);
  protected readonly assistantKey = signal('me');
  protected readonly draft = signal('');
  protected readonly sending = signal(false);
  protected readonly deciding = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly suggestions = computed(() => SUGGESTIONS[this.assistantKey()] ?? []);

  constructor() {
    void this.load();
  }

  protected titleOf(key: string): string {
    return this.status()?.assistants.find((assistant) => assistant.key === key)?.title ?? 'assistant';
  }

  protected actionsOf(ids: readonly string[]): readonly ActionItem[] {
    const actions = this.view()?.actions ?? [];
    return ids.map((id) => actions.find((action) => action.id === id)).filter((action): action is ActionItem => !!action);
  }

  protected newChat(): void {
    this.view.set(null);
    this.draft.set('');
    this.error.set(null);
  }

  protected async open(item: ConversationItem): Promise<void> {
    this.error.set(null);
    try {
      this.show(await this.api.conversation(item.id));
      this.assistantKey.set(item.assistantKey);
    } catch (error) {
      this.error.set(message(error, 'The chat could not be opened.'));
    }
  }

  protected async remove(item: ConversationItem): Promise<void> {
    try {
      await this.api.remove(item.id);
      if (this.view()?.conversation.id === item.id) {
        this.newChat();
      }
      this.conversations.set(await this.api.conversations());
    } catch (error) {
      this.error.set(message(error, 'The chat could not be deleted.'));
    }
  }

  protected onEnter(event: KeyboardEvent): void {
    if (!event.shiftKey) {
      event.preventDefault();
      void this.send();
    }
  }

  protected async send(): Promise<void> {
    const text = this.draft().trim();
    if (!text || this.sending()) {
      return;
    }
    this.sending.set(true);
    this.error.set(null);
    this.draft.set('');
    const current = this.view();
    if (current) {
      this.view.set({ ...current, messages: [...current.messages, { role: 'user', text, toolsUsed: [], actionIds: [] }] });
    }
    this.scrollDown();
    try {
      this.show(current ? await this.api.send(current.conversation.id, text) : await this.api.start(this.assistantKey(), text));
      this.conversations.set(await this.api.conversations());
    } catch (error) {
      this.view.set(current);
      this.draft.set(text);
      this.error.set(message(error, 'The assistant could not answer just now.'));
    } finally {
      this.sending.set(false);
    }
  }

  protected async decide(action: ActionItem, approve: boolean): Promise<void> {
    this.deciding.set(true);
    this.error.set(null);
    try {
      const decided = approve ? await this.api.approve(action.id) : await this.api.reject(action.id);
      const current = this.view();
      if (current) {
        this.view.set({ ...current, actions: current.actions.map((item) => (item.id === decided.id ? decided : item)) });
      }
    } catch (error) {
      this.error.set(message(error, 'The proposal could not be decided.'));
    } finally {
      this.deciding.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const [status, conversations] = await Promise.all([this.api.status(), this.api.conversations()]);
      this.status.set(status);
      this.conversations.set(conversations);
      if (!status.assistants.some((assistant) => assistant.key === this.assistantKey()) && status.assistants.length) {
        this.assistantKey.set(status.assistants[0].key);
      }
    } catch (error) {
      this.error.set(message(error, 'The assistants could not be loaded.'));
    }
  }

  private show(view: ConversationView): void {
    this.view.set(view);
    this.scrollDown();
  }

  private scrollDown(): void {
    queueMicrotask(() => {
      const element = this.thread()?.nativeElement;
      if (element) {
        element.scrollTop = element.scrollHeight;
      }
    });
  }
}
