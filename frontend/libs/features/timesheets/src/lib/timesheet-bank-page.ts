import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { TimesheetsApi, addDaysIso, isoToday } from '@gggs/shared/delivery';
import type { TimesheetBankWeek, TimesheetStatus } from '@gggs/shared/models';
import { EmptyState, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { TimesheetTabs } from './timesheet-tabs';

const STATUSES: readonly TimesheetStatus[] = ['Draft', 'Submitted', 'Approved', 'Rejected'];

/** The Timesheet bank: every week already filled in, with what went to each project and who approves it. */
@Component({
  selector: 'gggs-timesheet-bank-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, StatusBadge, EmptyState, Skeleton, TimesheetTabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My timesheet" subtitle="Weeks you have filled in, split by project." />
    <gggs-timesheet-tabs />

    <section class="panel stack">
      <div class="toolbar bank__filters">
        <label class="field">
          <span class="field__label">From</span>
          <input type="date" [value]="from()" (change)="from.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">To</span>
          <input type="date" [value]="to()" (change)="to.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Status</span>
          <select [value]="status()" (change)="status.set($any($event.target).value)">
            <option value="">All</option>
            @for (option of statuses; track option) {
              <option [value]="option">{{ option }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Project</span>
          <select [value]="projectId()" (change)="projectId.set($any($event.target).value)">
            <option value="">All</option>
            @for (project of projects(); track project.id) {
              <option [value]="project.id" [selected]="project.id === projectId()">{{ project.label }}</option>
            }
          </select>
        </label>
        <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="load()">Filter</button>
      </div>

      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }

      @if (weeks(); as list) {
        @if (list.length) {
          <div class="toolbar">
            <button type="button" class="btn btn--link btn--sm" (click)="expandAll()">Expand all</button>
            <button type="button" class="btn btn--link btn--sm" (click)="open.set(emptySet)">Collapse all</button>
          </div>
          <div class="bank__scroll" tabindex="0" role="region" aria-label="Timesheet weeks">
            <table class="table bank__table">
              <thead>
                <tr>
                  <th scope="col">Period</th>
                  <th scope="col" class="bank__num">Working hours</th>
                  <th scope="col" class="bank__num">Leave hours</th>
                  <th scope="col" class="bank__num">Holiday hours</th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                @for (week of list; track week.id) {
                  <tr>
                    <td>
                      <button type="button" class="btn btn--link btn--sm" [attr.aria-expanded]="open().has(week.id)" [attr.aria-label]="(open().has(week.id) ? 'Collapse ' : 'Expand ') + week.weekStart" (click)="toggle(week.id)">
                        <i [class]="open().has(week.id) ? 'pi pi-minus-circle' : 'pi pi-plus-circle'" aria-hidden="true"></i>
                      </button>
                      <a [routerLink]="['/me/timesheet']" [queryParams]="{ week: week.weekStart }">{{ week.weekStart | date: 'd MMM y' }} – {{ week.weekEnd | date: 'd MMM y' }}</a>
                      @if (week.isLate) {
                        <span class="muted"> · late</span>
                      }
                    </td>
                    <td class="bank__num">{{ week.workingHours | number: '1.2-2' }}</td>
                    <td class="bank__num">{{ week.leaveHours | number: '1.2-2' }}</td>
                    <td class="bank__num">{{ week.holidayHours | number: '1.2-2' }}</td>
                    <td><gggs-status-badge [status]="week.status" /></td>
                  </tr>
                  @if (open().has(week.id)) {
                    <tr>
                      <td colspan="5" class="bank__detail">
                        <table class="table">
                          <thead>
                            <tr>
                              <th scope="col">Project</th>
                              <th scope="col" class="bank__num">Hours</th>
                              <th scope="col" class="bank__num">Billable</th>
                              <th scope="col">Approver</th>
                            </tr>
                          </thead>
                          <tbody>
                            @for (project of week.projects; track project.projectId ?? 'none') {
                              <tr>
                                <td>{{ project.projectCode ? project.projectCode + ' · ' + project.projectName : 'Other time' }}</td>
                                <td class="bank__num">{{ project.hours | number: '1.2-2' }}</td>
                                <td class="bank__num">{{ project.billableHours | number: '1.2-2' }}</td>
                                <td>{{ project.approverName ?? 'Your approver' }}</td>
                              </tr>
                            } @empty {
                              <tr>
                                <td colspan="4" class="muted">No hours booked.</td>
                              </tr>
                            }
                          </tbody>
                        </table>
                      </td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>
        } @else {
          <gggs-empty-state icon="pi pi-inbox" title="No timesheets in this period" description="Try a wider period, or another status or project." />
        }
      } @else if (!error()) {
        <gggs-skeleton [lines]="6" />
      }
    </section>
  `,
  styles: `
    .bank__filters {
      align-items: end;
      flex-wrap: wrap;
    }

    .bank__scroll {
      overflow-x: auto;
    }

    .bank__table {
      min-width: 44rem;
    }

    .bank__num {
      text-align: end;
    }

    .bank__detail {
      padding-inline-start: var(--gggs-space-6);
      background: var(--gggs-color-surface-sunken, transparent);
    }
  `,
})
export class TimesheetBankPage {
  private readonly api = inject(TimesheetsApi);

  protected readonly statuses = STATUSES;
  protected readonly emptySet: ReadonlySet<string> = new Set();
  protected readonly from = signal(addDaysIso(isoToday(), -182));
  protected readonly to = signal(isoToday());
  protected readonly status = signal<TimesheetStatus | ''>('');
  protected readonly projectId = signal('');
  protected readonly weeks = signal<readonly TimesheetBankWeek[] | null>(null);
  protected readonly open = signal<ReadonlySet<string>>(this.emptySet);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Every project seen so far, so filtering by one does not empty the list of the others. */
  private readonly seen = signal<ReadonlyMap<string, string>>(new Map());
  protected readonly projects = computed(() =>
    [...this.seen()].map(([id, label]) => ({ id, label })).sort((first, second) => first.label.localeCompare(second.label)),
  );

  constructor() {
    void this.load();
  }

  protected toggle(id: string): void {
    this.open.update((open) => {
      const next = new Set(open);
      if (!next.delete(id)) {
        next.add(id);
      }
      return next;
    });
  }

  protected expandAll(): void {
    this.open.set(new Set(this.weeks()?.map((week) => week.id)));
  }

  protected async load(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const weeks = await this.api.bank({ from: this.from(), to: this.to(), status: this.status(), projectId: this.projectId() });
      this.weeks.set(weeks);
      this.seen.update((seen) => {
        const next = new Map(seen);
        for (const project of weeks.flatMap((week) => week.projects)) {
          if (project.projectId) {
            next.set(project.projectId, `${project.projectCode} · ${project.projectName}`);
          }
        }
        return next;
      });
    } catch (error) {
      this.weeks.set(null);
      this.error.set(error instanceof ApiError ? error.message : 'The timesheet bank could not be loaded.');
    } finally {
      this.busy.set(false);
    }
  }
}
