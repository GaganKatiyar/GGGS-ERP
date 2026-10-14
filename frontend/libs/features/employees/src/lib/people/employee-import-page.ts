import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import type { ImportResult, ImportRow } from '@gggs/shared/models';
import { EmptyState, FileUpload, KpiCard, PageHeader } from '@gggs/shared/ui';

/** The CSV columns, in template order. Codes refer to organization records and master data. */
const COLUMNS: readonly (keyof ImportRow)[] = [
  'employeeCode',
  'firstName',
  'middleName',
  'lastName',
  'officialEmail',
  'mobilePhone',
  'joiningDate',
  'departmentCode',
  'positionCode',
  'jobCode',
  'gradeCode',
  'locationCode',
  'managerEmployeeCode',
  'employmentTypeCode',
  'workModeCode',
  'designation',
];

const MAX_ROWS = 1000;

/**
 * Splits CSV text into rows of cells: quoted cells may hold commas, newlines and doubled quotes.
 * Exported for tests.
 */
export function parseCsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let quoted = false;

  for (let i = 0; i < text.length; i++) {
    const char = text[i];
    if (quoted) {
      if (char === '"' && text[i + 1] === '"') {
        cell += '"';
        i++;
      } else if (char === '"') {
        quoted = false;
      } else {
        cell += char;
      }
    } else if (char === '"') {
      quoted = true;
    } else if (char === ',') {
      row.push(cell);
      cell = '';
    } else if (char === '\n' || char === '\r') {
      if (char === '\r' && text[i + 1] === '\n') {
        i++;
      }
      row.push(cell);
      rows.push(row);
      row = [];
      cell = '';
    } else {
      cell += char;
    }
  }

  if (cell || row.length) {
    row.push(cell);
    rows.push(row);
  }

  return rows.filter((cells) => cells.some((value) => value.trim()));
}

/** CSV rows to import rows, matching headers case-insensitively (trim also drops a BOM) and ignoring unknown columns. Exported for tests. */
export function toImportRows(cells: string[][]): { rows: ImportRow[]; unknown: string[] } {
  const [header = [], ...data] = cells;
  const byName = new Map(COLUMNS.map((column) => [column.toLowerCase(), column]));
  const mapped = header.map((name) => byName.get(name.trim().toLowerCase()));
  const unknown = header.filter((_, index) => !mapped[index]).map((name) => name.trim());

  const rows = data.map((values) => {
    const row: Partial<Record<keyof ImportRow, string>> = {};
    mapped.forEach((column, index) => {
      const value = values[index]?.trim();
      if (column && value) {
        row[column] = value;
      }
    });
    return row as ImportRow;
  });

  return { rows, unknown };
}

/**
 * Bulk hire from a CSV (spec §116): the file is checked row by row first - nothing is saved - and
 * only a clean preview can be imported, all or nothing.
 */
@Component({
  selector: 'gggs-employee-import-page',
  imports: [RouterLink, PageHeader, FileUpload, KpiCard, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Import employees" subtitle="Upload a CSV, review every row, then import them together.">
      <div class="toolbar">
        <button type="button" class="btn" (click)="downloadTemplate()"><i class="pi pi-download" aria-hidden="true"></i> Template</button>
        <a class="btn" routerLink="..">Back to directory</a>
      </div>
    </gggs-page-header>

    <div class="stack">
      <section class="panel stack">
        <gggs-file-upload accept=".csv,text/csv" prompt="Drop a CSV file here, or click to choose one" (fileSelected)="read($event)" />
        <p class="muted">
          Up to {{ maxRows }} rows. Use codes for organization records (department, position, job, grade, location) and master data (employment type, work mode), and
          the manager's employee code. Dates are YYYY-MM-DD.
        </p>
        @if (fileName()) {
          <p>
            <strong>{{ fileName() }}</strong> · {{ rows().length }} rows
            @if (unknownColumns().length) {
              <span class="muted"> · ignored columns: {{ unknownColumns().join(', ') }}</span>
            }
          </p>
        }
        @if (error()) {
          <p class="field__error" role="alert">{{ error() }}</p>
        }
        <div class="toolbar">
          <button type="button" class="btn" [disabled]="!rows().length || busy()" (click)="check()">{{ busy() && !committing() ? 'Checking…' : 'Check rows' }}</button>
          <button type="button" class="btn btn--primary" [disabled]="!canCommit() || busy()" (click)="commit()">
            {{ committing() ? 'Importing…' : 'Import ' + rows().length + ' employees' }}
          </button>
        </div>
      </section>

      @if (result(); as r) {
        <div class="kpi-row">
          <gggs-kpi-card label="Rows" icon="pi pi-list" [value]="r.total" />
          <gggs-kpi-card label="Ready" icon="pi pi-check" [value]="r.succeeded" />
          <gggs-kpi-card label="With errors" icon="pi pi-times" [value]="r.failed" />
        </div>

        @if (r.committed) {
          <gggs-empty-state icon="pi pi-check-circle" [title]="r.succeeded + ' employees imported'" description="They are in the directory now." actionLabel="Open directory" (action)="done()" />
        } @else {
          <section class="panel">
            <table class="import__table">
              <caption class="muted">{{ r.failed ? 'Fix these rows in the file and check again.' : 'Every row is valid.' }}</caption>
              <thead>
                <tr>
                  <th scope="col">Row</th>
                  <th scope="col">Employee code</th>
                  <th scope="col">Result</th>
                </tr>
              </thead>
              <tbody>
                @for (row of visibleRows(); track row.rowNumber) {
                  <tr [class.import__row--error]="!row.succeeded">
                    <td>{{ row.rowNumber }}</td>
                    <td>{{ row.employeeCode ?? '—' }}</td>
                    <td>
                      @if (row.succeeded) {
                        <span class="import__ok">Ready</span>
                      } @else {
                        <ul class="import__errors">
                          @for (message of row.errors; track $index) {
                            <li>{{ message }}</li>
                          }
                        </ul>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </section>
        }
      }
    </div>
  `,
  styles: `
    .import__table {
      width: 100%;
      border-collapse: collapse;
    }

    .import__table caption {
      padding-block-end: var(--gggs-space-2);
      text-align: start;
    }

    .import__table th,
    .import__table td {
      padding: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
      vertical-align: top;
    }

    .import__row--error td {
      background: var(--gggs-color-danger-subtle, transparent);
    }

    .import__ok {
      color: var(--gggs-color-success);
    }

    .import__errors {
      margin: 0;
      padding-inline-start: 1rem;
      color: var(--gggs-color-danger);
    }
  `,
})
export class EmployeeImportPage {
  private readonly api = inject(EmployeeDirectoryApi);

  protected readonly maxRows = MAX_ROWS;
  protected readonly fileName = signal<string | null>(null);
  protected readonly rows = signal<readonly ImportRow[]>([]);
  protected readonly unknownColumns = signal<readonly string[]>([]);
  protected readonly result = signal<ImportResult | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly committing = signal(false);

  /** Errors first: they are what needs attention. */
  protected readonly visibleRows = computed(() => [...(this.result()?.rows ?? [])].sort((a, b) => Number(a.succeeded) - Number(b.succeeded) || a.rowNumber - b.rowNumber));
  protected readonly canCommit = computed(() => {
    const result = this.result();
    return !!result && !result.committed && result.failed === 0 && result.total > 0;
  });

  protected async read(file: File): Promise<void> {
    this.result.set(null);
    this.error.set(null);
    this.fileName.set(file.name);

    const { rows, unknown } = toImportRows(parseCsv(await file.text()));
    this.unknownColumns.set(unknown);

    if (rows.length > MAX_ROWS) {
      this.rows.set([]);
      this.error.set(`The file has ${rows.length} rows; import at most ${MAX_ROWS} at a time.`);
      return;
    }

    this.rows.set(rows);
    if (!rows.length) {
      this.error.set('No rows found. The first line must be the header from the template.');
    }
  }

  protected check(): Promise<void> {
    return this.send(false);
  }

  protected commit(): Promise<void> {
    return this.send(true);
  }

  protected done(): void {
    this.fileName.set(null);
    this.rows.set([]);
    this.result.set(null);
    history.back();
  }

  protected downloadTemplate(): void {
    const example = ['', 'Asha', '', 'Rao', 'asha.rao@example.com', '+91 98450 00000', '2026-11-02', 'ENG', '', '', '', '', '', 'FULL_TIME', 'OFFICE', 'Software Engineer'];
    const blob = new Blob([`${COLUMNS.join(',')}\r\n${example.join(',')}\r\n`], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'employee-import-template.csv';
    link.click();
    URL.revokeObjectURL(url);
  }

  private async send(commit: boolean): Promise<void> {
    this.busy.set(true);
    this.committing.set(commit);
    this.error.set(null);
    try {
      this.result.set(await this.api.importRows(this.rows(), commit));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The file could not be processed. Try again.');
    } finally {
      this.busy.set(false);
      this.committing.set(false);
    }
  }
}
