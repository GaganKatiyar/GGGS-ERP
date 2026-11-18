import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { OrgStructureApi, type PlannedUnit, type StructureImportResult, type StructurePlan } from '@gggs/shared/org-reference';
import { EmptyState, FileUpload, KpiCard, PageHeader } from '@gggs/shared/ui';

/** The columns a structure file has, for the template download. */
const TEMPLATE = [
  'DepartmentCode,Department,FunctionName,FunctionCode,RoleName,RoleCode,RoleLevelCode',
  'D-TS,Technology Services,UX Practice,UX,Business Analyst,TS-BA,L1',
  'D-TS,Technology Services,UX Practice,UX,Business Analyst,TS-BA,L2',
  'D-QUAL,Quality,,,QA Lead,QUAL-QAL,L1',
].join('\r\n');

type Section = 'departments' | 'functions' | 'roles';

/**
 * Departments, functions and roles from one file (CSV, or the Markdown table an export gives):
 * preview what would be created or changed, then apply it all at once. Each role gets a
 * designation per level ("Business Analyst L2"). Nothing missing from the file is removed.
 */
@Component({
  selector: 'gggs-structure-import-page',
  imports: [RouterLink, PageHeader, FileUpload, KpiCard, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Import structure" subtitle="Departments, their functions and roles from a file. Preview first; nothing is saved until you apply.">
      <div class="toolbar">
        <button type="button" class="btn" (click)="downloadTemplate()"><i class="pi pi-download" aria-hidden="true"></i> Template</button>
        <a class="btn" routerLink="../structure">Back to structure</a>
      </div>
    </gggs-page-header>

    <div class="stack">
      <section class="panel stack">
        <gggs-file-upload accept=".csv,.md,.txt,text/csv,text/markdown" prompt="Drop a CSV or Markdown file here, or click to choose one" (fileSelected)="read($event)" />
        <p class="muted">
          Columns: Department, FunctionName, FunctionCode, RoleName, RoleCode, RoleLevelCode, and optionally DepartmentCode. NULL or an empty cell means none. A role belongs to its
          department and is open to all of that department's functions; each role gets a designation for every level. Repeated lines are read once, and departments with no
          functions or roles are skipped.
        </p>
        @if (fileName()) {
          <p><strong>{{ fileName() }}</strong></p>
        }
        @if (error()) {
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
        }
        <div class="toolbar">
          <button type="button" class="btn btn--primary" [disabled]="!plan()?.canApply || busy() || !!applied()" (click)="apply()">
            {{ applying() ? 'Applying…' : 'Apply' }}
          </button>
        </div>
      </section>

      @if (applied(); as done) {
        <gggs-empty-state
          icon="pi pi-check-circle"
          title="The structure is in"
          [description]="summary(done)"
          actionLabel="Open the structure"
          (action)="openStructure()"
        />
      } @else if (plan(); as p) {
        <div class="kpi-row">
          <gggs-kpi-card label="Departments" icon="pi pi-sitemap" [value]="p.departments.length" [hint]="changes(p.departments)" />
          <gggs-kpi-card label="Functions" icon="pi pi-share-alt" [value]="p.functions.length" [hint]="changes(p.functions)" />
          <gggs-kpi-card label="Roles" icon="pi pi-id-card" [value]="p.roles.length" [hint]="changes(p.roles)" />
          <gggs-kpi-card label="New designations" icon="pi pi-tag" [value]="p.newDesignations" hint="One per role and level" />
        </div>

        @if (p.errors.length) {
          <section class="panel">
            <h2 class="sim__title">Fix these in the file, then upload it again</h2>
            <ul class="sim__issues sim__issues--error">
              @for (issue of p.errors; track $index) {
                <li>{{ issue.line ? 'Line ' + issue.line + ': ' : '' }}{{ issue.message }}</li>
              }
            </ul>
          </section>
        }
        @if (p.warnings.length) {
          <section class="panel">
            <h2 class="sim__title">Good to know</h2>
            <ul class="sim__issues">
              @for (issue of p.warnings; track $index) {
                <li>{{ issue.message }}</li>
              }
            </ul>
          </section>
        }

        <section class="panel stack">
          <div class="toolbar" role="tablist" aria-label="What the file holds">
            @for (tab of tabs; track tab.key) {
              <button type="button" role="tab" class="btn" [class.btn--primary]="section() === tab.key" [attr.aria-selected]="section() === tab.key" (click)="section.set(tab.key)">
                {{ tab.label }} ({{ p[tab.key].length }})
              </button>
            }
            <label class="sim__only">
              <input type="checkbox" [checked]="changedOnly()" (change)="changedOnly.set($any($event.target).checked)" />
              <span>Only what changes</span>
            </label>
          </div>
          <table class="table">
            <thead>
              <tr>
                <th scope="col">Code</th>
                <th scope="col">Name</th>
                @if (section() !== 'departments') {
                  <th scope="col">Department</th>
                }
                <th scope="col">Change</th>
              </tr>
            </thead>
            <tbody>
              @for (unit of visibleUnits(); track unit.code) {
                <tr>
                  <td>{{ unit.code }}</td>
                  <td>{{ unit.name }}</td>
                  @if (section() !== 'departments') {
                    <td>{{ departmentName(unit.departmentCode) }}</td>
                  }
                  <td>
                    <span [class]="'sim__change sim__change--' + unit.change.toLowerCase()">{{ label(unit.change) }}</span>
                    @if (unit.note) {
                      <span class="muted"> · {{ unit.note }}</span>
                    }
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="4" class="muted">Nothing changes here.</td>
                </tr>
              }
            </tbody>
          </table>
        </section>
      }
    </div>
  `,
  styles: `
    .sim__title {
      margin: 0 0 var(--gggs-space-2, 0.5rem);
      font-size: 1rem;
    }

    .sim__issues {
      margin: 0;
      padding-inline-start: 1.25rem;
    }

    .sim__issues--error {
      color: var(--gggs-color-danger);
    }

    .sim__only {
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      margin-inline-start: auto;
    }

    .sim__change--create {
      color: var(--gggs-color-success);
      font-weight: 600;
    }

    .sim__change--update {
      color: var(--gggs-color-warning, inherit);
      font-weight: 600;
    }

    .sim__change--unchanged {
      color: var(--text-muted, #6b7280);
    }
  `,
})
export class StructureImportPage {
  private readonly api = inject(OrgStructureApi);
  private readonly router = inject(Router);

  protected readonly tabs: readonly { key: Section; label: string }[] = [
    { key: 'departments', label: 'Departments' },
    { key: 'functions', label: 'Functions' },
    { key: 'roles', label: 'Roles' },
  ];

  protected readonly fileName = signal<string | null>(null);
  private readonly content = signal<string | null>(null);
  protected readonly plan = signal<StructurePlan | null>(null);
  protected readonly applied = signal<StructureImportResult | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly applying = signal(false);
  protected readonly section = signal<Section>('departments');
  protected readonly changedOnly = signal(false);

  protected readonly visibleUnits = computed(() => {
    const units = this.plan()?.[this.section()] ?? [];
    return this.changedOnly() ? units.filter((unit) => unit.change !== 'Unchanged') : units;
  });

  private readonly departmentNames = computed(() => new Map((this.plan()?.departments ?? []).map((unit) => [unit.code, unit.name])));

  protected async read(file: File): Promise<void> {
    this.fileName.set(file.name);
    this.plan.set(null);
    this.applied.set(null);
    this.error.set(null);
    this.busy.set(true);
    try {
      const text = await file.text();
      this.content.set(text);
      this.plan.set(await this.api.previewStructureImport(text));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The file could not be read.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async apply(): Promise<void> {
    const content = this.content();
    if (!content) {
      return;
    }
    this.busy.set(true);
    this.applying.set(true);
    this.error.set(null);
    try {
      this.applied.set(await this.api.applyStructureImport(content));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The structure could not be imported.');
    } finally {
      this.busy.set(false);
      this.applying.set(false);
    }
  }

  protected changes(units: readonly PlannedUnit[]): string {
    const created = units.filter((unit) => unit.change === 'Create').length;
    const updated = units.filter((unit) => unit.change === 'Update').length;
    return `${created} new · ${updated} changed`;
  }

  protected summary(done: StructureImportResult): string {
    return (
      `${done.departmentsCreated} departments, ${done.functionsCreated} functions and ${done.rolesCreated} roles added; ` +
      `${done.departmentsUpdated + done.functionsUpdated + done.rolesUpdated} updated; ${done.designationsCreated} designations made.`
    );
  }

  protected label(change: PlannedUnit['change']): string {
    return change === 'Create' ? 'New' : change === 'Update' ? 'Changed' : 'No change';
  }

  protected departmentName(code: string | undefined): string {
    return code ? (this.departmentNames().get(code) ?? code) : '—';
  }

  protected openStructure(): void {
    void this.router.navigate(['/org/structure']);
  }

  protected downloadTemplate(): void {
    const url = URL.createObjectURL(new Blob([TEMPLATE], { type: 'text/csv' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = 'org-structure-template.csv';
    link.click();
    URL.revokeObjectURL(url);
  }
}
