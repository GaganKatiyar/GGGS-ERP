import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { OnboardingCase, OnboardingTask } from '@gggs/shared/models';
import { OnboardingApi } from '../data/onboarding-api';

/** One case's checklist: every task copied from its template, with a Complete action per task. */
@Component({
  selector: 'gggs-onboarding-case-detail-page',
  imports: [RouterLink, HasPermissionDirective, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (onboardingCase(); as c) {
      <gggs-page-header title="Onboarding case" [subtitle]="c.status + ' - ' + c.completionPercent + '% complete'">
        <a routerLink="/onboarding" class="btn">Back</a>
      </gggs-page-header>

      <gggs-card title="Tasks">
        @if (tasks(); as items) {
          <table class="tasks-table">
            <thead>
              <tr>
                <th scope="col">Title</th>
                <th scope="col">Assignee</th>
                <th scope="col">Due date</th>
                <th scope="col">Status</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              @for (task of items; track task.id) {
                <tr>
                  <td>{{ task.title }}</td>
                  <td>{{ task.assigneeRole }}</td>
                  <td>{{ task.dueDate }}</td>
                  <td>{{ task.status }}</td>
                  <td *gggsHasPermission="'onboarding.task.complete'">
                    @if (task.status === 'NotStarted') {
                      <gggs-button size="sm" (click)="complete(task.id)" [disabled]="isActing()">Complete</gggs-button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">Loading…</p>
        }

        @if (c.status === 'InProgress') {
          <div class="actions" *gggsHasPermission="'onboarding.case.manage'">
            <gggs-button (click)="cancel()" [disabled]="isActing()">Cancel case</gggs-button>
          </div>
        }

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }
      </gggs-card>
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    .tasks-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .tasks-table th,
    .tasks-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .actions {
      display: flex;
      gap: 0.75rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .field__error {
      margin: 0.75rem 0 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
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
export class OnboardingCaseDetailPage {
  private readonly api = inject(OnboardingApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly onboardingCase = signal<OnboardingCase | null>(null);
  protected readonly tasks = signal<readonly OnboardingTask[] | null>(null);
  protected readonly isActing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  private readonly caseId = this.route.snapshot.paramMap.get('id') ?? '';

  constructor() {
    void this.load();
  }

  protected async complete(taskId: string): Promise<void> {
    if (this.isActing()) {
      return;
    }

    this.isActing.set(true);
    this.errorMessage.set(null);

    try {
      await this.api.completeTask(taskId);
      await this.load();
    } catch {
      this.errorMessage.set('That task could not be completed. Please try again.');
    } finally {
      this.isActing.set(false);
    }
  }

  protected async cancel(): Promise<void> {
    if (this.isActing()) {
      return;
    }

    this.isActing.set(true);
    this.errorMessage.set(null);

    try {
      this.onboardingCase.set(await this.api.cancelCase(this.caseId));
    } catch {
      this.errorMessage.set('The case could not be cancelled. Please try again.');
    } finally {
      this.isActing.set(false);
    }
  }

  private async load(): Promise<void> {
    const [onboardingCase, tasks] = await Promise.all([
      this.api.getCase(this.caseId),
      this.api.listCaseTasks(this.caseId),
    ]);

    this.onboardingCase.set(onboardingCase);
    this.tasks.set(tasks);
  }
}
