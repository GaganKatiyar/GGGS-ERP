import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, model, output, signal } from '@angular/core';

/**
 * The bar above every list: a debounced search box, room for filters, and a clear button that
 * appears once something is filtered. Filters are projected so each page keeps its own.
 */
@Component({
  selector: 'gggs-filter-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="filter-bar toolbar" role="search">
      <label class="filter-bar__search">
        <i class="pi pi-search" aria-hidden="true"></i>
        <input
          type="search"
          [placeholder]="placeholder()"
          [value]="search()"
          [attr.aria-label]="placeholder() || 'Search'"
          (input)="onInput($event)"
        />
      </label>
      <ng-content />
      @if (active()) {
        <button type="button" class="btn btn--link btn--sm" (click)="clear()">Clear filters</button>
      }
    </div>
  `,
  styles: `
    .filter-bar {
      margin-block-end: var(--gggs-space-4);
    }

    .filter-bar__search {
      position: relative;
      display: inline-flex;
      align-items: center;
      flex: 1 1 16rem;
      max-width: 26rem;
    }

    .filter-bar__search .pi {
      position: absolute;
      inset-inline-start: var(--gggs-space-3);
      color: var(--gggs-color-text-subtle);
      pointer-events: none;
    }

    .filter-bar__search input {
      width: 100%;
      padding-inline-start: var(--gggs-space-8);
    }

    :host ::ng-deep select {
      width: auto;
      min-width: 10rem;
    }
  `,
})
export class FilterBar {
  readonly placeholder = input('Search');
  /** True when any filter (including the search) is applied, to show "Clear filters". */
  readonly active = input(false);
  readonly debounceMs = input(300);
  readonly search = model('');
  readonly cleared = output<void>();

  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  protected onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.search.set(value), this.debounceMs());
  }

  protected clear(): void {
    clearTimeout(this.timer);
    this.search.set('');
    this.cleared.emit();
  }
}

export interface DateRange {
  readonly from: string | null;
  readonly to: string | null;
}

/** Two dates, from and to, as ISO strings (yyyy-MM-dd). "To" cannot be set before "from". */
@Component({
  selector: 'gggs-date-range',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="date-range" role="group" [attr.aria-label]="label()">
      <input type="date" [value]="value().from ?? ''" [attr.aria-label]="label() + ' from'" (change)="set('from', $event)" />
      <span aria-hidden="true">–</span>
      <input
        type="date"
        [value]="value().to ?? ''"
        [min]="value().from ?? ''"
        [attr.aria-label]="label() + ' to'"
        (change)="set('to', $event)"
      />
    </span>
  `,
  styles: `
    .date-range {
      display: inline-flex;
      align-items: center;
      gap: var(--gggs-space-2);
    }

    input {
      padding: var(--gggs-space-2);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-sm);
      background: var(--gggs-color-surface-raised);
      color: inherit;
      font: inherit;
    }
  `,
})
export class DateRangeInput {
  readonly label = input('Date range');
  readonly value = model<DateRange>({ from: null, to: null });

  protected set(edge: 'from' | 'to', event: Event): void {
    const date = (event.target as HTMLInputElement).value || null;
    const next = { ...this.value(), [edge]: date };
    if (next.from && next.to && next.to < next.from) {
      next.to = next.from;
    }
    this.value.set(next);
  }
}

/**
 * Pick or drop one file. Emits the file; uploading it is the caller's job. `accept` and `maxBytes`
 * are checked here so an obviously wrong file never leaves the browser.
 */
@Component({
  selector: 'gggs-file-upload',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label
      class="file-upload"
      [class.file-upload--over]="isOver()"
      (dragover)="$event.preventDefault(); isOver.set(true)"
      (dragleave)="isOver.set(false)"
      (drop)="onDrop($event)"
    >
      <i class="pi pi-upload" aria-hidden="true"></i>
      <span>{{ fileName() ?? prompt() }}</span>
      <input type="file" [accept]="accept()" (change)="onPick($event)" />
    </label>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styles: `
    .file-upload {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--gggs-space-2);
      padding: var(--gggs-space-6);
      border: 2px dashed var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      color: var(--gggs-color-text-muted);
      cursor: pointer;
      text-align: center;
    }

    .file-upload--over {
      border-color: var(--gggs-color-primary);
      background: var(--gggs-color-primary-subtle);
    }

    input {
      position: absolute;
      width: 1px;
      height: 1px;
      opacity: 0;
    }
  `,
})
export class FileUpload {
  readonly accept = input('');
  readonly maxBytes = input(10 * 1024 * 1024);
  readonly prompt = input('Drop a file here, or click to choose one');
  readonly fileSelected = output<File>();

  protected readonly isOver = signal(false);
  protected readonly fileName = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected onPick(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) {
      this.take(file);
    }
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isOver.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) {
      this.take(file);
    }
  }

  private take(file: File): void {
    const accepted = this.accept()
      .split(',')
      .map((type) => type.trim().toLowerCase())
      .filter(Boolean);

    if (accepted.length > 0 && !accepted.some((type) => file.name.toLowerCase().endsWith(type) || file.type === type)) {
      this.error.set(`Choose a ${accepted.join(' or ')} file.`);
      return;
    }

    if (file.size > this.maxBytes()) {
      this.error.set(`That file is larger than ${Math.round(this.maxBytes() / 1024 / 1024)} MB.`);
      return;
    }

    this.error.set(null);
    this.fileName.set(file.name);
    this.fileSelected.emit(file);
  }
}
