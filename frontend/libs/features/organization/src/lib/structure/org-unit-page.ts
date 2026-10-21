import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective, PermissionStore } from '@gggs/core/permissions';
import { DataTable, DataTableCell, type DataTableQuery } from '@gggs/shared/data-table';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { LookupSelect } from '@gggs/shared/master-data';
import type { OrgUnit, PagedResult } from '@gggs/shared/models';
import { OrgSelect, OrgStructureApi } from '@gggs/shared/org-reference';
import { FilterBar, FormDialog, PageHeader, StatusBadge } from '@gggs/shared/ui';
import { ORG_KINDS, type OrgField, type OrgKindConfig } from './org-kinds';

/**
 * One page for every kind of organization record (spec §10), driven by {@link ORG_KINDS}.
 *
 * The eight copy-paste pages it replaces differed only in their columns and fields, so those are
 * configuration now and the behaviour - search, sort, paging, create, edit, deactivate, close - is
 * written and tested once.
 */
@Component({
  selector: 'gggs-org-unit-page',
  imports: [
    ReactiveFormsModule,
    HasPermissionDirective,
    PageHeader,
    DataTable,
    DataTableCell,
    FilterBar,
    FormDialog,
    StatusBadge,
    LookupSelect,
    OrgSelect,
    EmployeeSelect,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let cfg = config();
    <gggs-page-header [title]="cfg.title" [subtitle]="cfg.subtitle">
      <button type="button" class="btn btn--primary" *gggsHasPermission="cfg.managePermission" (click)="openCreate()">
        <i class="pi pi-plus" aria-hidden="true"></i> New {{ cfg.singular }}
      </button>
    </gggs-page-header>

    <gggs-filter-bar
      [placeholder]="'Search ' + cfg.title.toLowerCase()"
      [active]="!!search() || activeFilter() !== 'all'"
      [(search)]="search"
      (searchChange)="reload()"
      (cleared)="activeFilter.set('all'); reload()"
    >
      <select aria-label="Status" [value]="activeFilter()" (change)="setActiveFilter($event)">
        <option value="all">All</option>
        <option value="active">Active</option>
        <option value="inactive">Inactive</option>
      </select>
    </gggs-filter-bar>

    <gggs-data-table
      [result]="result()"
      [columns]="cfg.columns"
      [loading]="loading()"
      [exportName]="cfg.kind"
      [emptyTitle]="'No ' + cfg.title.toLowerCase() + ' yet'"
      [emptyDescription]="canManage() ? 'Create the first one with the button above.' : undefined"
      (queryChange)="onQuery($event)"
      (rowClick)="openEdit($event)"
    >
      <ng-template gggsCell="isActive" let-row>
        <gggs-status-badge [status]="row.isActive ? 'Active' : 'Inactive'" />
      </ng-template>
    </gggs-data-table>

    <gggs-form-dialog
      [(visible)]="dialogOpen"
      [title]="editing() ? 'Edit ' + cfg.singular : 'New ' + cfg.singular"
      [busy]="saving()"
      [error]="error()"
      [saveDisabled]="!canManage()"
      (save)="save()"
    >
      <!-- Rendered only while open: the form gains its controls when the dialog opens, and binding
           fields to a form without them throws and stops the page rendering. -->
      @if (dialogOpen()) {
      <div class="form-grid" [formGroup]="form()">
        @for (field of visibleFields(); track field.key) {
          <!-- eslint-disable-next-line @angular-eslint/template/label-has-associated-control -- every @switch branch below renders exactly one control -->
          <label class="field" [class.form-grid__full]="field.fullWidth || field.type.kind === 'textarea'">
            <span class="field__label">{{ field.label }}{{ field.required ? ' *' : '' }}</span>
            @switch (field.type.kind) {
              @case ('textarea') {
                <textarea rows="3" [formControlName]="field.key"></textarea>
              }
              @case ('number') {
                <input type="number" [formControlName]="field.key" [min]="$any(field.type).min ?? null" [step]="$any(field.type).step ?? 1" />
              }
              @case ('date') {
                <input type="date" [formControlName]="field.key" />
              }
              @case ('checkbox') {
                <input type="checkbox" [formControlName]="field.key" />
              }
              @case ('lookup') {
                <gggs-lookup-select
                  [formControlName]="field.key"
                  [list]="$any(field.type).list"
                  [parent]="$any(field.type).parentField ? $any(formValue()[$any(field.type).parentField] ?? null) : undefined"
                  [ariaLabel]="field.label"
                />
              }
              @case ('org') {
                <gggs-org-select
                  [formControlName]="field.key"
                  [kind]="$any(field.type).orgKind"
                  [excludeId]="$any(field.type).orgKind === cfg.kind ? editing()?.id : null"
                  [ariaLabel]="field.label"
                />
              }
              @case ('employee') {
                <gggs-employee-select [formControlName]="field.key" [ariaLabel]="field.label" />
              }
              @default {
                <input type="text" [formControlName]="field.key" [attr.maxlength]="$any(field.type).maxLength ?? null" />
              }
            }
            @if (field.hint) {
              <span class="field__hint">{{ field.hint }}</span>
            }
          </label>
        }
      </div>
      }

      @if (editing(); as unit) {
        <div class="toolbar" *gggsHasPermission="cfg.managePermission">
          @if (unit.isActive) {
            @if (cfg.closable) {
              <label class="field org-unit-page__close">
                <span class="field__label">Close on</span>
                <input type="date" [value]="closeDate()" (change)="closeDate.set($any($event.target).value)" />
              </label>
              <button type="button" class="btn btn--danger" [disabled]="saving() || !closeDate()" (click)="close(unit)">Close</button>
            } @else {
              <button type="button" class="btn btn--danger" [disabled]="saving()" (click)="setActive(unit, false)">Deactivate</button>
            }
          } @else {
            <button type="button" class="btn" [disabled]="saving()" (click)="setActive(unit, true)">Reactivate</button>
          }
        </div>
      }
    </gggs-form-dialog>
  `,
  styles: `
    .org-unit-page__close {
      flex-direction: row;
      align-items: center;
    }
  `,
})
export class OrgUnitPage {
  private readonly api = inject(OrgStructureApi);
  private readonly permissions = inject(PermissionStore);

  /** The {@link ORG_KINDS} key, bound from the route's data. */
  readonly kindKey = input.required<string>();

  protected readonly config = computed<OrgKindConfig>(() => ORG_KINDS[this.kindKey()]);
  protected readonly canManage = computed(() => this.permissions.hasAny([this.config().managePermission]));

  protected readonly result = signal<PagedResult<OrgUnit> | null>(null);
  protected readonly loading = signal(false);
  protected readonly search = signal('');
  protected readonly activeFilter = signal<'all' | 'active' | 'inactive'>('all');
  private query: DataTableQuery = { page: 1, pageSize: 25 };

  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<OrgUnit | null>(null);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly closeDate = signal('');
  protected readonly form = signal(new FormGroup<Record<string, FormControl<unknown>>>({}));
  protected readonly formValue = signal<Record<string, unknown>>({});

  protected readonly visibleFields = computed(() =>
    this.config().fields.filter((field) => !(field.createOnly && this.editing())),
  );

  protected onQuery(query: DataTableQuery): void {
    this.query = query;
    void this.load();
  }

  protected reload(): void {
    this.query = { ...this.query, page: 1 };
    void this.load();
  }

  protected setActiveFilter(event: Event): void {
    this.activeFilter.set((event.target as HTMLSelectElement).value as 'all' | 'active' | 'inactive');
    this.reload();
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.buildForm(null);
    this.dialogOpen.set(true);
  }

  protected openEdit(unit: OrgUnit): void {
    this.editing.set(unit);
    this.closeDate.set('');
    this.buildForm(unit);
    this.dialogOpen.set(true);
  }

  protected async save(): Promise<void> {
    const form = this.form();
    if (form.invalid) {
      form.markAllAsTouched();
      this.error.set('Fill in the required fields.');
      return;
    }

    await this.run(async () => {
      const body = OrgUnitPage.toBody(this.config().fields, form.getRawValue());
      const editing = this.editing();

      if (editing) {
        await this.api.update(this.config().kind, editing.id, { ...body, code: editing.code });
      } else {
        await this.api.create(this.config().kind, body);
      }
    });
  }

  protected async setActive(unit: OrgUnit, active: boolean): Promise<void> {
    await this.run(() => this.api.setActive(this.config().kind, unit.id, active));
  }

  protected async close(unit: OrgUnit): Promise<void> {
    await this.run(() => this.api.close(this.config().kind as 'departments' | 'positions', unit.id, this.closeDate()));
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.saving.set(true);
    this.error.set(null);

    try {
      await action();
      this.dialogOpen.set(false);
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Something went wrong. Try again.');
    } finally {
      this.saving.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      const filter = this.activeFilter();
      this.result.set(
        await this.api.list(this.config().kind, {
          ...this.query,
          search: this.search() || undefined,
          active: filter === 'all' ? undefined : filter === 'active',
        }),
      );
    } finally {
      this.loading.set(false);
    }
  }

  private buildForm(unit: OrgUnit | null): void {
    const controls: Record<string, FormControl<unknown>> = {};

    for (const field of this.config().fields) {
      const value = unit ? OrgUnitPage.read(unit, field) : field.type.kind === 'checkbox' ? false : null;
      controls[field.key] = new FormControl<unknown>(value, field.required ? Validators.required : []);
    }

    const form = new FormGroup(controls);
    this.formValue.set(form.getRawValue());
    form.valueChanges.subscribe(() => this.formValue.set(form.getRawValue()));
    this.form.set(form);
    this.error.set(null);
  }

  private static read(unit: OrgUnit, field: OrgField): unknown {
    const value = field.key.split('.').reduce<unknown>((current, part) => (current as Record<string, unknown> | undefined)?.[part], unit);
    return field.type.kind === 'tags' && Array.isArray(value) ? value.join(', ') : (value ?? null);
  }

  /** Flat form values back into the API's shape: dotted keys nest, tags split, blanks become null. */
  static toBody(fields: readonly OrgField[], values: Record<string, unknown>): Record<string, unknown> {
    const body: Record<string, unknown> = {};

    for (const field of fields) {
      let value = values[field.key];

      if (field.type.kind === 'tags') {
        value = String(value ?? '').split(',').map((tag) => tag.trim()).filter(Boolean);
      } else if (field.type.kind === 'number') {
        value = value === '' || value === null || value === undefined ? null : Number(value);
      } else if (value === '') {
        value = null;
      }

      const path = field.key.split('.');
      let target = body;
      for (const part of path.slice(0, -1)) {
        target = (target[part] ??= {}) as Record<string, unknown>;
      }
      target[path[path.length - 1]] = value;
    }

    return body;
  }
}
