import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal, untracked } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { ProjectWorkload } from '@gggs/shared/models';
import { EmptyState, Skeleton } from '@gggs/shared/ui';

/** Who has what, week by week: the hours left on their tasks against the hours this project has of them. */
@Component({
  selector: 'gggs-workload-tab',
  imports: [DatePipe, DecimalPipe, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <div class="stack">
        <p class="muted wl__note">
          Each cell is planned hours against capacity. What is left on a task spreads over the working days to its due date, and capacity comes from each person's allocation here.
          Work on other projects and leave are not counted.
          @if (v.unassignedTasks) {
            <strong>{{ v.unassignedTasks }} open {{ v.unassignedTasks === 1 ? 'task has' : 'tasks have' }} nobody assigned.</strong>
          }
        </p>
        <section class="panel wl__scroll">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">Person</th>
                <th scope="col" class="wl__num">Open</th>
                <th scope="col" class="wl__num">Late</th>
                <th scope="col" class="wl__num">Hours left</th>
                @for (week of v.weeks; track week) {
                  <th scope="col" class="wl__num">{{ week | date: 'd MMM' }}</th>
                }
                <th scope="col" class="wl__num" title="Hours on tasks with no due date">No date</th>
                <th scope="col" class="wl__num" title="Tasks with no estimate">No estimate</th>
              </tr>
            </thead>
            <tbody>
              @for (person of v.people; track person.employeeId) {
                <tr>
                  <th scope="row">{{ person.employeeName ?? 'Someone' }}</th>
                  <td class="wl__num">{{ person.openTasks }}</td>
                  <td class="wl__num" [class.wl__over]="person.overdue > 0">{{ person.overdue }}</td>
                  <td class="wl__num">{{ person.remainingHours | number: '1.0-1' }}</td>
                  @for (week of person.weeks; track week.weekStart) {
                    <td class="wl__num" [class.wl__over]="week.plannedHours > week.capacityHours" [class.wl__idle]="week.plannedHours === 0">
                      {{ week.plannedHours | number: '1.0-1' }} / {{ week.capacityHours | number: '1.0-0' }}
                    </td>
                  }
                  <td class="wl__num">{{ person.unscheduledHours | number: '1.0-1' }}</td>
                  <td class="wl__num">{{ person.unestimated }}</td>
                </tr>
              } @empty {
                <tr><td [attr.colspan]="v.weeks.length + 6"><gggs-empty-state icon="pi pi-users" title="Nobody on the team yet" /></td></tr>
              }
            </tbody>
          </table>
        </section>
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
  `,
  styles: `
    .wl__note {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .wl__scroll {
      overflow-x: auto;
    }

    .wl__num {
      text-align: end !important;
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }

    .wl__over {
      color: var(--gggs-color-danger);
      font-weight: 600;
    }

    .wl__idle {
      color: var(--gggs-color-text-muted);
    }
  `,
})
export class WorkloadTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();

  protected readonly view = signal<ProjectWorkload | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const id = this.projectId();
      untracked(() => {
        this.api.workload(id).then(
          (view) => this.view.set(view),
          (error: unknown) => this.error.set(error instanceof ApiError ? error.message : 'The workload could not be loaded.'),
        );
      });
    });
  }
}
