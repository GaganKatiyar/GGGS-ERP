import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi, isoToday } from '@gggs/shared/delivery';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { Meeting, MeetingAction, MeetingStatus, MeetingType, TeamMember } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, StatusBadge } from '@gggs/shared/ui';

/** Project meetings (spec §54): agenda before, minutes and actions after - actions tracked to done. */
@Component({
  selector: 'gggs-meetings-tab',
  imports: [DatePipe, ReactiveFormsModule, FormDialog, EmptyState, StatusBadge, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel stack">
      <div class="toolbar">
        <h2 class="meetings__heading">Meetings</h2>
        @if (canManage()) {
          <button type="button" class="btn btn--primary btn--sm" (click)="edit(null)">Schedule meeting</button>
        }
      </div>
      @for (meeting of meetings(); track meeting.id) {
        <article class="meetings__item">
          <div class="meetings__row">
            <strong>{{ meeting.title }}</strong>
            <span class="muted">{{ humanize(meeting.type) }} · {{ meeting.date | date: 'EEE d MMM y' }}{{ meeting.startTime ? ' ' + meeting.startTime.slice(0, 5) : '' }}{{ meeting.location ? ' · ' + meeting.location : '' }}</span>
            <gggs-status-badge [status]="meeting.status" />
            @if (canManage()) {
              <button type="button" class="btn btn--link btn--sm" (click)="edit(meeting)">Edit</button>
            }
          </div>
          @if (meeting.participantNames.length || meeting.externalParticipants) {
            <p class="muted meetings__people">{{ meeting.participantNames.join(', ') }}{{ meeting.externalParticipants ? ' · ' + meeting.externalParticipants : '' }}</p>
          }
          @if (meeting.agenda) {
            <p class="meetings__text"><strong>Agenda:</strong> {{ meeting.agenda }}</p>
          }
          @if (meeting.minutes) {
            <p class="meetings__text"><strong>Minutes:</strong> {{ meeting.minutes }}</p>
          }
          @if (meeting.actions.length || canManage()) {
            <div class="meetings__actions">
              @for (action of meeting.actions; track action.id) {
                <label class="meetings__action" [class.meetings__action--done]="action.status === 'Done'">
                  <input type="checkbox" [checked]="action.status === 'Done'" [disabled]="!canTick(action)" (change)="tick(meeting, action, $any($event.target).checked)" />
                  <span>{{ action.description }}</span>
                  <span class="muted">{{ action.ownerName ?? 'No owner' }}</span>
                  <span [class.meetings__late]="action.isOverdue">{{ action.dueDate ? (action.dueDate | date: 'd MMM') : '' }}</span>
                </label>
              }
              @if (canManage()) {
                <button type="button" class="btn btn--link btn--sm" (click)="openAction(meeting)">Add action</button>
              }
            </div>
          }
        </article>
      } @empty {
        <gggs-empty-state icon="pi pi-calendar" title="No meetings yet" />
      }
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="formOpen" [title]="editing() ? 'Edit meeting' : 'Schedule a meeting'" size="lg" [busy]="busy()" [error]="dialogError()" (save)="save()">
      <div class="form-grid">
        <label class="field form-grid__full">
          <span class="field__label">Title *</span>
          <input type="text" maxlength="200" [value]="title()" (input)="title.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Type</span>
          <select [value]="type()" (change)="type.set($any($event.target).value)">
            @for (option of types; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Status</span>
          <select [value]="status()" (change)="status.set($any($event.target).value)">
            <option value="Scheduled">Scheduled</option>
            <option value="Held">Held</option>
            <option value="Cancelled">Cancelled</option>
          </select>
        </label>
        <label class="field">
          <span class="field__label">Date *</span>
          <input type="date" [value]="date()" (input)="date.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Time</span>
          <input type="time" [value]="time()" (input)="time.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Minutes long</span>
          <input type="number" min="5" max="1440" [value]="duration() ?? ''" (input)="duration.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Where</span>
          <input type="text" maxlength="300" placeholder="Room or call link" [value]="location()" (input)="location.set($any($event.target).value)" />
        </label>
        <fieldset class="meetings__picker form-grid__full">
          <legend>From the team</legend>
          @for (member of team(); track member.id) {
            <label><input type="checkbox" [checked]="participants().includes(member.employeeId)" (change)="toggleParticipant(member.employeeId)" /> {{ member.employeeName }}</label>
          } @empty {
            <span class="muted">Add people to the team first.</span>
          }
        </fieldset>
        <label class="field form-grid__full">
          <span class="field__label">Others (client, partners)</span>
          <input type="text" maxlength="1000" [value]="external()" (input)="external.set($any($event.target).value)" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Agenda</span>
          <textarea rows="3" maxlength="8000" [value]="agenda()" (input)="agenda.set($any($event.target).value)"></textarea>
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Minutes</span>
          <textarea rows="4" [value]="minutes()" (input)="minutes.set($any($event.target).value)"></textarea>
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="actionOpen" title="Add an action" [busy]="busy()" [error]="dialogError()" (save)="saveAction()">
      <div class="form-grid">
        <label class="field form-grid__full">
          <span class="field__label">What *</span>
          <input type="text" maxlength="1000" [value]="actionText()" (input)="actionText.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Owner</span>
          <gggs-employee-select [formControl]="actionOwner" ariaLabel="Owner" />
        </label>
        <label class="field">
          <span class="field__label">Due</span>
          <input type="date" [value]="actionDue()" (input)="actionDue.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .meetings__heading {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .meetings__item {
      padding-block: var(--gggs-space-3);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .meetings__row {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-3);
      align-items: center;
    }

    .meetings__people,
    .meetings__text {
      margin: var(--gggs-space-1) 0;
      white-space: pre-wrap;
    }

    .meetings__actions {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-1);
      margin-block-start: var(--gggs-space-2);
      padding-inline-start: var(--gggs-space-3);
      border-inline-start: 2px solid var(--gggs-color-border);
    }

    .meetings__action {
      display: grid;
      grid-template-columns: auto 1fr auto auto;
      gap: var(--gggs-space-2);
      align-items: center;
    }

    .meetings__action--done span:first-of-type {
      text-decoration: line-through;
      color: var(--gggs-color-text-muted);
    }

    .meetings__late {
      color: var(--gggs-color-danger, #dc2626);
    }

    .meetings__picker {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-3);
      margin: 0;
      padding: var(--gggs-space-2) var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
    }
  `,
})
export class MeetingsTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();
  readonly canManage = input(false);

  /** The signed-in person, who may tick off their own actions. */
  readonly me = input<string | undefined>(undefined);

  protected readonly humanize = humanize;
  protected readonly types: readonly MeetingType[] = ['Kickoff', 'StatusReview', 'SprintPlanning', 'SprintReview', 'Retrospective', 'Steering', 'Client', 'Other'];
  protected readonly meetings = signal<readonly Meeting[]>([]);
  protected readonly team = signal<readonly TeamMember[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly formOpen = signal(false);
  protected readonly editing = signal<Meeting | null>(null);
  protected readonly title = signal('');
  protected readonly type = signal<MeetingType>('StatusReview');
  protected readonly status = signal<MeetingStatus>('Scheduled');
  protected readonly date = signal(isoToday());
  protected readonly time = signal('');
  protected readonly duration = signal<number | null>(30);
  protected readonly location = signal('');
  protected readonly participants = signal<readonly string[]>([]);
  protected readonly external = signal('');
  protected readonly agenda = signal('');
  protected readonly minutes = signal('');
  protected readonly actionOpen = signal(false);
  protected readonly actionMeeting = signal<Meeting | null>(null);
  protected readonly actionText = signal('');
  protected readonly actionDue = signal('');
  protected readonly actionOwner = new FormControl<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  constructor() {
    effect(() => {
      const projectId = this.projectId();
      void this.load(projectId);
      void this.api.team(projectId).then((team) => this.team.set(team.filter((member) => member.status === 'Active')), () => this.team.set([]));
    });
  }

  protected canTick(action: MeetingAction): boolean {
    return this.canManage() || (!!action.ownerEmployeeId && action.ownerEmployeeId === this.me());
  }

  protected toggleParticipant(id: string): void {
    this.participants.update((ids) => (ids.includes(id) ? ids.filter((value) => value !== id) : [...ids, id]));
  }

  protected edit(meeting: Meeting | null): void {
    this.editing.set(meeting);
    this.title.set(meeting?.title ?? '');
    this.type.set(meeting?.type ?? 'StatusReview');
    this.status.set(meeting?.status ?? 'Scheduled');
    this.date.set(meeting?.date ?? isoToday());
    this.time.set(meeting?.startTime?.slice(0, 5) ?? '');
    this.duration.set(meeting?.durationMinutes ?? 30);
    this.location.set(meeting?.location ?? '');
    this.participants.set(meeting?.participantEmployeeIds ?? []);
    this.external.set(meeting?.externalParticipants ?? '');
    this.agenda.set(meeting?.agenda ?? '');
    this.minutes.set(meeting?.minutes ?? '');
    this.dialogError.set(null);
    this.formOpen.set(true);
  }

  protected async save(): Promise<void> {
    if (!this.title().trim() || !this.date()) {
      this.dialogError.set('Give the meeting a title and a date.');
      return;
    }

    await this.dialog(async () => {
      await this.api.saveMeeting(this.projectId(), this.editing()?.id ?? null, {
        title: this.title().trim(),
        type: this.type(),
        status: this.status(),
        date: this.date(),
        startTime: this.time() ? `${this.time()}:00` : null,
        durationMinutes: this.duration(),
        location: this.location() || null,
        participantEmployeeIds: this.participants(),
        externalParticipants: this.external() || null,
        agenda: this.agenda() || null,
        minutes: this.minutes() || null,
      });
      this.formOpen.set(false);
    });
  }

  protected openAction(meeting: Meeting): void {
    this.actionMeeting.set(meeting);
    this.actionText.set('');
    this.actionDue.set('');
    this.actionOwner.setValue(null);
    this.dialogError.set(null);
    this.actionOpen.set(true);
  }

  protected async saveAction(): Promise<void> {
    const meeting = this.actionMeeting();
    if (!meeting || !this.actionText().trim()) {
      this.dialogError.set('Say what needs doing.');
      return;
    }

    await this.dialog(async () => {
      await this.api.saveAction(this.projectId(), meeting.id, null, { description: this.actionText().trim(), ownerEmployeeId: this.actionOwner.value, dueDate: this.actionDue() || null });
      this.actionOpen.set(false);
    });
  }

  protected async tick(meeting: Meeting, action: MeetingAction, done: boolean): Promise<void> {
    this.error.set(null);
    try {
      await this.api.saveAction(this.projectId(), meeting.id, action.id, {
        description: action.description,
        ownerEmployeeId: action.ownerEmployeeId ?? null,
        dueDate: action.dueDate ?? null,
        status: done ? 'Done' : 'Open',
      });
      await this.load(this.projectId());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The action could not be updated.');
    }
  }

  private async dialog(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await action();
      await this.load(this.projectId());
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(projectId: string): Promise<void> {
    try {
      this.meetings.set(await this.api.meetings(projectId));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Meetings could not be loaded.');
    }
  }
}
