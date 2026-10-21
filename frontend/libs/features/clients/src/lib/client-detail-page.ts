import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { ClientsApi, ContractsApi, ProjectsApi } from '@gggs/shared/delivery';
import { RecordDocuments } from '@gggs/shared/documents';
import type { ClientDetail, ContactItem, ContractDetail, ContractItem, ProjectItem } from '@gggs/shared/models';
import { CONTRACT_TYPE_LABELS } from '@gggs/shared/models';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge, Tabs, type TabItem } from '@gggs/shared/ui';
import { ClientForm } from './client-form';
import { ContractForm } from './contract-form';

/** One client: details, contacts, contracts, projects and documents. */
@Component({
  selector: 'gggs-client-detail-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, KpiCard, Tabs, StatusBadge, EmptyState, Skeleton, FormDialog, RecordDocuments, ClientForm, ContractForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (detail(); as d) {
      <gggs-page-header [title]="d.client.name" [subtitle]="d.client.code + (d.client.industryName ? ' · ' + d.client.industryName : '')">
        <div class="toolbar">
          <gggs-status-badge [status]="d.client.status" />
          <a class="btn" routerLink="/clients">All clients</a>
          @if (canManage) {
            <button type="button" class="btn" (click)="formOpen.set(true)">Edit</button>
          }
        </div>
      </gggs-page-header>

      <div class="kpi-row client__kpis">
        <gggs-kpi-card label="Active contracts" [value]="activeContracts()" icon="pi pi-file-check" />
        <gggs-kpi-card label="Open projects" [value]="openProjects()" icon="pi pi-folder" />
        <gggs-kpi-card label="Contacts" [value]="d.contacts.length" icon="pi pi-users" />
        <gggs-kpi-card label="Account manager" [value]="d.client.accountManagerName ?? '—'" icon="pi pi-user" />
      </div>

      <gggs-tabs [tabs]="tabs" [(selected)]="tab" />

      <div class="client__body">
        @switch (tab()) {
          @case ('overview') {
            <section class="panel">
              <dl class="client__facts">
                <dt>Legal name</dt>
                <dd>{{ d.client.legalName ?? '—' }}</dd>
                <dt>Address</dt>
                <dd>{{ [d.client.address, d.client.city, d.client.postalCode].filter(present).join(', ') || '—' }}</dd>
                <dt>Website</dt>
                <dd>
                  @if (d.client.website) {
                    <a [href]="d.client.website" target="_blank" rel="noopener">{{ d.client.website }}</a>
                  } @else {
                    —
                  }
                </dd>
                <dt>Email</dt>
                <dd>{{ d.client.email ?? '—' }}</dd>
                <dt>Phone</dt>
                <dd>{{ d.client.phone ?? '—' }}</dd>
                <dt>Tax ID</dt>
                <dd>{{ d.client.taxId ?? '—' }}</dd>
                <dt>Client since</dt>
                <dd>{{ d.client.createdDate | date: 'mediumDate' }}</dd>
              </dl>
              @if (d.client.notes) {
                <p>{{ d.client.notes }}</p>
              }
            </section>
          }
          @case ('contacts') {
            <section class="panel stack">
              <div class="toolbar">
                <h2 class="client__heading">Contacts</h2>
                @if (canManageContacts) {
                  <button type="button" class="btn btn--primary btn--sm" (click)="editContact(null)">Add contact</button>
                }
              </div>
              @for (contact of d.contacts; track contact.id) {
                <div class="client__row">
                  <div>
                    <strong>{{ contact.name }}</strong>
                    @if (contact.isPrimary) {
                      <gggs-status-badge status="Primary" />
                    }
                    <div class="muted">{{ [contact.designation, contact.department].filter(present).join(' · ') }}</div>
                  </div>
                  <div>
                    @if (contact.email) {
                      <a [href]="'mailto:' + contact.email">{{ contact.email }}</a>
                    }
                    <div class="muted">{{ contact.phone }}</div>
                  </div>
                  @if (canManageContacts) {
                    <div class="client__actions">
                      <button type="button" class="btn btn--link btn--sm" (click)="editContact(contact)">Edit</button>
                      <button type="button" class="btn btn--link btn--sm" (click)="removeContact(contact)">Remove</button>
                    </div>
                  }
                </div>
              } @empty {
                <gggs-empty-state icon="pi pi-users" title="No contacts yet" />
              }
            </section>
          }
          @case ('contracts') {
            <section class="panel stack">
              <div class="toolbar">
                <h2 class="client__heading">Contracts</h2>
                @if (canManageContracts) {
                  <button type="button" class="btn btn--primary btn--sm" (click)="contractOpen.set(true)">New contract</button>
                }
              </div>
              @for (contract of contracts(); track contract.id) {
                <a class="client__row client__link" [routerLink]="['/contracts']" [queryParams]="{ open: contract.id }">
                  <div><strong>{{ contract.number }}</strong> {{ contract.title }} <span class="muted">{{ typeLabels[contract.type] }}</span></div>
                  <div class="muted">{{ contract.startDate | date: 'd MMM y' }} – {{ contract.endDate ? (contract.endDate | date: 'd MMM y') : 'open' }}</div>
                  <div>{{ contract.value === undefined ? '' : contract.currency + ' ' + (contract.value | number: '1.0-0') }}</div>
                  <gggs-status-badge [status]="contract.status" />
                </a>
              } @empty {
                <gggs-empty-state icon="pi pi-file" title="No contracts yet" />
              }
            </section>
          }
          @case ('projects') {
            <section class="panel stack">
              @for (project of projects(); track project.id) {
                <a class="client__row client__link" [routerLink]="['/projects', project.id]">
                  <div><strong>{{ project.code }}</strong> {{ project.name }}</div>
                  <div class="muted">{{ project.projectManagerName ?? 'No manager' }}</div>
                  <div>{{ project.progressPercent | number: '1.0-0' }}% done</div>
                  <gggs-status-badge [status]="project.healthStatus" />
                </a>
              } @empty {
                <gggs-empty-state icon="pi pi-folder" title="No projects for this client" />
              }
            </section>
          }
          @case ('documents') {
            <gggs-record-documents ownerType="Client" [ownerId]="d.client.id" [canUpload]="canManage" [categories]="['Contract', 'Other', 'Project']" />
          }
        }
        @if (error()) {
          <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
        }
      </div>

      <gggs-client-form [(visible)]="formOpen" [client]="d.client" (saved)="detail.set($event)" />
      <gggs-contract-form [(visible)]="contractOpen" [clientId]="d.client.id" (saved)="contractSaved($event)" />
    } @else {
      <gggs-skeleton [lines]="6" />
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    }

    <gggs-form-dialog [(visible)]="contactOpen" [title]="editingContact() ? 'Edit contact' : 'Add a contact'" [busy]="busy()" [error]="dialogError()" (save)="saveContact()">
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Name *</span>
          <input type="text" maxlength="200" [value]="contactName()" (input)="contactName.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Designation</span>
          <input type="text" maxlength="100" [value]="contactDesignation()" (input)="contactDesignation.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Email</span>
          <input type="email" maxlength="256" [value]="contactEmail()" (input)="contactEmail.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Phone</span>
          <input type="tel" maxlength="32" [value]="contactPhone()" (input)="contactPhone.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Department</span>
          <input type="text" maxlength="100" [value]="contactDepartment()" (input)="contactDepartment.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span><input type="checkbox" [checked]="contactPrimary()" (change)="contactPrimary.set($any($event.target).checked)" /> Primary contact</span>
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .client__kpis {
      margin-block-end: var(--gggs-space-4);
    }

    .kpi-row {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(9rem, 45%), 1fr));
      gap: var(--gggs-space-3);
    }

    .client__body {
      margin-block-start: var(--gggs-space-4);
    }

    .client__heading {
      margin-inline-end: auto;
      margin-block: 0;
      font-size: var(--gggs-font-size-md);
    }

    .client__facts {
      display: grid;
      grid-template-columns: 10rem 1fr;
      gap: var(--gggs-space-2);
      margin: 0;
    }

    .client__facts dt {
      color: var(--gggs-color-text-muted);
    }

    .client__facts dd {
      margin: 0;
    }

    .client__row {
      display: grid;
      grid-template-columns: 2fr 1.5fr 1fr auto;
      gap: var(--gggs-space-3);
      align-items: center;
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .client__link {
      color: inherit;
      text-decoration: none;
    }

    .client__link:hover {
      background: var(--gggs-color-surface-hover, transparent);
    }

    .client__actions {
      white-space: nowrap;
    }
  `,
})
export class ClientDetailPage {
  private readonly api = inject(ClientsApi);
  private readonly contractsApi = inject(ContractsApi);
  private readonly projectsApi = inject(ProjectsApi);
  private readonly permissions = inject(PermissionStore);

  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  protected readonly typeLabels = CONTRACT_TYPE_LABELS;
  protected readonly canManage = this.permissions.has('client.client.manage');
  protected readonly canManageContacts = this.permissions.has('client.contact.manage');
  protected readonly canManageContracts = this.permissions.has('contract.contract.manage');
  protected readonly tabs: readonly TabItem[] = [
    { key: 'overview', label: 'Overview' },
    { key: 'contacts', label: 'Contacts' },
    { key: 'contracts', label: 'Contracts' },
    { key: 'projects', label: 'Projects' },
    { key: 'documents', label: 'Documents' },
  ];

  protected readonly tab = signal('overview');
  protected readonly detail = signal<ClientDetail | null>(null);
  protected readonly contracts = signal<readonly ContractItem[]>([]);
  protected readonly projects = signal<readonly ProjectItem[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly formOpen = signal(false);
  protected readonly contractOpen = signal(false);

  protected readonly contactOpen = signal(false);
  protected readonly editingContact = signal<ContactItem | null>(null);
  protected readonly contactName = signal('');
  protected readonly contactDesignation = signal('');
  protected readonly contactEmail = signal('');
  protected readonly contactPhone = signal('');
  protected readonly contactDepartment = signal('');
  protected readonly contactPrimary = signal(false);
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  protected readonly present = (value: string | undefined | null): boolean => !!value;

  constructor() {
    effect(() => {
      void this.load(this.id());
    });
  }

  protected activeContracts(): number {
    return this.contracts().filter((contract) => contract.status === 'Active').length;
  }

  protected openProjects(): number {
    return this.projects().filter((project) => project.status === 'Active' || project.status === 'OnHold').length;
  }

  protected editContact(contact: ContactItem | null): void {
    this.editingContact.set(contact);
    this.contactName.set(contact?.name ?? '');
    this.contactDesignation.set(contact?.designation ?? '');
    this.contactEmail.set(contact?.email ?? '');
    this.contactPhone.set(contact?.phone ?? '');
    this.contactDepartment.set(contact?.department ?? '');
    this.contactPrimary.set(contact?.isPrimary ?? false);
    this.dialogError.set(null);
    this.contactOpen.set(true);
  }

  protected async saveContact(): Promise<void> {
    if (!this.contactName().trim()) {
      this.dialogError.set('Give the contact a name.');
      return;
    }

    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await this.api.saveContact(this.id(), this.editingContact()?.id ?? null, {
        name: this.contactName().trim(),
        designation: this.contactDesignation() || null,
        email: this.contactEmail() || null,
        phone: this.contactPhone() || null,
        department: this.contactDepartment() || null,
        isPrimary: this.contactPrimary(),
      });
      this.contactOpen.set(false);
      await this.load(this.id());
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The contact could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async removeContact(contact: ContactItem): Promise<void> {
    try {
      await this.api.removeContact(this.id(), contact.id);
      await this.load(this.id());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The contact could not be removed.');
    }
  }

  protected async contractSaved(saved: ContractDetail): Promise<void> {
    this.contracts.update((list) => [saved.contract, ...list.filter((item) => item.id !== saved.contract.id)]);
  }

  private async load(id: string): Promise<void> {
    this.error.set(null);
    try {
      const [detail, contracts, projects] = await Promise.all([
        this.api.get(id),
        this.contractsApi.list({ clientId: id, pageSize: 200 }).catch(() => null),
        this.projectsApi.list({ clientId: id, includeClosed: true, pageSize: 200 }).catch(() => null),
      ]);
      this.detail.set(detail);
      this.contracts.set(contracts?.items ?? []);
      this.projects.set(projects?.items ?? []);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The client could not be loaded.');
    }
  }
}
