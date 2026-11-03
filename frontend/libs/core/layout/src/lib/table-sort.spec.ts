import { describe, expect, it } from 'vitest';
import { parseDate, parseNumber, refreshSortableTables, sortOrder, toggleSort } from './table-sort';

/** The value, or a failed test if it is missing. */
function must<T>(value: T | null | undefined): T {
  if (value === null || value === undefined) {
    throw new Error('Expected a value');
  }
  return value;
}

describe('parseNumber', () => {
  it('reads figures the way pages print them', () => {
    expect(parseNumber('1,23,456')).toBe(123456);
    expect(parseNumber('₹9,045')).toBe(9045);
    expect(parseNumber('46.9%')).toBe(46.9);
    expect(parseNumber('7.5 h')).toBe(7.5);
    expect(parseNumber('−12')).toBe(-12);
  });

  it('refuses text that only contains a number', () => {
    expect(parseNumber('₹9,045 × 24 at 8%')).toBeUndefined();
    expect(parseNumber('LN-2026-00004')).toBeUndefined();
  });
});

describe('parseDate', () => {
  it('reads printed dates but not bare numbers', () => {
    expect(parseDate('22 Oct 2026')).toBe(Date.parse('22 Oct 2026'));
    expect(parseDate('2026-10-22')).toBe(Date.parse('2026-10-22'));
    expect(parseDate('7')).toBeUndefined();
  });
});

describe('sortOrder', () => {
  it('sorts money as numbers, not as text', () => {
    expect(sortOrder(['₹9,045', '₹20,000', '₹8,871'], 'ascending')).toEqual([2, 0, 1]);
  });

  it('sorts dates by time', () => {
    expect(sortOrder(['2 Nov 2026', '22 Oct 2026', '30 Sep 2026'], 'ascending')).toEqual([2, 1, 0]);
  });

  it('sorts text naturally and keeps blanks last in either direction', () => {
    expect(sortOrder(['Item 10', '—', 'Item 9'], 'ascending')).toEqual([2, 0, 1]);
    expect(sortOrder(['Item 10', '—', 'Item 9'], 'descending')).toEqual([0, 2, 1]);
  });
});

function table(html: string): HTMLTableElement {
  const host = document.createElement('div');
  host.innerHTML = html;
  document.body.replaceChildren(host);
  refreshSortableTables(host);
  return must(host.querySelector('table'));
}

const names = (element: HTMLTableElement) => Array.from(must(element.tBodies[0]).rows).map((row) => must(row.cells[0]).textContent);
const header = (element: HTMLTableElement, column: number) => must(must(element.tHead).rows[0]?.cells[column]);

describe('sorting a page table', () => {
  const simple = `<table><thead><tr><th>Name</th><th class="num">Net</th><th class="actions"></th></tr></thead>
    <tbody><tr><td>Rohan</td><td>₹8,000</td><td></td></tr><tr><td>Meera</td><td>₹1,34,206</td><td></td></tr><tr><td>Priya</td><td>₹60,000</td><td></td></tr></tbody></table>`;

  it('marks the headers it can sort, and not the actions column', () => {
    const element = table(simple);
    expect(header(element, 0).getAttribute('aria-sort')).toBe('none');
    expect(header(element, 0).tabIndex).toBe(0);
    expect(header(element, 2).hasAttribute('aria-sort')).toBe(false);
  });

  it('cycles ascending, descending, then the page order', () => {
    const element = table(simple);
    const net = header(element, 1);

    toggleSort(net);
    expect(net.getAttribute('aria-sort')).toBe('ascending');
    expect(names(element)).toEqual(['Rohan', 'Priya', 'Meera']);

    toggleSort(net);
    expect(names(element)).toEqual(['Meera', 'Priya', 'Rohan']);

    toggleSort(net);
    expect(net.getAttribute('aria-sort')).toBe('none');
    expect(names(element)).toEqual(['Rohan', 'Meera', 'Priya']);
  });

  it('keeps a chosen sort when the page adds a row', () => {
    const element = table(simple);
    toggleSort(header(element, 0));
    must(element.tBodies[0]).insertAdjacentHTML('beforeend', '<tr><td>Aarav</td><td>₹1</td><td></td></tr>');
    refreshSortableTables(must(element.parentElement));
    expect(names(element)).toEqual(['Aarav', 'Meera', 'Priya', 'Rohan']);
  });

  it('leaves editable grids and merged cells alone', () => {
    const editable = table(`<table><thead><tr><th>Day</th></tr></thead><tbody><tr><td><input /></td></tr><tr><td>Tue</td></tr></tbody></table>`);
    expect(editable.querySelector('[aria-sort]')).toBeNull();

    const merged = table(`<table><thead><tr><th>A</th><th>B</th></tr></thead><tbody><tr><td colspan="2">x</td></tr><tr><td>1</td><td>2</td></tr></tbody></table>`);
    expect(merged.querySelector('[aria-sort]')).toBeNull();
  });
});
