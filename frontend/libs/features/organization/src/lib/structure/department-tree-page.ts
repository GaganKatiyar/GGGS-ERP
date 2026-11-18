import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { DepartmentNode, OrgUnit } from '@gggs/shared/models';
import { OrgSelect, OrgStructureApi } from '@gggs/shared/org-reference';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton } from '@gggs/shared/ui';
import { codePart, FUNCTION_CATALOG, suggestedFunctions } from './function-catalog';

/** A business unit and the departments in it; `unit` is null for departments not placed in one. */
interface UnitGroup {
  readonly unit: OrgUnit | null;
  readonly departments: readonly DepartmentNode[];
  readonly headcount: number;
}

interface DepartmentDraft {
  template: string;
  name: string;
  code: string;
  businessUnitId: string | null;
  locationId: string | null;
  headEmployeeId: string | null;
  functions: readonly string[];
}

interface FunctionDraft {
  name: string;
  code: string;
  headEmployeeId: string | null;
}

const emptyDepartment = (): DepartmentDraft => ({ template: '', name: '', code: '', businessUnitId: null, locationId: null, headEmployeeId: null, functions: [] });

/**
 * The organization structure (spec §10): business unit → department → function, with headcounts.
 * Two levels only - a department holds functions and a function holds nothing below it. People
 * sit in a function (or a department with none), and each department adds up its functions.
 */
@Component({
  selector: 'gggs-department-tree-page',
  imports: [FormsModule, RouterLink, PageHeader, KpiCard, EmptyState, Skeleton, FormDialog, OrgSelect, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Organization structure" subtitle="Business units, the departments in them, and each department's functions.">
      <a class="btn" routerLink="../departments">List view</a>
      @if (canImport) {
        <a class="btn" routerLink="../import"><i class="pi pi-upload" aria-hidden="true"></i> Import structure</a>
      }
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="openDepartment()"><i class="pi pi-plus" aria-hidden="true"></i> Add department</button>
      }
    </gggs-page-header>

    <div class="kpi-row org__kpis">
      <gggs-kpi-card label="Business units" icon="pi pi-briefcase" [value]="unitCount()" />
      <gggs-kpi-card label="Departments" icon="pi pi-sitemap" [value]="departments().length" />
      <gggs-kpi-card label="Functions" icon="pi pi-share-alt" [value]="functionCount()" />
      <gggs-kpi-card label="People placed" icon="pi pi-users" [value]="peopleCount()" />
    </div>

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else if (departments().length === 0) {
      <gggs-empty-state icon="pi pi-sitemap" title="No departments yet" description="Add a department, then the functions inside it." />
    } @else {
      <label class="org__search">
        <i class="pi pi-search" aria-hidden="true"></i>
        <input type="search" placeholder="Find a department or function" aria-label="Find a department or function" [ngModel]="query()" (ngModelChange)="query.set($event)" />
      </label>

      @for (group of groups(); track group.unit?.id ?? 'none') {
        <section class="org__unit" [attr.aria-label]="group.unit?.name ?? 'Not in a business unit'">
          <header class="org__unit-head">
            <span class="org__badge org__badge--unit">BU</span>
            <h2>{{ group.unit?.name ?? 'Not in a business unit' }}</h2>
            <span class="muted">{{ group.departments.length }} {{ group.departments.length === 1 ? 'department' : 'departments' }} · {{ group.headcount }} people</span>
          </header>

          <div class="org__depts">
            @for (department of group.departments; track department.id) {
              <article class="panel org__dept">
                <div class="org__dept-head">
                  <div>
                    <span class="org__badge org__badge--dept">Department</span>
                    <h3>{{ department.name }} <span class="muted org__code">{{ department.code }}</span></h3>
                  </div>
                  <div class="org__dept-meta">
                    <span class="org__count" [attr.aria-label]="total(department) + ' people'">{{ total(department) }}</span>
                    @if (canManage) {
                      <button type="button" class="btn btn--sm" (click)="openFunction(department)"><i class="pi pi-plus" aria-hidden="true"></i> Add function</button>
                    }
                  </div>
                </div>

                @if (activeFunctions(department).length) {
                  <div class="table-wrap">
                    <table class="table table--compact">
                      <thead>
                        <tr>
                          <th scope="col" class="num org__sr" data-nosort><abbr title="Row number">#</abbr></th>
                          <th scope="col">Function</th>
                          <th scope="col">Code</th>
                          <th scope="col" class="num">People</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (fn of activeFunctions(department); track fn.id; let i = $index) {
                          <tr>
                            <td class="num muted">{{ i + 1 }}</td>
                            <td><strong>{{ fn.name }}</strong></td>
                            <td class="muted">{{ fn.code }}</td>
                            <td class="num">{{ fn.headcount }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                  @if (department.headcount) {
                    <p class="muted org__note">{{ department.headcount }} placed on {{ department.name }} itself, not on a function.</p>
                  }
                } @else {
                  <p class="org__empty">No functions yet. {{ department.headcount ? department.headcount + ' people are placed on the department itself.' : '' }}</p>
                }

                @if (canManage && missingSuggestions(department).length) {
                  <div class="org__chips">
                    <span class="muted">Suggested:</span>
                    @for (name of missingSuggestions(department); track name) {
                      <button type="button" class="org__chip" (click)="openFunction(department, name)">+ {{ name }}</button>
                    }
                  </div>
                }
              </article>
            }
          </div>
        </section>
      } @empty {
        <gggs-empty-state icon="pi pi-search" title="Nothing matches" [description]="'No department or function is called ' + query() + '.'" />
      }
    }

    <!-- Add a department, optionally with functions from the suggestions -->
    <gggs-form-dialog
      [(visible)]="departmentOpen"
      title="Add department"
      description="A department sits in a business unit. Its functions are the teams people are placed in."
      saveLabel="Add department"
      [busy]="saving()"
      [error]="error()"
      (save)="saveDepartment()"
    >
      @if (departmentOpen()) {
        <div class="form-grid">
          <label class="field form-grid__full">
            <span class="field__label">Start from</span>
            <select [ngModel]="draft().template" (ngModelChange)="useTemplate($event)" aria-label="Start from">
              <option value="">Blank department</option>
              @for (name of templates(); track name) {
                <option [value]="name">{{ name }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">Name *</span>
            <input type="text" maxlength="200" [ngModel]="draft().name" (ngModelChange)="patchDraft({ name: $event })" />
          </label>
          <label class="field">
            <span class="field__label">Code *</span>
            <input type="text" maxlength="32" [ngModel]="draft().code" (ngModelChange)="patchDraft({ code: $event })" />
          </label>
          <label class="field">
            <span class="field__label">Business unit</span>
            <gggs-org-select kind="business-units" placeholder="Choose" ariaLabel="Business unit" [ngModel]="draft().businessUnitId" (ngModelChange)="patchDraft({ businessUnitId: $event })" />
          </label>
          <label class="field">
            <span class="field__label">Location</span>
            <gggs-org-select kind="locations" placeholder="Choose" ariaLabel="Location" [ngModel]="draft().locationId" (ngModelChange)="patchDraft({ locationId: $event })" />
          </label>
          <label class="field form-grid__full">
            <span class="field__label">Department head</span>
            <gggs-employee-select ariaLabel="Department head" [ngModel]="draft().headEmployeeId" (ngModelChange)="patchDraft({ headEmployeeId: $event })" />
          </label>
          @if (draftSuggestions().length) {
            <fieldset class="form-grid__full org__checks">
              <legend class="field__label">Functions to create</legend>
              @for (name of draftSuggestions(); track name) {
                <label class="org__check">
                  <input type="checkbox" [checked]="draft().functions.includes(name)" (change)="toggleDraftFunction(name, $any($event.target).checked)" />
                  {{ name }}
                </label>
              }
            </fieldset>
          }
        </div>
      }
    </gggs-form-dialog>

    <!-- Add a function to one department -->
    <gggs-form-dialog
      [(visible)]="functionOpen"
      [title]="'Add function to ' + (target()?.name ?? '')"
      description="People are placed on a function. It takes the department's business unit, location and cost center."
      saveLabel="Add function"
      [busy]="saving()"
      [error]="error()"
      (save)="saveFunction()"
    >
      @if (functionOpen()) {
        <div class="form-grid">
          @if (target(); as department) {
            @if (missingSuggestions(department).length) {
              <div class="form-grid__full org__chips">
                <span class="muted">Suggested:</span>
                @for (name of missingSuggestions(department); track name) {
                  <button type="button" class="org__chip" [class.org__chip--on]="fnDraft().name === name" (click)="nameFunction(name)">{{ name }}</button>
                }
              </div>
            }
          }
          <label class="field">
            <span class="field__label">Function name *</span>
            <input type="text" maxlength="200" [ngModel]="fnDraft().name" (ngModelChange)="nameFunction($event)" />
          </label>
          <label class="field">
            <span class="field__label">Code *</span>
            <input type="text" maxlength="32" [ngModel]="fnDraft().code" (ngModelChange)="fnDraft.set({ ...fnDraft(), code: $event })" />
          </label>
          <label class="field form-grid__full">
            <span class="field__label">Function head</span>
            <gggs-employee-select ariaLabel="Function head" [ngModel]="fnDraft().headEmployeeId" (ngModelChange)="fnDraft.set({ ...fnDraft(), headEmployeeId: $event })" />
          </label>
        </div>
      }
    </gggs-form-dialog>
  `,
  styles: `
    .org__kpis {
      margin-block-end: var(--gggs-space-4);
    }

    .org__search {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
      max-width: 24rem;
      margin-block-end: var(--gggs-space-4);
      padding-inline: var(--gggs-space-2);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-raised);
      color: var(--gggs-color-text-muted);
    }

    .org__search input {
      width: 100%;
      padding-block: var(--gggs-space-2);
      border: 0;
      outline: 0;
      background: none;
      color: var(--gggs-color-text);
      font: inherit;
    }

    .org__search:focus-within {
      border-color: var(--gggs-color-primary);
    }

    .org__unit {
      margin-block-end: var(--gggs-space-5, 1.5rem);
    }

    .org__unit-head {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
      align-items: center;
      margin-block-end: var(--gggs-space-3);
    }

    .org__unit-head h2 {
      margin: 0;
      font-size: var(--gggs-font-size-lg, 1.125rem);
    }

    .org__depts {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 26rem), 1fr));
      gap: var(--gggs-space-3);
      align-items: start;
    }

    .org__dept {
      display: grid;
      gap: var(--gggs-space-3);
    }

    .org__dept-head {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
      align-items: flex-start;
      justify-content: space-between;
    }

    .org__dept-head h3 {
      margin: var(--gggs-space-1) 0 0;
      font-size: var(--gggs-font-size-md);
    }

    .org__dept-meta {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
    }

    .org__code {
      font-size: var(--gggs-font-size-xs);
      font-weight: 400;
    }

    .org__badge {
      display: inline-block;
      padding: 1px 8px;
      border-radius: var(--gggs-radius-sm, 6px);
      font-size: var(--gggs-font-size-xs);
      font-weight: var(--gggs-font-weight-semibold);
      letter-spacing: 0.04em;
      text-transform: uppercase;
    }

    .org__badge--unit {
      background: var(--gggs-color-info-subtle);
      color: var(--gggs-color-info);
    }

    .org__badge--dept {
      background: var(--gggs-color-primary-subtle);
      color: var(--gggs-color-primary);
    }

    .org__count {
      min-width: 2.5rem;
      padding: 0 var(--gggs-space-2);
      border-radius: 999px;
      background: var(--gggs-color-primary-subtle);
      color: var(--gggs-color-primary);
      font-weight: var(--gggs-font-weight-semibold);
      text-align: center;
      font-variant-numeric: tabular-nums;
    }

    .org__sr {
      width: 3rem;
    }

    .org__sr abbr {
      text-decoration: none;
    }

    .org__empty {
      margin: 0;
      padding: var(--gggs-space-3);
      border: 1px dashed var(--gggs-color-border-strong);
      border-radius: var(--gggs-radius-md);
      color: var(--gggs-color-text-muted);
      text-align: center;
    }

    .org__note {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .org__chips {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-1);
      align-items: center;
      font-size: var(--gggs-font-size-sm);
    }

    .org__chip {
      padding: 3px 10px;
      border: 1px dashed var(--gggs-color-border-strong);
      border-radius: 999px;
      background: transparent;
      color: var(--gggs-color-text);
      font: inherit;
      cursor: pointer;
    }

    .org__chip:hover,
    .org__chip--on {
      border-color: var(--gggs-color-primary);
      background: var(--gggs-color-primary-subtle);
      color: var(--gggs-color-primary);
    }

    .org__checks {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(12rem, 1fr));
      gap: var(--gggs-space-2);
      margin: 0;
      padding: 0;
      border: 0;
    }

    .org__checks legend {
      margin-block-end: var(--gggs-space-2);
    }

    .org__check {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
      padding: var(--gggs-space-2);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
    }

    .org__check:has(input:checked) {
      border-color: var(--gggs-color-primary);
      background: var(--gggs-color-primary-subtle);
    }
  `,
})
export class DepartmentTreePage {
  private readonly api = inject(OrgStructureApi);

  protected readonly canManage = inject(PermissionStore).hasAny(['org.department.manage']);
  /** Importing creates roles and designations as well as departments, so it needs structure rights. */
  protected readonly canImport = inject(PermissionStore).hasAny(['org.structure.manage']);

  protected readonly loading = signal(true);
  protected readonly tree = signal<readonly DepartmentNode[]>([]);
  protected readonly units = signal<readonly OrgUnit[]>([]);
  protected readonly query = signal('');

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly departmentOpen = signal(false);
  protected readonly draft = signal<DepartmentDraft>(emptyDepartment());
  protected readonly functionOpen = signal(false);
  protected readonly target = signal<DepartmentNode | null>(null);
  protected readonly fnDraft = signal<FunctionDraft>({ name: '', code: '', headEmployeeId: null });

  /** Open departments; closed ones stay in the data for history but not on this page. */
  protected readonly departments = computed(() => this.tree().filter((node) => node.isActive));
  protected readonly functionCount = computed(() => this.departments().reduce((sum, node) => sum + this.activeFunctions(node).length, 0));
  protected readonly peopleCount = computed(() => this.departments().reduce((sum, node) => sum + this.total(node), 0));
  protected readonly unitCount = computed(() => this.units().length);

  protected readonly groups = computed<readonly UnitGroup[]>(() => {
    const term = this.query().trim().toLowerCase();
    const matches = (node: DepartmentNode) =>
      !term || node.name.toLowerCase().includes(term) || node.code.toLowerCase().includes(term) || node.children.some((fn) => fn.name.toLowerCase().includes(term));

    const shown = this.departments().filter(matches);
    const group = (unit: OrgUnit | null): UnitGroup => {
      const departments = shown.filter((node) => (unit ? node.businessUnitId === unit.id : !this.units().some((u) => u.id === node.businessUnitId)));
      return { unit, departments, headcount: departments.reduce((sum, node) => sum + this.total(node), 0) };
    };

    return [...this.units().map(group), group(null)].filter((entry) => entry.departments.length > 0);
  });

  /** Department names from the catalog that are not taken yet - the "Start from" choices. */
  protected readonly templates = computed(() => {
    const taken = new Set(this.departments().map((node) => node.name.toLowerCase()));
    return Object.keys(FUNCTION_CATALOG).filter((name) => !taken.has(name.toLowerCase()));
  });

  protected readonly draftSuggestions = computed(() => suggestedFunctions(this.draft().name));

  constructor() {
    void this.load();
  }

  protected activeFunctions(department: DepartmentNode): readonly DepartmentNode[] {
    return department.children.filter((fn) => fn.isActive);
  }

  protected total(department: DepartmentNode): number {
    return department.headcount + this.activeFunctions(department).reduce((sum, fn) => sum + fn.headcount, 0);
  }

  protected missingSuggestions(department: DepartmentNode): readonly string[] {
    const taken = new Set(department.children.map((fn) => fn.name.toLowerCase()));
    return suggestedFunctions(department.name).filter((name) => !taken.has(name.toLowerCase()));
  }

  // ----- Add department ---------------------------------------------------------------------

  protected openDepartment(): void {
    this.draft.set({ ...emptyDepartment(), businessUnitId: this.units()[0]?.id ?? null });
    this.error.set(null);
    this.departmentOpen.set(true);
  }

  protected useTemplate(name: string): void {
    const suggestions = suggestedFunctions(name);
    this.patchDraft({ template: name, name, code: name ? codePart(name) : '', functions: suggestions.slice(0, 2) });
  }

  protected patchDraft(patch: Partial<DepartmentDraft>): void {
    this.draft.set({ ...this.draft(), ...patch });
  }

  protected toggleDraftFunction(name: string, on: boolean): void {
    const functions = this.draft().functions.filter((entry) => entry !== name);
    this.patchDraft({ functions: on ? [...functions, name] : functions });
  }

  protected async saveDepartment(): Promise<void> {
    const draft = this.draft();
    const code = draft.code.trim().toUpperCase();
    if (!draft.name.trim() || !code) {
      this.error.set('Give the department a name and a code.');
      return;
    }

    await this.run(async () => {
      const id = await this.api.create('departments', {
        code,
        name: draft.name.trim(),
        parentDepartmentId: null,
        businessUnitId: draft.businessUnitId,
        headEmployeeId: draft.headEmployeeId,
        locationId: draft.locationId,
        costCenterId: null,
        effectiveFrom: null,
      });
      for (const name of draft.functions) {
        await this.api.create('departments', {
          code: `${code}-${codePart(name)}`.slice(0, 32),
          name,
          parentDepartmentId: id,
          businessUnitId: draft.businessUnitId,
          headEmployeeId: null,
          locationId: draft.locationId,
          costCenterId: null,
          effectiveFrom: null,
        });
      }
      this.departmentOpen.set(false);
    });
  }

  // ----- Add function -----------------------------------------------------------------------

  protected openFunction(department: DepartmentNode, name = ''): void {
    this.target.set(department);
    this.fnDraft.set({ name: '', code: '', headEmployeeId: null });
    this.nameFunction(name);
    this.error.set(null);
    this.functionOpen.set(true);
  }

  /** Naming a function fills its code from the department's, until someone edits the code. */
  protected nameFunction(name: string): void {
    const department = this.target();
    const draft = this.fnDraft();
    const autoCode = (value: string) => (department && value.trim() ? `${department.code}-${codePart(value)}`.slice(0, 32) : '');
    const codeWasAuto = !draft.code || draft.code === autoCode(draft.name);
    this.fnDraft.set({ ...draft, name, code: codeWasAuto ? autoCode(name) : draft.code });
  }

  protected async saveFunction(): Promise<void> {
    const department = this.target();
    const draft = this.fnDraft();
    if (!department || !draft.name.trim() || !draft.code.trim()) {
      this.error.set('Give the function a name and a code.');
      return;
    }

    await this.run(async () => {
      await this.api.create('departments', {
        code: draft.code.trim().toUpperCase(),
        name: draft.name.trim(),
        parentDepartmentId: department.id,
        businessUnitId: department.businessUnitId ?? null,
        headEmployeeId: draft.headEmployeeId,
        locationId: department.locationId ?? null,
        costCenterId: department.costCenterId ?? null,
        effectiveFrom: null,
      });
      this.functionOpen.set(false);
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    try {
      await action();
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That did not save. Try again.');
      // A department may have saved before one of its functions failed; show what is there now.
      await this.load();
    } finally {
      this.saving.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const [tree, units] = await Promise.all([this.api.departmentTree(), this.api.active('business-units')]);
      this.tree.set(tree);
      this.units.set(units);
    } finally {
      this.loading.set(false);
    }
  }
}
