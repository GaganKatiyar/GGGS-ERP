import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { PermissionStore } from '@gggs/core/permissions';
import type { CalculationType, ComponentInput, ComponentItem, PayrollSettings, SalaryPreview, StructureItem, StructureLineInput } from '@gggs/shared/models';
import { CALCULATION_TYPES, COMPONENT_KINDS, INPUT_KINDS, humanize } from '@gggs/shared/models';
import { MoneyPipe, PayrollApi } from '@gggs/shared/money';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';
import { message } from './payroll-shared';

interface LineDraft {
  componentId: string;
  override: boolean;
  calculationType: CalculationType;
  value: number | null;
  percentOf: string;
  formula: string;
}

const blankComponent = (): ComponentInput => ({
  code: '',
  name: '',
  kind: 'Earning',
  calculationType: 'Fixed',
  value: 0,
  percentOf: null,
  formula: null,
  inputKind: null,
  isTaxable: true,
  pfApplicable: false,
  esiApplicable: true,
  ptApplicable: true,
  isProrated: true,
  showOnPayslip: true,
  displayOrder: 100,
  description: null,
});

/**
 * Salary structures and components (spec §21, §23): what a CTC is made of and how each part is
 * worked out - with a preview of any CTC on a structure before anyone is paid on it.
 */
@Component({
  selector: 'gggs-salary-structures-page',
  imports: [MoneyPipe, PageHeader, Tabs, StatusBadge, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Salary structures" subtitle="Components and the structures that lay a CTC out.">
      @if (tab() === 'structures' && canStructure) {
        <button type="button" class="btn btn--primary" (click)="editStructure(null)"><i class="pi pi-plus" aria-hidden="true"></i> New structure</button>
      }
      @if (tab() === 'components' && canComponent) {
        <button type="button" class="btn btn--primary" (click)="editComponent(null)"><i class="pi pi-plus" aria-hidden="true"></i> New component</button>
      }
    </gggs-page-header>
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />

    @if (tab() === 'structures') {
      <div class="pay__two">
        <section class="panel stack">
          @for (structure of structures(); track structure.id) {
            <article class="stack pay__structure">
              <div class="toolbar">
                <h2 class="pay__heading">{{ structure.name }} <span class="muted">{{ structure.code }}</span></h2>
                <gggs-status-badge [status]="structure.isActive ? 'Active' : 'Inactive'" />
                <span class="muted">{{ structure.employeeCount }} on it</span>
                <span class="pay__spacer"></span>
                <button type="button" class="btn btn--link btn--sm" (click)="previewOf.set(structure.id); preview()">Preview</button>
                @if (canStructure) {
                  <button type="button" class="btn btn--link btn--sm" (click)="editStructure(structure)">Edit</button>
                }
              </div>
              @if (structure.description) {
                <p class="muted">{{ structure.description }}</p>
              }
              <p>
                @for (line of structure.lines; track line.componentId; let last = $last) {
                  <span>{{ line.name }} ({{ describe(line.calculationType, line.value, line.percentOf, line.formula) }}){{ last ? '' : ' · ' }}</span>
                }
              </p>
            </article>
          } @empty {
            <gggs-empty-state icon="pi pi-sitemap" title="No structures yet" />
          }
        </section>
        <section class="panel stack">
          <h2 class="pay__heading">Preview a CTC</h2>
          <div class="pay__grid">
            <label class="field">
              <span class="field__label">Structure</span>
              <select [value]="previewOf()" (change)="previewOf.set($any($event.target).value); preview()">
                <option value="">Choose</option>
                @for (structure of structures(); track structure.id) {
                  <option [value]="structure.id">{{ structure.name }}</option>
                }
              </select>
            </label>
            <label class="field">
              <span class="field__label">Annual CTC</span>
              <input type="number" min="0" step="10000" [value]="previewCtc()" (input)="previewCtc.set(+$any($event.target).value)" (change)="preview()" />
            </label>
          </div>
          @if (previewed(); as p) {
            <table class="pay__table">
              <thead><tr><th scope="col">Component</th><th scope="col" class="pay__num">Monthly</th><th scope="col" class="pay__num">Annual</th></tr></thead>
              <tbody>
                @for (line of p.lines; track line.code) {
                  <tr [class.muted]="line.kind !== 'Earning'"><td>{{ line.name }} <span class="muted">{{ humanize(line.kind) }}</span></td><td class="pay__num">{{ line.monthly | gggsMoney }}</td><td class="pay__num">{{ line.annual | gggsMoney }}</td></tr>
                }
              </tbody>
              <tfoot>
                <tr><th scope="row">Take-home</th><td class="pay__num"><strong>{{ p.monthlyTakeHome | gggsMoney }}</strong></td><td class="pay__num">{{ p.monthlyTakeHome * 12 | gggsMoney }}</td></tr>
              </tfoot>
            </table>
            @for (issue of p.issues; track issue) {
              <p class="pay__warning">{{ issue }}</p>
            }
          } @else {
            <p class="muted">Choose a structure and a CTC to see the monthly breakdown, statutory deductions and take-home.</p>
          }
        </section>
      </div>
    } @else {
      <section class="panel pay__scroll">
        <table class="pay__table">
          <thead>
            <tr><th scope="col">Code</th><th scope="col">Name</th><th scope="col">Kind</th><th scope="col">Calculation</th><th scope="col">Counts towards</th><th scope="col">Status</th><th scope="col"><span class="muted">Actions</span></th></tr>
          </thead>
          <tbody>
            @for (component of components(); track component.id) {
              <tr>
                <td><code>{{ component.code }}</code></td>
                <td>{{ component.name }}{{ component.isSystem ? ' · system' : '' }}</td>
                <td>{{ humanize(component.kind) }}</td>
                <td>{{ component.calculationType === 'Input' ? 'Input: ' + humanize(component.inputKind ?? '') : describe(component.calculationType, component.value, component.percentOf, component.formula) }}</td>
                <td class="muted">{{ flags(component) }}</td>
                <td><gggs-status-badge [status]="component.isActive ? 'Active' : 'Inactive'" /></td>
                <td>
                  @if (canComponent && component.calculationType !== 'Statutory') {
                    <button type="button" class="btn btn--link btn--sm" (click)="editComponent(component)">Edit</button>
                  }
                  @if (canComponent && !component.isSystem) {
                    <button type="button" class="btn btn--link btn--sm" (click)="toggleComponent(component)">{{ component.isActive ? 'Retire' : 'Restore' }}</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </section>
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }

    <gggs-form-dialog [(visible)]="structureOpen" [title]="editingStructure() ? 'Edit structure' : 'New structure'" [busy]="busy()" [error]="formError()" [saveDisabled]="!structureName().trim() || !lines().length" (save)="saveStructure()" size="lg">
      <div class="pay__grid">
        @if (!editingStructure()) {
          <label class="field">
            <span class="field__label">Code</span>
            <input type="text" maxlength="30" [value]="structureCode()" (input)="structureCode.set($any($event.target).value)" />
          </label>
        }
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="100" [value]="structureName()" (input)="structureName.set($any($event.target).value)" />
        </label>
      </div>
      <label class="field">
        <span class="field__label">Description</span>
        <input type="text" maxlength="500" [value]="structureDescription()" (input)="structureDescription.set($any($event.target).value)" />
      </label>
      <table class="pay__table">
        <thead><tr><th scope="col">Component</th><th scope="col">Calculated</th><th scope="col"><span class="muted">Remove</span></th></tr></thead>
        <tbody>
          @for (line of lines(); track line.componentId; let index = $index) {
            <tr>
              <td>{{ componentName(line.componentId) }}</td>
              <td>
                <label class="pay__flag">
                  <input type="checkbox" [checked]="line.override" (change)="patchLine(index, { override: $any($event.target).checked })" />
                  <span>{{ line.override ? 'Differently on this structure:' : 'As the component says (' + componentDefault(line.componentId) + ')' }}</span>
                </label>
                @if (line.override) {
                  <div class="toolbar">
                    <select aria-label="Calculation" [value]="line.calculationType" (change)="patchLine(index, { calculationType: $any($event.target).value })">
                      @for (type of lineTypes; track type) {
                        <option [value]="type">{{ type }}</option>
                      }
                    </select>
                    @if (line.calculationType === 'Fixed' || line.calculationType === 'Percentage') {
                      <input type="number" aria-label="Value" min="0" [value]="line.value ?? ''" (input)="patchLine(index, { value: $any($event.target).value === '' ? null : +$any($event.target).value })" />
                    }
                    @if (line.calculationType === 'Percentage') {
                      <input type="text" aria-label="Percentage of" placeholder="CTC or a code" [value]="line.percentOf" (input)="patchLine(index, { percentOf: $any($event.target).value })" />
                    }
                    @if (line.calculationType === 'Formula') {
                      <input type="text" aria-label="Formula" placeholder="e.g. MIN(BASIC * 0.5, 50000)" [value]="line.formula" (input)="patchLine(index, { formula: $any($event.target).value })" />
                    }
                  </div>
                }
              </td>
              <td><button type="button" class="btn btn--link btn--sm" aria-label="Remove" (click)="removeLine(index)"><i class="pi pi-times" aria-hidden="true"></i></button></td>
            </tr>
          }
        </tbody>
      </table>
      <label class="field">
        <span class="field__label">Add a component</span>
        <select [value]="''" (change)="addLine($any($event.target).value); $any($event.target).value = ''">
          <option value="">Choose…</option>
          @for (component of addable(); track component.id) {
            <option [value]="component.id">{{ component.name }} ({{ component.code }})</option>
          }
        </select>
      </label>
      <p class="muted">Employer PF, ESI and gratuity sit inside the CTC when the structure lists them. Deductions and inputs are worked out each month and never belong to a structure.</p>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="componentOpen" [title]="editingComponent() ? 'Edit component' : 'New component'" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().name.trim() || (!editingComponent() && !(form().code ?? '').trim())" (save)="saveComponent()" size="lg">
      <div class="pay__grid">
        @if (!editingComponent()) {
          <label class="field">
            <span class="field__label">Code</span>
            <input type="text" maxlength="30" placeholder="e.g. LTA" [value]="form().code ?? ''" (input)="patch({ code: $any($event.target).value.toUpperCase() })" />
          </label>
        }
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="100" [value]="form().name" (input)="patch({ name: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Kind</span>
          <select [value]="form().kind" [disabled]="editingComponent()?.isSystem ?? false" (change)="patch({ kind: $any($event.target).value })">
            @for (kind of kinds; track kind) {
              <option [value]="kind">{{ humanize(kind) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Calculation</span>
          <select [value]="form().calculationType" [disabled]="editingComponent()?.isSystem ?? false" (change)="patch({ calculationType: $any($event.target).value })">
            @for (type of types; track type) {
              <option [value]="type">{{ type }}</option>
            }
          </select>
        </label>
        @if (form().calculationType === 'Fixed' || form().calculationType === 'Percentage') {
          <label class="field">
            <span class="field__label">{{ form().calculationType === 'Fixed' ? 'Monthly amount' : 'Percentage' }}</span>
            <input type="number" min="0" [value]="form().value ?? ''" (input)="patch({ value: $any($event.target).value === '' ? null : +$any($event.target).value })" />
          </label>
        }
        @if (form().calculationType === 'Percentage') {
          <label class="field">
            <span class="field__label">Of</span>
            <input type="text" placeholder="CTC or a component code" [value]="form().percentOf ?? ''" (input)="patch({ percentOf: $any($event.target).value.toUpperCase() })" />
          </label>
        }
        @if (form().calculationType === 'Input') {
          <label class="field">
            <span class="field__label">Input kind</span>
            <select [value]="form().inputKind ?? ''" (change)="patch({ inputKind: $any($event.target).value || null })">
              <option value="">Choose</option>
              @for (kind of inputKinds; track kind) {
                <option [value]="kind">{{ kind }}</option>
              }
            </select>
          </label>
        }
        <label class="field">
          <span class="field__label">Payslip order</span>
          <input type="number" min="0" [value]="form().displayOrder" (input)="patch({ displayOrder: +$any($event.target).value })" />
        </label>
      </div>
      @if (form().calculationType === 'Formula') {
        <label class="field">
          <span class="field__label">Formula</span>
          <input type="text" maxlength="500" placeholder="MIN(BASIC * 0.5, 50000)" [value]="form().formula ?? ''" (input)="patch({ formula: $any($event.target).value })" />
          <span class="muted">Component codes, CTC, numbers, + − × ÷, comparisons and MIN, MAX, ROUND, FLOOR, CEIL, IF, ABS.</span>
        </label>
      }
      <div class="pay__grid">
        <label class="pay__flag"><input type="checkbox" [checked]="form().isTaxable" (change)="patch({ isTaxable: $any($event.target).checked })" /> <span>Taxable</span></label>
        <label class="pay__flag"><input type="checkbox" [checked]="form().pfApplicable" (change)="patch({ pfApplicable: $any($event.target).checked })" /> <span>Counts for PF</span></label>
        <label class="pay__flag"><input type="checkbox" [checked]="form().esiApplicable" (change)="patch({ esiApplicable: $any($event.target).checked })" /> <span>Counts for ESI</span></label>
        <label class="pay__flag"><input type="checkbox" [checked]="form().ptApplicable" (change)="patch({ ptApplicable: $any($event.target).checked })" /> <span>Counts for professional tax</span></label>
        <label class="pay__flag"><input type="checkbox" [checked]="form().isProrated" (change)="patch({ isProrated: $any($event.target).checked })" /> <span>Prorated for unpaid days</span></label>
        <label class="pay__flag"><input type="checkbox" [checked]="form().showOnPayslip" (change)="patch({ showOnPayslip: $any($event.target).checked })" /> <span>Shown on payslips</span></label>
      </div>
      <label class="field">
        <span class="field__label">Description</span>
        <input type="text" maxlength="500" [value]="form().description ?? ''" (input)="patch({ description: $any($event.target).value })" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './payroll.css',
  styles: `
    .pay__structure {
      padding-block-end: var(--gggs-space-3);
      border-block-end: 1px solid var(--gggs-color-border);
    }
  `,
})
export class SalaryStructuresPage {
  private readonly api = inject(PayrollApi);
  private readonly permissions = inject(PermissionStore);

  protected readonly canStructure = this.permissions.has('payroll.structure.manage');
  protected readonly canComponent = this.permissions.has('payroll.component.manage');
  protected readonly tabs: readonly TabItem[] = [
    { key: 'structures', label: 'Structures' },
    { key: 'components', label: 'Components' },
  ];
  protected readonly kinds = COMPONENT_KINDS;
  protected readonly types = CALCULATION_TYPES;
  protected readonly lineTypes: readonly CalculationType[] = ['Fixed', 'Percentage', 'Formula', 'Balancing'];
  protected readonly inputKinds = INPUT_KINDS;
  protected readonly humanize = humanize;
  protected readonly tab = signal('structures');
  protected readonly structures = signal<readonly StructureItem[]>([]);
  protected readonly components = signal<readonly ComponentItem[]>([]);
  protected readonly previewOf = signal('');
  protected readonly previewCtc = signal(1_200_000);
  protected readonly previewed = signal<SalaryPreview | null>(null);
  protected readonly structureOpen = signal(false);
  protected readonly editingStructure = signal<StructureItem | null>(null);
  protected readonly structureCode = signal('');
  protected readonly structureName = signal('');
  protected readonly structureDescription = signal('');
  protected readonly lines = signal<readonly LineDraft[]>([]);
  protected readonly componentOpen = signal(false);
  protected readonly editingComponent = signal<ComponentItem | null>(null);
  protected readonly form = signal<ComponentInput>(blankComponent());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  /** Components a structure may hold: CTC parts, not deductions or inputs, and not already in it. */
  protected readonly addable = computed(() => {
    const used = new Set(this.lines().map((line) => line.componentId));
    return this.components().filter(
      (component) => component.isActive && !used.has(component.id) && (component.kind === 'Earning' || component.kind === 'EmployerContribution') && component.calculationType !== 'Input',
    );
  });

  constructor() {
    void this.load();
  }

  protected describe(type: CalculationType, value: number | undefined | null, percentOf: string | undefined | null, formula: string | undefined | null): string {
    switch (type) {
      case 'Fixed':
        return `${value ?? 0} a month`;
      case 'Percentage':
        return `${value ?? 0}% of ${percentOf ?? 'CTC'}`;
      case 'Formula':
        return formula ?? 'formula';
      case 'Balancing':
        return 'the rest of the CTC';
      case 'Statutory':
        return 'by its statutory rule';
      default:
        return type;
    }
  }

  protected flags(component: ComponentItem): string {
    return [component.isTaxable ? 'tax' : '', component.pfApplicable ? 'PF' : '', component.esiApplicable ? 'ESI' : '', component.ptApplicable ? 'PT' : '', component.isProrated ? 'prorated' : '']
      .filter(Boolean)
      .join(', ');
  }

  protected componentName(id: string): string {
    return this.components().find((component) => component.id === id)?.name ?? '?';
  }

  protected componentDefault(id: string): string {
    const component = this.components().find((candidate) => candidate.id === id);
    return component ? this.describe(component.calculationType, component.value, component.percentOf, component.formula) : '';
  }

  protected async preview(): Promise<void> {
    if (!this.previewOf() || this.previewCtc() <= 0) {
      return;
    }

    try {
      this.previewed.set(await this.api.preview(this.previewOf(), { annualCtc: this.previewCtc() }));
      this.error.set(null);
    } catch (error) {
      this.error.set(message(error, 'The preview could not be worked out.'));
    }
  }

  protected editStructure(structure: StructureItem | null): void {
    this.editingStructure.set(structure);
    this.structureCode.set(structure?.code ?? '');
    this.structureName.set(structure?.name ?? '');
    this.structureDescription.set(structure?.description ?? '');
    this.lines.set(
      structure?.lines.map((line) => ({
        componentId: line.componentId,
        override: line.overridden,
        calculationType: line.calculationType,
        value: line.value ?? null,
        percentOf: line.percentOf ?? '',
        formula: line.formula ?? '',
      })) ?? [],
    );
    this.formError.set(null);
    this.structureOpen.set(true);
  }

  protected addLine(componentId: string): void {
    const component = this.components().find((candidate) => candidate.id === componentId);
    if (component) {
      this.lines.update((lines) => [
        ...lines,
        { componentId, override: false, calculationType: component.calculationType, value: component.value ?? null, percentOf: component.percentOf ?? '', formula: component.formula ?? '' },
      ]);
    }
  }

  protected patchLine(index: number, change: Partial<LineDraft>): void {
    this.lines.update((lines) => lines.map((line, at) => (at === index ? { ...line, ...change } : line)));
  }

  protected removeLine(index: number): void {
    this.lines.update((lines) => lines.filter((_, at) => at !== index));
  }

  protected async saveStructure(): Promise<void> {
    const lines: StructureLineInput[] = this.lines().map((line) =>
      line.override
        ? { componentId: line.componentId, calculationType: line.calculationType, value: line.value, percentOf: line.percentOf || null, formula: line.formula || null }
        : { componentId: line.componentId },
    );
    await this.save(async () => {
      await this.api.saveStructure(this.editingStructure()?.id ?? null, {
        code: this.structureCode().trim() || null,
        name: this.structureName().trim(),
        description: this.structureDescription().trim() || null,
        lines,
      });
      this.structureOpen.set(false);
    });
  }

  protected editComponent(component: ComponentItem | null): void {
    this.editingComponent.set(component);
    this.form.set(
      component
        ? {
            code: component.code,
            name: component.name,
            kind: component.kind,
            calculationType: component.calculationType,
            value: component.value ?? null,
            percentOf: component.percentOf ?? null,
            formula: component.formula ?? null,
            inputKind: component.inputKind ?? null,
            isTaxable: component.isTaxable,
            pfApplicable: component.pfApplicable,
            esiApplicable: component.esiApplicable,
            ptApplicable: component.ptApplicable,
            isProrated: component.isProrated,
            showOnPayslip: component.showOnPayslip,
            displayOrder: component.displayOrder,
            description: component.description ?? null,
          }
        : blankComponent(),
    );
    this.formError.set(null);
    this.componentOpen.set(true);
  }

  protected patch(change: Partial<ComponentInput>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected async saveComponent(): Promise<void> {
    await this.save(async () => {
      await this.api.saveComponent(this.editingComponent()?.id ?? null, this.form());
      this.componentOpen.set(false);
    });
  }

  protected async toggleComponent(component: ComponentItem): Promise<void> {
    try {
      await this.api.setComponentActive(component.id, !component.isActive);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'The component could not be changed.'));
    }
  }

  private async save(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await action();
      await this.load();
    } catch (error) {
      this.formError.set(message(error, 'That could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const [structures, components] = await Promise.all([this.api.structures(), this.api.components(true)]);
      this.structures.set(structures);
      this.components.set(components);
      if (!this.previewOf() && structures.length) {
        this.previewOf.set(structures[0].id);
        void this.preview();
      }
    } catch (error) {
      this.error.set(message(error, 'Salary structures could not be loaded.'));
    }
  }
}

/** Payroll settings: pay day, proration, loss of pay, overtime, the default regime and the employer's statutory details. */
@Component({
  selector: 'gggs-payroll-settings-page',
  imports: [PageHeader, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Payroll settings" subtitle="How the month is paid, and the employer details on payslips and returns." />
    @if (settings(); as s) {
      <section class="panel stack">
        <h2 class="pay__heading">The month</h2>
        <div class="pay__grid">
          <label class="field">
            <span class="field__label">Pay day</span>
            <input type="number" min="1" max="31" [value]="s.payDay" (input)="patch({ payDay: +$any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">Prorate by</span>
            <select [value]="s.prorationBasis" (change)="patch({ prorationBasis: $any($event.target).value })">
              <option value="CalendarDays">Days in the month</option>
              <option value="ThirtyDays">A 30-day month</option>
            </select>
          </label>
          <label class="field">
            <span class="field__label">Default tax regime</span>
            <select [value]="s.defaultRegime" (change)="patch({ defaultRegime: $any($event.target).value })">
              <option value="New">New regime</option>
              <option value="Old">Old regime</option>
            </select>
          </label>
          <label class="field">
            <span class="field__label">Default professional-tax state</span>
            <input type="text" maxlength="16" placeholder="IN-KA" [value]="s.defaultStateCode ?? ''" (input)="patch({ defaultStateCode: $any($event.target).value })" />
          </label>
        </div>
        <label class="pay__flag">
          <input type="checkbox" [checked]="s.lopFromAttendance" (change)="patch({ lopFromAttendance: $any($event.target).checked })" />
          <span>Count absences in attendance as loss of pay (unpaid leave always counts)</span>
        </label>
        <label class="pay__flag">
          <input type="checkbox" [checked]="s.overtimeFromTimesheets" (change)="patch({ overtimeFromTimesheets: $any($event.target).checked })" />
          <span>Pay approved overtime from timesheets</span>
        </label>
        @if (s.overtimeFromTimesheets) {
          <div class="pay__grid">
            <label class="field">
              <span class="field__label">Overtime multiplier</span>
              <input type="number" min="1" max="5" step="0.25" [value]="s.overtimeMultiplier" (input)="patch({ overtimeMultiplier: +$any($event.target).value })" />
            </label>
            <label class="field">
              <span class="field__label">Standard hours a day</span>
              <input type="number" min="1" max="24" step="0.5" [value]="s.standardHoursPerDay" (input)="patch({ standardHoursPerDay: +$any($event.target).value })" />
            </label>
          </div>
        }
      </section>
      <section class="panel stack">
        <h2 class="pay__heading">Employer details</h2>
        <div class="pay__grid">
          <label class="field">
            <span class="field__label">Employer name</span>
            <input type="text" maxlength="200" [value]="s.employerName ?? ''" (input)="patch({ employerName: $any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">PAN</span>
            <input type="text" maxlength="10" [value]="s.employerPan ?? ''" (input)="patch({ employerPan: $any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">TAN</span>
            <input type="text" maxlength="10" [value]="s.employerTan ?? ''" (input)="patch({ employerTan: $any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">PF establishment code</span>
            <input type="text" maxlength="30" [value]="s.pfEstablishmentCode ?? ''" (input)="patch({ pfEstablishmentCode: $any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">ESI employer code</span>
            <input type="text" maxlength="30" [value]="s.esiEmployerCode ?? ''" (input)="patch({ esiEmployerCode: $any($event.target).value })" />
          </label>
        </div>
        <label class="field">
          <span class="field__label">Address on payslips</span>
          <input type="text" maxlength="500" [value]="s.employerAddress ?? ''" (input)="patch({ employerAddress: $any($event.target).value })" />
        </label>
        <div class="toolbar">
          <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="save()">Save settings</button>
        </div>
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }
    @if (saved()) {
      <p class="pay__notice" role="status">Settings saved.</p>
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './payroll.css',
})
export class PayrollSettingsPage {
  private readonly api = inject(PayrollApi);

  protected readonly settings = signal<PayrollSettings | null>(null);
  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.api.settings().then(
      (settings) => this.settings.set(settings),
      (error: unknown) => this.error.set(message(error, 'Payroll settings could not be loaded.')),
    );
  }

  protected patch(change: Partial<PayrollSettings>): void {
    this.saved.set(false);
    this.settings.update((settings) => (settings ? { ...settings, ...change } : settings));
  }

  protected async save(): Promise<void> {
    const settings = this.settings();
    if (!settings) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      this.settings.set(await this.api.saveSettings(settings));
      this.saved.set(true);
    } catch (error) {
      this.error.set(message(error, 'Payroll settings could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}

