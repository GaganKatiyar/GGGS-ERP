import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button } from '@gggs/shared/ui';
import type { GoalListItem } from '@gggs/shared/models';
import { PerformanceApi } from '../data/performance-api';

/** One employee's goals for a cycle - a list with an inline check-in form per goal, and a form to
 * set a new goal while the cycle is Open. */
@Component({
  selector: 'gggs-employee-goals-panel',
  imports: [ReactiveFormsModule, HasPermissionDirective, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (goals(); as items) {
      @if (items.length > 0) {
        <table class="goals-table">
          <thead>
            <tr>
              <th scope="col">Title</th>
              <th scope="col">Progress</th>
              <th scope="col">Status</th>
              <th scope="col"></th>
            </tr>
          </thead>
          <tbody>
            @for (goal of items; track goal.id) {
              <tr>
                <td>{{ goal.title }}</td>
                <td>{{ goal.currentValue }} / {{ goal.targetValue }} {{ goal.unit }}</td>
                <td>{{ goal.status }}</td>
                <td class="goal-actions" *gggsHasPermission="'performance.goal.manage-own'">
                  @if (goal.status === 'Active') {
                    <gggs-button size="sm" (click)="toggleCheckIn(goal.id)">
                      {{ expandedGoalId() === goal.id ? 'Cancel' : 'Check in' }}
                    </gggs-button>
                  }
                </td>
              </tr>
              @if (expandedGoalId() === goal.id) {
                <tr>
                  <td colspan="4" class="check-in-cell">
                    <form [formGroup]="checkInForm" (ngSubmit)="submitCheckIn(goal.id)" novalidate class="check-in-form">
                      <label class="field">
                        <span class="field__label">Date</span>
                        <input type="date" formControlName="checkInDate" required />
                      </label>
                      <label class="field">
                        <span class="field__label">Value</span>
                        <input type="number" formControlName="value" required />
                      </label>
                      <label class="field">
                        <span class="field__label">Comment (optional)</span>
                        <input type="text" formControlName="comment" maxlength="500" />
                      </label>
                      <gggs-button type="submit" size="sm" [disabled]="isSaving()">
                        {{ isSaving() ? 'Saving…' : 'Record check-in' }}
                      </gggs-button>
                    </form>
                  </td>
                </tr>
              }
            }
          </tbody>
        </table>
      } @else {
        <p class="muted">No goals yet.</p>
      }
    } @else {
      <p class="muted">Loading…</p>
    }

    @if (cycleIsOpen()) {
      <form
        *gggsHasPermission="'performance.goal.manage-own'"
        [formGroup]="createForm"
        (ngSubmit)="submitCreate()"
        novalidate
        class="create-form"
      >
        <label class="field">
          <span class="field__label">Title</span>
          <input type="text" formControlName="title" maxlength="200" required />
        </label>

        <label class="field">
          <span class="field__label">Target value</span>
          <input type="number" formControlName="targetValue" min="1" required />
        </label>

        <label class="field">
          <span class="field__label">Unit</span>
          <input type="text" formControlName="unit" maxlength="30" placeholder="%" required />
        </label>

        <label class="field">
          <span class="field__label">Due date</span>
          <input type="date" formControlName="dueDate" required />
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Creating…' : 'Set goal' }}
        </gggs-button>
      </form>
    } @else {
      <p class="muted" *gggsHasPermission="'performance.goal.manage-own'">
        The cycle must be Open before a goal can be set against it.
      </p>
    }
  `,
  styles: `
    .goals-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .goals-table th,
    .goals-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .goal-actions {
      display: flex;
      gap: 0.5rem;
    }

    .check-in-cell {
      background: var(--gggs-color-surface-sunken, #f5f6f8);
      padding: 0.75rem 1rem;
    }

    .check-in-form,
    .create-form {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
      align-items: end;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
    }

    .field__label {
      font-weight: 600;
    }

    .field__error {
      grid-column: 1 / -1;
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 1rem;
    }
  `,
})
export class EmployeeGoalsPanel {
  private readonly api = inject(PerformanceApi);
  private readonly formBuilder = inject(FormBuilder);

  readonly cycleId = input.required<string>();
  readonly employeeId = input.required<string>();
  readonly cycleIsOpen = input.required<boolean>();

  protected readonly goals = signal<readonly GoalListItem[] | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly expandedGoalId = signal<string | null>(null);

  protected readonly createForm = this.formBuilder.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    targetValue: [0, [Validators.required, Validators.min(1)]],
    unit: ['%', Validators.required],
    dueDate: ['', Validators.required],
  });

  protected readonly checkInForm = this.formBuilder.group({
    checkInDate: ['', Validators.required],
    value: [0, Validators.required],
    comment: [''],
  });

  constructor() {
    void this.load();
  }

  protected toggleCheckIn(goalId: string): void {
    this.expandedGoalId.set(this.expandedGoalId() === goalId ? null : goalId);
    this.checkInForm.reset();
  }

  protected async submitCheckIn(goalId: string): Promise<void> {
    if (this.checkInForm.invalid || this.isSaving()) {
      this.checkInForm.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const value = this.checkInForm.getRawValue();

    try {
      await this.api.checkInGoal(goalId, {
        checkInDate: value.checkInDate ?? '',
        value: value.value ?? 0,
        comment: value.comment || undefined,
      });

      this.expandedGoalId.set(null);
      await this.load();
    } catch {
      this.errorMessage.set('The check-in could not be recorded. Please try again.');
    } finally {
      this.isSaving.set(false);
    }
  }

  protected async submitCreate(): Promise<void> {
    if (this.createForm.invalid || this.isSaving()) {
      this.createForm.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const value = this.createForm.getRawValue();

    try {
      await this.api.createGoal({
        cycleId: this.cycleId(),
        employeeId: this.employeeId(),
        title: value.title ?? '',
        targetValue: value.targetValue ?? 0,
        unit: value.unit ?? '%',
        dueDate: value.dueDate ?? '',
      });

      this.createForm.reset({ unit: '%' });
      await this.load();
    } catch {
      this.errorMessage.set('The goal could not be set. Please try again.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async load(): Promise<void> {
    this.goals.set(await this.api.listGoalsForEmployee(this.employeeId()));
  }
}
