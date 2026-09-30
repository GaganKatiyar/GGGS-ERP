import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmployeePicker } from '@gggs/shared/employee-reference';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { EmployeeListItem, PerformanceCycle } from '@gggs/shared/models';
import { PerformanceApi } from '../data/performance-api';
import { EmployeeGoalsPanel } from '../goals/employee-goals-panel';
import { EmployeeReviewPanel } from '../reviews/employee-review-panel';

/** One cycle: its status actions, then goals and a review for whichever employee is picked. */
@Component({
  selector: 'gggs-performance-cycle-detail-page',
  imports: [RouterLink, HasPermissionDirective, EmployeePicker, PageHeader, Card, Button, EmployeeGoalsPanel, EmployeeReviewPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (cycle(); as c) {
      <gggs-page-header [title]="c.name" [subtitle]="c.status">
        <a routerLink="/performance/cycles" class="btn">Back</a>
      </gggs-page-header>

      <gggs-card title="Cycle" class="section">
        <div class="actions" *gggsHasPermission="'performance.cycle.manage'">
          @if (c.status === 'Draft') {
            <gggs-button size="sm" (click)="openCycle()" [disabled]="isActing()">Open</gggs-button>
          }
          @if (c.status === 'Open') {
            <gggs-button size="sm" (click)="closeCycle()" [disabled]="isActing()">Close</gggs-button>
          }
        </div>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }
      </gggs-card>

      <gggs-card title="Employee" class="section">
        <span class="field__label" id="employee-label">Look up an employee</span>
        <gggs-employee-picker aria-labelledby="employee-label" (employeeSelected)="onEmployeeSelected($event)" />
        @if (selectedEmployeeName()) {
          <p class="selected-name">{{ selectedEmployeeName() }}</p>
        }
      </gggs-card>

      @if (selectedEmployeeId(); as employeeId) {
        <gggs-card title="Goals" class="section">
          <gggs-employee-goals-panel [cycleId]="c.id" [employeeId]="employeeId" [cycleIsOpen]="c.status === 'Open'" />
        </gggs-card>

        <gggs-card title="Review">
          <gggs-employee-review-panel [cycleId]="c.id" [employeeId]="employeeId" [cycleIsOpen]="c.status === 'Open'" />
        </gggs-card>
      }
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .actions {
      display: flex;
      gap: 0.75rem;
    }

    .field__label {
      font-weight: 600;
      font-size: 0.875rem;
    }

    .selected-name {
      margin: 0.5rem 0 0;
      font-size: 0.8125rem;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .field__error {
      margin: 0.75rem 0 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .btn {
      padding: 0.5rem 1rem;
      font-weight: 600;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #ffffff);
      text-decoration: none;
      color: inherit;
      cursor: pointer;
    }
  `,
})
export class PerformanceCycleDetailPage {
  private readonly api = inject(PerformanceApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly cycle = signal<PerformanceCycle | null>(null);
  protected readonly isActing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly selectedEmployeeId = signal<string | null>(null);
  protected readonly selectedEmployeeName = signal<string | null>(null);

  private readonly cycleId = this.route.snapshot.paramMap.get('id') ?? '';

  constructor() {
    void this.load();
  }

  protected onEmployeeSelected(employee: EmployeeListItem): void {
    this.selectedEmployeeId.set(employee.id);
    this.selectedEmployeeName.set(employee.displayName);
  }

  protected async openCycle(): Promise<void> {
    await this.act(() => this.api.openCycle(this.cycleId));
  }

  protected async closeCycle(): Promise<void> {
    await this.act(() => this.api.closeCycle(this.cycleId));
  }

  private async act(run: () => Promise<PerformanceCycle>): Promise<void> {
    if (this.isActing()) {
      return;
    }

    this.isActing.set(true);
    this.errorMessage.set(null);

    try {
      this.cycle.set(await run());
    } catch {
      this.errorMessage.set('That action could not be completed. Please try again.');
    } finally {
      this.isActing.set(false);
    }
  }

  private async load(): Promise<void> {
    this.cycle.set(await this.api.getCycle(this.cycleId));
  }
}
