import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import { RecordDocuments } from '@gggs/shared/documents';
import type { ActivityEntry, TaskComment, TaskConversationView } from '@gggs/shared/models';
import { describeActivity, insertMention, matchingPeople, mentionAt, mentionedIn } from './mention-text';

type Pane = 'comments' | 'history' | 'files' | 'code';

/** The talk around a task: comments with @mentions, who is watching, its files and everything that has changed. */
@Component({
  selector: 'gggs-task-conversation',
  imports: [DatePipe, RecordDocuments],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="talk">
      <div class="talk__bar" role="tablist" aria-label="Task conversation">
        <button type="button" role="tab" class="talk__tab" [class.talk__tab--on]="pane() === 'comments'" [attr.aria-selected]="pane() === 'comments'" (click)="pane.set('comments')">Comments ({{ view()?.comments?.length ?? 0 }})</button>
        <button type="button" role="tab" class="talk__tab" [class.talk__tab--on]="pane() === 'history'" [attr.aria-selected]="pane() === 'history'" (click)="pane.set('history')">History</button>
        <button type="button" role="tab" class="talk__tab" [class.talk__tab--on]="pane() === 'files'" [attr.aria-selected]="pane() === 'files'" (click)="pane.set('files')">Files</button>
        <button type="button" role="tab" class="talk__tab" [class.talk__tab--on]="pane() === 'code'" [attr.aria-selected]="pane() === 'code'" (click)="pane.set('code')">Code ({{ view()?.codeLinks?.length ?? 0 }})</button>
        <span class="talk__spacer"></span>
        @if (view(); as v) {
          <span class="muted talk__small" [title]="watcherNames()">{{ v.watchers.length }} watching</span>
          <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="toggleWatch()">{{ v.isWatching ? 'Stop watching' : 'Watch' }}</button>
        }
      </div>

      @switch (pane()) {
        @case ('comments') {
          @for (comment of view()?.comments ?? []; track comment.id) {
            <article class="talk__comment">
              <header class="talk__who">
                <strong>{{ comment.authorName ?? 'Someone' }}</strong>
                <span class="muted talk__small">{{ comment.postedAt | date: 'd MMM y, HH:mm' }}{{ comment.editedAt ? ' · edited' : '' }}</span>
                <span class="talk__spacer"></span>
                @if (comment.canEdit) {
                  <button type="button" class="btn btn--link btn--sm" (click)="startEdit(comment)">Edit</button>
                }
                @if (comment.canDelete) {
                  <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="remove(comment)">Delete</button>
                }
              </header>
              @if (editing() === comment.id) {
                <textarea rows="3" maxlength="4000" aria-label="Edit comment" [value]="editText()" (input)="editText.set($any($event.target).value)"></textarea>
                <div class="toolbar">
                  <button type="button" class="btn btn--sm" [disabled]="busy() || !editText().trim()" (click)="saveEdit(comment)">Save</button>
                  <button type="button" class="btn btn--link btn--sm" (click)="editing.set(null)">Cancel</button>
                </div>
              } @else {
                <p class="talk__body">{{ comment.body }}</p>
              }
            </article>
          } @empty {
            <p class="muted talk__small">No comments yet.</p>
          }

          @if (view()?.canComment) {
            <div class="talk__new">
              <label class="field">
                <span class="field__label">Add a comment (type &#64; to mention someone)</span>
                <textarea #newComment rows="3" maxlength="4000" [value]="text()" (input)="onType($event)" (keyup)="onCaret($event)" (click)="onCaret($event)"></textarea>
              </label>
              @if (suggestions().length) {
                <ul class="talk__suggest" role="listbox" aria-label="People to mention">
                  @for (person of suggestions(); track person.employeeId) {
                    <li role="option" aria-selected="false"><button type="button" class="talk__person" (click)="choose(person.name, newComment)">{{ person.name }}</button></li>
                  }
                </ul>
              }
              <div class="toolbar">
                <button type="button" class="btn btn--sm" [disabled]="busy() || !text().trim()" (click)="post()">Comment</button>
                @if (named().length) {
                  <span class="muted talk__small">Tells {{ named().length === 1 ? 'one person' : named().length + ' people' }} by name.</span>
                }
              </div>
            </div>
          }
        }
        @case ('history') {
          @for (entry of view()?.activity ?? []; track entry.id) {
            <p class="talk__line"><strong>{{ entry.actorName ?? 'Someone' }}</strong> {{ line(entry) }} <span class="muted talk__small">{{ entry.at | date: 'd MMM y, HH:mm' }}</span></p>
          } @empty {
            <p class="muted talk__small">Nothing recorded yet. Changes made from now on appear here.</p>
          }
        }
        @case ('code') {
          @for (link of view()?.codeLinks ?? []; track link.provider + link.kind + link.externalId) {
            <p class="talk__line">
              <i [class]="link.kind === 'PullRequest' ? 'pi pi-arrow-right-arrow-left' : 'pi pi-code'" aria-hidden="true"></i>
              @if (link.url) {
                <a [href]="link.url" target="_blank" rel="noopener noreferrer">{{ link.title }}</a>
              } @else {
                {{ link.title }}
              }
              <span class="muted talk__small">{{ link.kind === 'PullRequest' ? 'Pull request' : 'Commit' }}{{ link.state ? ', ' + link.state : '' }}{{ link.author ? ', ' + link.author : '' }}, {{ link.occurredAt | date: 'd MMM y' }}</span>
            </p>
          } @empty {
            <p class="muted talk__small">Commits and pull requests that name this task's code appear here, once a code host is connected.</p>
          }
        }
        @case ('files') {
          <gggs-record-documents ownerType="ProjectTask" [ownerId]="taskId()" [canUpload]="view()?.canAttach ?? false" />
        }
      }

      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>
  `,
  styles: `
    .talk {
      margin-block-start: var(--gggs-space-4);
      padding-block-start: var(--gggs-space-3);
      border-block-start: 1px solid var(--gggs-color-border);
    }

    .talk__bar {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
      align-items: center;
      margin-block-end: var(--gggs-space-3);
    }

    .talk__tab {
      padding: var(--gggs-space-1) var(--gggs-space-2);
      border: 0;
      border-block-end: 2px solid transparent;
      background: none;
      color: inherit;
      cursor: pointer;
    }

    .talk__tab--on {
      border-block-end-color: var(--gggs-color-primary);
      font-weight: 600;
    }

    .talk__spacer {
      flex: 1;
    }

    .talk__small {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .talk__comment {
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .talk__who {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
      align-items: center;
    }

    .talk__body {
      margin: var(--gggs-space-1) 0 0;
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }

    .talk__line {
      margin: 0;
      padding-block: var(--gggs-space-1);
    }

    .talk__line a,
    .talk__line .talk__small {
      margin-inline-start: var(--gggs-space-1);
    }

    .talk__new {
      margin-block-start: var(--gggs-space-3);
    }

    .talk__suggest {
      margin: var(--gggs-space-1) 0;
      padding: 0;
      list-style: none;
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
    }

    .talk__person {
      inline-size: 100%;
      padding: var(--gggs-space-1) var(--gggs-space-2);
      border: 0;
      background: none;
      color: inherit;
      text-align: start;
      cursor: pointer;
    }

    .talk__person:hover,
    .talk__person:focus-visible {
      background: var(--gggs-color-surface-hover);
    }
  `,
})
export class TaskConversation {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();
  readonly taskId = input.required<string>();

  protected readonly pane = signal<Pane>('comments');
  protected readonly view = signal<TaskConversationView | null>(null);
  protected readonly text = signal('');
  protected readonly caret = signal(0);
  protected readonly editing = signal<string | null>(null);
  protected readonly editText = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly line = describeActivity as (entry: ActivityEntry) => string;
  protected readonly watcherNames = computed(() => (this.view()?.watchers ?? []).map((watcher) => watcher.name ?? 'Someone').join(', '));
  protected readonly named = computed(() => mentionedIn(this.text(), this.view()?.people ?? []));
  private readonly mention = computed(() => mentionAt(this.text(), this.caret()));
  protected readonly suggestions = computed(() => {
    const mention = this.mention();
    return mention ? matchingPeople(this.view()?.people ?? [], mention.query) : [];
  });

  constructor() {
    effect(() => {
      const projectId = this.projectId();
      const taskId = this.taskId();
      untracked(() => {
        this.pane.set('comments');
        this.text.set('');
        this.editing.set(null);
        void this.run(async () => this.view.set(await this.api.taskConversation(projectId, taskId)), 'The conversation could not be loaded.');
      });
    });
  }

  protected onType(event: Event): void {
    const area = event.target as HTMLTextAreaElement;
    this.text.set(area.value);
    this.caret.set(area.selectionStart);
  }

  protected onCaret(event: Event): void {
    this.caret.set((event.target as HTMLTextAreaElement).selectionStart);
  }

  protected choose(name: string, area: HTMLTextAreaElement): void {
    const mention = this.mention();
    if (mention) {
      const next = insertMention(this.text(), this.caret(), mention.start, name);
      this.text.set(next.text);
      this.caret.set(next.caret);
      area.value = next.text;
      area.focus();
      area.setSelectionRange(next.caret, next.caret);
    }
  }

  protected async post(): Promise<void> {
    const body = this.text().trim();
    await this.run(async () => {
      this.view.set(await this.api.addTaskComment(this.projectId(), this.taskId(), body, this.named()));
      this.text.set('');
    }, 'The comment could not be posted.');
  }

  protected startEdit(comment: TaskComment): void {
    this.editing.set(comment.id);
    this.editText.set(comment.body);
  }

  protected async saveEdit(comment: TaskComment): Promise<void> {
    await this.run(async () => {
      this.view.set(await this.api.editTaskComment(this.projectId(), this.taskId(), comment.id, this.editText().trim()));
      this.editing.set(null);
    }, 'The comment could not be saved.');
  }

  protected async remove(comment: TaskComment): Promise<void> {
    await this.run(async () => this.view.set(await this.api.deleteTaskComment(this.projectId(), this.taskId(), comment.id)), 'The comment could not be deleted.');
  }

  protected async toggleWatch(): Promise<void> {
    const on = !this.view()?.isWatching;
    await this.run(async () => this.view.set(await this.api.watchTask(this.projectId(), this.taskId(), on)), 'That did not work.');
  }

  private async run(action: () => Promise<void>, fallback: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : fallback);
    } finally {
      this.busy.set(false);
    }
  }
}
