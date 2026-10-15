import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { ClientsApi, ContractsApi, isoToday } from '@gggs/shared/delivery';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { LookupSelect } from '@gggs/shared/master-data';
import type { BillingModel, ClientItem, ContactItem, ContractDetail, ContractItem, ContractType } from '@gggs/shared/models';
import { BILLING_MODEL_LABELS, CONTRACT_TYPE_LABELS, LOOKUP_LISTS } from '@gggs/shared/models';
import { FormDialog } from '@gggs/shared/ui';

/** Drafting or amending a contract. Signed contracts keep their type and currency; the server says so if asked otherwise. */
@Component({
  selector: 'gggs-contract-form',
  imports: [ReactiveFormsModule, FormDialog, LookupSelect, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog [(visible)]="visible" [title]="contract() ? 'Edit ' + contract()?.number : 'New contract'" width="min(52rem, 96vw)" [busy]="busy()" [error]="error()" (save)="save()">
      <div class="form-grid" [formGroup]="form">
        <label class="field">
          <span class="field__label">Client *</span>
          <select formControlName="clientId" (change)="onClient()">
            <option value="">Choose a client</option>
            @for (client of clients(); track client.id) {
              <option [value]="client.id">{{ client.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Number <span class="muted">(empty: numbered for you)</span></span>
          <input type="text" formControlName="number" maxlength="40" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Title *</span>
          <input type="text" formControlName="title" maxlength="200" />
        </label>
        <label class="field">
          <span class="field__label">Type *</span>
          <select formControlName="type">
            @for (type of types; track type) {
              <option [value]="type">{{ typeLabels[type] }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Under MSA</span>
          <select formControlName="parentContractId">
            <option value="">None</option>
            @for (msa of msas(); track msa.id) {
              <option [value]="msa.id">{{ msa.number }} - {{ msa.title }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Starts *</span>
          <input type="date" formControlName="startDate" />
        </label>
        <label class="field">
          <span class="field__label">Ends <span class="muted">(empty: open-ended)</span></span>
          <input type="date" formControlName="endDate" />
        </label>
        <label class="field">
          <span class="field__label">Currency *</span>
          <gggs-lookup-select [list]="lists.currency" ariaLabel="Currency" formControlName="currency" />
        </label>
        <label class="field">
          <span class="field__label">Value</span>
          <input type="number" min="0" step="1000" formControlName="value" />
        </label>
        <label class="field">
          <span class="field__label">Billing model</span>
          <select formControlName="billingModel">
            @for (model of billingModels; track model) {
              <option [value]="model">{{ billingLabels[model] }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Payment terms (days)</span>
          <input type="number" min="0" max="365" formControlName="paymentTermsDays" />
        </label>
        <label class="field">
          <span class="field__label">Renewal due</span>
          <input type="date" formControlName="renewalDate" />
        </label>
        <label class="field">
          <span><input type="checkbox" formControlName="autoRenew" /> Renews itself for the same term</span>
        </label>
        <label class="field">
          <span class="field__label">Owner</span>
          <gggs-employee-select formControlName="ownerEmployeeId" ariaLabel="Owner" />
        </label>
        <label class="field">
          <span class="field__label">Client signatory</span>
          <select formControlName="clientContactId">
            <option value="">Not set</option>
            @for (contact of contacts(); track contact.id) {
              <option [value]="contact.id">{{ contact.name }}{{ contact.designation ? ', ' + contact.designation : '' }}</option>
            }
          </select>
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Payment terms</span>
          <input type="text" formControlName="paymentTerms" maxlength="500" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Tax terms</span>
          <input type="text" formControlName="taxTerms" maxlength="500" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Notes</span>
          <textarea rows="2" formControlName="notes" maxlength="2000"></textarea>
        </label>
      </div>
    </gggs-form-dialog>
  `,
})
export class ContractForm {
  private readonly clientsApi = inject(ClientsApi);
  private readonly api = inject(ContractsApi);

  readonly visible = model(false);
  readonly contract = input<ContractItem | null>(null);

  /** Pre-selects the client when drafting from a client's page. */
  readonly clientId = input<string | null>(null);
  readonly saved = output<ContractDetail>();

  protected readonly lists = LOOKUP_LISTS;
  protected readonly typeLabels = CONTRACT_TYPE_LABELS;
  protected readonly billingLabels = BILLING_MODEL_LABELS;
  protected readonly types = Object.keys(CONTRACT_TYPE_LABELS) as ContractType[];
  protected readonly billingModels = Object.keys(BILLING_MODEL_LABELS) as BillingModel[];
  protected readonly clients = signal<readonly ClientItem[]>([]);
  protected readonly contacts = signal<readonly ContactItem[]>([]);
  protected readonly clientContracts = signal<readonly ContractItem[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly msas = computed(() => this.clientContracts().filter((item) => item.type === 'Msa' && item.id !== this.contract()?.id));

  protected readonly form = new FormGroup({
    clientId: new FormControl('', { nonNullable: true, validators: Validators.required }),
    number: new FormControl<string | null>(null),
    title: new FormControl('', { nonNullable: true, validators: Validators.required }),
    type: new FormControl<ContractType>('Sow', { nonNullable: true }),
    parentContractId: new FormControl<string>('', { nonNullable: true }),
    startDate: new FormControl(isoToday(), { nonNullable: true, validators: Validators.required }),
    endDate: new FormControl<string | null>(null),
    currency: new FormControl<string | null>('INR', { validators: Validators.required }),
    value: new FormControl<number | null>(null),
    billingModel: new FormControl<BillingModel>('TimeAndMaterial', { nonNullable: true }),
    paymentTermsDays: new FormControl<number | null>(30),
    renewalDate: new FormControl<string | null>(null),
    autoRenew: new FormControl(false, { nonNullable: true }),
    ownerEmployeeId: new FormControl<string | null>(null),
    clientContactId: new FormControl<string>('', { nonNullable: true }),
    paymentTerms: new FormControl<string | null>(null),
    taxTerms: new FormControl<string | null>(null),
    notes: new FormControl<string | null>(null),
  });

  constructor() {
    effect(() => {
      if (!this.visible()) {
        return;
      }

      const contract = this.contract();
      this.error.set(null);
      this.form.reset({
        clientId: contract?.clientId ?? this.clientId() ?? '',
        number: contract?.number ?? null,
        title: contract?.title ?? '',
        type: contract?.type ?? 'Sow',
        parentContractId: contract?.parentContractId ?? '',
        startDate: contract?.startDate ?? isoToday(),
        endDate: contract?.endDate ?? null,
        currency: contract?.currency ?? 'INR',
        value: contract?.value ?? null,
        billingModel: contract?.billingModel ?? 'TimeAndMaterial',
        paymentTermsDays: contract?.paymentTermsDays ?? 30,
        renewalDate: contract?.renewalDate ?? null,
        autoRenew: contract?.autoRenew ?? false,
        ownerEmployeeId: contract?.ownerEmployeeId ?? null,
        clientContactId: contract?.clientContactId ?? '',
        paymentTerms: contract?.paymentTerms ?? null,
        taxTerms: contract?.taxTerms ?? null,
        notes: contract?.notes ?? null,
      });
      if (contract) {
        this.form.controls.clientId.disable();
        this.form.controls.number.disable();
      } else {
        this.form.controls.clientId.enable();
        this.form.controls.number.enable();
      }

      void this.clientsApi.active().then((clients) => this.clients.set(clients));
      void this.loadClientDetails(this.form.controls.clientId.value);
    });
  }

  protected onClient(): void {
    this.form.patchValue({ parentContractId: '', clientContactId: '' });
    void this.loadClientDetails(this.form.controls.clientId.value);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.error.set('Choose the client, give the contract a title, a start date and a currency.');
      return;
    }

    const value = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    try {
      const saved = await this.api.save(this.contract()?.id ?? null, {
        number: value.number?.trim() || null,
        clientId: value.clientId,
        title: value.title.trim(),
        type: value.type,
        parentContractId: value.parentContractId || null,
        startDate: value.startDate,
        endDate: value.endDate || null,
        currency: value.currency ?? 'INR',
        value: value.value,
        billingModel: value.billingModel,
        paymentTermsDays: value.paymentTermsDays,
        paymentTerms: value.paymentTerms,
        taxTerms: value.taxTerms,
        renewalDate: value.renewalDate || null,
        autoRenew: value.autoRenew,
        ownerEmployeeId: value.ownerEmployeeId,
        clientContactId: value.clientContactId || null,
        notes: value.notes,
      });
      this.visible.set(false);
      this.saved.emit(saved);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The contract could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  private async loadClientDetails(clientId: string): Promise<void> {
    if (!clientId) {
      this.contacts.set([]);
      this.clientContracts.set([]);
      return;
    }

    const [detail, contracts] = await Promise.all([this.clientsApi.get(clientId), this.api.list({ clientId, pageSize: 200 })]);
    this.contacts.set(detail.contacts);
    this.clientContracts.set(contracts.items);
  }
}
