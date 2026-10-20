import { DatePipe, KeyValuePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { HelpdeskApi } from '@gggs/shared/experience';
import type {
  HelpdeskCategoryInput,
  HelpdeskCategoryItem,
  HelpdeskDashboard,
  KnowledgeArticleInput,
  KnowledgeArticleItem,
  KnowledgeArticleView,
  TicketItem,
  TicketPriority,
  TicketStatus,
  TicketView,
} from '@gggs/shared/models';
import { TICKET_PRIORITIES, TICKET_STATUSES, humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const blankCategory = (): HelpdeskCategoryInput => ({ code: '', name: '', description: null, ownerEmployeeId: null, responseHours: 8, resolutionHours: 48, isConfidential: false, isActive: true });

/** The helpdesk queue: SLA at a glance, tickets to work, and the categories that route them. */
@Component({
  selector: 'gggs-helpdesk-page',
  imports: [FormsModule, RouterLink, DatePipe, KeyValuePipe, PageHeader, KpiCard, StatusBadge, EmptyState, FormDialog, Skeleton, Tabs, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Helpdesk" subtitle="Tickets routed by category, worked against response and resolution targets.">
      <a class="btn" routerLink="/helpdesk/knowledge">Knowledge base</a>
    </gggs-page-header>
    @if (dashboard(); as d) {
      <div class="xp__kpis">
        <gggs-kpi-card label="Open" [value]="d.open" />
        <gggs-kpi-card label="Unassigned" [value]="d.unassigned" />
        <gggs-kpi-card label="Response missed" [value]="d.responseBreached" />
        <gggs-kpi-card label="Resolution missed" [value]="d.resolutionBreached" />
        <gggs-kpi-card label="Response target met" [value]="d.responseSlaMetPercent + '%'" hint="Last 90 days" />
        <gggs-kpi-card label="Average resolution" [value]="d.averageResolutionHours !== undefined ? d.averageResolutionHours + ' h' : '—'" />
        <gggs-kpi-card label="Satisfaction" [value]="d.averageSatisfaction !== undefined ? d.averageSatisfaction + ' ★' : '—'" />
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="3" />
    }
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />
    <section class="panel stack">
      @if (tab() === 'queue') {
        <div class="toolbar xp__filters">
          <label class="field">
            <span class="field__label">Status</span>
            <select [ngModel]="status()" (ngModelChange)="status.set($event); load()">
              <option value="">Open tickets</option>
              @for (option of statuses; track option) {
                <option [value]="option">{{ humanize(option) }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">Category</span>
            <select [ngModel]="categoryId()" (ngModelChange)="categoryId.set($event); load()">
              <option value="">Any</option>
              @for (category of categories(); track category.id) {
                <option [value]="category.id">{{ category.name }}</option>
              }
            </select>
          </label>
          <label class="xp__flag"><input type="checkbox" [ngModel]="mine()" (ngModelChange)="mine.set($event); load()" /> Assigned to me</label>
          <label class="xp__flag"><input type="checkbox" [ngModel]="breached()" (ngModelChange)="breached.set($event); load()" /> Missed target</label>
        </div>
        @for (ticket of tickets(); track ticket.id) {
          <a class="xp__row" [routerLink]="['/helpdesk/tickets', ticket.id]">
            <span><strong>{{ ticket.subject }}</strong> <span class="muted">{{ ticket.number }}{{ ticket.isConfidential ? ' · confidential' : '' }}</span></span>
            <span>{{ ticket.requesterName }} · {{ ticket.categoryName }}</span>
            <span>{{ ticket.assigneeName ?? 'Unassigned' }}</span>
            <span [class.xp__danger]="ticket.responseBreached || ticket.resolutionBreached">{{ humanize(ticket.priority) }} · due {{ ticket.resolutionDueAt | date: 'd MMM HH:mm' }}</span>
            <gggs-status-badge [status]="ticket.status" />
          </a>
        } @empty {
          <gggs-empty-state icon="pi pi-check-circle" title="Nothing in the queue" />
        }
      } @else {
        @if (canManageCategories) {
          <div class="toolbar"><button type="button" class="btn" (click)="editCategory(null)">New category</button></div>
        }
        @for (category of categories(); track category.id) {
          <div class="xp__row">
            <span><strong>{{ category.name }}</strong> <span class="muted">{{ category.code }}{{ category.isConfidential ? ' · confidential' : '' }}</span></span>
            <span>respond in {{ category.responseHours }} h · resolve in {{ category.resolutionHours }} h</span>
            <span>{{ category.ownerName ?? 'No owner' }}</span>
            <span>
              @if (canManageCategories) {
                <button type="button" class="btn btn--link btn--sm" (click)="editCategory(category)">Edit</button>
              }
            </span>
            <gggs-status-badge [status]="category.isActive ? 'Active' : 'Inactive'" />
          </div>
        }
        @if (dashboard(); as d) {
          <h2 class="xp__heading">Open by category</h2>
          @for (entry of d.openByCategory | keyvalue; track entry.key) {
            <div class="xp__flag"><strong>{{ entry.value }}</strong> {{ entry.key }}</div>
          }
        }
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>

    <gggs-form-dialog [(visible)]="categoryOpen" [title]="categoryEditing() ? 'Edit category' : 'New category'" [busy]="busy()" [error]="formError()" [saveDisabled]="!categoryForm().code.trim() || !categoryForm().name.trim()" (save)="saveCategory()">
      <div class="xp__grid">
        <label class="field">
          <span class="field__label">Code</span>
          <input type="text" maxlength="30" [value]="categoryForm().code" (input)="patchCategory({ code: $any($event.target).value.toUpperCase() })" />
        </label>
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="100" [value]="categoryForm().name" (input)="patchCategory({ name: $any($event.target).value })" />
        </label>
        <label class="field xp__wide">
          <span class="field__label">Description</span>
          <input type="text" maxlength="500" [value]="categoryForm().description ?? ''" (input)="patchCategory({ description: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Respond within (hours)</span>
          <input type="number" min="1" [value]="categoryForm().responseHours" (input)="patchCategory({ responseHours: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Resolve within (hours)</span>
          <input type="number" min="1" [value]="categoryForm().resolutionHours" (input)="patchCategory({ resolutionHours: +$any($event.target).value })" />
        </label>
        <label class="field xp__wide">
          <span class="field__label">Owner (new tickets go to them)</span>
          <gggs-employee-select [ngModel]="categoryForm().ownerEmployeeId" (ngModelChange)="patchCategory({ ownerEmployeeId: $event })" />
        </label>
        <label class="xp__flag"><input type="checkbox" [checked]="categoryForm().isConfidential" (change)="patchCategory({ isConfidential: $any($event.target).checked })" /> Confidential</label>
        <label class="xp__flag"><input type="checkbox" [checked]="categoryForm().isActive" (change)="patchCategory({ isActive: $any($event.target).checked })" /> Active</label>
      </div>
    </gggs-form-dialog>
  `,
})
export class HelpdeskPage {
  private readonly api = inject(HelpdeskApi);

  protected readonly canManageCategories = inject(PermissionStore).has('helpdesk.category.manage');
  protected readonly humanize = humanize;
  protected readonly statuses = TICKET_STATUSES;
  protected readonly tabs: readonly TabItem[] = [
    { key: 'queue', label: 'Queue' },
    { key: 'categories', label: 'Categories' },
  ];
  protected readonly tab = signal('queue');
  protected readonly dashboard = signal<HelpdeskDashboard | null>(null);
  protected readonly tickets = signal<readonly TicketItem[]>([]);
  protected readonly categories = signal<readonly HelpdeskCategoryItem[]>([]);
  protected readonly status = signal<TicketStatus | ''>('');
  protected readonly categoryId = signal('');
  protected readonly mine = signal(false);
  protected readonly breached = signal(false);
  protected readonly categoryOpen = signal(false);
  protected readonly categoryEditing = signal<HelpdeskCategoryItem | null>(null);
  protected readonly categoryForm = signal<HelpdeskCategoryInput>(blankCategory());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.dashboard().then((d) => this.dashboard.set(d), (error: unknown) => this.error.set(message(error, 'The overview could not be loaded.')));
    this.api.categories().then((c) => this.categories.set(c), () => undefined);
    this.load();
  }

  protected load(): void {
    const status = this.status();
    this.api
      .tickets({ status: status || null, openOnly: status ? null : true, categoryId: this.categoryId() || null, mine: this.mine() || null, breached: this.breached() || null })
      .then((tickets) => this.tickets.set(tickets), (error: unknown) => this.error.set(message(error, 'The queue could not be loaded.')));
  }

  protected patchCategory(change: Partial<HelpdeskCategoryInput>): void {
    this.categoryForm.update((form) => ({ ...form, ...change }));
  }

  protected editCategory(category: HelpdeskCategoryItem | null): void {
    this.categoryEditing.set(category);
    this.categoryForm.set(
      category
        ? {
            code: category.code,
            name: category.name,
            description: category.description ?? null,
            ownerEmployeeId: category.ownerEmployeeId ?? null,
            responseHours: category.responseHours,
            resolutionHours: category.resolutionHours,
            isConfidential: category.isConfidential,
            isActive: category.isActive,
          }
        : blankCategory(),
    );
    this.formError.set(null);
    this.categoryOpen.set(true);
  }

  protected async saveCategory(): Promise<void> {
    this.busy.set(true);
    try {
      this.categories.set(await this.api.saveCategory(this.categoryEditing()?.id ?? null, this.categoryForm()));
      this.categoryOpen.set(false);
    } catch (error) {
      this.formError.set(message(error, 'The category could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** One ticket: the conversation, and what the helpdesk or the requester can do next. */
@Component({
  selector: 'gggs-ticket-page',
  imports: [FormsModule, DatePipe, PageHeader, StatusBadge, FormDialog, Skeleton, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="v.ticket.subject" [subtitle]="v.ticket.number + ' · ' + v.ticket.categoryName + (v.ticket.isConfidential ? ' · confidential' : '')">
        @if (v.isAgent && open(v)) {
          <button type="button" class="btn" (click)="assignOpen.set(true)">Assign</button>
          <button type="button" class="btn" (click)="move('WaitingOnRequester')">Waiting on requester</button>
          <button type="button" class="btn btn--primary" (click)="resolveOpen.set(true)">Resolve</button>
        }
        @if (v.isRequester) {
          @if (v.ticket.status === 'Resolved') {
            <button type="button" class="btn" (click)="reopenOpen.set(true)">Reopen</button>
            <button type="button" class="btn btn--primary" (click)="close()">Close</button>
          }
          @if (open(v)) {
            <button type="button" class="btn btn--link" (click)="cancel()">Cancel ticket</button>
          }
        }
      </gggs-page-header>
      <section class="panel xp__grid">
        <div><span class="muted">Status</span><br /><gggs-status-badge [status]="v.ticket.status" /></div>
        <div><span class="muted">Priority</span><br />{{ humanize(v.ticket.priority) }}</div>
        <div><span class="muted">Raised by</span><br />{{ v.ticket.requesterName }} · {{ v.ticket.createdDate | date: 'd MMM y HH:mm' }}</div>
        <div><span class="muted">Assigned to</span><br />{{ v.ticket.assigneeName ?? 'Unassigned' }}</div>
        <div [class.xp__danger]="v.ticket.responseBreached"><span class="muted">Respond by</span><br />{{ v.ticket.responseDueAt | date: 'd MMM HH:mm' }}{{ v.ticket.firstRespondedAt ? ' · answered ' + (v.ticket.firstRespondedAt | date: 'd MMM HH:mm') : '' }}</div>
        <div [class.xp__danger]="v.ticket.resolutionBreached"><span class="muted">Resolve by</span><br />{{ v.ticket.resolutionDueAt | date: 'd MMM HH:mm' }}</div>
      </section>
      <section class="panel stack">
        <p class="xp__article">{{ v.description }}</p>
        @if (v.resolution) {
          <p class="xp__notice"><strong>Resolution:</strong> {{ v.resolution }}</p>
        }
        @for (comment of v.comments; track comment.id) {
          <div class="xp__comment" [class.xp__comment--internal]="comment.isInternal">
            <span class="muted">{{ comment.authorName ?? 'Someone' }} · {{ comment.createdDate | date: 'd MMM y HH:mm' }}{{ comment.isInternal ? ' · internal note' : '' }}</span>
            <span class="xp__article">{{ comment.body }}</span>
          </div>
        }
        @if (v.ticket.status !== 'Closed' && v.ticket.status !== 'Cancelled') {
          <label class="field">
            <span class="field__label">Reply</span>
            <textarea rows="3" maxlength="4000" [value]="reply()" (input)="reply.set($any($event.target).value)"></textarea>
          </label>
          <div class="toolbar">
            @if (v.isAgent) {
              <label class="xp__flag"><input type="checkbox" [checked]="internal()" (change)="internal.set($any($event.target).checked)" /> Internal note</label>
            }
            <button type="button" class="btn btn--primary" [disabled]="!reply().trim() || busy()" (click)="comment()">Send</button>
          </div>
        }
        @if (v.isRequester && (v.ticket.status === 'Resolved' || v.ticket.status === 'Closed')) {
          <div class="toolbar">
            <span>How did we do?</span>
            @for (star of stars; track star) {
              <button type="button" class="btn btn--sm" [class.btn--primary]="(v.ticket.satisfactionRating ?? 0) >= star" [attr.aria-label]="star + ' stars'" (click)="rate(star)">★</button>
            }
          </div>
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="assignOpen" title="Assign ticket" saveLabel="Assign" [busy]="busy()" [error]="formError()" (save)="assign()">
      <label class="field">
        <span class="field__label">Assignee</span>
        <gggs-employee-select [ngModel]="assignee()" (ngModelChange)="assignee.set($event)" />
      </label>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="resolveOpen" title="Resolve ticket" saveLabel="Resolve" [busy]="busy()" [error]="formError()" [saveDisabled]="!resolution().trim()" (save)="resolve()">
      <label class="field">
        <span class="field__label">How it was resolved</span>
        <textarea rows="4" maxlength="2000" [value]="resolution()" (input)="resolution.set($any($event.target).value)"></textarea>
      </label>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="reopenOpen" title="Reopen ticket" saveLabel="Reopen" [busy]="busy()" [error]="formError()" [saveDisabled]="!reason().trim()" (save)="reopen()">
      <label class="field">
        <span class="field__label">What is still wrong</span>
        <textarea rows="3" maxlength="1000" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea>
      </label>
    </gggs-form-dialog>
  `,
})
export class TicketPage {
  private readonly api = inject(HelpdeskApi);

  readonly id = input.required<string>();

  protected readonly humanize = humanize;
  protected readonly stars = [1, 2, 3, 4, 5];
  protected readonly view = signal<TicketView | null>(null);
  protected readonly reply = signal('');
  protected readonly internal = signal(false);
  protected readonly assignOpen = signal(false);
  protected readonly assignee = signal<string | null>(null);
  protected readonly resolveOpen = signal(false);
  protected readonly resolution = signal('');
  protected readonly reopenOpen = signal(false);
  protected readonly reason = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.ticket(this.id()).then(
        (view) => this.view.set(view),
        (error: unknown) => this.error.set(message(error, 'The ticket could not be loaded.')),
      );
    });
  }

  protected open(view: TicketView): boolean {
    return view.ticket.status === 'Open' || view.ticket.status === 'InProgress' || view.ticket.status === 'WaitingOnRequester';
  }

  protected comment(): Promise<void> {
    return this.run(async () => {
      this.view.set(await this.api.comment(this.id(), this.reply(), this.internal()));
      this.reply.set('');
      this.internal.set(false);
    });
  }

  protected assign(): Promise<void> {
    return this.run(async () => {
      this.view.set(await this.api.assign(this.id(), this.assignee()));
      this.assignOpen.set(false);
    });
  }

  protected move(status: TicketStatus): Promise<void> {
    return this.run(async () => this.view.set(await this.api.status(this.id(), status, null)));
  }

  protected resolve(): Promise<void> {
    return this.run(async () => {
      this.view.set(await this.api.status(this.id(), 'Resolved', this.resolution()));
      this.resolveOpen.set(false);
    });
  }

  protected reopen(): Promise<void> {
    return this.run(async () => {
      this.view.set(await this.api.reopen(this.id(), this.reason()));
      this.reopenOpen.set(false);
    });
  }

  protected close(): Promise<void> {
    return this.run(async () => this.view.set(await this.api.close(this.id())));
  }

  protected cancel(): Promise<void> {
    return this.run(async () => this.view.set(await this.api.cancel(this.id())));
  }

  protected rate(rating: number): Promise<void> {
    return this.run(async () => this.view.set(await this.api.rate(this.id(), rating, null)));
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    this.error.set(null);
    try {
      await work();
    } catch (error) {
      const text = message(error, 'That did not work.');
      this.formError.set(text);
      this.error.set(text);
    } finally {
      this.busy.set(false);
    }
  }
}

/** Self-help articles: searched by everyone, written by the knowledge team. */
@Component({
  selector: 'gggs-knowledge-page',
  imports: [FormsModule, DatePipe, PageHeader, StatusBadge, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Knowledge base" subtitle="Answers to common questions - try here before raising a ticket.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="edit(null)">New article</button>
      }
    </gggs-page-header>
    <section class="panel stack">
      <div class="toolbar xp__filters">
        <label class="field">
          <span class="field__label">Search</span>
          <input type="search" placeholder="password, payslip, VPN..." [ngModel]="search()" (ngModelChange)="search.set($event)" (keyup.enter)="load()" />
        </label>
        <button type="button" class="btn" (click)="load()">Search</button>
      </div>
      @for (article of articles(); track article.id) {
        <div class="xp__row">
          <span><strong>{{ article.title }}</strong> <span class="muted">{{ article.categoryName ?? '' }}</span></span>
          <span class="muted">{{ article.tags ?? '' }}</span>
          <span class="muted">{{ article.views }} views · {{ article.helpfulVotes }} helpful</span>
          <span class="toolbar">
            <button type="button" class="btn btn--sm" (click)="read(article.id)">Read</button>
            @if (canManage) {
              <button type="button" class="btn btn--link btn--sm" (click)="edit(article)">Edit</button>
            }
          </span>
          <gggs-status-badge [status]="article.isPublished ? 'Published' : 'Draft'" />
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-book" title="No articles found" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>

    <gggs-form-dialog [(visible)]="readOpen" [title]="readingTitle()" saveLabel="Close" (save)="readOpen.set(false)" size="lg">
      @if (reading(); as r) {
        <div class="stack">
          <p class="xp__article">{{ r.body }}</p>
          <p class="muted">Updated {{ r.article.modifiedDate | date: 'd MMM y' }}</p>
          <div class="toolbar">
            <span>Was this helpful?</span>
            <button type="button" class="btn btn--sm" (click)="vote(true)">Yes</button>
            <button type="button" class="btn btn--sm" (click)="vote(false)">No</button>
            @if (voted()) {
              <span class="xp__notice">Thanks for the feedback.</span>
            }
          </div>
        </div>
      }
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="editOpen" [title]="editing() ? 'Edit article' : 'New article'" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().title.trim() || !form().body.trim()" (save)="save()" size="lg">
      <div class="xp__grid">
        <label class="field xp__wide">
          <span class="field__label">Title</span>
          <input type="text" maxlength="200" [value]="form().title" (input)="patch({ title: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Category</span>
          <select [value]="form().categoryId ?? ''" (change)="patch({ categoryId: $any($event.target).value || null })">
            <option value="">General</option>
            @for (category of categories(); track category.id) {
              <option [value]="category.id">{{ category.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Search keywords (comma-separated)</span>
          <input type="text" maxlength="300" [value]="form().tags ?? ''" (input)="patch({ tags: $any($event.target).value || null })" />
        </label>
        <label class="field xp__wide">
          <span class="field__label">Article</span>
          <textarea rows="10" [value]="form().body" (input)="patch({ body: $any($event.target).value })"></textarea>
        </label>
        <label class="xp__flag"><input type="checkbox" [checked]="form().publish" (change)="patch({ publish: $any($event.target).checked })" /> Published</label>
      </div>
    </gggs-form-dialog>
  `,
})
export class KnowledgePage {
  private readonly api = inject(HelpdeskApi);

  protected readonly canManage = inject(PermissionStore).has('helpdesk.knowledge.manage');
  protected readonly articles = signal<readonly KnowledgeArticleItem[]>([]);
  protected readonly categories = signal<readonly HelpdeskCategoryItem[]>([]);
  protected readonly search = signal('');
  protected readonly readOpen = signal(false);
  protected readonly reading = signal<KnowledgeArticleView | null>(null);
  protected readonly readingTitle = computed(() => this.reading()?.article.title ?? '');
  protected readonly voted = signal(false);
  protected readonly editOpen = signal(false);
  protected readonly editing = signal<KnowledgeArticleItem | null>(null);
  protected readonly form = signal<KnowledgeArticleInput>({ categoryId: null, title: '', body: '', tags: null, publish: true });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.load();
    this.api.categories().then((categories) => this.categories.set(categories), () => undefined);
  }

  protected load(): void {
    this.api.articles(this.search() || null).then((articles) => this.articles.set(articles), (error: unknown) => this.error.set(message(error, 'Articles could not be loaded.')));
  }

  protected async read(id: string): Promise<void> {
    try {
      this.reading.set(await this.api.article(id));
      this.voted.set(false);
      this.readOpen.set(true);
    } catch (error) {
      this.error.set(message(error, 'The article could not be opened.'));
    }
  }

  protected async vote(helpful: boolean): Promise<void> {
    const article = this.reading();
    if (article && !this.voted()) {
      this.reading.set(await this.api.vote(article.article.id, helpful));
      this.voted.set(true);
    }
  }

  protected patch(change: Partial<KnowledgeArticleInput>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected async edit(article: KnowledgeArticleItem | null): Promise<void> {
    this.editing.set(article);
    this.formError.set(null);
    if (article) {
      const view = await this.api.article(article.id);
      this.form.set({ categoryId: article.categoryId ?? null, title: article.title, body: view.body, tags: article.tags ?? null, publish: article.isPublished });
    } else {
      this.form.set({ categoryId: null, title: '', body: '', tags: null, publish: true });
    }

    this.editOpen.set(true);
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.saveArticle(this.editing()?.id ?? null, this.form());
      this.editOpen.set(false);
      this.load();
    } catch (error) {
      this.formError.set(message(error, 'The article could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** My tickets, and raising a new one with suggested articles first. */
@Component({
  selector: 'gggs-my-tickets-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My tickets" subtitle="Ask IT, HR, payroll or facilities for help.">
      <a class="btn" routerLink="/helpdesk/knowledge">Knowledge base</a>
      <button type="button" class="btn btn--primary" (click)="open()"><i class="pi pi-plus" aria-hidden="true"></i> Raise a ticket</button>
    </gggs-page-header>
    <section class="panel stack">
      @for (ticket of tickets(); track ticket.id) {
        <a class="xp__row" [routerLink]="['/me/tickets', ticket.id]">
          <span><strong>{{ ticket.subject }}</strong> <span class="muted">{{ ticket.number }}</span></span>
          <span>{{ ticket.categoryName }} · {{ humanize(ticket.priority) }}</span>
          <span>{{ ticket.assigneeName ?? 'Waiting to be picked up' }}</span>
          <span class="muted">{{ ticket.createdDate | date: 'd MMM y' }}</span>
          <gggs-status-badge [status]="ticket.status" />
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-question-circle" title="No tickets" description="Raise one when you need help." />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>

    <gggs-form-dialog [(visible)]="dialogOpen" title="Raise a ticket" saveLabel="Raise" [busy]="busy()" [error]="formError()" [saveDisabled]="!categoryId() || !subject().trim() || !description().trim()" (save)="raise()" size="lg">
      <div class="stack">
        <label class="field">
          <span class="field__label">Category</span>
          <select [value]="categoryId()" (change)="categoryId.set($any($event.target).value)">
            <option value="">Choose</option>
            @for (category of categories(); track category.id) {
              <option [value]="category.id">{{ category.name }}{{ category.isConfidential ? ' (confidential)' : '' }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Subject</span>
          <input type="text" maxlength="200" [value]="subject()" (input)="subject.set($any($event.target).value)" (change)="suggest()" />
        </label>
        @if (suggestions().length) {
          <div class="xp__card">
            <strong>These articles may answer it:</strong>
            @for (article of suggestions(); track article.id) {
              <a [routerLink]="['/helpdesk/knowledge']">{{ article.title }}</a>
            }
          </div>
        }
        <label class="field">
          <span class="field__label">Details</span>
          <textarea rows="5" maxlength="4000" [value]="description()" (input)="description.set($any($event.target).value)"></textarea>
        </label>
        <label class="field">
          <span class="field__label">Priority</span>
          <select [value]="priority()" (change)="priority.set($any($event.target).value)">
            @for (option of priorities; track option) {
              <option [value]="option">{{ option }}</option>
            }
          </select>
        </label>
      </div>
    </gggs-form-dialog>
  `,
})
export class MyTicketsPage {
  private readonly api = inject(HelpdeskApi);

  protected readonly humanize = humanize;
  protected readonly priorities = TICKET_PRIORITIES;
  protected readonly tickets = signal<readonly TicketItem[]>([]);
  protected readonly categories = signal<readonly HelpdeskCategoryItem[]>([]);
  protected readonly suggestions = signal<readonly KnowledgeArticleItem[]>([]);
  protected readonly dialogOpen = signal(false);
  protected readonly categoryId = signal('');
  protected readonly subject = signal('');
  protected readonly description = signal('');
  protected readonly priority = signal<TicketPriority>('Medium');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.mine().then((tickets) => this.tickets.set(tickets), (error: unknown) => this.error.set(message(error, 'Your tickets could not be loaded.')));
    this.api.categories().then((categories) => this.categories.set(categories.filter((category) => category.isActive)), () => undefined);
  }

  protected open(): void {
    this.categoryId.set('');
    this.subject.set('');
    this.description.set('');
    this.priority.set('Medium');
    this.suggestions.set([]);
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected async suggest(): Promise<void> {
    const subject = this.subject().trim();
    this.suggestions.set(subject.length < 3 ? [] : (await this.api.articles(subject)).slice(0, 3));
  }

  protected async raise(): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.raise({ categoryId: this.categoryId(), subject: this.subject(), description: this.description(), priority: this.priority() });
      this.dialogOpen.set(false);
      this.tickets.set(await this.api.mine());
    } catch (error) {
      this.formError.set(message(error, 'The ticket could not be raised.'));
    } finally {
      this.busy.set(false);
    }
  }
}
