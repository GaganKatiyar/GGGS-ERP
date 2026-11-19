import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { PermissionStore } from '@gggs/core/permissions';
import { filter, firstValueFrom } from 'rxjs';
import { NAVIGATION, filterNavigation, isBuilt } from '../navigation';
import { VisibleNavigation } from '../visible-navigation';
import { buildTopics, findTopic, keyTerms, questionsFor, sharesKeyTerm, topicForUrl } from './help-engine';
import type { HelpContent, HelpTopic } from './help-model';

/** What the help pages tell the server (`/api/v1/help/feedback`). */
export type HelpFeedbackKind = 'Unanswered' | 'AnswerRated' | 'PageRated';

/** Something from the organisation's own knowledge base or policies that may answer a question. */
export interface CompanyAnswer {
  readonly kind: 'article' | 'policy';
  readonly title: string;
  readonly summary: string | null;
  readonly link: string;
}

interface HelpNoteItem {
  readonly topicId: string;
  readonly text: string;
}

interface KnowledgeHit {
  readonly id: string;
  readonly title: string;
  readonly categoryName?: string | null;
  readonly tags?: string | null;
}

interface MyPolicyHit {
  readonly policyId: string;
  readonly title: string;
  readonly summary?: string | null;
}

/** Who may search the knowledge base (the same permissions the server asks for). */
const KNOWLEDGE_ACCESS = ['self.ticket.create', 'helpdesk.ticket.view', 'helpdesk.knowledge.manage'];

/**
 * The Help & Wiki's state, shared by the Help & Wiki page, the help panel on every screen and the
 * help assistant.
 * <p>
 * Help pages follow the menu: someone sees help for exactly the screens they can open. People who
 * help others (administrators) can switch to every screen.
 * </p>
 */
@Injectable({ providedIn: 'root' })
export class HelpCenter {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly permissions = inject(PermissionStore);
  private readonly visible = inject(VisibleNavigation);

  private loading: Promise<HelpContent> | null = null;
  private notesLoaded = false;
  private policies: readonly MyPolicyHit[] | null = null;

  /** The guides, once loaded (a separate download, fetched the first time help is opened). */
  readonly content = signal<HelpContent | null>(null);
  readonly url = signal(this.router.url);
  readonly panelOpen = signal(false);
  readonly assistantOpen = signal(false);
  /** Administrators can read help for every screen, to help others. */
  readonly showAll = signal(false);
  readonly notes = signal<Readonly<Record<string, string>>>({});

  readonly canShowAll = computed(() => this.permissions.has('admin.setting.view') || this.permissions.has('admin.user.view'));
  readonly canEditNotes = computed(() => this.permissions.has('admin.setting.manage'));

  readonly topics = computed<readonly HelpTopic[]>(() => {
    const content = this.content();
    if (!content) {
      return [];
    }
    const navigation = this.showAll() && this.canShowAll() ? filterNavigation(NAVIGATION, isBuilt) : this.visible.items();
    return buildTopics(navigation, content);
  });

  /** The help page for the screen on show; "Getting started" where a screen has none. */
  readonly current = computed(() => topicForUrl(this.url(), this.topics()) ?? findTopic('getting-started', this.topics()));

  readonly questions = computed(() => {
    const content = this.content();
    return content ? questionsFor(content.questions, this.topics()) : [];
  });

  /** Whether "Raise a ticket" can be offered: the person can open My tickets. */
  readonly canRaiseTicket = computed(() => this.visible.items().some((group) => (group.children ?? [group]).some((page) => page.route === '/me/tickets')));

  constructor() {
    this.router.events.pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd)).subscribe((event) => {
      this.url.set(event.urlAfterRedirects);
    });
  }

  /** Loads the guides and the organisation's notes (once). */
  async load(): Promise<void> {
    if (!this.content()) {
      this.loading ??= import('./help-content').then((module) => module.HELP_CONTENT);
      this.content.set(await this.loading);
    }
    if (!this.notesLoaded) {
      this.notesLoaded = true;
      try {
        const notes = await firstValueFrom(this.http.get<readonly HelpNoteItem[]>('/api/v1/help/notes'));
        this.notes.set(Object.fromEntries(notes.map((note) => [note.topicId, note.text])));
      } catch {
        // Notes are an extra: help works without them.
        this.notesLoaded = false;
      }
    }
  }

  async openPanel(): Promise<void> {
    this.assistantOpen.set(false);
    this.panelOpen.set(true);
    await this.load();
  }

  async openAssistant(): Promise<void> {
    this.panelOpen.set(false);
    this.assistantOpen.set(true);
    await this.load();
  }

  async saveNote(topicId: string, text: string): Promise<void> {
    const saved = await firstValueFrom(this.http.put<HelpNoteItem>(`/api/v1/help/notes/${encodeURIComponent(topicId)}`, { text }));
    const notes = { ...this.notes() };
    if (saved.text) {
      notes[topicId] = saved.text;
    } else {
      delete notes[topicId];
    }
    this.notes.set(notes);
  }

  /** Tells us a guide or answer helped or not, or that a question had no answer. Never fails the page. */
  feedback(kind: HelpFeedbackKind, topicId: string | null, question: string | null, helpful: boolean | null): void {
    this.http.post('/api/v1/help/feedback', { kind, topicId, question, helpful }).subscribe({ error: () => undefined });
  }

  /**
   * The organisation's own answers to a question: knowledge base articles and the person's policies
   * whose words match. Only searched where the person may read them.
   */
  async companyAnswers(question: string): Promise<CompanyAnswer[]> {
    if (!keyTerms(question).length) {
      return [];
    }

    const [articles, policies] = await Promise.all([this.searchKnowledge(question), this.matchPolicies(question)]);
    return [...articles, ...policies].slice(0, 4);
  }

  raiseTicket(question: string): void {
    this.assistantOpen.set(false);
    void this.router.navigate(['/me/tickets'], { queryParams: { raise: question.slice(0, 200) } });
  }

  private async searchKnowledge(question: string): Promise<CompanyAnswer[]> {
    if (!KNOWLEDGE_ACCESS.some((code) => this.permissions.has(code))) {
      return [];
    }
    try {
      const hits = await firstValueFrom(this.http.get<readonly KnowledgeHit[]>('/api/v1/helpdesk/knowledge', { params: { search: keyTerms(question).join(' ').slice(0, 100) } }));
      // The search matches loosely; keep only articles that share a meaningful word with the question.
      return hits
        .filter((hit) => sharesKeyTerm(`${hit.title} ${hit.tags ?? ''}`, question))
        .slice(0, 3)
        .map((hit) => ({ kind: 'article', title: hit.title, summary: hit.categoryName ?? null, link: '/helpdesk/knowledge' }));
    } catch {
      return [];
    }
  }

  private async matchPolicies(question: string): Promise<CompanyAnswer[]> {
    if (!this.permissions.has('self.policy.view')) {
      return [];
    }
    try {
      this.policies ??= await firstValueFrom(this.http.get<readonly MyPolicyHit[]>('/api/v1/me/policies'));
    } catch {
      return [];
    }
    return this.policies
      .filter((policy) => sharesKeyTerm(`${policy.title} ${policy.summary ?? ''}`, question))
      .slice(0, 2)
      .map((policy) => ({ kind: 'policy', title: policy.title, summary: policy.summary ?? null, link: '/me/policies' }));
  }
}
