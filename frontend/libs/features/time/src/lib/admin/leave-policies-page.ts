import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import type { LeavePlanAssignment, LeavePlanSummary } from '@gggs/shared/models';
import { LeaveApi } from '@gggs/shared/time';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { MONTHS } from './leave-policy-model';

/**
 * Leave policies (spec §20): named sets of leave types and rules. Each engagement type follows one
 * (Policy assignment); this page lists them, starts new ones and copies existing ones.
 */
@Component({
  selector: 'gggs-leave-policies-page',
  imports: [RouterLink, PageHeader, StatusBadge, FormDialog, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Leave policies" subtitle="Build a policy once, then assign it to the engagement types that follow it.">
      <div class="toolbar">
        <a class="btn" routerLink="../policy-assignment">Policy assignment</a>
        <a class="btn" routerLink="../types">Leave types</a>
        <button type="button" class="btn btn--primary" (click)="openNew()"><i class="pi pi-plus" aria-hidden="true"></i> New policy</button>
      </div>
    </gggs-page-header>

    @if (unassigned().length) {
      <p class="alert alert--warning" role="status">
        <i class="pi pi-exclamation-triangle" aria-hidden="true"></i>
        <span>No leave policy is assigned to {{ unassigned().join(', ') }}. Employees of {{ unassigned().length === 1 ? 'that type' : 'those types' }} cannot apply for leave. <a routerLink="../policy-assignment">Assign one</a></span>
      </p>
    }

    @if (loading()) {
      <gggs-skeleton [lines]="5" />
    } @else if (plans().length === 0) {
      <gggs-empty-state icon="pi pi-book" title="No leave policies yet" description="Create one, add its leave types and rules, then assign it to engagement types." actionLabel="New policy" (action)="openNew()" />
    } @else {
      <div class="panel panel--flush table-wrap" tabindex="0" role="region" aria-label="Leave policies">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">Policy</th>
              <th scope="col">Leave year</th>
              <th scope="col">Leave types</th>
              <th scope="col">Assigned to</th>
              <th scope="col">Exceptions</th>
              <th scope="col">Status</th>
              <th scope="col"><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (plan of plans(); track plan.id) {
              <tr>
                <td>
                  <a [routerLink]="[plan.id]"><strong>{{ plan.name }}</strong></a>
                  <span class="policies__code">{{ plan.code }}</span>
                </td>
                <td>{{ plan.leaveYear }}</td>
                <td>{{ plan.leaveTypeCount }}</td>
                <td>
                  @for (code of plan.assignedTo; track code) {
                    <span class="policies__chip">{{ label(code) }}</span>
                  } @empty {
                    <span class="muted">Not assigned</span>
                  }
                </td>
                <td>{{ plan.exceptionCount || '—' }}</td>
                <td><gggs-status-badge [status]="plan.isActive ? 'Active' : 'Inactive'" /></td>
                <td class="policies__actions">
                  <a class="btn btn--link btn--sm" [routerLink]="[plan.id]">Edit</a>
                  <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Copy ' + plan.name" (click)="copy(plan)">Copy</button>
                  <button type="button" class="btn btn--link btn--sm" (click)="toggle(plan)">{{ plan.isActive ? 'Retire' : 'Restore' }}</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="newOpen" title="New leave policy" saveLabel="Create and add leave types" [busy]="busy()" [error]="dialogError()" (save)="create()">
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Name *</span>
          <input type="text" maxlength="100" placeholder="Full-time India" [value]="name()" (input)="name.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Code *</span>
          <input type="text" maxlength="32" placeholder="FT_INDIA" [value]="code()" (input)="code.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Leave year starts in</span>
          <select [value]="startMonth()" (change)="startMonth.set(+$any($event.target).value)">
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
    </gggs-form-dialog>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .policies__code { display: block; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    .policies__chip { display: inline-block; margin: 0 var(--gggs-space-1) var(--gggs-space-1) 0; padding: 0 var(--gggs-space-2); border-radius: var(--gggs-radius-pill); background: var(--gggs-color-surface-sunken); font-size: var(--gggs-font-size-xs); }
    .policies__actions { white-space: nowrap; }
  `,
})
export class LeavePoliciesPage {
  private readonly api = inject(LeaveApi);
  private readonly router = inject(Router);

  protected readonly months = MONTHS;
  protected readonly loading = signal(true);
  protected readonly plans = signal<readonly LeavePlanSummary[]>([]);
  protected readonly assignments = signal<readonly LeavePlanAssignment[]>([]);
  protected readonly error = signal<string | null>(null);

  protected readonly newOpen = signal(false);
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);
  protected readonly name = signal('');
  protected readonly code = signal('');
  protected readonly description = signal('');
  protected readonly startMonth = signal(1);

  protected readonly unassigned = computed(() => this.assignments().filter((row) => !row.planId).map((row) => row.label));
  private readonly labels = computed(() => new Map(this.assignments().map((row) => [row.employmentTypeCode, row.label])));

  constructor() {
    void this.load();
  }

  protected label(code: string): string {
    return this.labels().get(code) ?? code;
  }

  protected openNew(): void {
    this.name.set('');
    this.code.set('');
    this.description.set('');
    this.startMonth.set(1);
    this.dialogError.set(null);
    this.newOpen.set(true);
  }

  protected async create(): Promise<void> {
    if (!this.name().trim() || !this.code().trim()) {
      this.dialogError.set('Give the policy a name and a code.');
      return;
    }

    this.busy.set(true);
    this.dialogError.set(null);
    try {
      const plan = await this.api.savePlan(null, { code: this.code().trim(), name: this.name().trim(), description: this.description().trim() || null, leaveYearStartMonth: this.startMonth() });
      this.newOpen.set(false);
      await this.router.navigate(['/leave/policies', plan.id]);
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The policy could not be created.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async copy(plan: LeavePlanSummary): Promise<void> {
    await this.act(async () => {
      const copy = await this.api.copyPlan(plan.id);
      await this.router.navigate(['/leave/policies', copy.id]);
    });
  }

  protected async toggle(plan: LeavePlanSummary): Promise<void> {
    await this.act(async () => {
      await this.api.setPlanActive(plan.id, !plan.isActive);
      await this.load();
    });
  }

  private async act(call: () => Promise<void>): Promise<void> {
    this.error.set(null);
    try {
      await call();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    }
  }

  private async load(): Promise<void> {
    try {
      const [plans, assignments] = await Promise.all([this.api.plans(), this.api.assignments()]);
      this.plans.set(plans);
      this.assignments.set(assignments);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Leave policies could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
