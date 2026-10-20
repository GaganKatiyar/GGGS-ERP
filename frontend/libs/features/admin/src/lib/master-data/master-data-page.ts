import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { MasterDataApi } from '@gggs/shared/master-data';
import type { LookupList, LookupValue } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

/**
 * Master data (spec §113): the lists behind every dropdown. Lists are fixed by PeopleOS; values in
 * an extensible list can be added, relabelled, reordered and retired by the tenant. Values PeopleOS
 * ships keep their code - rules elsewhere rely on it - but may still be relabelled or retired.
 */
@Component({
  selector: 'gggs-master-data-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, FormDialog, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Master data" subtitle="The values behind every dropdown in PeopleOS." />

    <div class="master-data">
      <nav class="master-data__lists" aria-label="Lists">
        @for (list of lists(); track list.code) {
          <button type="button" class="master-data__list" [class.master-data__list--active]="list.code === selected()?.code" [attr.aria-current]="list.code === selected()?.code" (click)="select(list)">
            {{ list.name }}
            @if (!list.tenantExtensible) {
              <i class="pi pi-lock muted" aria-label="Fixed list"></i>
            }
          </button>
        }
      </nav>

      <section class="panel stack master-data__values">
        @if (selected(); as list) {
          <div class="toolbar">
            <h2 class="master-data__heading">{{ list.name }}</h2>
            @if (list.tenantExtensible) {
              <button type="button" class="btn btn--primary btn--sm" *gggsHasPermission="'master-data.lookup.manage'" (click)="openCreate()">Add value</button>
            }
          </div>
          @if (!list.tenantExtensible) {
            <p class="muted">PeopleOS maintains this list. Values can be relabelled or retired but not added.</p>
          }
          @if (list.parentList) {
            <label class="field master-data__filter">
              <span class="field__label">Under</span>
              <input type="search" placeholder="Filter by parent code" [value]="parentFilter()" (input)="parentFilter.set($any($event.target).value)" />
            </label>
          }

          @if (loading()) {
            <gggs-skeleton [lines]="6" />
          } @else if (visibleValues().length === 0) {
            <gggs-empty-state title="No values" />
          } @else {
            <table class="table master-data__table">
              <thead>
                <tr>
                  <th scope="col">Code</th>
                  <th scope="col">Label</th>
                  @if (list.parentList) {
                    <th scope="col">Parent</th>
                  }
                  <th scope="col">Order</th>
                  <th scope="col">Status</th>
                  <th scope="col"><span class="visually-hidden">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (value of visibleValues(); track value.id) {
                  <tr>
                    <td><code>{{ value.code }}</code></td>
                    <td>
                      {{ value.label }}
                      @if (value.isSystem) {
                        <span class="muted master-data__system">built-in</span>
                      }
                    </td>
                    @if (list.parentList) {
                      <td>{{ value.parentCode ?? '—' }}</td>
                    }
                    <td>{{ value.sortOrder }}</td>
                    <td><gggs-status-badge [status]="value.isActive ? 'Active' : 'Inactive'" /></td>
                    <td class="master-data__actions">
                      <ng-container *gggsHasPermission="'master-data.lookup.manage'">
                        <button type="button" class="btn btn--link btn--sm" (click)="openEdit(value)">Edit</button>
                        <button type="button" class="btn btn--link btn--sm" (click)="toggle(value)">{{ value.isActive ? 'Retire' : 'Restore' }}</button>
                      </ng-container>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }
          @if (listError()) {
            <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ listError() }}</span></p>
          }
        } @else {
          <gggs-skeleton [lines]="6" />
        }
      </section>
    </div>

    <gggs-form-dialog [(visible)]="dialogOpen" [title]="editing() ? 'Edit value' : 'Add value'" [busy]="busy()" [error]="error()" (save)="save()">
      <div class="form-grid" [formGroup]="form">
        <label class="field">
          <span class="field__label">Code *</span>
          <input type="text" formControlName="code" maxlength="64" autocomplete="off" />
          <span class="field__hint">Upper case, no spaces; it cannot change later.</span>
        </label>
        <label class="field">
          <span class="field__label">Label *</span>
          <input type="text" formControlName="label" maxlength="200" />
        </label>
        @if (selected()?.parentList && !editing()) {
          <label class="field">
            <span class="field__label">Parent code</span>
            <input type="text" formControlName="parentCode" maxlength="64" />
          </label>
        }
        <label class="field">
          <span class="field__label">Order</span>
          <input type="number" formControlName="sortOrder" min="0" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .master-data {
      display: grid;
      grid-template-columns: minmax(12rem, 16rem) 1fr;
      gap: var(--gggs-space-4);
      align-items: start;
    }

    @media (max-width: 48rem) {
      .master-data {
        grid-template-columns: 1fr;
      }
    }

    .master-data__lists {
      display: flex;
      flex-direction: column;
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-raised);
      overflow: hidden;
    }

    .master-data__list {
      display: flex;
      justify-content: space-between;
      padding: var(--gggs-space-2) var(--gggs-space-3);
      border: 0;
      border-block-end: 1px solid var(--gggs-color-border);
      background: transparent;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .master-data__list:last-child {
      border-block-end: 0;
    }

    .master-data__list--active {
      background: var(--gggs-color-primary-subtle);
      color: var(--gggs-color-primary);
      font-weight: var(--gggs-font-weight-semibold);
    }

    .master-data__heading {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .master-data__filter {
      max-width: 16rem;
    }



    .master-data__system {
      margin-inline-start: var(--gggs-space-2);
      font-size: var(--gggs-font-size-xs);
    }

    .master-data__actions {
      white-space: nowrap;
      text-align: end;
    }

    .visually-hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class MasterDataPage {
  private readonly api = inject(MasterDataApi);

  protected readonly lists = signal<readonly LookupList[]>([]);
  protected readonly selected = signal<LookupList | null>(null);
  protected readonly values = signal<readonly LookupValue[]>([]);
  protected readonly parentFilter = signal('');
  protected readonly loading = signal(false);
  protected readonly listError = signal<string | null>(null);

  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<LookupValue | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^[A-Z0-9_]+$/)] }),
    label: new FormControl('', { nonNullable: true, validators: Validators.required }),
    parentCode: new FormControl<string | null>(null),
    sortOrder: new FormControl(0, { nonNullable: true }),
  });

  protected readonly visibleValues = computed(() => {
    const filter = this.parentFilter().trim().toUpperCase();
    return filter ? this.values().filter((value) => value.parentCode?.toUpperCase() === filter) : this.values();
  });

  constructor() {
    void this.api.lists().then((lists) => {
      this.lists.set(lists);
      if (lists.length) {
        this.select(lists[0]);
      }
    });
  }

  protected select(list: LookupList): void {
    this.selected.set(list);
    this.parentFilter.set('');
    void this.loadValues();
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.form.reset({ code: '', label: '', parentCode: null, sortOrder: (this.values()[this.values().length - 1]?.sortOrder ?? 0) + 10 });
    this.form.controls.code.enable();
    this.error.set(null);
    this.dialogOpen.set(true);
  }

  protected openEdit(value: LookupValue): void {
    this.editing.set(value);
    this.form.reset({ code: value.code, label: value.label, parentCode: value.parentCode ?? null, sortOrder: value.sortOrder });
    this.form.controls.code.disable();
    this.error.set(null);
    this.dialogOpen.set(true);
  }

  protected async save(): Promise<void> {
    const list = this.selected();
    if (!list || this.form.invalid) {
      this.error.set('Fill in a code (A-Z, 0-9, _) and a label.');
      return;
    }

    const value = this.form.getRawValue();
    const editing = this.editing();
    this.busy.set(true);
    this.error.set(null);
    try {
      if (editing) {
        await this.api.update(list.code, editing.id, { label: value.label.trim(), sortOrder: value.sortOrder });
      } else {
        await this.api.create(list.code, { code: value.code, label: value.label.trim(), parentCode: value.parentCode?.trim() || null });
      }
      this.dialogOpen.set(false);
      await this.loadValues();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The value could not be saved. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async toggle(value: LookupValue): Promise<void> {
    const list = this.selected();
    if (!list) {
      return;
    }
    this.listError.set(null);
    try {
      await this.api.setActive(list.code, value.id, !value.isActive);
      await this.loadValues();
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'The value could not be changed.');
    }
  }

  private async loadValues(): Promise<void> {
    const list = this.selected();
    if (!list) {
      return;
    }
    this.loading.set(true);
    this.listError.set(null);
    try {
      this.values.set(await this.api.allValues(list.code));
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'The values could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
