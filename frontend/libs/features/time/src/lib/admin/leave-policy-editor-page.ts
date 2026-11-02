import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { MasterDataApi } from '@gggs/shared/master-data';
import type { AccrualFrequency, LeavePlanDetail, LeavePlanItemInput, LeaveType, LookupValue } from '@gggs/shared/models';
import { LOOKUP_LISTS } from '@gggs/shared/models';
import { LeaveApi } from '@gggs/shared/time';
import { EmptyState, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { MONTHS, defaultRules, itemsChanged, ruleSummary, toInput } from './leave-policy-model';

type Draft = { -readonly [K in keyof LeavePlanItemInput]: LeavePlanItemInput[K] };

/**
 * One leave policy: its name and leave year, the leave types it grants, and the rules each follows
 * there - entitlement and accrual, who can take it, and how requests are limited. Everything is
 * saved together from the bar at the bottom.
 */
@Component({
  selector: 'gggs-leave-policy-editor-page',
  imports: [RouterLink, PageHeader, StatusBadge, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (saved(); as plan) {
      <gggs-page-header [title]="plan.name" [subtitle]="'Leave year ' + plan.leaveYear + ' · ' + (plan.assignedTo.length ? 'assigned to ' + plan.assignedTo.length + ' engagement type(s)' : 'not assigned yet')">
        <div class="toolbar">
          <gggs-status-badge [status]="plan.isActive ? 'Active' : 'Inactive'" />
          <a class="btn" routerLink="/leave/policy-assignment">Assign</a>
          <a class="btn" routerLink="/leave/policies">All policies</a>
        </div>
      </gggs-page-header>

      <section class="panel editor__details" aria-labelledby="details-heading">
        <h2 id="details-heading" class="editor__heading">Details</h2>
        <div class="form-grid">
          <label class="field">
            <span class="field__label">Name *</span>
            <input type="text" maxlength="100" [value]="name()" (input)="name.set($any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">Code</span>
            <input type="text" [value]="plan.code" disabled />
          </label>
          <label class="field">
            <span class="field__label">Leave year starts in</span>
            <select (change)="startMonth.set(+$any($event.target).value)">
              @for (month of months; track month; let index = $index) {
                <option [value]="index + 1" [selected]="index + 1 === startMonth()">{{ month }}</option>
              }
            </select>
          </label>
          <label class="field form-grid__full">
            <span class="field__label">Description</span>
            <textarea rows="2" maxlength="500" [value]="description()" (input)="description.set($any($event.target).value)"></textarea>
          </label>
        </div>
      </section>

      <div class="editor">
        <nav class="panel editor__types" aria-label="Leave types in this policy">
          <h2 class="editor__heading">Leave types</h2>
          @for (item of items(); track item.leaveTypeId) {
            <button type="button" class="editor__type" [class.editor__type--active]="item.leaveTypeId === selectedId()" [attr.aria-current]="item.leaveTypeId === selectedId() ? 'true' : null" (click)="selectedId.set(item.leaveTypeId)">
              <span class="editor__dot" [style.background]="type(item.leaveTypeId)?.color" aria-hidden="true"></span>
              <span><strong>{{ type(item.leaveTypeId)?.name }}</strong><span class="editor__line">{{ summary(item) }}</span></span>
            </button>
          } @empty {
            <p class="muted">No leave types yet. Add the first one below.</p>
          }
          @if (addable().length) {
            <div class="editor__add">
              <label class="sr-only" for="add-type">Leave type to add</label>
              <select id="add-type" #pick>
                @for (type of addable(); track type.id) {
                  <option [value]="type.id">{{ type.name }}</option>
                }
              </select>
              <button type="button" class="btn btn--sm" (click)="add(pick.value)">Add</button>
            </div>
          }
        </nav>

        <section class="panel editor__rules" aria-live="polite">
          @if (selected(); as item) {
            <div class="toolbar">
              <h2 class="editor__heading">{{ type(item.leaveTypeId)?.name }}</h2>
              <button type="button" class="btn btn--link btn--sm" (click)="remove(item.leaveTypeId)">Remove from policy</button>
            </div>
            <p class="editor__summary">{{ summary(item) }}</p>

            <fieldset class="editor__group">
              <legend>Entitlement and accrual</legend>
              <div class="form-grid">
                <label class="field">
                  <span class="field__label">Days per year</span>
                  <input type="number" min="0" max="366" step="0.5" [value]="item.annualEntitlement" (input)="patch('annualEntitlement', +$any($event.target).value)" />
                </label>
                <label class="field">
                  <span class="field__label">Credited</span>
                  <select (change)="patch('accrual', $any($event.target).value)">
                    @for (option of accruals; track option.value) {
                      <option [value]="option.value" [selected]="option.value === item.accrual">{{ option.label }}</option>
                    }
                  </select>
                </label>
                @if (item.accrual !== 'None') {
                  <label class="field">
                    <span class="field__label">Credited on</span>
                    <select (change)="patch('creditTiming', $any($event.target).value)">
                      <option value="StartOfPeriod" [selected]="item.creditTiming === 'StartOfPeriod'">The first day of the period</option>
                      <option value="EndOfPeriod" [selected]="item.creditTiming === 'EndOfPeriod'">The last day of the period</option>
                    </select>
                  </label>
                  <label class="field">
                    <span class="field__label">Joining cut-off (day of month)</span>
                    <input type="number" min="1" max="31" [value]="item.joiningCutoffDay" (input)="patch('joiningCutoffDay', +$any($event.target).value)" />
                  </label>
                  @if (item.accrual !== 'Monthly') {
                    <label class="field">
                      <span><input type="checkbox" [checked]="item.prorateOnJoining" (change)="patch('prorateOnJoining', $any($event.target).checked)" /> Prorate for someone joining part-way through</span>
                    </label>
                  }
                }
                <label class="field">
                  <span class="field__label">May go negative by (days)</span>
                  <input type="number" min="0" max="366" step="0.5" [value]="item.maxNegativeBalance" (input)="patch('maxNegativeBalance', +$any($event.target).value)" />
                </label>
                <label class="field form-grid__full">
                  <span><input type="checkbox" [checked]="item.countSandwichedDays" (change)="patch('countSandwichedDays', $any($event.target).checked)" /> Sandwich rule: weekends and holidays between leave days count as leave</span>
                </label>
              </div>
            </fieldset>

            <fieldset class="editor__group">
              <legend>Year end</legend>
              <div class="form-grid">
                <label class="field">
                  <span class="field__label">Carry forward up to (days)</span>
                  <input type="number" min="0" step="0.5" placeholder="All" [value]="item.carryForwardLimit ?? ''" (input)="patch('carryForwardLimit', optional($any($event.target).value))" />
                </label>
                <label class="field">
                  <span class="field__label">Carried days expire after (months)</span>
                  <input type="number" min="1" max="11" placeholder="Never" [value]="item.carryForwardExpiryMonths ?? ''" (input)="patch('carryForwardExpiryMonths', optional($any($event.target).value))" />
                </label>
                <label class="field">
                  <span><input type="checkbox" [checked]="item.encashAtYearEnd" (change)="patch('encashAtYearEnd', $any($event.target).checked)" /> Pay out days above the carry-forward limit</span>
                </label>
                @if (item.encashAtYearEnd) {
                  <label class="field">
                    <span class="field__label">Pay out at most (days)</span>
                    <input type="number" min="0" step="0.5" placeholder="All of the excess" [value]="item.maxEncashDays ?? ''" (input)="patch('maxEncashDays', optional($any($event.target).value))" />
                  </label>
                }
              </div>
              <p class="muted editor__note">Paid with the next payroll at basic ÷ 26 per day; anything still above the limit lapses.</p>
            </fieldset>

            <fieldset class="editor__group">
              <legend>Granted per occurrence <span class="muted">(maternity, paternity)</span></legend>
              <div class="form-grid">
                <label class="field">
                  <span class="field__label">Days each time</span>
                  <input type="number" min="0.5" step="0.5" placeholder="Not granted per occurrence" [value]="item.grantPerOccurrence ?? ''" (input)="patch('grantPerOccurrence', optional($any($event.target).value))" />
                </label>
                <label class="field">
                  <span class="field__label">Times in the whole employment</span>
                  <input type="number" min="1" max="50" placeholder="No limit" [value]="item.maxOccurrencesInEmployment ?? ''" (input)="patch('maxOccurrencesInEmployment', optional($any($event.target).value))" />
                </label>
              </div>
              <p class="muted editor__note">Granted afresh for each request instead of drawn from a balance; set the crediting to "Granted by HR".</p>
            </fieldset>

            <fieldset class="editor__group">
              <legend>Who can take it</legend>
              <div class="form-grid">
                <div class="field form-grid__full">
                  <span class="field__label" id="genders-label">Genders <span class="muted">(none ticked: everyone)</span></span>
                  <div class="editor__checks" role="group" aria-labelledby="genders-label">
                    @for (gender of genders(); track gender.code) {
                      <label><input type="checkbox" [checked]="item.eligibleGenderCodes.includes(gender.code)" (change)="toggleGender(gender.code, $any($event.target).checked)" /> {{ gender.label }}</label>
                    }
                  </div>
                </div>
                <label class="field">
                  <span class="field__label">After months of service</span>
                  <input type="number" min="0" max="600" [value]="item.minServiceMonths" (input)="patch('minServiceMonths', +$any($event.target).value)" />
                </label>
                <label class="field">
                  <span><input type="checkbox" [checked]="item.allowDuringProbation" (change)="patch('allowDuringProbation', $any($event.target).checked)" /> Allowed during probation</span>
                </label>
                <label class="field">
                  <span><input type="checkbox" [checked]="item.allowDuringNotice" (change)="patch('allowDuringNotice', $any($event.target).checked)" /> Allowed during the notice period</span>
                </label>
              </div>
            </fieldset>

            <fieldset class="editor__group">
              <legend>Requests <span class="muted">(empty: no limit)</span></legend>
              <div class="form-grid">
                <label class="field">
                  <span class="field__label">Shortest request (days)</span>
                  <input type="number" min="0.5" step="0.5" [value]="item.minDaysPerRequest ?? ''" (input)="patch('minDaysPerRequest', optional($any($event.target).value))" />
                </label>
                <label class="field">
                  <span class="field__label">Longest stretch (days)</span>
                  <input type="number" min="1" [value]="item.maxConsecutiveDays ?? ''" (input)="patch('maxConsecutiveDays', optional($any($event.target).value))" />
                </label>
                <label class="field">
                  <span class="field__label">Requests per leave year</span>
                  <input type="number" min="1" [value]="item.maxRequestsPerYear ?? ''" (input)="patch('maxRequestsPerYear', optional($any($event.target).value))" />
                </label>
                <label class="field">
                  <span class="field__label">Notice (days ahead)</span>
                  <input type="number" min="0" max="365" [value]="item.minNoticeDays" (input)="patch('minNoticeDays', +$any($event.target).value)" />
                </label>
                <label class="field">
                  <span class="field__label">Apply up to days after</span>
                  <input type="number" min="0" max="365" [value]="item.maxBackdatedDays ?? ''" (input)="patch('maxBackdatedDays', optional($any($event.target).value))" />
                </label>
                <label class="field">
                  <span class="field__label">Apply up to days ahead</span>
                  <input type="number" min="0" max="730" [value]="item.maxAdvanceDays ?? ''" (input)="patch('maxAdvanceDays', optional($any($event.target).value))" />
                </label>
                <label class="field">
                  <span class="field__label">Document needed above (days)</span>
                  <input type="number" min="0" step="0.5" [value]="item.documentRequiredAboveDays ?? ''" (input)="patch('documentRequiredAboveDays', optional($any($event.target).value))" />
                </label>
              </div>
              <p class="muted editor__note">Notice and the backdated and ahead limits bind employees; HR applying on someone's behalf is exempt.</p>
            </fieldset>
          } @else {
            <gggs-empty-state icon="pi pi-list" title="Choose a leave type" description="Pick one on the left to set its rules, or add one." />
          }
        </section>
      </div>

      @if (dirty()) {
        <div class="save-bar elevated" role="region" aria-label="Unsaved changes">
          <span>Unsaved changes to this policy</span>
          <button type="button" class="btn" [disabled]="busy()" (click)="reset(plan)">Discard</button>
          <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="save(plan)">{{ busy() ? 'Saving…' : 'Save' }}</button>
        </div>
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }

    @if (message()) {
      <p class="alert alert--success" role="status">{{ message() }}</p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .editor { display: flex; flex-wrap: wrap; align-items: flex-start; gap: var(--gggs-space-4); }
    .editor__types { flex: 1 1 16rem; gap: var(--gggs-space-2); max-width: 22rem; }
    .editor__rules { flex: 999 1 30rem; min-width: 0; }
    .editor__heading { margin: 0; margin-inline-end: auto; font-size: var(--gggs-font-size-md); }
    .editor__type { display: flex; gap: var(--gggs-space-2); width: 100%; padding: var(--gggs-space-2) var(--gggs-space-3); border: 1px solid transparent; border-radius: var(--gggs-radius-md); background: transparent; color: var(--gggs-color-text); font: inherit; text-align: start; cursor: pointer; }
    .editor__type:hover { background: var(--gggs-color-surface-hover); }
    .editor__type--active { border-color: var(--gggs-color-primary); background: var(--gggs-color-primary-subtle); }
    .editor__dot { flex: none; width: 0.65rem; height: 0.65rem; margin-block-start: 0.4rem; border-radius: 50%; }
    .editor__line { display: block; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    .editor__add { display: flex; gap: var(--gggs-space-2); margin-block-start: var(--gggs-space-2); }
    .editor__add select { flex: 1; min-width: 0; }
    .editor__summary { margin: 0; color: var(--gggs-color-text-muted); }
    .editor__group { margin: 0; padding: var(--gggs-space-4); border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-lg); }
    .editor__group legend { padding: 0 var(--gggs-space-2); font-weight: var(--gggs-font-weight-semibold); }
    .editor__checks { display: flex; flex-wrap: wrap; gap: var(--gggs-space-4); }
    .editor__note { margin: var(--gggs-space-3) 0 0; font-size: var(--gggs-font-size-sm); }
    .save-bar { position: sticky; inset-block-end: var(--gggs-space-4); display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-3); padding: var(--gggs-space-3) var(--gggs-space-4); box-shadow: var(--gggs-shadow-4); }
    .save-bar span { flex: 1; }
  `,
})
export class LeavePolicyEditorPage {
  private readonly api = inject(LeaveApi);
  private readonly masterData = inject(MasterDataApi);

  /** The route's policy id. */
  readonly id = input.required<string>();

  protected readonly months = MONTHS;
  protected readonly accruals: readonly { value: AccrualFrequency; label: string }[] = [
    { value: 'Monthly', label: 'Monthly (a twelfth each month)' },
    { value: 'Quarterly', label: 'Quarterly (a quarter each quarter)' },
    { value: 'HalfYearly', label: 'Half-yearly (half each half-year)' },
    { value: 'Yearly', label: 'Yearly (all at once)' },
    { value: 'None', label: 'Granted by HR (no accrual)' },
  ];

  protected readonly saved = signal<LeavePlanDetail | null>(null);
  protected readonly types = signal<readonly LeaveType[]>([]);
  protected readonly genders = signal<readonly LookupValue[]>([]);
  protected readonly items = signal<readonly Draft[]>([]);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly name = signal('');
  protected readonly description = signal('');
  protected readonly startMonth = signal(1);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly selected = computed(() => this.items().find((item) => item.leaveTypeId === this.selectedId()) ?? null);
  protected readonly addable = computed(() => this.types().filter((type) => type.isActive && !this.items().some((item) => item.leaveTypeId === type.id)));
  private readonly genderLabels = computed(() => new Map(this.genders().map((gender) => [gender.code, gender.label])));
  private readonly detailsDirty = computed(() => {
    const plan = this.saved();
    return !!plan && (this.name().trim() !== plan.name || (this.description().trim() || undefined) !== (plan.description ?? undefined) || this.startMonth() !== plan.leaveYearStartMonth);
  });
  protected readonly dirty = computed(() => {
    const plan = this.saved();
    return this.detailsDirty() || (!!plan && itemsChanged(plan.items, this.items()));
  });

  constructor() {
    effect(() => void this.load(this.id()));
  }

  protected type(id: string): LeaveType | undefined {
    return this.types().find((type) => type.id === id);
  }

  protected summary(item: Draft): string {
    return ruleSummary(item, this.genderLabels());
  }

  protected optional(value: string): number | undefined {
    return value === '' ? undefined : Number(value);
  }

  protected patch<K extends keyof Draft>(field: K, value: Draft[K]): void {
    const id = this.selectedId();
    this.items.update((items) => items.map((item) => (item.leaveTypeId === id ? { ...item, [field]: value } : item)));
    this.message.set(null);
  }

  protected toggleGender(code: string, on: boolean): void {
    const current = this.selected()?.eligibleGenderCodes ?? [];
    this.patch('eligibleGenderCodes', on ? [...current, code] : current.filter((value) => value !== code));
  }

  protected add(leaveTypeId: string): void {
    if (!leaveTypeId) {
      return;
    }
    this.items.update((items) => [...items, { leaveTypeId, ...defaultRules() }]);
    this.selectedId.set(leaveTypeId);
  }

  protected remove(leaveTypeId: string): void {
    this.items.update((items) => items.filter((item) => item.leaveTypeId !== leaveTypeId));
    this.selectedId.set(this.items()[0]?.leaveTypeId ?? null);
  }

  protected reset(plan: LeavePlanDetail): void {
    this.name.set(plan.name);
    this.description.set(plan.description ?? '');
    this.startMonth.set(plan.leaveYearStartMonth);
    this.items.set(plan.items.map(toInput));
    if (!plan.items.some((item) => item.leaveTypeId === this.selectedId())) {
      this.selectedId.set(plan.items[0]?.leaveTypeId ?? null);
    }
  }

  protected async save(plan: LeavePlanDetail): Promise<void> {
    if (!this.name().trim()) {
      this.error.set('Give the policy a name.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      let detail = plan;
      if (this.detailsDirty()) {
        detail = await this.api.savePlan(plan.id, { code: plan.code, name: this.name().trim(), description: this.description().trim() || null, leaveYearStartMonth: this.startMonth() });
      }
      if (itemsChanged(plan.items, this.items())) {
        detail = await this.api.setPlanItems(plan.id, this.items().map(toInput));
      }
      this.saved.set(detail);
      this.reset(detail);
      this.message.set('Saved. Employees on this policy follow the new rules from their next request and accrual.');
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The policy could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    try {
      const [plan, types, genders] = await Promise.all([this.api.plan(id), this.api.types(true), this.masterData.values(LOOKUP_LISTS.gender)]);
      this.types.set(types);
      this.genders.set(genders);
      this.saved.set(plan);
      this.reset(plan);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'This leave policy could not be loaded.');
    }
  }
}
