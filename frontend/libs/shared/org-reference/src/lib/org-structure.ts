import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  Injectable,
  effect,
  forwardRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { NG_VALUE_ACCESSOR, type ControlValueAccessor } from '@angular/forms';
import type { DepartmentNode, OrgKind, OrgUnit, PagedResult, PositionOccupancy } from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

export interface OrgListQuery {
  readonly page?: number;
  readonly pageSize?: number;
  readonly sort?: string;
  readonly search?: string;
  readonly active?: boolean;
}

/**
 * The organization structure API (`/api/v1/organizations/*`), shared because the organization
 * screens, the employee forms and later modules (projects, resources) all pick from it - and a
 * feature never imports another feature.
 */
@Injectable({ providedIn: 'root' })
export class OrgStructureApi {
  private readonly http = inject(HttpClient);

  list<T extends OrgUnit = OrgUnit>(kind: OrgKind, query: OrgListQuery = {}): Promise<PagedResult<T>> {
    const params: Record<string, string | number | boolean> = {};
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        params[key] = value as string | number | boolean;
      }
    }

    return firstValueFrom(this.http.get<PagedResult<T>>(`/api/v1/organizations/${kind}`, { params }));
  }

  /** Every active record of a kind, for dropdowns. Organization lists are small; two hundred covers them. */
  async active<T extends OrgUnit = OrgUnit>(kind: OrgKind): Promise<readonly T[]> {
    const page = await this.list<T>(kind, { active: true, pageSize: 200, sort: 'name' });
    return page.items;
  }

  get<T extends OrgUnit = OrgUnit>(kind: OrgKind, id: string): Promise<T> {
    return firstValueFrom(this.http.get<T>(`/api/v1/organizations/${kind}/${id}`));
  }

  async create(kind: OrgKind, body: object): Promise<string> {
    const created = await firstValueFrom(this.http.post<{ id: string }>(`/api/v1/organizations/${kind}`, body));
    return created.id;
  }

  async update(kind: OrgKind, id: string, body: object): Promise<void> {
    await firstValueFrom(this.http.put(`/api/v1/organizations/${kind}/${id}`, body));
  }

  async setActive(kind: OrgKind, id: string, active: boolean): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/organizations/${kind}/${id}/${active ? 'activate' : 'deactivate'}`, {}));
  }

  async close(kind: 'departments' | 'positions', id: string, effectiveTo: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/organizations/${kind}/${id}/close`, { effectiveTo }));
  }

  departmentTree(): Promise<readonly DepartmentNode[]> {
    return firstValueFrom(this.http.get<readonly DepartmentNode[]>('/api/v1/organizations/departments/tree'));
  }

  occupancy(positionIds: readonly string[] = []): Promise<readonly PositionOccupancy[]> {
    return firstValueFrom(
      this.http.get<readonly PositionOccupancy[]>('/api/v1/organizations/positions/occupancy', { params: { ids: [...positionIds] } }),
    );
  }
}

/**
 * A dropdown of active organization records of one kind. Works with reactive forms; the value is
 * the record's id.
 */
@Component({
  selector: 'gggs-org-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => OrgSelect), multi: true }],
  template: `
    <select
      [value]="value() ?? ''"
      [disabled]="disabled()"
      [attr.aria-label]="ariaLabel()"
      (change)="choose($event)"
      (blur)="onTouched()"
    >
      <option value="">{{ placeholder() }}</option>
      @for (option of options(); track option.id) {
        @if (option.id !== excludeId()) {
          <option [value]="option.id" [selected]="option.id === value()">{{ option.name }} ({{ option.code }})</option>
        }
      }
    </select>
  `,
  styles: `
    select {
      width: 100%;
    }
  `,
})
export class OrgSelect implements ControlValueAccessor {
  private readonly api = inject(OrgStructureApi);

  readonly kind = input.required<OrgKind>();
  readonly placeholder = input('None');
  readonly ariaLabel = input<string | undefined>(undefined);
  /** Left out of the options - a department cannot be its own parent. */
  readonly excludeId = input<string | null | undefined>(undefined);

  protected readonly options = signal<readonly OrgUnit[]>([]);
  protected readonly value = signal<string | null>(null);
  protected readonly disabled = signal(false);

  private onChange: (value: string | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  constructor() {
    effect(async () => this.options.set(await this.api.active(this.kind())));
  }

  writeValue(value: string | null): void {
    this.value.set(value || null);
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

  protected choose(event: Event): void {
    const value = (event.target as HTMLSelectElement).value || null;
    this.value.set(value);
    this.onChange(value);
  }
}
