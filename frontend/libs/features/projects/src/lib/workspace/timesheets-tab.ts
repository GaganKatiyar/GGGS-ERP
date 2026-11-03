import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { TimesheetsApi, addDaysIso, isoToday } from '@gggs/shared/delivery';
import type { ProjectHoursRow, TimesheetApprovalRow } from '@gggs/shared/models';
import { EmptyState, Skeleton, StatusBadge } from '@gggs/shared/ui';

interface PersonHours {
  readonly employeeId: string;
  readonly name: string;
  readonly hours: number;
  readonly approved: number;
}

/** A project's timesheets: who booked how much in a period, and the hours still waiting on a decision. */
@Component({
  selector: 'gggs-project-timesheets-tab',
  imports: [DatePipe, DecimalPipe, RouterLink, StatusBadge, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel stack">
      <div class="toolbar proj-ts__filters">
        <label class="field">
          <span class="field__label">From</span>
          <input type="date" [value]="from()" (change)="from.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">To</span>
          <input type="date" [value]="to()" (change)="to.set($any($event.target).value)" />
        </label>
      </div>

      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }

      @if (people(); as list) {
        <h2 class="proj-ts__heading">Hours by person</h2>
        @if (list.length) {
          <div class="proj-ts__scroll" tabindex="0" role="region" aria-label="Hours by person">
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">Person</th>
                  <th scope="col" class="proj-ts__num">Booked</th>
                  <th scope="col" class="proj-ts__num">Approved</th>
                  <th scope="col" class="proj-ts__num">Waiting</th>
                </tr>
              </thead>
              <tbody>
                @for (person of list; track person.employeeId) {
                  <tr>
                    <td>{{ person.name }}</td>
                    <td class="proj-ts__num">{{ person.hours | number: '1.2-2' }}</td>
                    <td class="proj-ts__num">{{ person.approved | number: '1.2-2' }}</td>
                    <td class="proj-ts__num">{{ person.hours - person.approved | number: '1.2-2' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else {
          <gggs-empty-state icon="pi pi-stopwatch" title="No hours in this period" description="Submitted and approved hours on this project appear here." />
        }

        <h2 class="proj-ts__heading">Waiting for a decision</h2>
        @for (row of waiting(); track row.id) {
          <div class="proj-ts__row">
            <span>{{ row.employeeName ?? row.employeeCode }}</span>
            <span>Week of {{ row.weekStart | date: 'd MMM y' }}</span>
            <span>{{ row.hours | number: '1.0-2' }} h</span>
            <span class="muted">{{ row.ownerName ?? '' }}</span>
            <gggs-status-badge [status]="row.status" />
          </div>
        } @empty {
          <p class="muted">Nothing is waiting on this project.</p>
        }
        @if (waiting().length) {
          <div class="toolbar"><a class="btn btn--sm" routerLink="/approvals">Open approvals inbox</a></div>
        }
      } @else if (!error()) {
        <gggs-skeleton [lines]="5" />
      }
    </section>
  `,
  styles: `
    .proj-ts__filters {
      align-items: end;
    }

    .proj-ts__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .proj-ts__scroll {
      overflow-x: auto;
    }

    .proj-ts__num {
      text-align: end;
    }

    .proj-ts__row {
      display: grid;
      grid-template-columns: minmax(0, 2fr) minmax(0, 1.5fr) 5rem minmax(0, 1.5fr) 7rem;
      gap: var(--gggs-space-3);
      align-items: center;
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    @media (max-width: 56rem) {
      .proj-ts__row {
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
      }
    }
  `,
})
export class ProjectTimesheetsTab {
  private readonly api = inject(TimesheetsApi);

  readonly projectId = input.required<string>();

  protected readonly from = signal(addDaysIso(isoToday(), -30));
  protected readonly to = signal(isoToday());
  protected readonly hours = signal<readonly ProjectHoursRow[] | null>(null);
  protected readonly waiting = signal<readonly TimesheetApprovalRow[]>([]);
  protected readonly error = signal<string | null>(null);

  protected readonly people = computed<readonly PersonHours[] | null>(() => {
    const rows = this.hours();
    if (!rows) {
      return null;
    }

    const people = new Map<string, PersonHours>();
    for (const row of rows) {
      const person = people.get(row.employeeId) ?? { employeeId: row.employeeId, name: row.employeeName ?? '—', hours: 0, approved: 0 };
      people.set(row.employeeId, { ...person, hours: person.hours + row.hours, approved: person.approved + row.approvedHours });
    }

    return [...people.values()].sort((first, second) => first.name.localeCompare(second.name));
  });

  constructor() {
    effect(() => {
      void this.load(this.projectId(), this.from(), this.to());
    });
  }

  private async load(projectId: string, from: string, to: string): Promise<void> {
    if (!from || !to) {
      return;
    }

    this.error.set(null);
    try {
      const [hours, waiting] = await Promise.all([this.api.projectHours(projectId, from, to), this.api.approvals({ projectId, status: 'Submitted', pageSize: 100 })]);
      this.hours.set(hours);
      this.waiting.set(waiting.items);
    } catch (error) {
      this.hours.set(null);
      this.error.set(error instanceof ApiError ? error.message : "This project's timesheets could not be loaded.");
    }
  }
}
