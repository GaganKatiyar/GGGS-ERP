import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { TaskImportPreview } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { FormDialog } from '@gggs/shared/ui';

const MOST_BYTES = 2_000_000;

/** Brings tasks in from a spreadsheet or a Jira CSV export: nothing is saved until the preview has been seen. */
@Component({
  selector: 'gggs-task-import-dialog',
  imports: [FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog [(visible)]="visible" title="Import tasks" size="lg" [saveLabel]="'Import ' + (preview()?.willCreate ?? 0) + ' tasks'" [busy]="busy()" [error]="error()" [saveDisabled]="!preview()?.willCreate" (save)="commit()">
      <div class="stack">
        <p class="muted imp__note">
          A CSV with a Title or Summary column, or a Jira CSV export. Type, priority, assignee, status, parent, dates, estimate and story points are picked up when present.
          Importing the same file again skips what came in before, if the file has a Key column.
        </p>
        <label class="field">
          <span class="field__label">File</span>
          <input type="file" accept=".csv,text/csv" (change)="choose($event)" />
        </label>

        @if (preview(); as p) {
          <p class="imp__sum">
            <strong>{{ p.willCreate }}</strong> will come in.
            @if (p.alreadyImported) {
              {{ p.alreadyImported }} came in before and will be skipped.
            }
            @if (p.withErrors) {
              <span class="imp__bad">{{ p.withErrors }} cannot come in.</span>
            }
          </p>
          <div class="imp__scroll" tabindex="0" role="region" aria-label="Rows to import">
            <table class="table">
              <thead>
                <tr><th scope="col" class="imp__num">Line</th><th scope="col">Title</th><th scope="col">Type</th><th scope="col">Priority</th><th scope="col">Assignee</th><th scope="col">Column</th><th scope="col">Parent</th><th scope="col">Notes</th></tr>
              </thead>
              <tbody>
                @for (row of p.rows; track row.line) {
                  <tr [class.imp__skip]="row.alreadyImported || row.errors.length">
                    <td class="imp__num">{{ row.line }}</td>
                    <td>{{ row.title || '(no title)' }}</td>
                    <td>{{ humanize(row.type) }}</td>
                    <td>{{ row.priority }}</td>
                    <td>{{ row.assigneeName ?? '' }}</td>
                    <td>{{ row.columnName }}</td>
                    <td>{{ row.parentKey ?? '' }}</td>
                    <td class="imp__notes">
                      @if (row.alreadyImported) {
                        <span>Came in before.</span>
                      }
                      @for (message of row.errors; track message) {
                        <span class="imp__bad">{{ message }}</span>
                      }
                      @for (message of row.warnings; track message) {
                        <span>{{ message }}</span>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .imp__note,
    .imp__sum {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .imp__scroll {
      overflow: auto;
      max-block-size: 22rem;
    }

    .imp__num {
      text-align: end !important;
    }

    .imp__skip {
      opacity: 0.65;
    }

    .imp__bad {
      color: var(--gggs-color-danger);
    }

    .imp__notes {
      font-size: var(--gggs-font-size-sm);
    }

    .imp__notes span {
      display: block;
    }
  `,
})
export class TaskImportDialog {
  private readonly api = inject(ProjectsApi);

  readonly visible = model(false);
  readonly projectId = input.required<string>();
  readonly imported = output<number>();

  protected readonly humanize = humanize;
  protected readonly preview = signal<TaskImportPreview | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private csv = '';

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => {
          this.preview.set(null);
          this.error.set(null);
          this.csv = '';
        });
      }
    });
  }

  protected async choose(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    this.preview.set(null);
    this.error.set(null);
    if (!file) {
      return;
    }

    if (file.size > MOST_BYTES) {
      this.error.set('That file is too big. Split it into files under 2 MB.');
      return;
    }

    this.busy.set(true);
    try {
      this.csv = await file.text();
      this.preview.set(await this.api.previewTaskImport(this.projectId(), this.csv));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That file could not be read.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async commit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const result = await this.api.importTasks(this.projectId(), this.csv);
      this.visible.set(false);
      this.imported.emit(result.created);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The tasks could not be imported.');
    } finally {
      this.busy.set(false);
    }
  }
}
