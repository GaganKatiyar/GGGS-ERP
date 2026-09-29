import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import type { EmployeeListItem } from '@gggs/shared/models';
import { EmployeeReferenceApi } from './employee-reference-api';

/**
 * Type-ahead employee search - the picker `attendance`, `leave` and `workflow` all need
 * somewhere to name an employee without a raw id, and none of them may own employee search
 * themselves (see `EmployeeReferenceApi`'s own remarks). Debounced by hand rather than through
 * RxJS operators, matching how the rest of this codebase's screens are written (signals + plain
 * `async`/`await`, not observable pipelines).
 */
@Component({
  selector: 'gggs-employee-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="employee-picker">
      <input
        #searchInput
        type="text"
        role="combobox"
        aria-autocomplete="list"
        [attr.aria-expanded]="isOpen()"
        [attr.aria-controls]="isOpen() ? listboxId : null"
        [placeholder]="placeholder()"
        [value]="query()"
        (input)="onInput($event)"
        (focus)="onFocus()"
        (blur)="onBlur()"
      />

      @if (isOpen()) {
        <ul class="employee-picker__list" [id]="listboxId" role="listbox">
          @if (isLoading()) {
            <li class="employee-picker__status">Searching…</li>
          } @else if (results().length === 0) {
            <li class="employee-picker__status">No employees found.</li>
          } @else {
            @for (employee of results(); track employee.id) {
              <li role="option" [attr.aria-selected]="false">
                <button
                  type="button"
                  class="employee-picker__option"
                  (mousedown)="select(employee)"
                >
                  <span class="employee-picker__name">{{ employee.displayName }}</span>
                  <span class="employee-picker__code">{{ employee.employeeCode }}</span>
                </button>
              </li>
            }
          }
        </ul>
      }
    </div>
  `,
  styles: `
    .employee-picker {
      position: relative;
    }

    .employee-picker input {
      width: 100%;
      font: inherit;
      color: var(--gggs-color-text, #1f2430);
      background: var(--gggs-color-surface-raised, #ffffff);
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: var(--gggs-radius-sm, 0.375rem);
      padding: var(--gggs-space-2, 0.5rem) var(--gggs-space-3, 0.75rem);
    }

    .employee-picker input:focus-visible {
      outline: 2px solid var(--gggs-color-primary, #1d4ed8);
      outline-offset: 1px;
    }

    .employee-picker__list {
      position: absolute;
      z-index: 10;
      inset-inline: 0;
      margin: 0.25rem 0 0;
      padding: 0.25rem;
      list-style: none;
      max-height: 16rem;
      overflow-y: auto;
      background: var(--gggs-color-surface-raised, #ffffff);
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: var(--gggs-radius-md, 0.5rem);
      box-shadow: var(--gggs-shadow-md, 0 4px 12px rgb(0 0 0 / 0.1));
    }

    .employee-picker__status {
      padding: 0.5rem 0.75rem;
      color: var(--gggs-color-text-muted, #5b6472);
      font-size: 0.875rem;
    }

    .employee-picker__option {
      display: flex;
      justify-content: space-between;
      gap: 0.5rem;
      width: 100%;
      padding: 0.5rem 0.75rem;
      border: none;
      background: none;
      border-radius: var(--gggs-radius-sm, 0.375rem);
      cursor: pointer;
      font: inherit;
      text-align: left;
    }

    .employee-picker__option:hover,
    .employee-picker__option:focus-visible {
      background: var(--gggs-color-surface-sunken, #f5f6f8);
    }

    .employee-picker__code {
      color: var(--gggs-color-text-muted, #5b6472);
      font-size: 0.8125rem;
    }
  `,
})
export class EmployeePicker {
  private readonly api = inject(EmployeeReferenceApi);
  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');

  readonly placeholder = input('Search by name or employee code…');

  /** Emits the chosen employee. The caller reads `.id` for whatever query it makes next. */
  readonly employeeSelected = output<EmployeeListItem>();

  protected readonly listboxId = `employee-picker-${crypto.randomUUID()}`;

  protected readonly query = signal('');
  protected readonly results = signal<readonly EmployeeListItem[]>([]);
  protected readonly isLoading = signal(false);
  protected readonly isFocused = signal(false);

  protected readonly isOpen = computed(() => this.isFocused() && this.query().trim().length > 0);

  private debounceHandle: ReturnType<typeof setTimeout> | undefined;
  private searchToken = 0;

  constructor() {
    // Clears the results as soon as the box empties, so a fast backspace never flashes a stale list.
    effect(() => {
      if (this.query().trim().length === 0) {
        this.results.set([]);
      }
    });
  }

  protected onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.query.set(value);

    clearTimeout(this.debounceHandle);

    if (value.trim().length === 0) {
      return;
    }

    this.debounceHandle = setTimeout(() => void this.search(value), 300);
  }

  protected onFocus(): void {
    this.isFocused.set(true);
  }

  protected onBlur(): void {
    // Delayed so the option's own (mousedown) fires first - a plain blur would close the list
    // before the click on an option ever registers.
    setTimeout(() => this.isFocused.set(false), 150);
  }

  protected select(employee: EmployeeListItem): void {
    this.query.set(employee.displayName);
    this.results.set([]);
    this.isFocused.set(false);
    this.employeeSelected.emit(employee);
    this.searchInput()?.nativeElement.blur();
  }

  private async search(value: string): Promise<void> {
    const token = ++this.searchToken;
    this.isLoading.set(true);

    try {
      const result = await this.api.search(value);

      // A slower earlier request landing after a faster later one would otherwise overwrite it
      // with stale results.
      if (token !== this.searchToken) {
        return;
      }

      this.results.set(result.items);
    } finally {
      if (token === this.searchToken) {
        this.isLoading.set(false);
      }
    }
  }
}
