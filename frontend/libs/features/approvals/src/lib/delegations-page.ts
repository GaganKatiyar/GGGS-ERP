import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { Delegation } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { WorkflowApi } from '@gggs/shared/workflow';

const today = () => new Date().toISOString().slice(0, 10);

/**
 * "While I am away, my approvals go to her." Tasks assigned during the dates go to the delegate,
 * marked as on the delegator's behalf; tasks already in the inbox stay where they are.
 */
@Component({
  selector: 'gggs-delegations-page',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, PageHeader, FormDialog, EmployeeSelect, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Delegations" subtitle="Who approves for whom while someone is away.">
      <div class="toolbar">
        <button type="button" class="btn btn--primary" (click)="openCreate()"><i class="pi pi-plus" aria-hidden="true"></i> Delegate</button>
        <a class="btn" routerLink="..">Back to inbox</a>
      </div>
    </gggs-page-header>

    @if (loading()) {
      <gggs-skeleton [lines]="4" />
    } @else if (delegations().length === 0) {
      <gggs-empty-state icon="pi pi-arrow-right-arrow-left" title="No delegations" description="Delegate your approvals before you go on leave." />
    } @else {
      <section class="panel">
        <table class="delegations__table">
          <thead>
            <tr>
              <th scope="col">From</th>
              <th scope="col">To</th>
              <th scope="col">Dates</th>
              <th scope="col">Reason</th>
              <th scope="col">Status</th>
              <th scope="col"><span class="delegations__hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (delegation of delegations(); track delegation.id) {
              <tr>
                <td>{{ delegation.delegatorName ?? '—' }}</td>
                <td>{{ delegation.delegateName ?? '—' }}</td>
                <td>{{ delegation.startsOn | date: 'mediumDate' }} – {{ delegation.endsOn | date: 'mediumDate' }}</td>
                <td>{{ delegation.reason ?? '—' }}</td>
                <td><gggs-status-badge [status]="!delegation.isActive ? 'Cancelled' : delegation.isCurrent ? 'Active' : 'Waiting'" /></td>
                <td>
                  @if (delegation.isActive) {
                    <button type="button" class="btn btn--link btn--sm" (click)="revoke(delegation)">Revoke</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </section>
    }
    @if (listError()) {
      <p class="field__error" role="alert">{{ listError() }}</p>
    }

    <gggs-form-dialog [(visible)]="createOpen" title="Delegate approvals" saveLabel="Delegate" [busy]="busy()" [error]="error()" (save)="create()">
      <div class="form-grid" [formGroup]="form">
        @if (managesAll) {
          <label class="field form-grid__full">
            <span class="field__label">Whose approvals <span class="muted">(blank: mine)</span></span>
            <gggs-employee-select formControlName="delegatorEmployeeId" ariaLabel="Whose approvals" />
          </label>
        }
        <label class="field form-grid__full">
          <span class="field__label">Delegate to *</span>
          <gggs-employee-select formControlName="delegateEmployeeId" ariaLabel="Delegate to" />
        </label>
        <label class="field">
          <span class="field__label">From *</span>
          <input type="date" formControlName="startsOn" />
        </label>
        <label class="field">
          <span class="field__label">Until *</span>
          <input type="date" formControlName="endsOn" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Reason</span>
          <input type="text" formControlName="reason" maxlength="300" placeholder="Annual leave" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .delegations__table {
      width: 100%;
      border-collapse: collapse;
    }

    .delegations__table th,
    .delegations__table td {
      padding: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
    }

    .delegations__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class DelegationsPage {
  private readonly api = inject(WorkflowApi);

  protected readonly managesAll = inject(PermissionStore).scopeFor('workflow.delegation.manage') === 'Tenant';

  protected readonly delegations = signal<readonly Delegation[]>([]);
  protected readonly loading = signal(true);
  protected readonly listError = signal<string | null>(null);
  protected readonly createOpen = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    delegatorEmployeeId: new FormControl<string | null>(null),
    delegateEmployeeId: new FormControl<string | null>(null, Validators.required),
    startsOn: new FormControl(today(), { nonNullable: true, validators: Validators.required }),
    endsOn: new FormControl(today(), { nonNullable: true, validators: Validators.required }),
    reason: new FormControl<string | null>(null),
  });

  constructor() {
    void this.load();
  }

  protected openCreate(): void {
    this.form.reset({ delegatorEmployeeId: null, delegateEmployeeId: null, startsOn: today(), endsOn: today(), reason: null });
    this.error.set(null);
    this.createOpen.set(true);
  }

  protected async create(): Promise<void> {
    const value = this.form.getRawValue();
    if (this.form.invalid || !value.delegateEmployeeId) {
      this.error.set('Choose who approves in your place, and the dates.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.createDelegation({
        delegatorEmployeeId: value.delegatorEmployeeId,
        delegateEmployeeId: value.delegateEmployeeId,
        startsOn: value.startsOn,
        endsOn: value.endsOn,
        reason: value.reason?.trim() || null,
      });
      this.createOpen.set(false);
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The delegation could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async revoke(delegation: Delegation): Promise<void> {
    this.listError.set(null);
    try {
      await this.api.revokeDelegation(delegation.id);
      await this.load();
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'The delegation could not be revoked.');
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.delegations.set(await this.api.delegations(this.managesAll ? 'all' : 'mine'));
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'Delegations could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
