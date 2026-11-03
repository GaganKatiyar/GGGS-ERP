import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import type { HolidayEntry, LeaveBalance, LeavePreview, LeaveRequest, LeaveType, ResolvedLeavePolicy } from '@gggs/shared/models';
import { LeaveApi, addDays, todayIso } from '@gggs/shared/time';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { ruleSummary } from '../admin/leave-policy-model';

/**
 * My leave: what I have, applying (with the days and any rule it breaks shown before sending), my
 * requests, and this year's holidays.
 */
@Component({
  selector: 'gggs-my-leave-page',
  imports: [DatePipe, DecimalPipe, HasPermissionDirective, PageHeader, KpiCard, StatusBadge, Skeleton, EmptyState, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My leave" subtitle="Balances, requests and holidays.">
      <button type="button" class="btn btn--primary" *gggsHasPermission="'self.leave.apply'" (click)="openApply()"><i class="pi pi-plus" aria-hidden="true"></i> Apply for leave</button>
    </gggs-page-header>

    @if (error()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="Leave could not be shown" [description]="error()!" />
    } @else if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else {
      <div class="stack">
        <div class="kpi-row">
          @for (balance of balances(); track balance.leaveTypeId) {
            <gggs-kpi-card
              [label]="balance.name"
              icon="pi pi-calendar"
              [value]="(balance.available | number: '1.0-1') ?? ''"
              [hint]="hint(balance)"
            />
          } @empty {
            <p class="muted">No leave policy covers you yet. Ask HR to assign one.</p>
          }
        </div>

        @if (policy(); as mine) {
          <details class="panel">
            <summary class="leave__policy">
              <span>Your leave policy: <strong>{{ mine.name }}</strong></span>
              <span class="muted">Leave year {{ mine.leaveYear }} · {{ mine.yearStart | date: 'mediumDate' }} – {{ mine.yearEnd | date: 'mediumDate' }}{{ mine.source === 'Exception' ? ' · given to you by HR' : '' }}</span>
            </summary>
            <dl class="definition-list">
              @for (item of mine.items; track item.leaveTypeId) {
                <dt>{{ item.leaveTypeName }}</dt>
                <dd>{{ summary(item) }}</dd>
              }
            </dl>
          </details>
        }

        <section class="panel stack">
          <h2 class="leave__heading">My requests</h2>
          @if (requests().length === 0) {
            <gggs-empty-state icon="pi pi-calendar-plus" title="No requests yet" />
          } @else {
            <table class="table grid">
              <thead>
                <tr>
                  <th scope="col">Type</th>
                  <th scope="col">Dates</th>
                  <th scope="col">Days</th>
                  <th scope="col">Reason</th>
                  <th scope="col">Status</th>
                  <th scope="col"><span class="grid__hidden">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (request of requests(); track request.id) {
                  <tr>
                    <td><span class="leave__dot" [style.background]="request.color" aria-hidden="true"></span> {{ request.leaveTypeName }}</td>
                    <td>
                      {{ request.startDate | date: 'd MMM' }}{{ request.startDate !== request.endDate ? ' – ' + (request.endDate | date: 'd MMM yyyy') : (request.startDate | date: ' yyyy') }}
                      @if (request.startsSecondHalf || request.endsFirstHalf) {
                        <span class="muted">(half day)</span>
                      }
                    </td>
                    <td>{{ request.days | number: '1.0-1' }}</td>
                    <td>
                      {{ request.reason ?? '' }}
                      @if (request.attachmentCount) {
                        <span class="muted" [attr.aria-label]="request.attachmentCount + ' document(s) attached'"><i class="pi pi-paperclip" aria-hidden="true"></i> {{ request.attachmentCount }}</span>
                      }
                    </td>
                    <td><gggs-status-badge [status]="request.status" /></td>
                    <td>
                      @if (request.status === 'Draft') {
                        <button type="button" class="btn btn--link btn--sm" (click)="openAttach(request)">Attach &amp; send</button>
                      }
                      @if (cancellable(request)) {
                        <button type="button" class="btn btn--link btn--sm" (click)="cancel(request)">Cancel</button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }
          @if (actionError()) {
            <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ actionError() }}</span></p>
          }
        </section>

        <section class="panel stack">
          <h2 class="leave__heading">Holidays {{ year }}</h2>
          @for (holiday of holidays(); track holiday.date) {
            <div class="leave__holiday" [class.leave__holiday--past]="holiday.date < today">
              <strong>{{ holiday.date | date: 'EEE d MMM' }}</strong>
              <span>{{ holiday.name }}</span>
              @if (holiday.isOptional) {
                <span class="muted">optional</span>
              }
            </div>
          } @empty {
            <p class="muted">No holidays are on your calendar this year.</p>
          }
        </section>
      </div>
    }

    <gggs-form-dialog [(visible)]="applyOpen" title="Apply for leave" saveLabel="Apply" [busy]="busy()" [saveDisabled]="!preview() || preview()!.problems.length > 0" [error]="dialogError()" (save)="apply()">
      <div class="stack">
        <div class="form-grid">
          <label class="field form-grid__full">
            <span class="field__label">Type *</span>
            <select [value]="typeId()" (change)="typeId.set($any($event.target).value); refreshPreview()">
              @for (type of types(); track type.id) {
                <option [value]="type.id">{{ type.name }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">From *</span>
            <input type="date" [value]="start()" (change)="setStart($any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">To *</span>
            <input type="date" [min]="start()" [value]="end()" (change)="end.set($any($event.target).value); refreshPreview()" />
          </label>
          @if (selectedType()?.allowHalfDay) {
            <label class="field">
              <span><input type="checkbox" [checked]="startsSecondHalf()" (change)="startsSecondHalf.set($any($event.target).checked); refreshPreview()" /> Start after lunch</span>
            </label>
            <label class="field">
              <span><input type="checkbox" [checked]="endsFirstHalf()" (change)="endsFirstHalf.set($any($event.target).checked); refreshPreview()" /> End at lunch</span>
            </label>
          }
          <label class="field form-grid__full">
            <span class="field__label">Reason</span>
            <input type="text" maxlength="500" [value]="reason()" (input)="reason.set($any($event.target).value)" />
          </label>
        </div>

        @if (preview(); as p) {
          <div class="preview" [class.preview--problem]="p.problems.length > 0" role="status">
            <strong>{{ p.days | number: '1.0-1' }} day(s)</strong> · {{ p.available | number: '1.0-1' }} available
            @for (problem of p.problems; track problem) {
              <div class="field__error">{{ problem }}</div>
            }
            @if (p.documentRequired && !p.problems.length) {
              <div><i class="pi pi-paperclip" aria-hidden="true"></i> This request needs a supporting document. You attach it next, then it goes for approval.</div>
            }
          </div>
        }
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="attachOpen" title="Attach a supporting document" saveLabel="Attach and send" [busy]="busy()" [saveDisabled]="!file()" [error]="dialogError()" (save)="attachAndSend()">
      <div class="stack">
        @if (attaching(); as request) {
          <p class="muted">{{ request.leaveTypeName }}, {{ request.startDate | date: 'd MMM' }} – {{ request.endDate | date: 'd MMM yyyy' }} ({{ request.days | number: '1.0-1' }} day(s)). A medical certificate or similar: PDF, JPEG or PNG, up to 5 MB.</p>
        }
        <label class="field">
          <span class="field__label">Document *</span>
          <input type="file" accept="application/pdf,image/jpeg,image/png" (change)="file.set($any($event.target).files?.[0] ?? null)" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .leave__policy {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-1) var(--gggs-space-3);
      cursor: pointer;
    }

    .leave__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .leave__dot {
      display: inline-block;
      width: 0.6rem;
      height: 0.6rem;
      border-radius: 50%;
    }

    .leave__holiday {
      display: flex;
      gap: var(--gggs-space-3);
      padding-block: var(--gggs-space-1, 0.25rem);
    }

    .leave__holiday--past {
      opacity: 0.55;
    }



    .grid__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }

    .preview {
      padding: var(--gggs-space-3);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-primary-subtle);
    }

    .preview--problem {
      background: var(--gggs-color-danger-subtle);
    }
  `,
})
export class MyLeavePage {
  private readonly api = inject(LeaveApi);

  protected readonly today = todayIso();
  protected readonly year = new Date().getFullYear();

  protected readonly balances = signal<readonly LeaveBalance[]>([]);
  protected readonly policy = signal<ResolvedLeavePolicy | null>(null);
  protected readonly summary = ruleSummary;
  protected readonly requests = signal<readonly LeaveRequest[]>([]);
  protected readonly holidays = signal<readonly HolidayEntry[]>([]);
  protected readonly types = signal<readonly LeaveType[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);

  protected readonly applyOpen = signal(false);
  protected readonly attachOpen = signal(false);
  protected readonly attaching = signal<LeaveRequest | null>(null);
  protected readonly file = signal<File | null>(null);
  protected readonly typeId = signal('');
  protected readonly start = signal(addDays(this.today, 1));
  protected readonly end = signal(addDays(this.today, 1));
  protected readonly startsSecondHalf = signal(false);
  protected readonly endsFirstHalf = signal(false);
  protected readonly reason = signal('');
  protected readonly preview = signal<LeavePreview | null>(null);
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  protected readonly selectedType = computed(() => this.types().find((type) => type.id === this.typeId()));

  private previewTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.previewTimer));
    void this.load();
  }

  protected hint(balance: LeaveBalance): string {
    const parts = [`${balance.takenThisYear} taken this year`];
    if (balance.pending) {
      parts.unshift(`${balance.pending} pending`);
    }
    return parts.join(' · ');
  }

  protected cancellable(request: LeaveRequest): boolean {
    return request.status === 'Draft' || request.status === 'Pending' || (request.status === 'Approved' && request.startDate > this.today);
  }

  protected async openApply(): Promise<void> {
    if (this.types().length === 0) {
      // Only the leave types the policy grants can be applied for.
      const granted = new Set(this.policy()?.items.map((item) => item.leaveTypeId));
      const types = await this.api.types();
      this.types.set(granted.size ? types.filter((type) => granted.has(type.id)) : types);
    }
    const covered = this.balances().map((balance) => balance.leaveTypeId);
    this.typeId.set(this.types().find((type) => covered.includes(type.id))?.id ?? this.types()[0]?.id ?? '');
    this.start.set(addDays(this.today, 1));
    this.end.set(addDays(this.today, 1));
    this.startsSecondHalf.set(false);
    this.endsFirstHalf.set(false);
    this.reason.set('');
    this.preview.set(null);
    this.dialogError.set(null);
    this.applyOpen.set(true);
    this.refreshPreview();
  }

  protected setStart(value: string): void {
    this.start.set(value);
    if (this.end() < value) {
      this.end.set(value);
    }
    this.refreshPreview();
  }

  protected refreshPreview(): void {
    clearTimeout(this.previewTimer);
    this.previewTimer = setTimeout(async () => {
      if (!this.typeId() || !this.start() || !this.end()) {
        return;
      }
      try {
        this.preview.set(await this.api.preview(this.application()));
        this.dialogError.set(null);
      } catch (error) {
        this.preview.set(null);
        this.dialogError.set(error instanceof ApiError ? error.message : 'Those dates could not be checked.');
      }
    }, 250);
  }

  protected async apply(): Promise<void> {
    this.busy.set(true);
    this.dialogError.set(null);
    try {
      const request = await this.api.apply(this.application());
      this.applyOpen.set(false);
      await this.load();
      if (request.status === 'Draft') {
        this.openAttach(request);
      }
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The request could not be sent.');
    } finally {
      this.busy.set(false);
    }
  }

  protected openAttach(request: LeaveRequest): void {
    this.attaching.set(request);
    this.file.set(null);
    this.dialogError.set(null);
    this.attachOpen.set(true);
  }

  protected async attachAndSend(): Promise<void> {
    const request = this.attaching();
    const file = this.file();
    if (!request || !file) {
      return;
    }

    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await this.api.attach(request.id, file);
      await this.api.submit(request.id);
      this.attachOpen.set(false);
      await this.load();
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The document could not be attached.');
      await this.load();
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(request: LeaveRequest): Promise<void> {
    this.actionError.set(null);
    try {
      await this.api.cancelMine(request.id, null);
      await this.load();
    } catch (error) {
      this.actionError.set(error instanceof ApiError ? error.message : 'The request could not be cancelled.');
    }
  }

  private application() {
    return {
      leaveTypeId: this.typeId(),
      startDate: this.start(),
      endDate: this.end(),
      startsSecondHalf: this.startsSecondHalf(),
      endsFirstHalf: this.endsFirstHalf(),
      reason: this.reason().trim() || null,
    };
  }

  private async load(): Promise<void> {
    try {
      const [balances, requests, holidays, policy] = await Promise.all([
        this.api.myBalances(),
        this.api.myRequests(),
        this.api.myHolidays(this.year),
        this.api.myPolicy().catch(() => null),
      ]);
      this.balances.set(balances);
      this.policy.set(policy);
      this.requests.set(requests.items);
      this.holidays.set(holidays);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Try again in a moment.');
    } finally {
      this.loading.set(false);
    }
  }
}
