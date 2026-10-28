/**
 * Click-to-sort for the plain HTML tables pages write themselves (the shared data table sorts on
 * the server and is left alone). The shell marks the headers of every eligible table and sorts
 * the rows already on screen, so no page has to opt in and no request changes.
 *
 * Only tables that are safe to reorder take part: one header row, one body, every row a simple
 * row of the header's width. Editable grids (rows holding inputs) are skipped, since a row that
 * moves while someone types in it would be worse than no sorting; so are tables with merged
 * cells, whose rows belong together.
 */
export type SortDirection = 'ascending' | 'descending';

const SORTABLE = 'data-gggs-sortable';

/** A number as a cell shows it - 1,23,456 or ₹9,045 or 46.9% or 7.5 h - or undefined if it is not one. */
export function parseNumber(text: string): number | undefined {
  const cleaned = text
    .replace(/[₹$€£,\s]/g, '')
    .replace(/−/g, '-')
    .replace(/(%|h|hrs?|days?)$/i, '');
  return /^-?\d*\.?\d+$/.test(cleaned) ? Number(cleaned) : undefined;
}

/** A date as a cell shows it (22 Oct 2026, 2026-10-22, Oct 22, 2026), or undefined. */
export function parseDate(text: string): number | undefined {
  // Date.parse reads a bare "7" as a year, so insist on something that looks like a date.
  if (!/\d/.test(text) || !(/[a-z]{3}/i.test(text) || /\d{4}-\d{2}-\d{2}/.test(text))) {
    return undefined;
  }
  const time = Date.parse(text);
  return Number.isNaN(time) ? undefined : time;
}

const isBlank = (text: string) => text === '' || text === '—' || text === '-';

/**
 * The order a column's cells sort in, as indexes into `texts`: by number when every non-blank cell
 * is a number, by date when every one is a date, otherwise as text with numbers inside read as
 * numbers. Blank cells go last either way, and ties keep their original order.
 */
export function sortOrder(texts: readonly string[], direction: SortDirection): number[] {
  const values = texts.map((text) => text.trim());
  const filled = values.filter((text) => !isBlank(text));
  const numbers = filled.every((text) => parseNumber(text) !== undefined);
  const dates = !numbers && filled.every((text) => parseDate(text) !== undefined);
  const sign = direction === 'ascending' ? 1 : -1;
  const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

  return values
    .map((text, index) => ({ text, index, key: numbers ? parseNumber(text) : dates ? parseDate(text) : undefined }))
    .sort((a, b) => {
      const blankA = isBlank(a.text);
      const blankB = isBlank(b.text);
      if (blankA || blankB) {
        return blankA === blankB ? a.index - b.index : blankA ? 1 : -1;
      }

      const difference = numbers || dates ? (a.key ?? 0) - (b.key ?? 0) : collator.compare(a.text, b.text);
      return difference === 0 ? a.index - b.index : sign * difference;
    })
    .map((entry) => entry.index);
}

/** The header cells of a table that is safe to reorder, or null when it is not. */
function sortableHeaderRow(table: HTMLTableElement): HTMLTableRowElement | null {
  const header = table.tHead?.rows.length === 1 ? table.tHead.rows[0] : undefined;
  const body = table.tBodies.length === 1 ? table.tBodies[0] : undefined;
  if (!header || !body || body.rows.length < 2 || body.querySelector('input, select, textarea')) {
    return null;
  }

  const width = header.cells.length;
  const simple = Array.from(body.rows).every((row) => row.cells.length === width && Array.from(row.cells).every((cell) => cell.colSpan === 1 && cell.rowSpan === 1));
  return simple ? header : null;
}

function isSortableHeader(cell: HTMLTableCellElement): boolean {
  return !!cell.textContent?.trim() && !cell.classList.contains('actions') && !cell.hasAttribute('data-nosort') && !cell.querySelector('input, button, a');
}

/** Puts rows back in the order the page rendered them, or sorts them by one column. */
function arrange(table: HTMLTableElement, column: number | null, direction: SortDirection | null): void {
  const body = table.tBodies[0];
  if (!body) {
    return;
  }

  const rows = Array.from(body.rows);
  // Remember where each row started, so "unsorted" is a real state rather than "sorted by row 1".
  rows.forEach((row, index) => (row.dataset['gggsOrder'] ??= `${index}`));
  const started = (index: number) => Number(rows[index]?.dataset['gggsOrder']);
  const order =
    column === null || direction === null
      ? rows.map((_, index) => index).sort((a, b) => started(a) - started(b))
      : sortOrder(rows.map((row) => row.cells[column]?.textContent ?? ''), direction);

  // Moving rows is a DOM change; skip it when they are already in place.
  if (order.every((from, to) => from === to)) {
    return;
  }
  for (const row of order.map((index) => rows[index])) {
    if (row) {
      body.appendChild(row);
    }
  }
}

/**
 * Marks the sortable headers of every eligible table under `root`, and re-applies a chosen sort
 * to rows the page has added or replaced since. Cheap to repeat: it only touches what changed.
 */
export function refreshSortableTables(root: ParentNode): void {
  root.querySelectorAll<HTMLTableElement>('table:not(.p-datatable-table)').forEach((table) => {
    const header = sortableHeaderRow(table);
    if (!header) {
      return;
    }

    for (const cell of Array.from(header.cells)) {
      if (!cell.hasAttribute(SORTABLE) && isSortableHeader(cell)) {
        cell.setAttribute(SORTABLE, '');
        cell.setAttribute('aria-sort', 'none');
        cell.tabIndex = 0;
        cell.title = `Sort by ${cell.textContent?.trim() ?? ''}`;
      }
    }

    const column = table.dataset['gggsSortColumn'];
    const direction = table.dataset['gggsSortDirection'] as SortDirection | undefined;
    if (column !== undefined && direction) {
      arrange(table, Number(column), direction);
    }
  });
}

/** A header was chosen: ascending, then descending, then back to the page's own order. */
export function toggleSort(header: HTMLTableCellElement): void {
  const table = header.closest('table');
  if (!table) {
    return;
  }

  const current = header.getAttribute('aria-sort');
  const next: SortDirection | null = current === 'ascending' ? 'descending' : current === 'descending' ? null : 'ascending';
  table.querySelectorAll(`th[${SORTABLE}]`).forEach((cell) => cell.setAttribute('aria-sort', 'none'));
  if (next) {
    header.setAttribute('aria-sort', next);
    table.dataset['gggsSortColumn'] = `${header.cellIndex}`;
    table.dataset['gggsSortDirection'] = next;
  } else {
    delete table.dataset['gggsSortColumn'];
    delete table.dataset['gggsSortDirection'];
  }
  arrange(table, next ? header.cellIndex : null, next);
}

/** The sortable header an event happened on, if any. */
export function sortableHeader(target: EventTarget | null): HTMLTableCellElement | null {
  return target instanceof Element ? target.closest<HTMLTableCellElement>(`th[${SORTABLE}]`) : null;
}
