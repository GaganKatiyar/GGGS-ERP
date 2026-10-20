import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { SkillsApi } from '@gggs/shared/delivery';
import type { MatrixRow, SkillItem, SkilledPerson } from '@gggs/shared/models';
import { SKILL_LEVEL_LABELS } from '@gggs/shared/models';
import { Drawer, EmptyState, FormDialog, PageHeader, StatusBadge, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/**
 * Skills (spec §60): the company's skill catalogue, and the matrix of how many people hold each
 * skill at each level - where the organisation is deep, thin, or short of its own targets.
 */
@Component({
  selector: 'gggs-skills-page',
  imports: [DecimalPipe, RouterLink, PageHeader, Tabs, StatusBadge, EmptyState, Drawer, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Skills" subtitle="The skill catalogue and where the organisation's depth lies.">
      @if (canManage() && tab() === 'catalog') {
        <button type="button" class="btn btn--primary" (click)="edit(null)"><i class="pi pi-plus" aria-hidden="true"></i> New skill</button>
      }
    </gggs-page-header>

    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />

    <section class="panel toolbar skills__filters">
      <label class="field">
        <span class="field__label">Category</span>
        <select [value]="category()" (change)="category.set($any($event.target).value)">
          <option value="">All</option>
          @for (option of categories(); track option) {
            <option [value]="option">{{ option }}</option>
          }
        </select>
      </label>
      @if (tab() === 'catalog') {
        <label class="field">
          <span class="field__label">Search</span>
          <input type="search" [value]="search()" (input)="search.set($any($event.target).value)" />
        </label>
      }
    </section>

    @if (tab() === 'catalog') {
      <section class="panel stack">
        @for (skill of filteredCatalog(); track skill.id) {
          <div class="skills__row">
            <button type="button" class="btn btn--link skills__name" (click)="showPeople(skill.id, skill.name)"><strong>{{ skill.name }}</strong></button>
            <span class="muted">{{ skill.code }} · {{ skill.category }}</span>
            <span>{{ skill.peopleCount }} {{ skill.peopleCount === 1 ? 'person' : 'people' }}</span>
            <gggs-status-badge [status]="skill.isActive ? 'Active' : 'Inactive'" />
            <span>
              @if (canManage()) {
                <button type="button" class="btn btn--link btn--sm" (click)="edit(skill)">Edit</button>
                <button type="button" class="btn btn--link btn--sm" (click)="toggle(skill)">{{ skill.isActive ? 'Retire' : 'Restore' }}</button>
              }
            </span>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-bolt" title="No skills match" />
        }
      </section>
    } @else {
      <section class="panel skills__scroll">
        <table class="table skills__matrix">
          <thead>
            <tr>
              <th scope="col">Skill</th>
              @for (level of levelColumns; track level) {
                <th scope="col">{{ levels[level] }}</th>
              }
              <th scope="col">People</th>
              <th scope="col">Average</th>
              <th scope="col">Below target</th>
            </tr>
          </thead>
          <tbody>
            @for (row of filteredMatrix(); track row.skillId) {
              <tr>
                <th scope="row">
                  <button type="button" class="btn btn--link" (click)="showPeople(row.skillId, row.skillName)">{{ row.skillName }}</button>
                  <span class="muted skills__category">{{ row.category }}</span>
                </th>
                @for (level of levelColumns; track level) {
                  <td class="skills__cell" [style.background]="heat(row, level)">{{ count(row, level) || '' }}</td>
                }
                <td>{{ row.people }}</td>
                <td>{{ row.averageLevel | number: '1.1-1' }}</td>
                <td [class.skills__gap]="row.withGap > 0">{{ row.withGap || '' }}</td>
              </tr>
            } @empty {
              <tr><td [attr.colspan]="levelColumns.length + 4"><gggs-empty-state icon="pi pi-th-large" title="No skills recorded yet" /></td></tr>
            }
          </tbody>
        </table>
      </section>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-drawer [(visible)]="peopleOpen" [title]="peopleTitle()">
      <div class="stack">
        @for (person of people(); track person.employeeId) {
          <div class="skills__person">
            <a [routerLink]="['/employees', person.employeeId]"><strong>{{ person.employeeName ?? person.employeeCode }}</strong></a>
            <span class="muted">{{ person.designation ?? '' }}</span>
            <span>{{ levels[person.level] }}{{ person.isPrimary ? ' · primary' : '' }}{{ person.yearsExperience ? ' · ' + person.yearsExperience + ' yrs' : '' }}</span>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-users" title="No one has this skill recorded" />
        }
      </div>
    </gggs-drawer>

    <gggs-form-dialog [(visible)]="formOpen" [title]="editing() ? 'Edit skill' : 'New skill'" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().code.trim() || !form().name.trim() || !form().category.trim()" (save)="save()">
      <label class="field">
        <span class="field__label">Code</span>
        <input type="text" maxlength="40" [value]="form().code" (input)="patch('code', $any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Name</span>
        <input type="text" maxlength="120" [value]="form().name" (input)="patch('name', $any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Category</span>
        <input type="text" maxlength="60" list="skill-categories" [value]="form().category" (input)="patch('category', $any($event.target).value)" />
        <datalist id="skill-categories">
          @for (option of categories(); track option) {
            <option [value]="option"></option>
          }
        </datalist>
      </label>
      <label class="field">
        <span class="field__label">Description</span>
        <textarea rows="3" maxlength="500" [value]="form().description" (input)="patch('description', $any($event.target).value)"></textarea>
      </label>
    </gggs-form-dialog>
  `,
  styles: `
    .skills__filters {
      margin-block: var(--gggs-space-3);
    }

    .skills__row {
      display: grid;
      grid-template-columns: 2fr 2fr 8rem 7rem 9rem;
      gap: var(--gggs-space-3);
      align-items: center;
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .skills__name {
      justify-self: start;
      padding: 0;
    }

    .skills__scroll {
      overflow-x: auto;
    }



    .skills__category {
      display: block;
      font-size: var(--gggs-font-size-xs);
      font-weight: normal;
    }

    .skills__cell {
      text-align: center;
      min-width: 5rem;
    }

    .skills__gap {
      color: var(--gggs-color-warning);
      font-weight: var(--gggs-font-weight-semibold);
    }

    .skills__person {
      display: grid;
      grid-template-columns: 1.5fr 1.5fr 1fr;
      gap: var(--gggs-space-2);
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    @media (max-width: 56rem) {
      .skills__row {
        grid-template-columns: 1fr 1fr;
      }
    }
  `,
})
export class SkillsPage {
  private readonly api = inject(SkillsApi);

  protected readonly canManage = signal(inject(PermissionStore).has('skill.skill.manage'));
  protected readonly tabs: readonly TabItem[] = [
    { key: 'catalog', label: 'Catalogue' },
    { key: 'matrix', label: 'Skill matrix' },
  ];
  protected readonly levels = SKILL_LEVEL_LABELS;
  protected readonly levelColumns = [1, 2, 3, 4, 5];
  protected readonly tab = signal('catalog');
  protected readonly catalog = signal<readonly SkillItem[]>([]);
  protected readonly matrix = signal<readonly MatrixRow[]>([]);
  protected readonly category = signal('');
  protected readonly search = signal('');
  protected readonly people = signal<readonly SkilledPerson[]>([]);
  protected readonly peopleOpen = signal(false);
  protected readonly peopleTitle = signal('');
  protected readonly formOpen = signal(false);
  protected readonly editing = signal<SkillItem | null>(null);
  protected readonly form = signal({ code: '', name: '', category: '', description: '' });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  protected readonly categories = computed(() => [...new Set(this.catalog().map((skill) => skill.category))].sort());
  protected readonly filteredCatalog = computed(() => {
    const term = this.search().trim().toLowerCase();
    return this.catalog().filter(
      (skill) => (!this.category() || skill.category === this.category()) && (!term || skill.name.toLowerCase().includes(term) || skill.code.toLowerCase().includes(term)),
    );
  });
  protected readonly filteredMatrix = computed(() => this.matrix().filter((row) => !this.category() || row.category === this.category()));
  private readonly peak = computed(() => Math.max(1, ...this.matrix().flatMap((row) => row.countsByLevel)));

  constructor() {
    void this.load();
  }

  /** People at a level (1-5); the API lists the counts from level 1. */
  protected count(row: MatrixRow, level: number): number {
    return row.countsByLevel[level - 1] ?? 0;
  }

  protected heat(row: MatrixRow, level: number): string {
    const count = this.count(row, level);
    return count ? `color-mix(in srgb, var(--gggs-color-primary) ${Math.round(10 + (count / this.peak()) * 50)}%, transparent)` : 'transparent';
  }

  protected async showPeople(skillId: string, name: string): Promise<void> {
    this.peopleTitle.set(`People with ${name}`);
    this.people.set([]);
    this.peopleOpen.set(true);
    try {
      this.people.set(await this.api.peopleWith(skillId));
    } catch (error) {
      this.error.set(message(error, 'The people could not be loaded.'));
    }
  }

  protected edit(skill: SkillItem | null): void {
    this.editing.set(skill);
    this.form.set({ code: skill?.code ?? '', name: skill?.name ?? '', category: skill?.category ?? this.category(), description: skill?.description ?? '' });
    this.formError.set(null);
    this.formOpen.set(true);
  }

  protected patch(field: 'code' | 'name' | 'category' | 'description', value: string): void {
    this.form.update((form) => ({ ...form, [field]: value }));
  }

  protected async save(): Promise<void> {
    const form = this.form();
    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.saveSkill(this.editing()?.id ?? null, { code: form.code.trim(), name: form.name.trim(), category: form.category.trim(), description: form.description.trim() || null });
      this.formOpen.set(false);
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'The skill could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async toggle(skill: SkillItem): Promise<void> {
    try {
      await this.api.setSkillActive(skill.id, !skill.isActive);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'The skill could not be changed.'));
    }
  }

  private async load(): Promise<void> {
    this.error.set(null);
    try {
      const [catalog, matrix] = await Promise.all([this.api.catalog({ includeInactive: this.canManage() }), this.api.matrix()]);
      this.catalog.set(catalog);
      this.matrix.set(matrix);
    } catch (error) {
      this.error.set(message(error, 'Skills could not be loaded.'));
    }
  }
}
