import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { DataHubApi, downloadFile } from '@gggs/shared/insight';
import type { ImportHandlerInfo, ImportOutcome, ImportRunItem } from '@gggs/shared/models';
import { EmptyState, PageHeader, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/** Bulk imports: choose what to import, download its template, upload, preview every row, then commit the valid ones. */
@Component({
  selector: 'gggs-imports-page',
  imports: [DatePipe, PageHeader, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Imports" subtitle="Bring data in from spreadsheets: check every row first, then import the valid ones." />
    <div class="hub">
      <section class="panel stack">
        <h2 class="xp__heading">What to import</h2>
        @for (handler of handlers(); track handler.key) {
          <button type="button" class="xp__card hub__choice" [class.hub__choice--active]="handler.key === selected()?.key" (click)="choose(handler)">
            <strong>{{ handler.title }}</strong>
            <span class="muted">{{ handler.description }}</span>
          </button>
        } @empty {
          <gggs-empty-state icon="pi pi-upload" title="Nothing you can import" />
        }
      </section>
      <section class="panel stack">
        @if (selected(); as handler) {
          <div class="toolbar">
            <h2 class="xp__heading">{{ handler.title }}</h2>
            <button type="button" class="btn btn--sm" (click)="template(handler)"><i class="pi pi-download" aria-hidden="true"></i> Template</button>
          </div>
          <table class="table hub__columns">
            <thead><tr><th>Column</th><th>Required</th><th>Example</th><th>Meaning</th></tr></thead>
            <tbody>
              @for (column of handler.columns; track column.name) {
                <tr><td><code>{{ column.name }}</code></td><td>{{ column.required ? 'Yes' : '' }}</td><td class="muted">{{ column.example ?? '' }}</td><td>{{ column.description }}</td></tr>
              }
            </tbody>
          </table>
          <label class="field">
            <span class="field__label">CSV file</span>
            <input type="file" accept=".csv,text/csv" (change)="read($any($event.target).files?.[0])" />
          </label>
          <label class="field">
            <span class="field__label">Or paste CSV</span>
            <textarea rows="6" spellcheck="false" [value]="csv()" (input)="csv.set($any($event.target).value); outcome.set(null)"></textarea>
          </label>
          <div class="toolbar">
            <button type="button" class="btn" [disabled]="!csv().trim() || busy()" (click)="preview(handler)">Check</button>
            <button type="button" class="btn btn--primary" [disabled]="!outcome() || !outcome()!.valid || committed() || busy()" (click)="commit(handler)">Import {{ outcome()?.valid ?? 0 }} valid row(s)</button>
          </div>
          @if (outcome(); as o) {
            <p [class.xp__notice]="committed()">
              {{ o.rows }} row(s): {{ o.valid }} valid, {{ o.rows - o.valid }} with problems{{ committed() ? '; ' + o.imported + ' imported.' : '.' }}
            </p>
            @for (result of problems(); track $index) {
              <div class="xp__row">
                <span>{{ result.row ? 'Row ' + result.row : 'Note' }}</span>
                <span class="xp__danger">{{ result.errors.join(' ') }}</span>
              </div>
            }
            @for (result of good(); track $index) {
              <div class="xp__row">
                <span>Row {{ result.row }}</span>
                <span>{{ result.summary }}</span>
              </div>
            }
          }
        } @else {
          <p class="muted">Choose what to import.</p>
        }
        @if (error()) {
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
        }
      </section>
    </div>
    <section class="panel stack">
      <h2 class="xp__heading">Recent imports</h2>
      @for (run of history(); track run.id) {
        <div class="xp__row">
          <span><strong>{{ titleOf(run.handlerKey) }}</strong> <span class="muted">{{ run.fileName }}</span></span>
          <span>{{ run.imported }} of {{ run.rows }} row(s) imported</span>
          <span class="muted">{{ run.createdDate | date: 'd MMM y, HH:mm' }}</span>
          <gggs-status-badge [status]="run.imported === run.rows ? 'Completed' : 'Warning'" />
        </div>
      } @empty {
        <p class="muted">No imports yet.</p>
      }
    </section>
  `,
  styles: `
    .hub {
      display: grid;
      grid-template-columns: 18rem minmax(0, 1fr);
      gap: var(--gggs-space-3);
      align-items: start;
    }

    .hub__choice {
      text-align: start;
      background: transparent;
      color: inherit;
      cursor: pointer;
    }

    .hub__choice--active {
      border-color: var(--gggs-color-primary);
      outline: 1px solid var(--gggs-color-primary);
    }



    textarea {
      font-family: var(--gggs-font-family-mono, monospace);
    }

    @media (max-width: 64rem) {
      .hub {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class ImportsPage {
  private readonly api = inject(DataHubApi);

  protected readonly handlers = signal<readonly ImportHandlerInfo[]>([]);
  protected readonly history = signal<readonly ImportRunItem[]>([]);
  protected readonly selected = signal<ImportHandlerInfo | null>(null);
  protected readonly csv = signal('');
  protected readonly fileName = signal<string | null>(null);
  protected readonly outcome = signal<ImportOutcome | null>(null);
  protected readonly committed = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly problems = computed(() => (this.outcome()?.results ?? []).filter((result) => result.errors.length > 0));
  protected readonly good = computed(() => (this.outcome()?.results ?? []).filter((result) => result.errors.length === 0).slice(0, 50));

  constructor() {
    this.api.imports().then((handlers) => this.handlers.set(handlers), (error: unknown) => this.error.set(message(error, 'Imports could not be loaded.')));
    this.loadHistory();
  }

  protected titleOf(key: string): string {
    return this.handlers().find((handler) => handler.key === key)?.title ?? key;
  }

  protected choose(handler: ImportHandlerInfo): void {
    this.selected.set(handler);
    this.csv.set('');
    this.fileName.set(null);
    this.outcome.set(null);
    this.committed.set(false);
    this.error.set(null);
  }

  protected async template(handler: ImportHandlerInfo): Promise<void> {
    downloadFile(`${handler.key}-template.csv`, 'text/csv', new Blob([await this.api.template(handler.key)], { type: 'text/csv' }));
  }

  protected async read(file: File | undefined): Promise<void> {
    if (file) {
      this.fileName.set(file.name);
      this.csv.set(await file.text());
      this.outcome.set(null);
      this.committed.set(false);
    }
  }

  protected preview(handler: ImportHandlerInfo): Promise<void> {
    return this.run(async () => {
      this.committed.set(false);
      this.outcome.set(await this.api.preview(handler.key, this.csv(), this.fileName()));
    });
  }

  protected commit(handler: ImportHandlerInfo): Promise<void> {
    return this.run(async () => {
      this.outcome.set(await this.api.run(handler.key, this.csv(), this.fileName()));
      this.committed.set(true);
      this.loadHistory();
    });
  }

  private loadHistory(): void {
    this.api.history().then((history) => this.history.set(history), () => undefined);
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await work();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}
