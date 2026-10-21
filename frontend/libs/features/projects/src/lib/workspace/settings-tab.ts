import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { BoardColumn, ColumnCategory, HistoryItem } from '@gggs/shared/models';
import { Timeline, type TimelineEntry } from '@gggs/shared/ui';

interface ColumnDraft {
  id: string | null;
  name: string;
  category: ColumnCategory;
  wipLimit: number | null;
  taskCount: number;
}

/** The project's board columns (spec §45: "configurable statuses") and its stage and status history. */
@Component({
  selector: 'gggs-settings-tab',
  imports: [Timeline],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="settings">
      <section class="panel stack">
        <h2 class="settings__heading">Board columns</h2>
        <p class="muted settings__hint">What a column means (backlog, to do, in progress, done) drives the rules; the name is yours.</p>
        @for (column of drafts(); track $index; let index = $index) {
          <div class="settings__column">
            <input type="text" maxlength="50" aria-label="Column name" [value]="column.name" [disabled]="!canManage()" (input)="update(index, 'name', $any($event.target).value)" />
            <select aria-label="Column meaning" [value]="column.category" [disabled]="!canManage()" (change)="update(index, 'category', $any($event.target).value)">
              <option value="Backlog">Backlog</option>
              <option value="Todo">To do</option>
              <option value="InProgress">In progress</option>
              <option value="Done">Done</option>
            </select>
            <input type="number" min="1" aria-label="WIP limit" placeholder="WIP" [value]="column.wipLimit ?? ''" [disabled]="!canManage()" (input)="update(index, 'wipLimit', $any($event.target).value === '' ? null : +$any($event.target).value)" />
            <span class="muted">{{ column.taskCount }} task(s)</span>
            @if (canManage()) {
              <span class="settings__buttons">
                <button type="button" class="btn btn--link btn--sm" [disabled]="index === 0" (click)="move(index, -1)" aria-label="Move up"><i class="pi pi-arrow-up" aria-hidden="true"></i></button>
                <button type="button" class="btn btn--link btn--sm" [disabled]="index === drafts().length - 1" (click)="move(index, 1)" aria-label="Move down"><i class="pi pi-arrow-down" aria-hidden="true"></i></button>
                <button type="button" class="btn btn--link btn--sm" [disabled]="column.taskCount > 0" (click)="remove(index)">Remove</button>
              </span>
            }
          </div>
        }
        @if (canManage()) {
          <div class="toolbar">
            <button type="button" class="btn btn--sm" (click)="add()">Add column</button>
            <button type="button" class="btn btn--primary btn--sm" [disabled]="busy()" (click)="save()">Save board</button>
          </div>
        }
        @if (message()) {
          <p class="settings__message" role="status">{{ message() }}</p>
        }
        @if (error()) {
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
        }
      </section>

      <section class="panel stack">
        <h2 class="settings__heading">History</h2>
        <gggs-timeline [entries]="entries()" />
      </section>
    </div>
  `,
  styles: `
    .settings {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(24rem, 100%), 1fr));
      gap: var(--gggs-space-3);
      align-items: start;
    }

    .settings__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .settings__hint {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .settings__column {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 9rem 5rem 6rem auto;
      gap: var(--gggs-space-2);
      align-items: center;
    }

    .settings__buttons {
      white-space: nowrap;
    }

    .settings__message {
      color: var(--gggs-color-success);
    }
  `,
})
export class SettingsTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();
  readonly canManage = input(false);

  protected readonly drafts = signal<readonly ColumnDraft[]>([]);
  protected readonly history = signal<readonly HistoryItem[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly entries = computed<readonly TimelineEntry[]>(() =>
    this.history().map((item) => ({
      date: item.changedDate,
      title: item.kind === 'Satisfaction' ? `Client satisfaction ${item.fromValue} → ${item.toValue}` : `${item.kind}: ${item.fromValue} → ${item.toValue}`,
      description: [item.changedByName, item.comment].filter((part) => !!part).join(' - ') || undefined,
      tone: item.toValue === 'Completed' ? 'success' : item.toValue === 'Cancelled' ? 'danger' : 'primary',
    })),
  );

  constructor() {
    effect(() => {
      void this.load(this.projectId());
    });
  }

  protected update<K extends keyof ColumnDraft>(index: number, field: K, value: ColumnDraft[K]): void {
    this.drafts.update((list) => list.map((column, at) => (at === index ? { ...column, [field]: value } : column)));
  }

  protected move(index: number, by: number): void {
    this.drafts.update((list) => {
      const next = [...list];
      const [column] = next.splice(index, 1);
      next.splice(index + by, 0, column);
      return next;
    });
  }

  protected remove(index: number): void {
    this.drafts.update((list) => list.filter((_, at) => at !== index));
  }

  protected add(): void {
    this.drafts.update((list) => {
      const doneAt = list.findIndex((column) => column.category === 'Done');
      const next = [...list];
      next.splice(doneAt < 0 ? next.length : doneAt, 0, { id: null, name: 'New column', category: 'InProgress', wipLimit: null, taskCount: 0 });
      return next;
    });
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      const saved = await this.api.saveColumns(
        this.projectId(),
        this.drafts().map((column) => ({ id: column.id, name: column.name.trim(), category: column.category, wipLimit: column.wipLimit })),
      );
      this.drafts.set(saved.map(toDraft));
      this.message.set('Board saved.');
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The board could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(projectId: string): Promise<void> {
    try {
      const [board, history] = await Promise.all([this.api.board(projectId), this.api.history(projectId)]);
      this.drafts.set(board.columns.map(toDraft));
      this.history.set(history);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Settings could not be loaded.');
    }
  }
}

function toDraft(column: BoardColumn): ColumnDraft {
  return { id: column.id, name: column.name, category: column.category, wipLimit: column.wipLimit ?? null, taskCount: column.taskCount };
}
