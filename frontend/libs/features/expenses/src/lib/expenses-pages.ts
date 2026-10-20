import { DatePipe, DecimalPipe, KeyValuePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { ExpensesApi } from '@gggs/shared/experience';
import type { ClaimItem, ClaimLineInput, ClaimStatus, ClaimView, ExpenseCategoryInput, ExpenseCategoryItem, ExpenseDashboard, PaymentMode, TravelDetailsInput, TravelItem, TravelStatus } from '@gggs/shared/models';
import { CLAIM_STATUSES, TRAVEL_MODES, TRAVEL_STATUSES, humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const today = () => new Date().toISOString().slice(0, 10);
const num = (value: string): number | null => (value === '' ? null : +value);

const blankTrip = (): TravelDetailsInput => ({ purpose: '', fromCity: '', toCity: '', departOn: today(), returnOn: today(), mode: 'Air', estimatedCost: 0, advanceRequested: 0, projectId: null });
const blankLine = (categoryId: string): ClaimLineInput => ({ categoryId, date: today(), description: '', amount: 0, merchant: null, hasReceipt: true, billable: false, justification: null });

/** Expenses overview: approvals and payments due, claims, travel and categories. */
@Component({
  selector: 'gggs-expenses-page',
  imports: [FormsModule, RouterLink, DatePipe, DecimalPipe, KeyValuePipe, PageHeader, KpiCard, StatusBadge, EmptyState, FormDialog, Skeleton, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Expenses and travel" subtitle="Claims checked against policy, approved through the workflow, and reimbursed through payroll or directly." />
    @if (dashboard(); as d) {
      <div class="xp__kpis">
        <gggs-kpi-card label="Awaiting approval" [value]="d.pendingApproval" [hint]="(d.pendingAmount | number: '1.0-0') + ' in all'" />
        <gggs-kpi-card label="Approved, unpaid" [value]="d.approvedUnpaid" [hint]="(d.approvedUnpaidAmount | number: '1.0-0') + ' to pay'" />
        <gggs-kpi-card label="Paid this month" [value]="(d.paidThisMonth | number: '1.0-0') ?? '0'" />
        <gggs-kpi-card label="Open travel advances" [value]="(d.openAdvances | number: '1.0-0') ?? '0'" />
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="3" />
    }
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />
    <section class="panel stack">
      @if (tab() === 'claims') {
        <div class="toolbar xp__filters">
          <label class="field">
            <span class="field__label">Status</span>
            <select [ngModel]="claimStatus()" (ngModelChange)="claimStatus.set($event); loadClaims()">
              <option value="">Any</option>
              @for (option of claimStatuses; track option) {
                <option [value]="option">{{ humanize(option) }}</option>
              }
            </select>
          </label>
        </div>
        @for (claim of claims(); track claim.id) {
          <a class="xp__row" [routerLink]="['/expenses/claims', claim.id]">
            <span><strong>{{ claim.title }}</strong> <span class="muted">{{ claim.number }}</span></span>
            <span>{{ claim.employeeName }}</span>
            <span>{{ claim.total | number: '1.2-2' }} {{ claim.currency }}{{ claim.policyFlags ? ' · ' + claim.policyFlags + ' outside policy' : '' }}</span>
            <span class="muted">{{ claim.submittedDate ? (claim.submittedDate | date: 'd MMM y') : 'not submitted' }}</span>
            <gggs-status-badge [status]="claim.status" />
          </a>
        } @empty {
          <gggs-empty-state icon="pi pi-receipt" title="No claims" />
        }
      } @else if (tab() === 'travel') {
        <div class="toolbar xp__filters">
          <label class="field">
            <span class="field__label">Status</span>
            <select [ngModel]="travelStatus()" (ngModelChange)="travelStatus.set($event); loadTrips()">
              <option value="">Any</option>
              @for (option of travelStatuses; track option) {
                <option [value]="option">{{ humanize(option) }}</option>
              }
            </select>
          </label>
        </div>
        @for (trip of trips(); track trip.id) {
          <a class="xp__row" [routerLink]="['/expenses/travel', trip.id]">
            <span><strong>{{ trip.fromCity }} → {{ trip.toCity }}</strong> <span class="muted">{{ trip.number }}</span></span>
            <span>{{ trip.employeeName }}</span>
            <span>{{ trip.departOn | date: 'd MMM' }} – {{ trip.returnOn | date: 'd MMM y' }}</span>
            <span class="muted">advance {{ (trip.advancePaid ?? 0) | number: '1.0-0' }} · claimed {{ trip.claimed | number: '1.0-0' }}</span>
            <gggs-status-badge [status]="trip.status" />
          </a>
        } @empty {
          <gggs-empty-state icon="pi pi-send" title="No travel requests" />
        }
      } @else {
        @if (canManageCategories) {
          <div class="toolbar"><button type="button" class="btn" (click)="editCategory(null)">New category</button></div>
        }
        @for (category of categories(); track category.id) {
          <div class="xp__row">
            <span><strong>{{ category.name }}</strong> <span class="muted">{{ category.code }}</span></span>
            <span>{{ category.limitPerLine ? 'Up to ' + (category.limitPerLine | number: '1.0-0') + ' a line' : 'No limit' }}</span>
            <span class="muted">receipt above {{ category.receiptRequiredAbove | number: '1.0-0' }}</span>
            <span>
              @if (canManageCategories) {
                <button type="button" class="btn btn--link btn--sm" (click)="editCategory(category)">Edit</button>
              }
            </span>
            <gggs-status-badge [status]="category.isActive ? 'Active' : 'Inactive'" />
          </div>
        }
        @if (dashboard(); as d) {
          <h2 class="xp__heading">Spent this month by category</h2>
          @for (entry of d.byCategoryThisMonth | keyvalue; track entry.key) {
            <div class="xp__flag"><strong>{{ entry.value | number: '1.0-0' }}</strong> {{ entry.key }}</div>
          } @empty {
            <p class="muted">Nothing claimed this month.</p>
          }
        }
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

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
          <span class="field__label">Limit per line (empty for none)</span>
          <input type="number" min="0" [value]="categoryForm().limitPerLine ?? ''" (input)="patchCategory({ limitPerLine: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Receipt needed above</span>
          <input type="number" min="0" [value]="categoryForm().receiptRequiredAbove" (input)="patchCategory({ receiptRequiredAbove: +$any($event.target).value })" />
        </label>
        <label class="xp__flag"><input type="checkbox" [checked]="categoryForm().isActive" (change)="patchCategory({ isActive: $any($event.target).checked })" /> Active</label>
      </div>
    </gggs-form-dialog>
  `,
  styleUrl: './xp.css',
})
export class ExpensesPage {
  private readonly api = inject(ExpensesApi);
  private readonly permissions = inject(PermissionStore);

  protected readonly canManageCategories = this.permissions.has('spend.category.manage');
  protected readonly humanize = humanize;
  protected readonly num = num;
  protected readonly claimStatuses = CLAIM_STATUSES;
  protected readonly travelStatuses = TRAVEL_STATUSES;
  protected readonly tabs: readonly TabItem[] = [
    { key: 'claims', label: 'Claims' },
    { key: 'travel', label: 'Travel' },
    { key: 'categories', label: 'Categories' },
  ];
  protected readonly tab = signal('claims');
  protected readonly dashboard = signal<ExpenseDashboard | null>(null);
  protected readonly claims = signal<readonly ClaimItem[]>([]);
  protected readonly trips = signal<readonly TravelItem[]>([]);
  protected readonly categories = signal<readonly ExpenseCategoryItem[]>([]);
  protected readonly claimStatus = signal<ClaimStatus | ''>('');
  protected readonly travelStatus = signal<TravelStatus | ''>('');
  protected readonly categoryOpen = signal(false);
  protected readonly categoryEditing = signal<ExpenseCategoryItem | null>(null);
  protected readonly categoryForm = signal<ExpenseCategoryInput>({ code: '', name: '', limitPerLine: null, receiptRequiredAbove: 500, isActive: true });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.api.dashboard().then((d) => this.dashboard.set(d), (error: unknown) => this.error.set(message(error, 'The overview could not be loaded.')));
    this.api.categories().then((c) => this.categories.set(c), () => undefined);
    this.loadClaims();
    this.loadTrips();
  }

  protected loadClaims(): void {
    this.api.claims({ status: this.claimStatus() || null }).then((claims) => this.claims.set(claims), (error: unknown) => this.error.set(message(error, 'Claims could not be loaded.')));
  }

  protected loadTrips(): void {
    this.api.trips(this.travelStatus() || null).then((trips) => this.trips.set(trips), () => undefined);
  }

  protected patchCategory(change: Partial<ExpenseCategoryInput>): void {
    this.categoryForm.update((form) => ({ ...form, ...change }));
  }

  protected editCategory(category: ExpenseCategoryItem | null): void {
    this.categoryEditing.set(category);
    this.categoryForm.set(
      category
        ? { code: category.code, name: category.name, limitPerLine: category.limitPerLine ?? null, receiptRequiredAbove: category.receiptRequiredAbove, isActive: category.isActive }
        : { code: '', name: '', limitPerLine: null, receiptRequiredAbove: 500, isActive: true },
    );
    this.formError.set(null);
    this.categoryOpen.set(true);
  }

  protected async saveCategory(): Promise<void> {
    this.busy.set(true);
    try {
      this.categories.set(await this.api.saveCategory(this.categoryEditing()?.id ?? null, this.categoryForm()));
      this.categoryOpen.set(false);
    } catch (error) {
      this.formError.set(message(error, 'The category could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** One claim with its lines and policy flags; finance pays it from here. */
@Component({
  selector: 'gggs-claim-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, StatusBadge, FormDialog, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <gggs-page-header [title]="v.claim.title" [subtitle]="v.claim.number + ' · ' + (v.claim.employeeName ?? '')">
        <a class="btn" routerLink="/expenses">All claims</a>
        @if (canPay && v.claim.status === 'Approved') {
          <button type="button" class="btn btn--primary" (click)="payOpen.set(true)">Pay</button>
        }
      </gggs-page-header>
      <section class="panel xp__grid">
        <div><span class="muted">Status</span><br /><gggs-status-badge [status]="v.claim.status" /></div>
        <div><span class="muted">Total</span><br /><strong>{{ v.claim.total | number: '1.2-2' }} {{ v.claim.currency }}</strong></div>
        <div><span class="muted">Trip</span><br />{{ v.claim.travelNumber ?? '—' }}</div>
        <div><span class="muted">Project</span><br />{{ v.claim.projectName ?? '—' }}</div>
        <div><span class="muted">Submitted</span><br />{{ v.claim.submittedDate ? (v.claim.submittedDate | date: 'd MMM y') : '—' }}</div>
        <div><span class="muted">Payment</span><br />{{ v.claim.paidOn ? (v.claim.paymentMode + ' on ' + (v.claim.paidOn | date: 'd MMM y')) : '—' }}{{ v.claim.paymentReference ? ' · ' + v.claim.paymentReference : '' }}</div>
      </section>
      <section class="panel stack">
        <h2 class="xp__heading">Lines</h2>
        @for (line of v.lines; track line.id) {
          <div class="xp__row">
            <span><strong>{{ line.description }}</strong> <span class="muted">{{ line.categoryName }}</span></span>
            <span>{{ line.date | date: 'd MMM y' }}{{ line.merchant ? ' · ' + line.merchant : '' }}</span>
            <span>{{ line.amount | number: '1.2-2' }}{{ line.billable ? ' · billable' : '' }}{{ line.hasReceipt ? '' : ' · no receipt' }}</span>
            <span class="xp__danger">{{ line.policyFlag ?? '' }}</span>
            <span class="muted">{{ line.justification ?? '' }}</span>
          </div>
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }

    <gggs-form-dialog [(visible)]="payOpen" title="Pay claim" saveLabel="Pay" [busy]="busy()" [error]="formError()" (save)="pay()">
      <div class="stack">
        <label class="field">
          <span class="field__label">How</span>
          <select [value]="mode()" (change)="mode.set($any($event.target).value)">
            <option value="Payroll">With the next payroll (reimbursement)</option>
            <option value="Direct">Paid directly (bank transfer)</option>
          </select>
        </label>
        <label class="field">
          <span class="field__label">Reference</span>
          <input type="text" maxlength="100" [value]="reference()" (input)="reference.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styleUrl: './xp.css',
})
export class ClaimPage {
  private readonly api = inject(ExpensesApi);

  readonly id = input.required<string>();

  protected readonly canPay = inject(PermissionStore).has('spend.claim.pay');
  protected readonly view = signal<ClaimView | null>(null);
  protected readonly payOpen = signal(false);
  protected readonly mode = signal<PaymentMode>('Payroll');
  protected readonly reference = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.claim(this.id()).then(
        (view) => this.view.set(view),
        (error: unknown) => this.error.set(message(error, 'The claim could not be loaded.')),
      );
    });
  }

  protected async pay(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      this.view.set(await this.api.pay(this.id(), this.mode(), this.reference() || null));
      this.payOpen.set(false);
    } catch (error) {
      this.formError.set(message(error, 'The claim could not be paid.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** One trip: its approval, advance and settlement against its claims. */
@Component({
  selector: 'gggs-trip-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, StatusBadge, FormDialog, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (trip(); as t) {
      <gggs-page-header [title]="t.fromCity + ' → ' + t.toCity" [subtitle]="t.number + ' · ' + (t.employeeName ?? '')">
        <a class="btn" routerLink="/expenses">All travel</a>
        @if (canAdvance && t.status === 'Approved') {
          @if (!t.advancePaid && t.advanceRequested > 0) {
            <button type="button" class="btn" (click)="advance.set(t.advanceRequested); advanceOpen.set(true)">Pay advance</button>
          }
          <button type="button" class="btn btn--primary" (click)="settle()">Settle</button>
        }
      </gggs-page-header>
      <section class="panel xp__grid">
        <div><span class="muted">Status</span><br /><gggs-status-badge [status]="t.status" /></div>
        <div><span class="muted">Purpose</span><br />{{ t.purpose }}</div>
        <div><span class="muted">Dates</span><br />{{ t.departOn | date: 'd MMM' }} – {{ t.returnOn | date: 'd MMM y' }} · {{ t.mode }}</div>
        <div><span class="muted">Estimate</span><br />{{ t.estimatedCost | number: '1.0-0' }}</div>
        <div><span class="muted">Advance</span><br />{{ t.advancePaid ? (t.advancePaid | number: '1.0-0') + ' paid ' + (t.advancePaidOn | date: 'd MMM') : t.advanceRequested + ' requested' }}</div>
        <div><span class="muted">Claimed</span><br />{{ t.claimed | number: '1.0-0' }}</div>
        <div><span class="muted">Settlement</span><br />{{ t.settledOn ? ((t.settlementAmount ?? 0) | number: '1.0-0') + ' on ' + (t.settledOn | date: 'd MMM y') : '—' }}</div>
      </section>
      @if (notice()) {
        <p class="xp__notice" role="status">{{ notice() }}</p>
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }

    <gggs-form-dialog [(visible)]="advanceOpen" title="Pay advance" saveLabel="Pay" [busy]="busy()" [error]="formError()" [saveDisabled]="advance() <= 0" (save)="payAdvance()">
      <label class="field">
        <span class="field__label">Amount</span>
        <input type="number" min="1" [value]="advance()" (input)="advance.set(+$any($event.target).value)" />
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './xp.css',
})
export class TripPage {
  private readonly api = inject(ExpensesApi);

  readonly id = input.required<string>();

  protected readonly canAdvance = inject(PermissionStore).has('spend.travel-advance.manage');
  protected readonly trip = signal<TravelItem | null>(null);
  protected readonly advanceOpen = signal(false);
  protected readonly advance = signal(0);
  protected readonly notice = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.api.trip(this.id()).then(
        (trip) => this.trip.set(trip),
        (error: unknown) => this.error.set(message(error, 'The trip could not be loaded.')),
      );
    });
  }

  protected async payAdvance(): Promise<void> {
    this.busy.set(true);
    try {
      this.trip.set(await this.api.payAdvance(this.id(), this.advance()));
      this.advanceOpen.set(false);
    } catch (error) {
      this.formError.set(message(error, 'The advance could not be paid.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async settle(): Promise<void> {
    try {
      const trip = await this.api.settle(this.id());
      this.trip.set(trip);
      const amount = trip.settlementAmount ?? 0;
      this.notice.set(amount === 0 ? 'Settled with nothing due either way.' : amount > 0 ? `${amount} goes to payroll as a reimbursement.` : `${-amount} of unspent advance is recovered through payroll.`);
    } catch (error) {
      this.error.set(message(error, 'The trip could not be settled.'));
    }
  }
}

/** My claims, with a line editor, and my travel requests. */
@Component({
  selector: 'gggs-my-expenses-page',
  imports: [DatePipe, DecimalPipe, PageHeader, StatusBadge, EmptyState, FormDialog, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My expenses" subtitle="Claim what you spent, and request travel with an advance.">
      <button type="button" class="btn" (click)="openTrip()">Request travel</button>
      <button type="button" class="btn btn--primary" (click)="openClaim()"><i class="pi pi-plus" aria-hidden="true"></i> New claim</button>
    </gggs-page-header>
    <gggs-tabs [tabs]="tabs" [(selected)]="tab" />
    @if (tab() === 'claims') {
      <section class="panel stack">
        @for (claim of claims(); track claim.id) {
          <div class="xp__row">
            <span><strong>{{ claim.title }}</strong> <span class="muted">{{ claim.number }}{{ claim.travelNumber ? ' · ' + claim.travelNumber : '' }}</span></span>
            <span>{{ claim.total | number: '1.2-2' }} {{ claim.currency }} · {{ claim.lines }} line(s)</span>
            <span class="muted">{{ claim.paidOn ? 'paid ' + (claim.paidOn | date: 'd MMM y') : claim.submittedDate ? 'submitted ' + (claim.submittedDate | date: 'd MMM y') : 'draft' }}</span>
            <span class="toolbar">
              <button type="button" class="btn btn--sm" (click)="openEditor(claim.id)">{{ claim.canEdit ? 'Edit' : 'View' }}</button>
              @if (claim.status === 'Draft' || claim.status === 'PendingApproval') {
                <button type="button" class="btn btn--link btn--sm" (click)="cancelClaim(claim.id)">Cancel</button>
              }
            </span>
            <gggs-status-badge [status]="claim.status" />
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-receipt" title="No claims yet" />
        }
      </section>
      @if (editor(); as v) {
        <section class="panel stack">
          <h2 class="xp__heading">{{ v.claim.number }} · {{ v.claim.title }}</h2>
          @for (line of lines(); track $index) {
            <div class="xp__line">
              <label class="field">
                <span class="field__label">Category</span>
                <select [value]="line.categoryId" [disabled]="!v.claim.canEdit" (change)="patchLine($index, { categoryId: $any($event.target).value })">
                  @for (category of categories(); track category.id) {
                    <option [value]="category.id">{{ category.name }}</option>
                  }
                </select>
              </label>
              <label class="field">
                <span class="field__label">Date</span>
                <input type="date" [value]="line.date" [disabled]="!v.claim.canEdit" (change)="patchLine($index, { date: $any($event.target).value })" />
              </label>
              <label class="field">
                <span class="field__label">Description</span>
                <input type="text" maxlength="300" [value]="line.description" [disabled]="!v.claim.canEdit" (input)="patchLine($index, { description: $any($event.target).value })" />
              </label>
              <label class="field">
                <span class="field__label">Amount</span>
                <input type="number" min="0" step="0.01" [value]="line.amount" [disabled]="!v.claim.canEdit" (input)="patchLine($index, { amount: +$any($event.target).value })" />
              </label>
              <span class="stack">
                <label class="xp__flag"><input type="checkbox" [checked]="line.hasReceipt" [disabled]="!v.claim.canEdit" (change)="patchLine($index, { hasReceipt: $any($event.target).checked })" /> Receipt</label>
                <label class="xp__flag"><input type="checkbox" [checked]="line.billable" [disabled]="!v.claim.canEdit" (change)="patchLine($index, { billable: $any($event.target).checked })" /> Billable</label>
              </span>
              @if (v.claim.canEdit) {
                <button type="button" class="btn btn--link btn--sm" aria-label="Remove line" (click)="removeLine($index)"><i class="pi pi-trash" aria-hidden="true"></i></button>
              }
              @if (flag($index); as warning) {
                <label class="field xp__wide">
                  <span class="field__label xp__danger">Outside policy: {{ warning }} - explain why</span>
                  <input type="text" maxlength="500" [value]="line.justification ?? ''" [disabled]="!v.claim.canEdit" (input)="patchLine($index, { justification: $any($event.target).value || null })" />
                </label>
              }
            </div>
          }
          <p><strong>Total {{ total() | number: '1.2-2' }} {{ v.claim.currency }}</strong></p>
          @if (v.claim.canEdit) {
            <div class="toolbar">
              <button type="button" class="btn" (click)="addLine()">Add line</button>
              <button type="button" class="btn" [disabled]="busy()" (click)="saveLines()">Save</button>
              <button type="button" class="btn btn--primary" [disabled]="busy() || !lines().length" (click)="submit()">Save and submit</button>
            </div>
          }
          @if (formError()) {
            <p class="field__error" role="alert">{{ formError() }}</p>
          }
        </section>
      }
    } @else {
      <section class="panel stack">
        @for (trip of trips(); track trip.id) {
          <div class="xp__row">
            <span><strong>{{ trip.fromCity }} → {{ trip.toCity }}</strong> <span class="muted">{{ trip.number }}</span></span>
            <span>{{ trip.departOn | date: 'd MMM' }} – {{ trip.returnOn | date: 'd MMM y' }} · {{ trip.mode }}</span>
            <span class="muted">advance {{ (trip.advancePaid ?? 0) | number: '1.0-0' }} · claimed {{ trip.claimed | number: '1.0-0' }}{{ trip.settledOn ? ' · settled' : '' }}</span>
            <span class="toolbar">
              @if (trip.status === 'Draft' || trip.status === 'Rejected') {
                <button type="button" class="btn btn--sm" (click)="tripAction(trip.id, 'submit')">Submit</button>
              }
              @if (trip.status === 'Draft' || trip.status === 'PendingApproval' || (trip.status === 'Approved' && !trip.advancePaid)) {
                <button type="button" class="btn btn--link btn--sm" (click)="tripAction(trip.id, 'cancel')">Cancel</button>
              }
            </span>
            <gggs-status-badge [status]="trip.status" />
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-send" title="No travel requests" />
        }
      </section>
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }

    <gggs-form-dialog [(visible)]="claimOpen" title="New claim" saveLabel="Create" [busy]="busy()" [error]="formError()" [saveDisabled]="!claimTitle().trim()" (save)="createClaim()">
      <div class="stack">
        <label class="field">
          <span class="field__label">Title</span>
          <input type="text" maxlength="200" placeholder="Client visit, Pune" [value]="claimTitle()" (input)="claimTitle.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">For a trip (optional)</span>
          <select [value]="claimTrip()" (change)="claimTrip.set($any($event.target).value)">
            <option value="">None</option>
            @for (trip of openTrips(); track trip.id) {
              <option [value]="trip.id">{{ trip.number }} · {{ trip.fromCity }} → {{ trip.toCity }}</option>
            }
          </select>
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="tripOpen" title="Request travel" saveLabel="Submit" [busy]="busy()" [error]="formError()" [saveDisabled]="!tripForm().purpose.trim() || !tripForm().fromCity.trim() || !tripForm().toCity.trim()" (save)="requestTrip()" size="lg">
      <div class="xp__grid">
        <label class="field xp__wide">
          <span class="field__label">Purpose</span>
          <input type="text" maxlength="500" [value]="tripForm().purpose" (input)="patchTrip({ purpose: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">From</span>
          <input type="text" maxlength="100" [value]="tripForm().fromCity" (input)="patchTrip({ fromCity: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">To</span>
          <input type="text" maxlength="100" [value]="tripForm().toCity" (input)="patchTrip({ toCity: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Depart</span>
          <input type="date" [value]="tripForm().departOn" (change)="patchTrip({ departOn: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Return</span>
          <input type="date" [value]="tripForm().returnOn" (change)="patchTrip({ returnOn: $any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Mode</span>
          <select [value]="tripForm().mode" (change)="patchTrip({ mode: $any($event.target).value })">
            @for (mode of modes; track mode) {
              <option [value]="mode">{{ mode }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Estimated cost</span>
          <input type="number" min="0" [value]="tripForm().estimatedCost" (input)="patchTrip({ estimatedCost: +$any($event.target).value })" />
        </label>
        <label class="field">
          <span class="field__label">Advance needed</span>
          <input type="number" min="0" [value]="tripForm().advanceRequested" (input)="patchTrip({ advanceRequested: +$any($event.target).value })" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styleUrl: './xp.css',
})
export class MyExpensesPage {
  private readonly api = inject(ExpensesApi);

  protected readonly modes = TRAVEL_MODES;
  protected readonly tabs: readonly TabItem[] = [
    { key: 'claims', label: 'Claims' },
    { key: 'travel', label: 'Travel' },
  ];
  protected readonly tab = signal('claims');
  protected readonly claims = signal<readonly ClaimItem[]>([]);
  protected readonly trips = signal<readonly TravelItem[]>([]);
  protected readonly categories = signal<readonly ExpenseCategoryItem[]>([]);
  protected readonly editor = signal<ClaimView | null>(null);
  protected readonly lines = signal<readonly ClaimLineInput[]>([]);
  protected readonly total = computed(() => this.lines().reduce((sum, line) => sum + (line.amount || 0), 0));
  protected readonly openTrips = computed(() => this.trips().filter((trip) => trip.status === 'Approved'));
  protected readonly claimOpen = signal(false);
  protected readonly claimTitle = signal('');
  protected readonly claimTrip = signal('');
  protected readonly tripOpen = signal(false);
  protected readonly tripForm = signal<TravelDetailsInput>(blankTrip());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.reload();
    this.api.categories().then((categories) => this.categories.set(categories.filter((category) => category.isActive)), () => undefined);
  }

  /** The policy warning a line would carry, worked out as the server does. */
  protected flag(index: number): string | null {
    const line = this.lines()[index];
    const category = this.categories().find((candidate) => candidate.id === line.categoryId);
    if (!category) {
      return null;
    }

    const flags: string[] = [];
    if (category.limitPerLine !== undefined && line.amount > category.limitPerLine) {
      flags.push(`over the ${category.limitPerLine} limit`);
    }

    if (!line.hasReceipt && line.amount > category.receiptRequiredAbove) {
      flags.push('receipt needed');
    }

    return flags.length ? flags.join('; ') : null;
  }

  protected openClaim(): void {
    this.claimTitle.set('');
    this.claimTrip.set('');
    this.formError.set(null);
    this.claimOpen.set(true);
  }

  protected async createClaim(): Promise<void> {
    await this.run(async () => {
      const view = await this.api.draft({ title: this.claimTitle(), travelRequestId: this.claimTrip() || null, projectId: null, currency: null });
      this.claimOpen.set(false);
      this.showEditor(view);
      this.claims.set(await this.api.myClaims());
    });
  }

  protected async openEditor(id: string): Promise<void> {
    try {
      this.showEditor(await this.api.claim(id));
    } catch (error) {
      this.error.set(message(error, 'The claim could not be opened.'));
    }
  }

  protected patchLine(index: number, change: Partial<ClaimLineInput>): void {
    this.lines.update((lines) => lines.map((line, at) => (at === index ? { ...line, ...change } : line)));
  }

  protected addLine(): void {
    this.lines.update((lines) => [...lines, blankLine(this.categories()[0]?.id ?? '')]);
  }

  protected removeLine(index: number): void {
    this.lines.update((lines) => lines.filter((_, at) => at !== index));
  }

  protected async saveLines(): Promise<void> {
    const view = this.editor();
    if (view) {
      await this.run(async () => {
        this.showEditor(await this.api.saveLines(view.claim.id, this.lines()));
        this.claims.set(await this.api.myClaims());
      });
    }
  }

  protected async submit(): Promise<void> {
    const view = this.editor();
    if (view) {
      await this.run(async () => {
        await this.api.saveLines(view.claim.id, this.lines());
        this.showEditor(await this.api.submit(view.claim.id));
        this.claims.set(await this.api.myClaims());
      });
    }
  }

  protected async cancelClaim(id: string): Promise<void> {
    try {
      await this.api.cancelClaim(id);
      this.editor.set(null);
      this.claims.set(await this.api.myClaims());
    } catch (error) {
      this.error.set(message(error, 'The claim could not be cancelled.'));
    }
  }

  protected patchTrip(change: Partial<TravelDetailsInput>): void {
    this.tripForm.update((form) => ({ ...form, ...change }));
  }

  protected openTrip(): void {
    this.tripForm.set(blankTrip());
    this.formError.set(null);
    this.tripOpen.set(true);
  }

  protected async requestTrip(): Promise<void> {
    await this.run(async () => {
      await this.api.requestTrip(this.tripForm(), true);
      this.tripOpen.set(false);
      this.tab.set('travel');
      this.trips.set(await this.api.myTrips());
    });
  }

  protected async tripAction(id: string, action: 'submit' | 'cancel'): Promise<void> {
    try {
      await this.api.tripAction(id, action);
      this.trips.set(await this.api.myTrips());
    } catch (error) {
      this.error.set(message(error, 'The travel request could not be changed.'));
    }
  }

  private showEditor(view: ClaimView): void {
    this.editor.set(view);
    this.lines.set(
      view.lines.map((line) => ({
        categoryId: line.categoryId,
        date: line.date,
        description: line.description,
        amount: line.amount,
        merchant: line.merchant ?? null,
        hasReceipt: line.hasReceipt,
        billable: line.billable,
        justification: line.justification ?? null,
      })),
    );
    if (view.claim.canEdit && !view.lines.length) {
      this.addLine();
    }
  }

  private reload(): void {
    this.api.myClaims().then((claims) => this.claims.set(claims), (error: unknown) => this.error.set(message(error, 'Your claims could not be loaded.')));
    this.api.myTrips().then((trips) => this.trips.set(trips), () => undefined);
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
