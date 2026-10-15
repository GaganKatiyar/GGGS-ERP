import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PermissionStore } from '@gggs/core/permissions';
import { ResourcesApi, SkillsApi, isoToday } from '@gggs/shared/delivery';
import type { AllocationOutcome, BenchItem, CalendarRow, CapacityCell, ForecastReport, SkillItem, TransferItem, TransferStatus, UtilizationGrouping, UtilizationReport } from '@gggs/shared/models';
import { HOUR_CATEGORIES, humanize } from '@gggs/shared/models';
import { Chart, EmptyState, KpiCard, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { AllocateDialog, message, outcomeText } from './allocation-dialogs';

const firstOfMonth = (): string => `${isoToday().slice(0, 7)}-01`;

/**
 * The allocation calendar (spec §57): people down the side, months across, each cell the share of
 * the month already allocated - coloured so over-allocation and bench time stand out.
 */
@Component({
  selector: 'gggs-resource-calendar-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, EmptyState, Skeleton, AllocateDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Allocation calendar" subtitle="How much of each month every person is already booked for.">
      @if (canAllocate) {
        <button type="button" class="btn btn--primary" (click)="allocateOpen.set(true)"><i class="pi pi-plus" aria-hidden="true"></i> Allocate</button>
      }
    </gggs-page-header>
    <section class="panel toolbar res__filters">
      <label class="field">
        <span class="field__label">From</span>
        <input type="month" [value]="from().slice(0, 7)" (change)="setFrom($any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Months</span>
        <select [value]="months()" (change)="months.set(+$any($event.target).value); load()">
          <option value="3">3</option>
          <option value="6">6</option>
          <option value="12">12</option>
        </select>
      </label>
      <label class="field">
        <span class="field__label">Skill</span>
        <select [value]="skillId()" (change)="skillId.set($any($event.target).value); load()">
          <option value="">Any</option>
          @for (skill of skills(); track skill.id) {
            <option [value]="skill.id">{{ skill.name }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Search</span>
        <input type="search" [value]="search()" (input)="search.set($any($event.target).value)" (keyup.enter)="load()" (search)="load()" />
      </label>
    </section>
    @if (notice()) {
      <p class="res__notice" role="status">{{ notice() }}</p>
    }
    @if (rows(); as list) {
      <section class="panel res__scroll">
        <table class="res__table">
          <thead>
            <tr>
              <th scope="col">Person</th>
              @for (cell of list[0]?.cells ?? []; track cell.from) {
                <th scope="col" class="res__cell">{{ cell.from | date: 'MMM yy' }}</th>
              }
            </tr>
          </thead>
          <tbody>
            @for (row of list; track row.employeeId) {
              <tr>
                <th scope="row">
                  <a [routerLink]="['/resources', row.employeeId]">{{ row.employeeName }}</a>
                  <br /><span class="muted">{{ row.designation ?? '' }}{{ row.topSkills.length ? ' · ' + row.topSkills.join(', ') : '' }}</span>
                </th>
                @for (cell of row.cells; track cell.from) {
                  <td [class]="'res__cell res__cell--' + cell.state.toLowerCase()" [title]="describe(cell)">
                    {{ cell.allocatedPercent | number: '1.0-0' }}%
                  </td>
                }
              </tr>
            } @empty {
              <tr><td [attr.colspan]="months() + 1"><gggs-empty-state icon="pi pi-calendar" title="No one matches" /></td></tr>
            }
          </tbody>
        </table>
      </section>
      <p class="muted">Grey: bench · blue: partly available · green: fully allocated · red: over-allocated · amber: mostly on leave. Hover a cell for the projects.</p>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
    <gggs-allocate-dialog [(visible)]="allocateOpen" (allocated)="allocated($event)" />
  `,
  styleUrl: './resources.css',
})
export class ResourceCalendarPage {
  private readonly api = inject(ResourcesApi);

  protected readonly canAllocate = inject(PermissionStore).has('resource.allocation.manage');
  protected readonly from = signal(firstOfMonth());
  protected readonly months = signal(6);
  protected readonly skillId = signal('');
  protected readonly search = signal('');
  protected readonly skills = signal<readonly SkillItem[]>([]);
  protected readonly rows = signal<readonly CalendarRow[] | null>(null);
  protected readonly allocateOpen = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    inject(SkillsApi)
      .catalog()
      .then(
        (skills) => this.skills.set(skills),
        () => undefined,
      );
    void this.load();
  }

  protected describe(cell: CapacityCell): string {
    const projects = cell.projects.map((share) => `${share.projectCode} ${share.percent}%${share.billable ? '' : ' (non-billable)'}`).join(', ');
    return `${humanize(cell.state)}${projects ? ': ' + projects : ''}${cell.leavePercent ? ` · leave ${Math.round(cell.leavePercent)}%` : ''}`;
  }

  protected setFrom(month: string): void {
    if (month) {
      this.from.set(`${month}-01`);
      void this.load();
    }
  }

  protected allocated(outcome: AllocationOutcome): void {
    this.notice.set(outcomeText(outcome));
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.rows.set(await this.api.calendar({ from: this.from(), months: this.months(), skillId: this.skillId(), search: this.search() }));
    } catch (error) {
      this.error.set(message(error, 'The calendar could not be loaded.'));
    }
  }
}

/** Capacity and demand (spec §62-63): supply against requested skills, and the months ahead. */
@Component({
  selector: 'gggs-resource-capacity-page',
  imports: [DecimalPipe, PageHeader, Chart, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Capacity & demand" subtitle="Free capacity in the months ahead, and the skills projects are asking for." />
    <section class="panel toolbar res__filters">
      <label class="field">
        <span class="field__label">Horizon</span>
        <select [value]="months()" (change)="months.set(+$any($event.target).value); load()">
          <option value="3">3 months</option>
          <option value="6">6 months</option>
          <option value="12">12 months</option>
        </select>
      </label>
    </section>
    @if (report(); as r) {
      <section class="panel stack">
        <h2 class="res__heading">Capacity (FTE)</h2>
        <gggs-chart kind="bar" title="Allocated against available FTE" [labels]="monthLabels()" [series]="capacitySeries()" />
        <div class="res__scroll">
          <table class="res__table">
            <thead>
              <tr>
                <th scope="col">Month</th>
                <th scope="col" class="res__num">People</th>
                <th scope="col" class="res__num">Allocated FTE</th>
                <th scope="col" class="res__num">Available FTE</th>
                <th scope="col" class="res__num">Leave FTE</th>
                <th scope="col" class="res__num">On bench</th>
                <th scope="col" class="res__num">Over-allocated</th>
              </tr>
            </thead>
            <tbody>
              @for (month of r.capacity; track month.month) {
                <tr>
                  <th scope="row">{{ month.month }}</th>
                  <td class="res__num">{{ month.people }}</td>
                  <td class="res__num">{{ month.allocatedFte | number: '1.0-1' }}</td>
                  <td class="res__num">{{ month.availableFte | number: '1.0-1' }}</td>
                  <td class="res__num">{{ month.leaveFte | number: '1.0-1' }}</td>
                  <td class="res__num">{{ month.onBench }}</td>
                  <td class="res__num" [class.res__danger]="month.overallocated > 0">{{ month.overallocated }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (r.peopleLeaving) {
          <p class="muted">{{ r.peopleLeaving }} {{ r.peopleLeaving === 1 ? 'person is' : 'people are' }} leaving within the horizon; their capacity is already taken off.</p>
        }
      </section>

      <section class="panel stack">
        <h2 class="res__heading">Skill demand against supply</h2>
        <p class="muted">Demand: people asked for in open requests. Supply: people with the skill at the level who are at least half free.</p>
        @if (r.skills.length) {
          <div class="res__scroll">
            <table class="res__table">
              <thead>
                <tr>
                  <th scope="col">Skill</th>
                  @for (month of r.skills[0].months; track month.month) {
                    <th scope="col" class="res__cell">{{ month.month }}</th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (skill of r.skills; track skill.skillId) {
                  <tr>
                    <th scope="row">{{ skill.skillName }}</th>
                    @for (month of skill.months; track month.month) {
                      <td class="res__cell" [class.res__danger]="month.gap > 0" [class.res__good]="month.gap <= 0 && month.demand > 0">
                        {{ month.demand }} / {{ month.supply }}{{ month.gap > 0 ? ' (short ' + month.gap + ')' : '' }}
                      </td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else {
          <gggs-empty-state icon="pi pi-chart-bar" title="No open skill demand" />
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './resources.css',
})
export class ResourceCapacityPage {
  private readonly api = inject(ResourcesApi);

  protected readonly months = signal(6);
  protected readonly report = signal<ForecastReport | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly monthLabels = computed(() => this.report()?.capacity.map((month) => month.month) ?? []);
  protected readonly capacitySeries = computed(() => {
    const capacity = this.report()?.capacity ?? [];
    return [
      { label: 'Allocated', values: capacity.map((month) => month.allocatedFte) },
      { label: 'Available', values: capacity.map((month) => month.availableFte) },
      { label: 'Leave', values: capacity.map((month) => month.leaveFte) },
    ];
  });

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.report.set(await this.api.forecast(this.months()));
    } catch (error) {
      this.error.set(message(error, 'The forecast could not be loaded.'));
    }
  }
}

/** The bench (spec §64): who is unallocated, for how long, at what cost, and what is next. */
@Component({
  selector: 'gggs-resource-bench-page',
  imports: [DatePipe, DecimalPipe, RouterLink, PageHeader, KpiCard, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Bench" subtitle="People without enough project work, longest first." />
    <div class="res__kpis">
      <gggs-kpi-card label="On bench" [value]="items().length" icon="pi pi-pause-circle" />
      <gggs-kpi-card label="Bench days (total)" [value]="totalDays()" />
      <gggs-kpi-card label="Cost a day" [value]="(dailyCost() | number: '1.0-0') ?? '0'" hint="at cost rate × 8 h" />
      <gggs-kpi-card label="With something lined up" [value]="lined()" />
    </div>
    <section class="panel stack">
      @for (item of items(); track item.employeeId) {
        <a class="res__row" [routerLink]="['/resources', item.employeeId]">
          <span><strong>{{ item.employeeName }}</strong><br /><span class="muted">{{ item.designation ?? '' }}{{ item.locationName ? ' · ' + item.locationName : '' }}</span></span>
          <span class="res__skills">
            @for (skill of item.skills.slice(0, 4); track skill.skillId) {
              <span class="res__chip" [class.res__chip--primary]="skill.isPrimary">{{ skill.name }} {{ skill.level }}</span>
            }
          </span>
          <span>{{ item.benchDays }} days{{ item.benchSince ? ' (since ' + (item.benchSince | date: 'd MMM') + ')' : '' }}<br /><span class="muted">{{ item.previousProject ? 'was on ' + item.previousProject : '' }}</span></span>
          <span>{{ item.availablePercent | number: '1.0-0' }}% free{{ item.costRate ? ' · ' + (item.costRate | number: '1.0-0') + '/h' : '' }}</span>
          <span class="muted">{{ item.nextProject ? item.nextProject + (item.nextAssignment ? ' from ' + (item.nextAssignment | date: 'd MMM') : '') : 'nothing next' }}</span>
        </a>
      } @empty {
        <gggs-empty-state icon="pi pi-check-circle" title="No one is on the bench" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
  `,
  styleUrl: './resources.css',
})
export class ResourceBenchPage {
  protected readonly items = signal<readonly BenchItem[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly totalDays = computed(() => this.items().reduce((sum, item) => sum + item.benchDays, 0));
  protected readonly dailyCost = computed(() => this.items().reduce((sum, item) => sum + ((item.costRate ?? 0) * 8 * item.availablePercent) / 100, 0));
  protected readonly lined = computed(() => this.items().filter((item) => item.nextProject).length);

  constructor() {
    inject(ResourcesApi)
      .bench()
      .then(
        (items) => this.items.set(items),
        (error: unknown) => this.error.set(message(error, 'The bench could not be loaded.')),
      );
  }
}

/**
 * Utilization (spec §74-75): billable hours against the hours people were available, from
 * approved timesheets - by person, project, department, manager or location, with a trend.
 */
@Component({
  selector: 'gggs-resource-utilization-page',
  imports: [DecimalPipe, PageHeader, KpiCard, Chart, EmptyState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Utilization" subtitle="Billable hours as a share of available hours, from approved timesheets." />
    <section class="panel toolbar res__filters">
      <label class="field">
        <span class="field__label">From</span>
        <input type="date" [value]="from()" (change)="from.set($any($event.target).value); load()" />
      </label>
      <label class="field">
        <span class="field__label">To</span>
        <input type="date" [value]="to()" (change)="to.set($any($event.target).value); load()" />
      </label>
      <label class="field">
        <span class="field__label">Group by</span>
        <select [value]="groupBy()" (change)="groupBy.set($any($event.target).value); load()">
          @for (option of groupings; track option) {
            <option [value]="option">{{ option }}</option>
          }
        </select>
      </label>
    </section>
    @if (report(); as r) {
      <div class="res__kpis">
        <gggs-kpi-card label="Utilization" [value]="(r.utilizationPercent | number: '1.0-1') + '%'" icon="pi pi-gauge" />
        <gggs-kpi-card label="Billable hours" [value]="r.billableHours | number: '1.0-0'" />
        <gggs-kpi-card label="Available hours" [value]="r.availableHours | number: '1.0-0'" />
        @for (category of categories; track category) {
          @if (hours(r.hoursByCategory, category) && category !== 'Billable') {
            <gggs-kpi-card [label]="humanize(category) + ' hours'" [value]="hours(r.hoursByCategory, category) | number: '1.0-0'" />
          }
        }
      </div>
      <section class="panel stack">
        <gggs-chart kind="line" title="Utilization trend (%)" [labels]="trendLabels()" [series]="trendSeries()" height="14rem" />
      </section>
      <section class="panel res__scroll">
        <table class="res__table">
          <thead>
            <tr>
              <th scope="col">{{ r.groupBy }}</th>
              <th scope="col" class="res__num">Available h</th>
              <th scope="col" class="res__num">Logged h</th>
              <th scope="col" class="res__num">Billable h</th>
              <th scope="col">Utilization</th>
            </tr>
          </thead>
          <tbody>
            @for (row of r.rows; track row.key ?? row.name) {
              <tr>
                <th scope="row">{{ row.name }}</th>
                <td class="res__num">{{ row.availableHours | number: '1.0-0' }}</td>
                <td class="res__num">{{ row.loggedHours | number: '1.0-1' }}</td>
                <td class="res__num">{{ row.billableHours | number: '1.0-1' }}</td>
                <td>
                  <strong>{{ row.utilizationPercent | number: '1.0-1' }}%</strong>
                  <div class="res__bar" aria-hidden="true"><div class="res__bar-fill" [style.width.%]="row.utilizationPercent > 100 ? 100 : row.utilizationPercent"></div></div>
                </td>
              </tr>
            } @empty {
              <tr><td colspan="5"><gggs-empty-state icon="pi pi-gauge" title="No approved hours in this period" /></td></tr>
            }
          </tbody>
        </table>
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './resources.css',
})
export class ResourceUtilizationPage {
  private readonly api = inject(ResourcesApi);

  protected readonly groupings: readonly UtilizationGrouping[] = ['Employee', 'Project', 'Department', 'Manager', 'Location'];
  protected readonly categories = HOUR_CATEGORIES;
  protected readonly humanize = humanize;
  protected readonly from = signal(firstOfMonth());
  protected readonly to = signal(isoToday());
  protected readonly groupBy = signal<UtilizationGrouping>('Employee');
  protected readonly report = signal<UtilizationReport | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly trendLabels = computed(() => this.report()?.trend.map((point) => point.month) ?? []);
  protected readonly trendSeries = computed(() => [{ label: 'Utilization %', values: this.report()?.trend.map((point) => point.utilizationPercent) ?? [] }]);

  constructor() {
    void this.load();
  }

  protected hours(byCategory: Readonly<Record<string, number>>, category: string): number {
    return byCategory[category] ?? 0;
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.report.set(await this.api.utilization({ from: this.from(), to: this.to(), groupBy: this.groupBy() }));
    } catch (error) {
      this.error.set(message(error, 'Utilization could not be loaded.'));
    }
  }
}

/** Transfers between projects (spec §57): requested, approved through the workflow, completed. */
@Component({
  selector: 'gggs-resource-transfers-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Transfers" subtitle="Moves between projects. Start one from a person's allocation." />
    <section class="panel toolbar res__filters">
      <label class="field">
        <span class="field__label">Status</span>
        <select [value]="status()" (change)="status.set($any($event.target).value); load()">
          <option value="">Any</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ option }}</option>
          }
        </select>
      </label>
    </section>
    <section class="panel stack">
      @for (transfer of transfers(); track transfer.id) {
        <div class="res__row">
          <a [routerLink]="['/resources', transfer.employeeId]"><strong>{{ transfer.employeeName ?? 'Employee' }}</strong></a>
          <span>{{ transfer.fromProject ?? '—' }} → {{ transfer.toProject ?? '—' }} · {{ transfer.allocationPercent }}%</span>
          <span class="muted">from {{ transfer.effectiveDate | date: 'd MMM y' }} · {{ transfer.reason }}</span>
          <gggs-status-badge [status]="transfer.status" />
          <span>
            @if (canManage && transfer.status === 'Pending') {
              <button type="button" class="btn btn--link btn--sm" (click)="cancel(transfer)">Cancel</button>
            }
          </span>
        </div>
      } @empty {
        <gggs-empty-state icon="pi pi-arrow-right-arrow-left" title="No transfers" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>
  `,
  styleUrl: './resources.css',
})
export class ResourceTransfersPage {
  private readonly api = inject(ResourcesApi);

  protected readonly canManage = inject(PermissionStore).has('resource.allocation.manage');
  protected readonly statuses: readonly TransferStatus[] = ['Pending', 'Completed', 'Rejected', 'Cancelled'];
  protected readonly status = signal<TransferStatus | ''>('');
  protected readonly transfers = signal<readonly TransferItem[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async cancel(transfer: TransferItem): Promise<void> {
    try {
      await this.api.cancelTransfer(transfer.id);
      await this.load();
    } catch (error) {
      this.error.set(message(error, 'The transfer could not be cancelled.'));
    }
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      this.transfers.set(await this.api.transfers(this.status()));
    } catch (error) {
      this.error.set(message(error, 'Transfers could not be loaded.'));
    }
  }
}
