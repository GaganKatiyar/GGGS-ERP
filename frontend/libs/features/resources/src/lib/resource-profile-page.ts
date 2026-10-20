import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PermissionStore } from '@gggs/core/permissions';
import { ResourcesApi } from '@gggs/shared/delivery';
import type { AllocationItem, AllocationOutcome, ResourceProfileView } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { AllocateDialog, AllocationEditDialog, message, outcomeText } from './allocation-dialogs';

interface RatesForm {
  costRate: number | null;
  billingRate: number | null;
  currency: string;
  capacityHoursPerWeek: number | null;
  noticePeriodDays: number | null;
  isBillable: boolean;
  notes: string;
}

/**
 * A person's resource profile (spec §56): skills, current and coming allocations, the next
 * months' capacity, allocation history and - for those allowed to see money - the rates.
 */
@Component({
  selector: 'gggs-resource-profile-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, StatusBadge, EmptyState, Skeleton, FormDialog, AllocateDialog, AllocationEditDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (profile(); as p) {
      <gggs-page-header [title]="p.employeeName" [subtitle]="p.employeeCode + (p.designation ? ' · ' + p.designation : '') + (p.departmentName ? ' · ' + p.departmentName : '')">
        <a class="btn" [routerLink]="['/employees', p.employeeId]">Employee 360</a>
        @if (canAllocate) {
          <button type="button" class="btn btn--primary" (click)="allocateOpen.set(true)">Allocate</button>
        }
      </gggs-page-header>

      <section class="panel stack">
        <div class="res__grid">
          <span><span class="muted">Location</span><br />{{ p.locationName ?? '—' }}{{ p.workModeCode ? ' · ' + p.workModeCode : '' }}</span>
          <span><span class="muted">In the company</span><br />{{ p.yearsInCompany | number: '1.0-1' }} yrs{{ p.joiningDate ? ' (since ' + (p.joiningDate | date: 'MMM y') + ')' : '' }}</span>
          @if (p.exitDate) {
            <span class="res__danger"><span class="muted">Leaving</span><br />{{ p.exitDate | date: 'd MMM y' }}</span>
          }
          @if (p.certifications.length) {
            <span><span class="muted">Certifications</span><br />{{ p.certifications.join(', ') }}</span>
          }
        </div>
        <div class="res__skills">
          @for (skill of p.skills; track skill.skillId) {
            <span class="res__chip" [class.res__chip--primary]="skill.isPrimary">{{ skill.name }} {{ skill.level }}</span>
          } @empty {
            <span class="muted">No skills recorded.</span>
          }
        </div>
      </section>

      <section class="panel stack">
        <h2 class="res__heading">Capacity</h2>
        <div class="res__months">
          @for (cell of p.capacity; track cell.from) {
            <div class="res__month">
              <span class="muted">{{ cell.from | date: 'MMM y' }}</span>
              <strong>{{ cell.allocatedPercent | number: '1.0-0' }}%</strong>
              <span class="muted">{{ cell.billablePercent | number: '1.0-0' }}% billable{{ cell.leavePercent ? ' · ' + (cell.leavePercent | number: '1.0-0') + '% leave' : '' }}</span>
              <gggs-status-badge [status]="cell.state" />
            </div>
          }
        </div>
      </section>

      <section class="panel stack">
        <h2 class="res__heading">Allocations</h2>
        @for (allocation of p.current.concat(p.upcoming); track allocation.memberId) {
          <div class="res__row">
            <a [routerLink]="['/projects', allocation.projectId]"><strong>{{ allocation.projectCode }}</strong> {{ allocation.projectName }}</a>
            <span>{{ allocation.roleName }} · {{ allocation.allocationPercent }}%</span>
            <span class="muted">{{ allocation.startDate | date: 'd MMM y' }} – {{ allocation.endDate ? (allocation.endDate | date: 'd MMM y') : 'ongoing' }}</span>
            <span>{{ allocation.billable ? 'billable' : 'non-billable' }}{{ allocation.isActive ? '' : ' · upcoming' }}</span>
            <span>
              @if (canAllocate) {
                <button type="button" class="btn btn--link btn--sm" (click)="edit(allocation)">Change</button>
              }
            </span>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-calendar" title="Not allocated to any project" />
        }
        @if (p.history.length) {
          <h3 class="res__heading">History</h3>
          @for (allocation of p.history; track allocation.memberId) {
            <div class="res__row">
              <span><strong>{{ allocation.projectCode }}</strong> {{ allocation.projectName }}</span>
              <span>{{ allocation.roleName }} · {{ allocation.allocationPercent }}%</span>
              <span class="muted">{{ allocation.startDate | date: 'd MMM y' }} – {{ allocation.endDate ? (allocation.endDate | date: 'd MMM y') : '' }}</span>
              <span></span>
              <span></span>
            </div>
          }
        }
      </section>

      @if (p.rates || p.canEditRates) {
        <section class="panel stack">
          <div class="toolbar">
            <h2 class="res__heading">Rates and capacity</h2>
            <span class="res__spacer"></span>
            @if (p.canEditRates) {
              <button type="button" class="btn btn--sm" (click)="editRates(p)">Edit</button>
            }
          </div>
          <div class="res__grid">
            <span><span class="muted">Cost rate</span><br />{{ (p.rates?.costRate ?? null) !== null ? (p.rates?.costRate | number: '1.0-2') + ' ' + p.rates?.currency + '/h' : '—' }}</span>
            <span><span class="muted">Billing rate</span><br />{{ (p.rates?.billingRate ?? null) !== null ? (p.rates?.billingRate | number: '1.0-2') + ' ' + p.rates?.currency + '/h' : '—' }}</span>
            <span><span class="muted">Capacity</span><br />{{ p.rates?.capacityHoursPerWeek ?? 40 }} h a week</span>
            <span><span class="muted">Notice period</span><br />{{ (p.rates?.noticePeriodDays ?? null) !== null ? p.rates?.noticePeriodDays + ' days' : '—' }}</span>
            <span><span class="muted">Billable resource</span><br />{{ (p.rates?.isBillable ?? true) ? 'Yes' : 'No' }}</span>
          </div>
          @if (p.rates?.notes) {
            <p class="muted">{{ p.rates?.notes }}</p>
          }
        </section>
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (notice()) {
      <p class="alert alert--success" role="status">{{ notice() }}</p>
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-allocate-dialog [(visible)]="allocateOpen" [employeeId]="employeeId()" (allocated)="allocated($event)" />
    <gggs-allocation-edit-dialog [(visible)]="editOpen" [allocation]="editing()" (changed)="changed($event)" />

    <gggs-form-dialog [(visible)]="ratesOpen" title="Rates and capacity" [busy]="busy()" [error]="ratesError()" (save)="saveRates()">
      <div class="res__grid">
        <label class="field">
          <span class="field__label">Cost rate (per hour)</span>
          <input type="number" min="0" step="0.01" [value]="rates().costRate ?? ''" (input)="patchRates({ costRate: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Billing rate (per hour)</span>
          <input type="number" min="0" step="0.01" [value]="rates().billingRate ?? ''" (input)="patchRates({ billingRate: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Currency</span>
          <input type="text" maxlength="3" [value]="rates().currency" (input)="patchRates({ currency: $any($event.target).value.toUpperCase() })" />
        </label>
        <label class="field">
          <span class="field__label">Hours a week</span>
          <input type="number" min="1" max="80" [value]="rates().capacityHoursPerWeek ?? ''" (input)="patchRates({ capacityHoursPerWeek: num($any($event.target).value) })" />
        </label>
        <label class="field">
          <span class="field__label">Notice period (days)</span>
          <input type="number" min="0" max="365" [value]="rates().noticePeriodDays ?? ''" (input)="patchRates({ noticePeriodDays: num($any($event.target).value) })" />
        </label>
      </div>
      <label class="res__flag">
        <input type="checkbox" [checked]="rates().isBillable" (change)="patchRates({ isBillable: $any($event.target).checked })" />
        <span>Can be billed to clients</span>
      </label>
      <label class="field">
        <span class="field__label">Notes</span>
        <textarea rows="2" maxlength="1000" [value]="rates().notes" (input)="patchRates({ notes: $any($event.target).value })"></textarea>
      </label>
    </gggs-form-dialog>
  `,
  styleUrl: './resources.css',
})
export class ResourceProfilePage {
  private readonly api = inject(ResourcesApi);

  readonly employeeId = input.required<string>();

  protected readonly canAllocate = inject(PermissionStore).has('resource.allocation.manage');
  protected readonly profile = signal<ResourceProfileView | null>(null);
  protected readonly allocateOpen = signal(false);
  protected readonly editOpen = signal(false);
  protected readonly editing = signal<AllocationItem | null>(null);
  protected readonly ratesOpen = signal(false);
  protected readonly rates = signal<RatesForm>({ costRate: null, billingRate: null, currency: 'INR', capacityHoursPerWeek: 40, noticePeriodDays: null, isBillable: true, notes: '' });
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly ratesError = signal<string | null>(null);

  constructor() {
    effect(() => {
      void this.load(this.employeeId());
    });
  }

  protected num(value: string): number | null {
    return value === '' ? null : Number(value);
  }

  protected edit(allocation: AllocationItem): void {
    this.editing.set(allocation);
    this.editOpen.set(true);
  }

  protected allocated(outcome: AllocationOutcome): void {
    this.notice.set(outcomeText(outcome));
    void this.load(this.employeeId());
  }

  protected changed(text: string): void {
    this.notice.set(text);
    void this.load(this.employeeId());
  }

  protected editRates(profile: ResourceProfileView): void {
    const rates = profile.rates;
    this.rates.set({
      costRate: rates?.costRate ?? null,
      billingRate: rates?.billingRate ?? null,
      currency: rates?.currency ?? 'INR',
      capacityHoursPerWeek: rates?.capacityHoursPerWeek ?? 40,
      noticePeriodDays: rates?.noticePeriodDays ?? null,
      isBillable: rates?.isBillable ?? true,
      notes: rates?.notes ?? '',
    });
    this.ratesError.set(null);
    this.ratesOpen.set(true);
  }

  protected patchRates(change: Partial<RatesForm>): void {
    this.rates.update((rates) => ({ ...rates, ...change }));
  }

  protected async saveRates(): Promise<void> {
    const rates = this.rates();
    this.busy.set(true);
    this.ratesError.set(null);
    try {
      this.profile.set(await this.api.saveProfile(this.employeeId(), { ...rates, currency: rates.currency || null, notes: rates.notes || null }));
      this.ratesOpen.set(false);
      this.notice.set('Rates saved.');
    } catch (error) {
      this.ratesError.set(message(error, 'The rates could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(employeeId: string): Promise<void> {
    this.error.set(null);
    try {
      this.profile.set(await this.api.profile(employeeId));
    } catch (error) {
      this.error.set(message(error, 'The profile could not be loaded.'));
    }
  }
}
