import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Injectable,
  computed,
  forwardRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { NG_VALUE_ACCESSOR, type ControlValueAccessor } from '@angular/forms';
import type {
  BankDetails,
  Dependent,
  EmergencyContactEntry,
  Employee360,
  EmployeeProfile,
  EmployeeRow,
  EmploymentHistoryEntry,
  HireEmployee,
  ImportResult,
  ImportRow,
  OrgChartNode,
  PagedResult,
  StatutoryDetails,
} from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

export interface EmployeeListQuery {
  readonly page?: number;
  readonly pageSize?: number;
  readonly sort?: string;
  readonly search?: string;
  readonly departmentId?: string;
  readonly locationId?: string;
  readonly managerEmployeeId?: string;
  readonly status?: string;
  readonly includeExited?: boolean;
}

/**
 * The Core HR API (`/api/v1/employees`). Shared because pickers across modules - and the
 * employees feature itself - all read the same directory, and a feature never imports another.
 */
@Injectable({ providedIn: 'root' })
export class EmployeeDirectoryApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/employees';

  list(query: EmployeeListQuery = {}): Promise<PagedResult<EmployeeRow>> {
    const params: Record<string, string | number | boolean> = {};
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        params[key] = value as string | number | boolean;
      }
    }

    return firstValueFrom(this.http.get<PagedResult<EmployeeRow>>(this.base, { params }));
  }

  get(id: string): Promise<EmployeeProfile> {
    return firstValueFrom(this.http.get<EmployeeProfile>(`${this.base}/${id}`));
  }

  /** The signed-in user's own record (self-service; needs no employee permission). */
  me(): Promise<EmployeeProfile> {
    return firstValueFrom(this.http.get<EmployeeProfile>('/api/v1/me/employee'));
  }

  get360(id: string): Promise<Employee360> {
    return firstValueFrom(this.http.get<Employee360>(`${this.base}/${id}/360`));
  }

  history(id: string): Promise<readonly EmploymentHistoryEntry[]> {
    return firstValueFrom(this.http.get<readonly EmploymentHistoryEntry[]>(`${this.base}/${id}/history`));
  }

  orgChart(rootId?: string, depth = 3): Promise<readonly OrgChartNode[]> {
    const params: Record<string, string | number> = { depth };
    if (rootId) {
      params['rootId'] = rootId;
    }
    return firstValueFrom(this.http.get<readonly OrgChartNode[]>(`${this.base}/org-chart`, { params }));
  }

  async hire(body: HireEmployee): Promise<{ id: string; employeeCode: string }> {
    return firstValueFrom(this.http.post<{ id: string; employeeCode: string }>(this.base, body));
  }

  async updatePersonal(id: string, body: object): Promise<void> {
    await firstValueFrom(this.http.put(`${this.base}/${id}/personal`, body));
  }

  async updateContact(id: string, body: object): Promise<void> {
    await firstValueFrom(this.http.put(`${this.base}/${id}/contact`, body));
  }

  revealStatutory(id: string): Promise<StatutoryDetails> {
    return firstValueFrom(this.http.get<StatutoryDetails>(`${this.base}/${id}/statutory`));
  }

  async updateStatutory(id: string, body: object): Promise<void> {
    await firstValueFrom(this.http.put(`${this.base}/${id}/statutory`, body));
  }

  revealBank(id: string): Promise<BankDetails> {
    return firstValueFrom(this.http.get<BankDetails>(`${this.base}/${id}/bank`));
  }

  async updateBank(id: string, body: object): Promise<void> {
    await firstValueFrom(this.http.put(`${this.base}/${id}/bank`, body));
  }

  async changeAssignment(id: string, body: object): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/assignment-changes`, body));
  }

  async lifecycle(id: string, action: 'confirm' | 'notice' | 'exit', body: object): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/${action}`, body));
  }

  /** When a fixed-term contract ends (null for open-ended); Separation proposes the exit ahead of it. */
  async setContractEnd(id: string, contractEndDate: string | null): Promise<void> {
    await firstValueFrom(this.http.put(`${this.base}/${id}/contract-end`, { contractEndDate }));
  }

  dependents(id: string): Promise<readonly Dependent[]> {
    return firstValueFrom(this.http.get<readonly Dependent[]>(`${this.base}/${id}/dependents`));
  }

  async addDependent(id: string, body: object): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/dependents`, body));
  }

  async removeDependent(id: string, dependentId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${id}/dependents/${dependentId}`));
  }

  emergencyContacts(id: string): Promise<readonly EmergencyContactEntry[]> {
    return firstValueFrom(this.http.get<readonly EmergencyContactEntry[]>(`${this.base}/${id}/emergency-contacts`));
  }

  async addEmergencyContact(id: string, body: object): Promise<void> {
    await firstValueFrom(this.http.post(`${this.base}/${id}/emergency-contacts`, body));
  }

  async removeEmergencyContact(id: string, contactId: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.base}/${id}/emergency-contacts/${contactId}`));
  }

  importRows(rows: readonly ImportRow[], commit: boolean): Promise<ImportResult> {
    return firstValueFrom(this.http.post<ImportResult>(`${this.base}/import${commit ? '' : '/preview'}`, { rows }));
  }
}

/**
 * Type-ahead employee search bound to a form control; the value is the employee's id. Shows the
 * chosen person's name, and lets the user clear it.
 */
@Component({
  selector: 'gggs-employee-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => EmployeeSelect), multi: true }],
  template: `
    <div class="employee-select">
      <input
        type="text"
        role="combobox"
        aria-autocomplete="list"
        [attr.aria-expanded]="isOpen()"
        [attr.aria-controls]="listboxId"
        [attr.aria-label]="ariaLabel()"
        [attr.aria-activedescendant]="isOpen() && active() >= 0 ? listboxId + '-' + active() : null"
        [placeholder]="placeholder()"
        [value]="text()"
        [disabled]="disabled()"
        (input)="onInput($event)"
        (focus)="onInput($event)"
        (blur)="close()"
        (keydown)="onKeydown($event)"
      />
      @if (value() && !disabled()) {
        <button type="button" class="employee-select__clear" aria-label="Clear" (mousedown)="clear()">
          <i class="pi pi-times" aria-hidden="true"></i>
        </button>
      }
      @if (isOpen()) {
        <ul class="employee-select__list" role="listbox" [id]="listboxId">
          @for (employee of options(); track employee.id; let index = $index) {
            <!-- The option itself is chosen (mouse, or arrow keys and Enter in the input); a button
                 inside an option would be an interactive control nested in another. -->
            <li
              role="option"
              class="employee-select__option"
              [id]="listboxId + '-' + index"
              [class.employee-select__option--active]="index === active()"
              [attr.aria-selected]="employee.id === value()"
              (mousedown)="$event.preventDefault(); pick(employee)"
            >
              <strong>{{ employee.displayName }}</strong>
              <span class="muted">{{ employee.employeeCode }} · {{ employee.designation ?? employee.departmentName ?? '' }}</span>
            </li>
          } @empty {
            <li class="muted employee-select__empty">No matching employees.</li>
          }
        </ul>
      }
    </div>
  `,
  styles: `
    .employee-select {
      position: relative;
    }

    input {
      width: 100%;
    }

    .employee-select__clear {
      position: absolute;
      inset-inline-end: 0.25rem;
      top: 50%;
      transform: translateY(-50%);
      border: 0;
      background: transparent;
      color: var(--gggs-color-text-muted);
      cursor: pointer;
    }

    .employee-select__list {
      position: absolute;
      z-index: 20;
      inset-inline: 0;
      max-height: 16rem;
      margin: 0.25rem 0 0;
      padding: 0.25rem 0;
      overflow-y: auto;
      list-style: none;
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-sm);
      background: var(--gggs-color-surface-raised);
      box-shadow: var(--gggs-shadow-md);
    }

    .employee-select__option {
      display: flex;
      flex-direction: column;
      padding: var(--gggs-space-2) var(--gggs-space-3);
      cursor: pointer;
    }

    .employee-select__option .muted {
      margin-inline-start: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .employee-select__option:hover,
    .employee-select__option--active {
      background: var(--gggs-color-surface-hover);
    }

    .employee-select__empty {
      padding: 0.375rem 0.75rem;
    }
  `,
})
export class EmployeeSelect implements ControlValueAccessor {
  private readonly api = inject(EmployeeDirectoryApi);

  readonly placeholder = input('Search by name or code');
  readonly ariaLabel = input<string | undefined>(undefined);
  /** Left out of the results - an employee cannot manage themselves. */
  readonly excludeId = input<string | null | undefined>(undefined);

  private static nextId = 0;
  protected readonly listboxId = `employee-select-${EmployeeSelect.nextId++}`;

  protected readonly value = signal<string | null>(null);
  protected readonly text = signal('');
  protected readonly results = signal<readonly EmployeeRow[]>([]);
  protected readonly isOpen = signal(false);
  /** The option highlighted from the keyboard, or -1. */
  protected readonly active = signal(-1);
  protected readonly options = computed(() => this.results().filter((employee) => employee.id !== this.excludeId()));
  protected readonly disabled = signal(false);

  private onChange: (value: string | null) => void = () => undefined;
  private onTouched: () => void = () => undefined;
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  writeValue(value: string | null): void {
    this.value.set(value || null);
    if (!value) {
      this.text.set('');
      return;
    }

    void this.api.get(value).then(
      (employee) => this.text.set(employee.displayName),
      () => this.text.set(''),
    );
  }

  registerOnChange(fn: (value: string | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  protected onInput(event: Event): void {
    const query = (event.target as HTMLInputElement).value;
    this.text.set(query);
    clearTimeout(this.timer);
    this.timer = setTimeout(async () => {
      const page = await this.api.list({ search: query, pageSize: 10 });
      this.results.set(page.items);
      this.active.set(-1);
      this.isOpen.set(true);
    }, 250);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.options().length;
    if (!this.isOpen() || count === 0) {
      return;
    }

    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      const step = event.key === 'ArrowDown' ? 1 : -1;
      this.active.set((this.active() + step + count) % count);
      event.preventDefault();
    } else if (event.key === 'Enter' && this.active() >= 0) {
      this.pick(this.options()[this.active()]);
      event.preventDefault();
    } else if (event.key === 'Escape') {
      this.isOpen.set(false);
    }
  }

  protected pick(employee: EmployeeRow): void {
    this.value.set(employee.id);
    this.text.set(employee.displayName);
    this.isOpen.set(false);
    this.onChange(employee.id);
  }

  protected clear(): void {
    this.value.set(null);
    this.text.set('');
    this.onChange(null);
  }

  protected close(): void {
    // After the option's mousedown has been handled.
    setTimeout(() => this.isOpen.set(false), 150);
    this.onTouched();
  }
}
