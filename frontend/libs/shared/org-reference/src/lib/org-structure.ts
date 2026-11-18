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
import type { DepartmentNode, OrgKind, OrgUnit, PagedResult } from '@gggs/shared/models';
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

  /**
   * Every active record of a kind, for dropdowns. Read 200 at a time (the server's page limit) -
   * designations, one per role and level, can run past one page.
   */
  async active<T extends OrgUnit = OrgUnit>(kind: OrgKind): Promise<readonly T[]> {
    // Levels read junior to senior (L1, L2 …), everything else alphabetically.
    const sort = kind === 'levels' ? 'code' : 'name';
    const rows: T[] = [];
    for (let page = 1; page <= 20; page++) {
      const result = await this.list<T>(kind, { active: true, page, pageSize: 200, sort });
      rows.push(...result.items);
      if (result.items.length < 200 || result.hasNextPage === false || rows.length >= (result.totalCount ?? Infinity)) {
        break;
      }
    }
    return rows;
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

  async close(kind: 'departments', id: string, effectiveTo: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/v1/organizations/${kind}/${id}/close`, { effectiveTo }));
  }

  departmentTree(): Promise<readonly DepartmentNode[]> {
    return firstValueFrom(this.http.get<readonly DepartmentNode[]>('/api/v1/organizations/departments/tree'));
  }

  /** What an org-structure file (CSV or a Markdown table) would change. Nothing is saved. */
  previewStructureImport(content: string): Promise<StructurePlan> {
    return firstValueFrom(this.http.post<StructurePlan>('/api/v1/organizations/structure-imports/preview', { content }));
  }

  /** Applies the file: the server plans it again and saves it all at once, or nothing if it has problems. */
  applyStructureImport(content: string): Promise<StructureImportResult> {
    return firstValueFrom(this.http.post<StructureImportResult>('/api/v1/organizations/structure-imports/apply', { content }));
  }
}

export type PlannedChange = 'Create' | 'Update' | 'Unchanged';

export interface StructureIssue {
  /** 0 for the file as a whole. */
  readonly line: number;
  readonly code: string;
  readonly message: string;
}

export interface PlannedUnit {
  readonly code: string;
  readonly name: string;
  /** A function's or role's department code; absent for departments. */
  readonly departmentCode?: string;
  readonly change: PlannedChange;
  readonly note?: string | null;
}

export interface StructurePlan {
  readonly rowsRead: number;
  readonly duplicateRows: number;
  readonly departments: readonly PlannedUnit[];
  readonly functions: readonly PlannedUnit[];
  readonly roles: readonly PlannedUnit[];
  readonly newDesignations: number;
  readonly errors: readonly StructureIssue[];
  readonly warnings: readonly StructureIssue[];
  readonly canApply: boolean;
}

export interface StructureImportResult {
  readonly departmentsCreated: number;
  readonly departmentsUpdated: number;
  readonly functionsCreated: number;
  readonly functionsUpdated: number;
  readonly rolesCreated: number;
  readonly rolesUpdated: number;
  readonly designationsCreated: number;
}

/**
 * The department whose roles someone placed in `departmentId` can hold: the department itself, or
 * - for a function - the department it belongs to. Null when nothing is chosen.
 */
export function roleDepartmentOf(departmentId: string | null | undefined, departments: readonly OrgUnit[]): string | null {
  if (!departmentId) {
    return null;
  }
  const chosen = departments.find((row) => row.id === departmentId);
  const parent = typeof chosen?.['parentDepartmentId'] === 'string' ? (chosen['parentDepartmentId'] as string) : null;
  return parent ?? departmentId;
}

/**
 * A filter for a role dropdown: the roles of `departmentId` (or of a function's department), plus
 * roles that belong to no department. With nothing chosen every role is offered.
 */
export function rolesFor(departmentId: string | null | undefined, departments: readonly OrgUnit[]): ((role: OrgUnit) => boolean) | null {
  const owner = roleDepartmentOf(departmentId, departments);
  if (!owner) {
    return null;
  }
  return (role) => !role['departmentId'] || role['departmentId'] === owner;
}

/** A filter for a designation dropdown: the chosen role's designations, plus free-standing ones. */
export function designationsFor(roleId: string | null | undefined): ((designation: OrgUnit) => boolean) | null {
  if (!roleId) {
    return null;
  }
  return (designation) => !designation['roleId'] || designation['roleId'] === roleId;
}

/** One choice in a department dropdown: a department, or one of its functions labelled "Department › Function". */
export interface DepartmentOption {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly label: string;
  readonly isFunction: boolean;
}

/**
 * Departments in tree order - each department followed by its functions - labelled so a function
 * reads "Sales & Marketing › Sales". A function whose department is not in the list (closed, or
 * filtered out) still shows, under its own name.
 */
export function departmentOptions(rows: readonly OrgUnit[]): readonly DepartmentOption[] {
  const byId = new Map(rows.map((row) => [row.id, row]));
  const parentOf = (row: OrgUnit) => (typeof row['parentDepartmentId'] === 'string' ? (row['parentDepartmentId'] as string) : null);
  const byName = (a: OrgUnit, b: OrgUnit) => a.name.localeCompare(b.name);

  const departments = rows.filter((row) => !parentOf(row) || !byId.has(parentOf(row) as string)).sort(byName);
  const options: DepartmentOption[] = [];
  for (const department of departments) {
    options.push({ id: department.id, code: department.code, name: department.name, label: department.name, isFunction: !!parentOf(department) });
    for (const fn of rows.filter((row) => parentOf(row) === department.id).sort(byName)) {
      options.push({ id: fn.id, code: fn.code, name: fn.name, label: `${department.name} › ${fn.name}`, isFunction: true });
    }
  }
  return options;
}

/**
 * A dropdown of active organization records of one kind. Works with reactive forms; the value is
 * the record's id. Departments list each department followed by its functions.
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
          <option [value]="option.id" [selected]="option.id === value()">{{ option.label }} ({{ option.code }})</option>
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
  /** Departments only, no functions - for choosing which department a function belongs to. */
  readonly topLevelOnly = input(false);
  /**
   * Only rows this accepts are offered - for example a department's roles. The current value is
   * always kept in the list so a saved choice still shows.
   */
  readonly where = input<((row: OrgUnit) => boolean) | null>(null);

  private readonly rows = signal<readonly OrgUnit[]>([]);
  protected readonly options = signal<readonly { id: string; code: string; label: string }[]>([]);
  protected readonly value = signal<string | null>(null);
  protected readonly disabled = signal(false);

  private onChange: (value: string | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  constructor() {
    effect(async () => {
      this.rows.set(await this.api.active(this.kind()));
    });

    effect(() => {
      const kind = this.kind();
      const where = this.where();
      const current = this.value();
      const rows = this.rows().filter((row) => !where || where(row) || row.id === current);
      if (kind !== 'departments') {
        this.options.set(rows.map((row) => ({ id: row.id, code: row.code, label: row.name })));
        return;
      }
      const options = departmentOptions(rows);
      this.options.set(this.topLevelOnly() ? options.filter((option) => !option.isFunction) : options);
    });
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
