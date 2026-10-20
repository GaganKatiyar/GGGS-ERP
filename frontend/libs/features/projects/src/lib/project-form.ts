import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { ClientsApi, ContractsApi, ProjectsApi, addDaysIso, isoToday } from '@gggs/shared/delivery';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { LookupSelect } from '@gggs/shared/master-data';
import type {
  ClientItem,
  Confidentiality,
  ContractItem,
  DeliveryMethodology,
  ProjectBillingType,
  ProjectDetail,
  ProjectItem,
  ProjectStage,
  ProjectType,
  WorkPriority,
} from '@gggs/shared/models';
import { LOOKUP_LISTS, PROJECT_STAGES, PROJECT_TYPE_LABELS, STAGE_LABELS } from '@gggs/shared/models';
import { OrgSelect } from '@gggs/shared/org-reference';
import { FormDialog } from '@gggs/shared/ui';

/** Creating or editing a project's master record (spec §39). */
@Component({
  selector: 'gggs-project-form',
  imports: [ReactiveFormsModule, FormDialog, LookupSelect, EmployeeSelect, OrgSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog [(visible)]="visible" [title]="project() ? 'Edit ' + project()?.code : 'New project'" size="lg" [busy]="busy()" [error]="error()" (save)="save()">
      <div class="form-grid" [formGroup]="form">
        <label class="field">
          <span class="field__label">Code * <span class="muted">(prefixes task codes)</span></span>
          <input type="text" formControlName="code" maxlength="16" />
        </label>
        <label class="field">
          <span class="field__label">Name *</span>
          <input type="text" formControlName="name" maxlength="200" />
        </label>
        <label class="field">
          <span class="field__label">Type</span>
          <select formControlName="type">
            @for (type of types; track type) {
              <option [value]="type">{{ typeLabels[type] }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Category</span>
          <input type="text" formControlName="category" maxlength="100" placeholder="e.g. Digital commerce" />
        </label>
        <label class="field">
          <span class="field__label">Client</span>
          <select formControlName="clientId" (change)="onClient()">
            <option value="">Internal (no client)</option>
            @for (client of clients(); track client.id) {
              <option [value]="client.id">{{ client.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Contract</span>
          <select formControlName="contractId">
            <option value="">None</option>
            @for (contract of contracts(); track contract.id) {
              <option [value]="contract.id">{{ contract.number }} - {{ contract.title }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Project manager</span>
          <gggs-employee-select formControlName="projectManagerEmployeeId" ariaLabel="Project manager" />
        </label>
        <label class="field">
          <span class="field__label">Delivery manager</span>
          <gggs-employee-select formControlName="deliveryManagerEmployeeId" ariaLabel="Delivery manager" />
        </label>
        <label class="field">
          <span class="field__label">Account manager</span>
          <gggs-employee-select formControlName="accountManagerEmployeeId" ariaLabel="Account manager" />
        </label>
        <label class="field">
          <span class="field__label">Business unit</span>
          <gggs-org-select kind="business-units" ariaLabel="Business unit" formControlName="businessUnitId" />
        </label>
        <label class="field">
          <span class="field__label">Department</span>
          <gggs-org-select kind="departments" ariaLabel="Department" formControlName="departmentId" />
        </label>
        <label class="field">
          <span class="field__label">Location</span>
          <gggs-org-select kind="locations" ariaLabel="Location" formControlName="locationId" />
        </label>
        <label class="field">
          <span class="field__label">Starts *</span>
          <input type="date" formControlName="startDate" />
        </label>
        <label class="field">
          <span class="field__label">Ends (committed) *</span>
          <input type="date" formControlName="endDate" />
        </label>
        <label class="field">
          <span class="field__label">Forecast end</span>
          <input type="date" formControlName="plannedEndDate" />
        </label>
        @if (!project()) {
          <label class="field">
            <span class="field__label">Starting stage</span>
            <select formControlName="stage">
              @for (stage of stages; track stage) {
                <option [value]="stage">{{ stageLabels[stage] }}</option>
              }
            </select>
          </label>
        }
        <label class="field">
          <span class="field__label">Priority</span>
          <select formControlName="priority">
            <option value="Low">Low</option>
            <option value="Medium">Medium</option>
            <option value="High">High</option>
            <option value="Critical">Critical</option>
          </select>
        </label>
        <label class="field">
          <span class="field__label">Methodology</span>
          <select formControlName="methodology">
            <option value="Agile">Agile</option>
            <option value="Waterfall">Waterfall</option>
            <option value="Hybrid">Hybrid</option>
          </select>
        </label>
        <label class="field">
          <span class="field__label">Billing</span>
          <select formControlName="billingType">
            <option value="TimeAndMaterial">Time &amp; material</option>
            <option value="FixedPrice">Fixed price</option>
            <option value="Milestone">Milestone</option>
            <option value="Retainer">Retainer</option>
            <option value="NonBillable">Non-billable</option>
          </select>
        </label>
        <label class="field">
          <span class="field__label">Currency *</span>
          <gggs-lookup-select [list]="lists.currency" ariaLabel="Currency" formControlName="currency" />
        </label>
        <label class="field">
          <span class="field__label">Budget</span>
          <input type="number" min="0" step="1000" formControlName="budget" />
        </label>
        <label class="field">
          <span class="field__label">Revenue target</span>
          <input type="number" min="0" step="1000" formControlName="revenueTarget" />
        </label>
        <label class="field">
          <span class="field__label">Cost budget</span>
          <input type="number" min="0" step="1000" formControlName="costBudget" />
        </label>
        <label class="field">
          <span class="field__label">Work mode</span>
          <gggs-lookup-select [list]="lists.workMode" placeholder="Not set" ariaLabel="Work mode" formControlName="workModeCode" />
        </label>
        <label class="field">
          <span class="field__label">Confidentiality</span>
          <select formControlName="confidentiality">
            <option value="Public">Public</option>
            <option value="Internal">Internal</option>
            <option value="Confidential">Confidential</option>
            <option value="Restricted">Restricted</option>
          </select>
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Description</span>
          <textarea rows="3" formControlName="description" maxlength="4000"></textarea>
        </label>
      </div>
    </gggs-form-dialog>
  `,
})
export class ProjectForm {
  private readonly clientsApi = inject(ClientsApi);
  private readonly contractsApi = inject(ContractsApi);
  private readonly api = inject(ProjectsApi);

  readonly visible = model(false);
  readonly project = input<ProjectItem | null>(null);
  readonly saved = output<ProjectDetail>();

  protected readonly lists = LOOKUP_LISTS;
  protected readonly typeLabels = PROJECT_TYPE_LABELS;
  protected readonly stageLabels = STAGE_LABELS;
  protected readonly types = Object.keys(PROJECT_TYPE_LABELS) as ProjectType[];
  protected readonly stages = PROJECT_STAGES;
  protected readonly clients = signal<readonly ClientItem[]>([]);
  protected readonly contracts = signal<readonly ContractItem[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: Validators.required }),
    name: new FormControl('', { nonNullable: true, validators: Validators.required }),
    type: new FormControl<ProjectType>('TimeAndMaterial', { nonNullable: true }),
    category: new FormControl<string | null>(null),
    clientId: new FormControl<string>('', { nonNullable: true }),
    contractId: new FormControl<string>('', { nonNullable: true }),
    projectManagerEmployeeId: new FormControl<string | null>(null),
    deliveryManagerEmployeeId: new FormControl<string | null>(null),
    accountManagerEmployeeId: new FormControl<string | null>(null),
    businessUnitId: new FormControl<string | null>(null),
    departmentId: new FormControl<string | null>(null),
    locationId: new FormControl<string | null>(null),
    startDate: new FormControl(isoToday(), { nonNullable: true, validators: Validators.required }),
    endDate: new FormControl(addDaysIso(isoToday(), 90), { nonNullable: true, validators: Validators.required }),
    plannedEndDate: new FormControl<string | null>(null),
    stage: new FormControl<ProjectStage>('Planning', { nonNullable: true }),
    priority: new FormControl<WorkPriority>('Medium', { nonNullable: true }),
    methodology: new FormControl<DeliveryMethodology>('Agile', { nonNullable: true }),
    billingType: new FormControl<ProjectBillingType>('TimeAndMaterial', { nonNullable: true }),
    currency: new FormControl<string | null>('INR', { validators: Validators.required }),
    budget: new FormControl<number | null>(null),
    revenueTarget: new FormControl<number | null>(null),
    costBudget: new FormControl<number | null>(null),
    workModeCode: new FormControl<string | null>(null),
    confidentiality: new FormControl<Confidentiality>('Internal', { nonNullable: true }),
    description: new FormControl<string | null>(null),
  });

  constructor() {
    effect(() => {
      if (!this.visible()) {
        return;
      }

      const project = this.project();
      this.error.set(null);
      this.form.reset({
        code: project?.code ?? '',
        name: project?.name ?? '',
        type: project?.type ?? 'TimeAndMaterial',
        category: project?.category ?? null,
        clientId: project?.clientId ?? '',
        contractId: project?.contractId ?? '',
        projectManagerEmployeeId: project?.projectManagerEmployeeId ?? null,
        deliveryManagerEmployeeId: project?.deliveryManagerEmployeeId ?? null,
        accountManagerEmployeeId: project?.accountManagerEmployeeId ?? null,
        businessUnitId: project?.businessUnitId ?? null,
        departmentId: project?.departmentId ?? null,
        locationId: project?.locationId ?? null,
        startDate: project?.startDate ?? isoToday(),
        endDate: project?.endDate ?? addDaysIso(isoToday(), 90),
        plannedEndDate: project?.plannedEndDate ?? null,
        stage: 'Planning',
        priority: project?.priority ?? 'Medium',
        methodology: project?.methodology ?? 'Agile',
        billingType: project?.billingType ?? 'TimeAndMaterial',
        currency: project?.currency ?? 'INR',
        budget: project?.budget ?? null,
        revenueTarget: project?.revenueTarget ?? null,
        costBudget: project?.costBudget ?? null,
        workModeCode: project?.workModeCode ?? null,
        confidentiality: project?.confidentiality ?? 'Internal',
        description: project?.description ?? null,
      });
      if (project) {
        this.form.controls.code.disable();
      } else {
        this.form.controls.code.enable();
      }

      void this.clientsApi.active().then((clients) => this.clients.set(clients), () => this.clients.set([]));
      void this.loadContracts(this.form.controls.clientId.value);
    });
  }

  protected onClient(): void {
    this.form.patchValue({ contractId: '' });
    void this.loadContracts(this.form.controls.clientId.value);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.error.set('Give the project a code, a name, dates and a currency.');
      return;
    }

    const value = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    try {
      const saved = await this.api.save(this.project()?.id ?? null, {
        code: value.code.trim(),
        name: value.name.trim(),
        type: value.type,
        category: value.category,
        clientId: value.clientId || null,
        contractId: value.contractId || null,
        projectManagerEmployeeId: value.projectManagerEmployeeId,
        deliveryManagerEmployeeId: value.deliveryManagerEmployeeId,
        accountManagerEmployeeId: value.accountManagerEmployeeId,
        businessUnitId: value.businessUnitId,
        departmentId: value.departmentId,
        locationId: value.locationId,
        startDate: value.startDate,
        endDate: value.endDate,
        plannedEndDate: value.plannedEndDate || null,
        priority: value.priority,
        billingType: value.billingType,
        currency: value.currency ?? 'INR',
        budget: value.budget,
        revenueTarget: value.revenueTarget,
        costBudget: value.costBudget,
        description: value.description,
        workModeCode: value.workModeCode,
        confidentiality: value.confidentiality,
        methodology: value.methodology,
        stage: this.project() ? null : value.stage,
      });
      this.visible.set(false);
      this.saved.emit(saved);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The project could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  private async loadContracts(clientId: string): Promise<void> {
    if (!clientId) {
      this.contracts.set([]);
      return;
    }

    try {
      const page = await this.contractsApi.list({ clientId, pageSize: 200 });
      this.contracts.set(page.items.filter((contract) => contract.status !== 'Terminated' && contract.status !== 'Expired'));
    } catch {
      this.contracts.set([]);
    }
  }
}
