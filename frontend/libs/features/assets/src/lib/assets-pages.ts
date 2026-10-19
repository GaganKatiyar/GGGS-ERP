import { DatePipe, DecimalPipe, KeyValuePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import { AssetsApi } from '@gggs/shared/experience';
import type { AssetAction, AssetCategoryInput, AssetCategoryItem, AssetCondition, AssetDashboard, AssetDetailsInput, AssetItem, AssetRequestItem, AssetStatus, AssetView } from '@gggs/shared/models';
import { ASSET_CONDITIONS, ASSET_STATUSES, humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const num = (value: string): number | null => (value === '' ? null : +value);

const blankAsset = (): AssetDetailsInput => ({
  categoryId: '',
  name: '',
  make: null,
  model: null,
  serialNumber: null,
  purchaseDate: null,
  purchaseCost: null,
  vendor: null,
  warrantyUntil: null,
  locationId: null,
  notes: null,
});

/** The asset register: stock and assignments at a glance, the register itself, requests and categories. */
@Component({
  selector: 'gggs-assets-page',
  imports: [FormsModule, RouterLink, DecimalPipe, DatePipe, KeyValuePipe, PageHeader, KpiCard, StatusBadge, EmptyState, FormDialog, Skeleton, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Assets" subtitle="Laptops, phones, cards and furniture through their whole life: stock, assignment, repair and retirement.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="openRegister()"><i class="pi pi-plus" aria-hidden="true"></i> Register asset</button>
      }
    </gggs-page-header>
    @if (dashboard(); as d) {
      <div class="xp__kpis">
        <gggs-kpi-card label="Assets" [value]="d.total" />
        <gggs-kpi-card label="In stock" [value]="d.inStock" />
        <gggs-kpi-card label="Assigned" [value]="d.assigned" />
        <gggs-kpi-card label="In repair" [value]="d.inRepair" />
        <gggs-kpi-card label="Book value" [value]="(d.bookValue | number: '1.0-0') ?? '0'" />
        <gggs-kpi-card label="Warranty ending (60 days)" [value]="d.warrantyExpiring" />
        <gggs-kpi-card label="Open requests" [value]="d.openRequests" />
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="3" />
    }
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />
    <section class="panel stack">
      @if (tab() === 'register') {
        <div class="toolbar xp__filters">
          <label class="field">
            <span class="field__label">Search</span>
            <input type="search" placeholder="Tag, name or serial" [ngModel]="search()" (ngModelChange)="search.set($event)" (keyup.enter)="load()" />
          </label>
          <label class="field">
            <span class="field__label">Status</span>
            <select [ngModel]="status()" (ngModelChange)="status.set($event); load()">
              <option value="">Any</option>
              @for (option of statuses; track option) {
                <option [value]="option">{{ humanize(option) }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">Category</span>
            <select [ngModel]="categoryId()" (ngModelChange)="categoryId.set($event); load()">
              <option value="">Any</option>
              @for (category of categories(); track category.id) {
                <option [value]="category.id">{{ category.name }}</option>
              }
            </select>
          </label>
        </div>
        @for (asset of assets(); track asset.id) {
          <a class="xp__row" [routerLink]="['/assets', asset.id]">
            <span><strong>{{ asset.name }}</strong> <span class="muted">{{ asset.tag }}</span></span>
            <span>{{ asset.categoryName }}{{ asset.serialNumber ? ' · ' + asset.serialNumber : '' }}</span>
            <span>{{ asset.assignedName ?? '' }}</span>
            <span class="muted">book {{ asset.bookValue | number: '1.0-0' }}{{ asset.warrantyExpiringSoon ? ' · warranty ending' : '' }}</span>
            <gggs-status-badge [status]="asset.status" />
          </a>
        } @empty {
          <gggs-empty-state icon="pi pi-desktop" title="No assets match" />
        }
      } @else if (tab() === 'requests') {
        @for (request of requests(); track request.id) {
          <div class="xp__row">
            <span><strong>{{ request.employeeName }}</strong> <span class="muted">{{ request.categoryName }}</span></span>
            <span>{{ request.reason }}</span>
            <span class="muted">{{ request.createdDate | date: 'd MMM y' }}{{ request.assetTag ? ' · ' + request.assetTag : '' }}{{ request.response ? ' · ' + request.response : '' }}</span>
            <span class="toolbar">
              @if (canManage && request.status === 'Requested') {
                <button type="button" class="btn btn--sm" (click)="openFulfil(request)">Fulfil</button>
                <button type="button" class="btn btn--link btn--sm" (click)="reject(request)">Reject</button>
              }
            </span>
            <gggs-status-badge [status]="request.status" />
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-inbox" title="No requests" />
        }
      } @else {
        @if (canManageCategories) {
          <div class="toolbar"><button type="button" class="btn" (click)="editCategory(null)">New category</button></div>
        }
        @for (category of categories(); track category.id) {
          <div class="xp__row">
            <span><strong>{{ category.name }}</strong> <span class="muted">{{ category.code }}</span></span>
            <span>{{ category.depreciationYears ? category.depreciationYears + ' years straight-line' : 'Not depreciated' }}</span>
            <span class="muted">{{ category.assets }} assets · {{ category.inStock }} in stock</span>
            <span>
              @if (canManageCategories) {
                <button type="button" class="btn btn--link btn--sm" (click)="editCategory(category)">Edit</button>
              }
            </span>
            <gggs-status-badge [status]="category.isActive ? 'Active' : 'Inactive'" />
          </div>
        }
        @if (dashboard(); as d) {
          <h2 class="xp__heading">By category</h2>
          @for (entry of d.byCategory | keyvalue; track entry.key) {
            <div class="xp__flag"><strong>{{ entry.value }}</strong> {{ entry.key }}</div>
          }
        }
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="registerOpen" title="Register asset" [busy]="busy()" [error]="formError()" [saveDisabled]="!form().categoryId || !form().name.trim()" (save)="register()" width="min(48rem, 96vw)">
      <div class="xp__grid">
        <label class="field">
          <span class="field__label">Category</span>
          <select [value]="form().categoryId" (change)="patch({ categoryId: $any($event.target).value })">
            <option value="">Choose</option>
            @for (category of categories(); track category.id) {
              <option [value]="category.id">{{ category.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="150" [value]="form().name" (input)="patch({ name: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Make</span>
          <input type="text" maxlength="100" [value]="form().make ?? ''" (input)="patch({ make: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Model</span>
          <input type="text" maxlength="100" [value]="form().model ?? ''" (input)="patch({ model: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Serial number</span>
          <input type="text" maxlength="100" [value]="form().serialNumber ?? ''" (input)="patch({ serialNumber: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Vendor</span>
          <input type="text" maxlength="150" [value]="form().vendor ?? ''" (input)="patch({ vendor: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Purchased on</span>
          <input type="date" [value]="form().purchaseDate ?? ''" (change)="patch({ purchaseDate: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Cost</span>
          <input type="number" min="0" [value]="form().purchaseCost ?? ''" (input)="patch({ purchaseCost: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Warranty until</span>
          <input type="date" [value]="form().warrantyUntil ?? ''" (change)="patch({ warrantyUntil: $any($event.target).value || null })" />
        </label>
        <label class="field">
          <span class="field__label">Condition</span>
          <select [value]="condition()" (change)="condition.set($any($event.target).value)">
            @for (option of conditions; track option) {
              <option [value]="option">{{ option }}</option>
            }
          </select>
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="fulfilOpen" title="Fulfil request" saveLabel="Assign" [busy]="busy()" [error]="formError()" [saveDisabled]="!fulfilAssetId()" (save)="fulfil()">
      <label class="field">
        <span class="field__label">Asset from stock</span>
        <select [value]="fulfilAssetId()" (change)="fulfilAssetId.set($any($event.target).value)">
          <option value="">Choose</option>
          @for (asset of stock(); track asset.id) {
            <option [value]="asset.id">{{ asset.tag }} · {{ asset.name }}</option>
          }
        </select>
      </label>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="categoryOpen" [title]="categoryEditing() ? 'Edit category' : 'New category'" [busy]="busy()" [error]="formError()" [saveDisabled]="!categoryForm().code.trim() || !categoryForm().name.trim()" (save)="saveCategory()">
      <div class="xp__grid">
        <label class="field">
          <span class="field__label">Code</span>
          <input type="text" maxlength="30" [value]="categoryForm().code" (input)="patchCategory({ code: $any($event.target).value.toUpperCase() })" />
        </label>
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" maxlength="100" [value]="categoryForm().name" (input)="patchCategory({ name: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Depreciation (years)</span>
          <input type="number" min="0" max="20" [value]="categoryForm().depreciationYears" (input)="patchCategory({ depreciationYears: +$any($event.target).value })" />
        </label>
        <label class="xp__flag"><input type="checkbox" [checked]="categoryForm().requiresSerial" (change)="patchCategory({ requiresSerial: $any($event.target).checked })" /> Needs a serial number</label>
        <label class="xp__flag"><input type="checkbox" [checked]="categoryForm().isActive" (change)="patchCategory({ isActive: $any($event.target).checked })" /> Active</label>
      </div>
    </gggs-form-dialog>
  `,
  styleUrl: './xp.css',
})
export class AssetsPage {
  private readonly api = inject(AssetsApi);
  private readonly permissions = inject(PermissionStore);

  protected readonly canManage = this.permissions.has('asset.asset.manage');
  protected readonly canManageCategories = this.permissions.has('asset.category.manage');
  protected readonly humanize = humanize;
  protected readonly num = num;
  protected readonly statuses = ASSET_STATUSES;
  protected readonly conditions = ASSET_CONDITIONS;
  protected readonly tabs: readonly TabItem[] = [
    { key: 'register', label: 'Register' },
    { key: 'requests', label: 'Requests' },
    { key: 'categories', label: 'Categories' },
  ];
  protected readonly tab = signal('register');
  protected readonly dashboard = signal<AssetDashboard | null>(null);
  protected readonly assets = signal<readonly AssetItem[]>([]);
  protected readonly categories = signal<readonly AssetCategoryItem[]>([]);
  protected readonly requests = signal<readonly AssetRequestItem[]>([]);
  protected readonly stock = signal<readonly AssetItem[]>([]);
  protected readonly search = signal('');
  protected readonly status = signal<AssetStatus | ''>('');
  protected readonly categoryId = signal('');
  protected readonly registerOpen = signal(false);
  protected readonly form = signal<AssetDetailsInput>(blankAsset());
  protected readonly condition = signal<AssetCondition>('New');
  protected readonly fulfilOpen = signal(false);
  protected readonly fulfilRequest = signal<AssetRequestItem | null>(null);
  protected readonly fulfilAssetId = signal('');
  protected readonly categoryOpen = signal(false);
  protected readonly categoryEditing = signal<AssetCategoryItem | null>(null);
  protected readonly categoryForm = signal<AssetCategoryInput>({ code: '', name: '', depreciationYears: 3, requiresSerial: true, isActive: true });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.dashboard().then((d) => this.dashboard.set(d), (error: unknown) => this.error.set(message(error, 'The overview could not be loaded.')));
    this.api.categories().then((c) => this.categories.set(c), () => undefined);
    this.api.requests().then((r) => this.requests.set(r), () => undefined);
    this.load();
  }

  protected load(): void {
    this.api
      .assets({ search: this.search() || null, status: this.status() || null, categoryId: this.categoryId() || null })
      .then((assets) => this.assets.set(assets), (error: unknown) => this.error.set(message(error, 'The register could not be loaded.')));
  }

  protected patch(change: Partial<AssetDetailsInput>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected patchCategory(change: Partial<AssetCategoryInput>): void {
    this.categoryForm.update((form) => ({ ...form, ...change }));
  }

  protected openRegister(): void {
    this.form.set(blankAsset());
    this.condition.set('New');
    this.formError.set(null);
    this.registerOpen.set(true);
  }

  protected async register(): Promise<void> {
    await this.run(async () => {
      await this.api.register(this.form(), this.condition());
      this.registerOpen.set(false);
      this.load();
      this.dashboard.set(await this.api.dashboard());
    });
  }

  protected async openFulfil(request: AssetRequestItem): Promise<void> {
    this.fulfilRequest.set(request);
    this.fulfilAssetId.set('');
    this.formError.set(null);
    this.stock.set(await this.api.assets({ status: 'InStock', categoryId: request.categoryId }));
    this.fulfilOpen.set(true);
  }

  protected async fulfil(): Promise<void> {
    const request = this.fulfilRequest();
    if (!request) {
      return;
    }

    await this.run(async () => {
      await this.api.assign(this.fulfilAssetId(), request.employeeId, request.id);
      this.fulfilOpen.set(false);
      this.requests.set(await this.api.requests());
      this.load();
    });
  }

  protected async reject(request: AssetRequestItem): Promise<void> {
    const response = prompt('Why is the request declined?');
    if (response) {
      try {
        this.requests.set(await this.api.reject(request.id, response));
      } catch (error) {
        this.error.set(message(error, 'The request could not be rejected.'));
      }
    }
  }

  protected editCategory(category: AssetCategoryItem | null): void {
    this.categoryEditing.set(category);
    this.categoryForm.set(category ? { code: category.code, name: category.name, depreciationYears: category.depreciationYears, requiresSerial: category.requiresSerial, isActive: category.isActive } : { code: '', name: '', depreciationYears: 3, requiresSerial: true, isActive: true });
    this.formError.set(null);
    this.categoryOpen.set(true);
  }

  protected async saveCategory(): Promise<void> {
    await this.run(async () => {
      this.categories.set(await this.api.saveCategory(this.categoryEditing()?.id ?? null, this.categoryForm()));
      this.categoryOpen.set(false);
    });
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await work();
    } catch (error) {
      this.formError.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** One asset: its details, its holder, and every step of its life, with the actions its status allows. */
@Component({
  selector: 'gggs-asset-page',
  imports: [FormsModule, DatePipe, DecimalPipe, RouterLink, PageHeader, StatusBadge, FormDialog, Skeleton, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="v.asset.name" [subtitle]="v.asset.tag + ' · ' + v.asset.categoryName">
        <a class="btn" routerLink="/assets">All assets</a>
        @if (canManage) {
          @if (v.asset.status === 'InStock') {
            <button type="button" class="btn btn--primary" (click)="openDialog('assign')">Assign</button>
            <button type="button" class="btn" (click)="openDialog('repair')">Send for repair</button>
            <button type="button" class="btn" (click)="openDialog('retire')">Retire</button>
          }
          @if (v.asset.status === 'Assigned') {
            <button type="button" class="btn btn--primary" (click)="openDialog('return')">Return</button>
            <button type="button" class="btn" (click)="openDialog('lost')">Report lost</button>
          }
          @if (v.asset.status === 'InRepair') {
            <button type="button" class="btn btn--primary" (click)="openDialog('repaired')">Back from repair</button>
          }
          <button type="button" class="btn btn--link" (click)="openDialog('note')">Add note</button>
        }
      </gggs-page-header>
      <section class="panel xp__grid">
        <div><span class="muted">Status</span><br /><gggs-status-badge [status]="v.asset.status" /></div>
        <div><span class="muted">Condition</span><br />{{ v.asset.condition }}</div>
        <div><span class="muted">Held by</span><br />{{ v.asset.assignedName ?? '—' }}{{ v.asset.assignedOn ? ' since ' + (v.asset.assignedOn | date: 'd MMM y') : '' }}</div>
        <div><span class="muted">Make and model</span><br />{{ v.asset.make ?? '' }} {{ v.asset.model ?? '' }}</div>
        <div><span class="muted">Serial</span><br />{{ v.asset.serialNumber ?? '—' }}</div>
        <div><span class="muted">Purchased</span><br />{{ v.asset.purchaseDate ? (v.asset.purchaseDate | date: 'd MMM y') : '—' }}{{ v.asset.purchaseCost ? ' for ' + (v.asset.purchaseCost | number: '1.0-0') : '' }}</div>
        <div><span class="muted">Book value</span><br />{{ v.asset.bookValue | number: '1.0-0' }}</div>
        <div><span class="muted">Warranty</span><br />{{ v.asset.warrantyUntil ? (v.asset.warrantyUntil | date: 'd MMM y') : '—' }}</div>
      </section>
      <section class="panel stack">
        <h2 class="xp__heading">History</h2>
        @for (event of v.history; track event.id) {
          <div class="xp__row">
            <span><strong>{{ event.kind }}</strong></span>
            <span>{{ event.employeeName ?? '' }}</span>
            <span class="muted">{{ event.on | date: 'd MMM y' }}{{ event.byName ? ' by ' + event.byName : '' }}</span>
            <span>{{ event.cost ? (event.cost | number: '1.0-0') : '' }}</span>
            <span class="muted">{{ event.note ?? '' }}</span>
          </div>
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }

    <gggs-form-dialog [(visible)]="dialogOpen" [title]="dialogTitle()" [busy]="busy()" [error]="formError()" [saveDisabled]="action() === 'assign' && !employeeId()" (save)="act()">
      <div class="stack">
        @if (action() === 'assign') {
          <label class="field">
            <span class="field__label">Employee</span>
            <gggs-employee-select [ngModel]="employeeId()" (ngModelChange)="employeeId.set($event)" />
          </label>
        }
        @if (action() === 'return' || action() === 'repaired') {
          <label class="field">
            <span class="field__label">Condition</span>
            <select [value]="condition()" (change)="condition.set($any($event.target).value)">
              @for (option of conditions; track option) {
                <option [value]="option">{{ option }}</option>
              }
            </select>
          </label>
        }
        @if (action() === 'repaired') {
          <label class="field">
            <span class="field__label">Repair cost</span>
            <input type="number" min="0" [value]="cost() ?? ''" (input)="cost.set(num($any($event.target).value))" />
          </label>
        }
        @if (action() !== 'assign') {
          <label class="field">
            <span class="field__label">Note</span>
            <textarea rows="3" maxlength="500" [value]="note()" (input)="note.set($any($event.target).value)"></textarea>
          </label>
        }
      </div>
    </gggs-form-dialog>
  `,
  styleUrl: './xp.css',
})
export class AssetPage {
  private readonly api = inject(AssetsApi);

  readonly id = input.required<string>();

  protected readonly canManage = inject(PermissionStore).has('asset.asset.manage');
  protected readonly conditions = ASSET_CONDITIONS;
  protected readonly num = num;
  protected readonly view = signal<AssetView | null>(null);
  protected readonly action = signal<AssetAction | 'assign' | 'return'>('note');
  protected readonly dialogOpen = signal(false);
  protected readonly dialogTitle = signal('');
  protected readonly employeeId = signal<string | null>(null);
  protected readonly condition = signal<AssetCondition>('Good');
  protected readonly cost = signal<number | null>(null);
  protected readonly note = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.asset(this.id()).then(
        (view) => this.view.set(view),
        (error: unknown) => this.error.set(message(error, 'The asset could not be loaded.')),
      );
    });
  }

  protected openDialog(action: AssetAction | 'assign' | 'return'): void {
    const titles: Record<string, string> = { assign: 'Assign asset', return: 'Return asset', repair: 'Send for repair', repaired: 'Back from repair', retire: 'Retire asset', lost: 'Report lost', note: 'Add note' };
    this.action.set(action);
    this.dialogTitle.set(titles[action]);
    this.employeeId.set(null);
    this.condition.set('Good');
    this.cost.set(null);
    this.note.set('');
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected async act(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      const action = this.action();
      const note = this.note() || null;
      const view =
        action === 'assign'
          ? await this.api.assign(this.id(), this.employeeId() ?? '')
          : action === 'return'
            ? await this.api.return(this.id(), this.condition(), note)
            : await this.api.act(this.id(), action, { note, cost: this.cost(), condition: action === 'repaired' ? this.condition() : null });
      this.view.set(view);
      this.dialogOpen.set(false);
    } catch (error) {
      this.formError.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** What I hold, and asking for equipment. */
@Component({
  selector: 'gggs-my-assets-page',
  imports: [DatePipe, PageHeader, StatusBadge, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My assets" subtitle="Equipment assigned to you. Return it to IT or admin when you leave.">
      <button type="button" class="btn btn--primary" (click)="open()">Request equipment</button>
    </gggs-page-header>
    <section class="panel stack">
      @for (asset of assets(); track asset.id) {
        <div class="xp__row">
          <span><strong>{{ asset.name }}</strong> <span class="muted">{{ asset.tag }}</span></span>
          <span>{{ asset.categoryName }}{{ asset.serialNumber ? ' · ' + asset.serialNumber : '' }}</span>
          <span class="muted">since {{ asset.assignedOn | date: 'd MMM y' }}</span>
          <span>{{ asset.condition }}</span>
          <gggs-status-badge [status]="asset.status" />
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-desktop" title="Nothing assigned to you" />
      }
    </section>
    <section class="panel stack">
      <h2 class="xp__heading">My requests</h2>
      @for (request of requests(); track request.id) {
        <div class="xp__row">
          <span><strong>{{ request.categoryName }}</strong></span>
          <span>{{ request.reason }}</span>
          <span class="muted">{{ request.createdDate | date: 'd MMM y' }}{{ request.response ? ' · ' + request.response : '' }}</span>
          <span>
            @if (request.status === 'Requested') {
              <button type="button" class="btn btn--link btn--sm" (click)="cancel(request.id)">Cancel</button>
            }
          </span>
          <gggs-status-badge [status]="request.status" />
        </div>
      } @empty {
        <p class="muted">No requests.</p>
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="dialogOpen" title="Request equipment" saveLabel="Request" [busy]="busy()" [error]="formError()" [saveDisabled]="!categoryId() || !reason().trim()" (save)="request()">
      <div class="stack">
        <label class="field">
          <span class="field__label">What you need</span>
          <select [value]="categoryId()" (change)="categoryId.set($any($event.target).value)">
            <option value="">Choose</option>
            @for (category of categories(); track category.id) {
              <option [value]="category.id">{{ category.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Why</span>
          <textarea rows="3" maxlength="500" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea>
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styleUrl: './xp.css',
})
export class MyAssetsPage {
  private readonly api = inject(AssetsApi);

  protected readonly assets = signal<readonly AssetItem[]>([]);
  protected readonly requests = signal<readonly AssetRequestItem[]>([]);
  protected readonly categories = signal<readonly AssetCategoryItem[]>([]);
  protected readonly dialogOpen = signal(false);
  protected readonly categoryId = signal('');
  protected readonly reason = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.mine().then((assets) => this.assets.set(assets), (error: unknown) => this.error.set(message(error, 'Your assets could not be loaded.')));
    this.api.myRequests().then((requests) => this.requests.set(requests), () => undefined);
    this.api.categories().then((categories) => this.categories.set(categories.filter((category) => category.isActive)), () => undefined);
  }

  protected open(): void {
    this.categoryId.set('');
    this.reason.set('');
    this.formError.set(null);
    this.dialogOpen.set(true);
  }

  protected async request(): Promise<void> {
    this.busy.set(true);
    try {
      this.requests.set(await this.api.request(this.categoryId(), this.reason()));
      this.dialogOpen.set(false);
    } catch (error) {
      this.formError.set(message(error, 'The request could not be sent.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(id: string): Promise<void> {
    try {
      this.requests.set(await this.api.cancelRequest(id));
    } catch (error) {
      this.error.set(message(error, 'The request could not be cancelled.'));
    }
  }
}
