import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { ServiceLevelView, SlaPolicy, SlaTicket, SlaWeekday, WorkPriority } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, KpiCard, localIsoDate, Skeleton } from '@gggs/shared/ui';
import { hoursToMinutes, minutesToHours, resolutionState, responseState, type TargetState } from './service-level-text';

const WEEK: readonly SlaWeekday[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const PRIORITIES: readonly WorkPriority[] = ['Critical', 'High', 'Medium', 'Low'];

interface TargetDraft {
  readonly priority: WorkPriority;
  readonly covered: boolean;
  readonly responseHours: number;
  readonly resolutionHours: number;
  readonly creditPercent: number;
}

/** A project's service levels: its promise to the client, the tickets running against it, and the month's result. */
@Component({
  selector: 'gggs-service-tab',
  imports: [DatePipe, DecimalPipe, RouterLink, KpiCard, EmptyState, Skeleton, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view(); as v) {
      <div class="stack">
        <div class="toolbar">
          <label class="field sla__month">
            <span class="field__label">Month</span>
            <input type="month" [value]="month()" (change)="chooseMonth($any($event.target).value)" />
          </label>
          <span class="sla__spacer"></span>
          @if (v.canManage) {
            <button type="button" class="btn btn--sm" (click)="openPolicy()"><i class="pi pi-cog" aria-hidden="true"></i> {{ v.policy ? 'Edit service levels' : 'Set up service levels' }}</button>
          }
        </div>

        @if (!v.policy) {
          <section class="panel">
            <gggs-empty-state icon="pi pi-stopwatch" title="No service levels yet" description="Set response and resolution targets, and incidents and service requests raised after that run against them." />
          </section>
        } @else {
          <div class="xp__kpis">
            <gggs-kpi-card label="Opened" [value]="v.report.opened" [hint]="v.report.resolved + ' resolved'" icon="pi pi-ticket" />
            <gggs-kpi-card label="Response met" [value]="percent(v.report.responseMetPercent)" [hint]="v.report.responseMet + ' of ' + v.report.responseMeasured" />
            <gggs-kpi-card label="Resolution met" [value]="percent(v.report.resolutionMetPercent)" [hint]="v.report.resolutionMet + ' of ' + v.report.resolutionMeasured" />
            <gggs-kpi-card label="Breaches" [value]="v.report.breaches" [hint]="v.report.excluded + ' excluded'" />
            <gggs-kpi-card label="Service credit" [value]="(v.report.creditPercent | number: '1.0-2') + '%'" [hint]="'of the monthly fee, up to ' + (v.policy.monthlyCreditCapPercent | number: '1.0-2') + '%'" />
          </div>

          <section class="panel sla__scroll">
            <h3 class="sla__heading">Tickets</h3>
            <table class="table">
              <thead>
                <tr>
                  <th scope="col">Ticket</th><th scope="col">Priority</th><th scope="col">Column</th><th scope="col">Assignee</th><th scope="col">Response</th><th scope="col">Resolution</th><th scope="col"><span class="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (ticket of v.tickets; track ticket.taskId) {
                  <tr>
                    <th scope="row">
                      <a [routerLink]="['/projects', projectId()]" [queryParams]="{ tab: 'tasks', task: ticket.taskId }"><strong>{{ ticket.code }}</strong> {{ ticket.title }}</a>
                      <div class="muted sla__small">{{ humanize(ticket.type) }} · opened {{ ticket.openedAt | date: 'd MMM HH:mm' }}</div>
                      @if (ticket.excludedReason) {
                        <div class="muted sla__small">Excluded: {{ ticket.excludedReason }}</div>
                      }
                    </th>
                    <td>{{ ticket.priority }}</td>
                    <td>{{ ticket.columnName ?? '' }}</td>
                    <td>{{ ticket.assigneeName ?? 'Unassigned' }}</td>
                    <td><span [class]="'sla__state sla__state--' + response(ticket).tone">{{ response(ticket).label }}</span></td>
                    <td><span [class]="'sla__state sla__state--' + resolution(ticket).tone">{{ resolution(ticket).label }}</span></td>
                    <td class="sla__actions">
                      @if (v.canManageTickets && !ticket.respondedAt && !ticket.resolvedAt) {
                        <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="respond(ticket)">Mark answered</button>
                      }
                      @if (v.canManageCredits && (ticket.responseBreached || ticket.resolutionBreached)) {
                        @if (ticket.excludedReason) {
                          <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="reinstate(ticket)">Count again</button>
                        } @else {
                          <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="openExclude(ticket)">Exclude</button>
                        }
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr><td colspan="7" class="muted">Nothing open, and nothing resolved or missed this month.</td></tr>
                }
              </tbody>
            </table>
          </section>

          @if (v.report.priorities.length) {
            <section class="panel sla__scroll">
              <h3 class="sla__heading">By priority</h3>
              <table class="table">
                <thead>
                  <tr><th scope="col">Priority</th><th scope="col" class="sla__num">Opened</th><th scope="col" class="sla__num">Resolved</th><th scope="col" class="sla__num">Response met</th><th scope="col" class="sla__num">Resolution met</th></tr>
                </thead>
                <tbody>
                  @for (line of v.report.priorities; track line.priority) {
                    <tr>
                      <th scope="row">{{ line.priority }}</th>
                      <td class="sla__num">{{ line.opened }}</td>
                      <td class="sla__num">{{ line.resolved }}</td>
                      <td class="sla__num">{{ line.responseMet }} of {{ line.responseMeasured }}</td>
                      <td class="sla__num">{{ line.resolutionMet }} of {{ line.resolutionMeasured }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </section>
          }
        }
      </div>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="policyOpen" title="Service levels" size="lg" [busy]="busy()" [error]="formError()" (save)="savePolicy()">
      <div class="stack">
        <p class="muted sla__small">These apply to tickets raised from now on. Tickets already open keep the targets they started with.</p>
        <label class="sla__check">
          <input type="checkbox" [checked]="alwaysOn()" (change)="alwaysOn.set($any($event.target).checked)" />
          <span>The clock runs all the time (24×7)</span>
        </label>
        @if (!alwaysOn()) {
          <div class="sla__grid">
            <label class="field"><span class="field__label">Day starts</span><input type="time" [value]="dayStart()" (change)="dayStart.set($any($event.target).value)" /></label>
            <label class="field"><span class="field__label">Day ends</span><input type="time" [value]="dayEnd()" (change)="dayEnd.set($any($event.target).value)" /></label>
          </div>
          <fieldset class="sla__set">
            <legend class="field__label">Working days</legend>
            @for (day of week; track day) {
              <label class="sla__check"><input type="checkbox" [checked]="days().has(day)" (change)="toggleDay(day, $any($event.target).checked)" /><span>{{ day.slice(0, 3) }}</span></label>
            }
          </fieldset>
        }
        <div class="sla__scroll">
          <table class="table">
            <thead>
              <tr><th scope="col">Priority</th><th scope="col">Covered</th><th scope="col">Respond within (hours)</th><th scope="col">Resolve within (hours)</th><th scope="col">Credit per ticket (% of fee)</th></tr>
            </thead>
            <tbody>
              @for (target of targets(); track target.priority) {
                <tr>
                  <th scope="row">{{ target.priority }}</th>
                  <td><input type="checkbox" [attr.aria-label]="'Cover ' + target.priority" [checked]="target.covered" (change)="setTarget(target.priority, { covered: $any($event.target).checked })" /></td>
                  <td><input type="number" min="0" step="0.25" class="sla__input" [attr.aria-label]="target.priority + ' response hours'" [disabled]="!target.covered" [value]="target.responseHours" (input)="setTarget(target.priority, { responseHours: +$any($event.target).value })" /></td>
                  <td><input type="number" min="0" step="0.5" class="sla__input" [attr.aria-label]="target.priority + ' resolution hours'" [disabled]="!target.covered" [value]="target.resolutionHours" (input)="setTarget(target.priority, { resolutionHours: +$any($event.target).value })" /></td>
                  <td><input type="number" min="0" max="100" class="sla__input" [attr.aria-label]="target.priority + ' credit percent'" [disabled]="!target.covered" [value]="target.creditPercent" (input)="setTarget(target.priority, { creditPercent: +$any($event.target).value })" /></td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <label class="field sla__cap">
          <span class="field__label">Most credits can cost in a month (% of the fee)</span>
          <input type="number" min="0" max="100" [value]="cap()" (input)="cap.set(+$any($event.target).value)" />
        </label>
        @if (view()?.columns?.length) {
          <fieldset class="sla__set">
            <legend class="field__label">Columns where the ticket is with the client, which pause the resolution clock</legend>
            @for (column of view()?.columns ?? []; track column.id) {
              <label class="sla__check"><input type="checkbox" [checked]="paused().has(column.id)" (change)="togglePaused(column.id, $any($event.target).checked)" /><span>{{ column.name }}</span></label>
            }
          </fieldset>
        }
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="excludeOpen" [title]="'Exclude the breach on ' + (excluding()?.code ?? '')" saveLabel="Exclude" [busy]="busy()" [error]="formError()" [saveDisabled]="!reason().trim()" (save)="exclude()">
      <p class="muted sla__small">An excluded breach earns no service credit and counts as met in the report. Record why, as agreed with the client.</p>
      <label class="field">
        <span class="field__label">Reason</span>
        <input type="text" maxlength="500" [value]="reason()" (input)="reason.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>
  `,
  styles: `
    .sla__month {
      max-inline-size: 12rem;
    }

    .sla__spacer {
      flex: 1;
    }

    .sla__scroll {
      overflow-x: auto;
    }

    .sla__heading {
      margin: 0 0 var(--gggs-space-2);
      font-size: var(--gggs-font-size-md);
    }

    .sla__small {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
      font-weight: 400;
    }

    .sla__num {
      text-align: end !important;
      font-variant-numeric: tabular-nums;
    }

    .sla__actions {
      white-space: nowrap;
      text-align: end;
    }

    .sla__state {
      white-space: nowrap;
      font-weight: 600;
    }

    .sla__state--success {
      color: var(--gggs-color-success);
    }

    .sla__state--warning {
      color: var(--gggs-color-warning);
    }

    .sla__state--danger {
      color: var(--gggs-color-danger);
    }

    .sla__state--neutral {
      color: var(--gggs-color-text-muted);
    }

    .sla__grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      gap: var(--gggs-space-3);
    }

    .sla__set {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-3);
      margin: 0;
      padding: 0;
      border: 0;
    }

    .sla__check {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
    }

    .sla__input {
      inline-size: 6rem;
    }

    .sla__cap {
      max-inline-size: 22rem;
    }
  `,
})
export class ServiceTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();

  protected readonly humanize = humanize;
  protected readonly week = WEEK;
  protected readonly view = signal<ServiceLevelView | null>(null);
  protected readonly month = signal(localIsoDate().slice(0, 7));
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly busy = signal(false);
  private readonly now = signal(Date.now());

  protected readonly policyOpen = signal(false);
  protected readonly alwaysOn = signal(false);
  protected readonly dayStart = signal('09:00');
  protected readonly dayEnd = signal('18:00');
  protected readonly days = signal<ReadonlySet<SlaWeekday>>(new Set());
  protected readonly targets = signal<readonly TargetDraft[]>([]);
  protected readonly cap = signal(15);
  protected readonly paused = signal<ReadonlySet<string>>(new Set());

  protected readonly excludeOpen = signal(false);
  protected readonly excluding = signal<SlaTicket | null>(null);
  protected readonly reason = signal('');

  constructor() {
    effect(() => {
      const id = this.projectId();
      untracked(() => void this.load(id));
    });
  }

  protected response(ticket: SlaTicket): TargetState {
    return responseState(ticket, this.now());
  }

  protected resolution(ticket: SlaTicket): TargetState {
    return resolutionState(ticket, this.now());
  }

  protected percent(value: number | undefined): string {
    return value === undefined ? 'None yet' : `${value}%`;
  }

  protected chooseMonth(month: string): void {
    if (month) {
      this.month.set(month);
      void this.load(this.projectId());
    }
  }

  protected openPolicy(): void {
    const view = this.view();
    if (!view) {
      return;
    }

    const policy = view.policy ?? view.defaults;
    this.alwaysOn.set(policy.calendar.alwaysOn);
    const calendar = policy.calendar.alwaysOn ? view.defaults.calendar : policy.calendar;
    this.dayStart.set(calendar.dayStart.slice(0, 5));
    this.dayEnd.set(calendar.dayEnd.slice(0, 5));
    this.days.set(new Set(calendar.workingDays));
    this.targets.set(
      PRIORITIES.map((priority) => {
        const target = policy.targets.find((row) => row.priority === priority);
        const fallback = view.defaults.targets.find((row) => row.priority === priority);
        const source = target ?? fallback;
        return {
          priority,
          covered: Boolean(target),
          responseHours: minutesToHours(source?.responseMinutes ?? 60),
          resolutionHours: minutesToHours(source?.resolutionMinutes ?? 480),
          creditPercent: source?.creditPercent ?? 0,
        };
      }),
    );
    this.cap.set(policy.monthlyCreditCapPercent);

    // A first policy assumes a column that says it is waiting means waiting on the client.
    const waiting = view.policy ? view.policy.pausedColumnIds : view.columns.filter((column) => /wait/i.test(column.name)).map((column) => column.id);
    this.paused.set(new Set(waiting));
    this.formError.set(null);
    this.policyOpen.set(true);
  }

  protected toggleDay(day: SlaWeekday, on: boolean): void {
    this.days.update((days) => toggled(days, day, on));
  }

  protected togglePaused(id: string, on: boolean): void {
    this.paused.update((paused) => toggled(paused, id, on));
  }

  protected setTarget(priority: WorkPriority, change: Partial<TargetDraft>): void {
    this.targets.update((targets) => targets.map((target) => (target.priority === priority ? { ...target, ...change } : target)));
  }

  protected async savePolicy(): Promise<void> {
    const policy: SlaPolicy = {
      calendar: {
        alwaysOn: this.alwaysOn(),
        dayStart: this.alwaysOn() ? '00:00' : this.dayStart(),
        dayEnd: this.alwaysOn() ? '00:00' : this.dayEnd(),
        workingDays: this.alwaysOn() ? [] : WEEK.filter((day) => this.days().has(day)),
      },
      targets: this.targets()
        .filter((target) => target.covered)
        .map((target) => ({
          priority: target.priority,
          responseMinutes: hoursToMinutes(target.responseHours),
          resolutionMinutes: hoursToMinutes(target.resolutionHours),
          creditPercent: target.creditPercent,
        })),
      pausedColumnIds: [...this.paused()],
      monthlyCreditCapPercent: this.cap(),
    };
    await this.submit(async () => {
      await this.api.saveSlaPolicy(this.projectId(), policy);
      this.policyOpen.set(false);
    });
  }

  protected openExclude(ticket: SlaTicket): void {
    this.excluding.set(ticket);
    this.reason.set('');
    this.formError.set(null);
    this.excludeOpen.set(true);
  }

  protected async exclude(): Promise<void> {
    const ticket = this.excluding();
    if (ticket) {
      await this.submit(async () => {
        await this.api.excludeTicketBreach(this.projectId(), ticket.taskId, this.reason().trim());
        this.excludeOpen.set(false);
      });
    }
  }

  protected async respond(ticket: SlaTicket): Promise<void> {
    await this.act(() => this.api.respondToTicket(this.projectId(), ticket.taskId));
  }

  protected async reinstate(ticket: SlaTicket): Promise<void> {
    await this.act(() => this.api.reinstateTicketBreach(this.projectId(), ticket.taskId));
  }

  private async submit(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await action();
      await this.load(this.projectId());
    } catch (error) {
      this.formError.set(error instanceof ApiError ? error.message : 'That could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  private async act(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      await this.load(this.projectId());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That did not work.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(projectId: string): Promise<void> {
    const month = this.month();
    try {
      const view = await this.api.serviceLevels(projectId, `${month}-01`);
      if (projectId === this.projectId() && month === this.month()) {
        this.now.set(Date.now());
        this.view.set(view);
        this.error.set(null);
      }
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Service levels could not be loaded.');
    }
  }
}

function toggled<T>(set: ReadonlySet<T>, item: T, on: boolean): ReadonlySet<T> {
  const next = new Set(set);
  if (on) {
    next.add(item);
  } else {
    next.delete(item);
  }
  return next;
}
