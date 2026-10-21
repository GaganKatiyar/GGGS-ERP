import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { EmployeeDirectoryApi, EmployeeSelect } from '@gggs/shared/employee-reference';
import { SeparationApi } from '@gggs/shared/experience';
import type { AbsenceAlert, RetrenchmentResult, ShowCause, ShowCauseOutcome } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge, Tabs } from '@gggs/shared/ui';
import type { TabItem } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const today = () => new Date().toISOString().slice(0, 10);
const inDays = (days: number) => new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10);

/** Show-cause notices: issue, follow the reply and any enquiry, then warn, drop or terminate. */
@Component({
  selector: 'gggs-disciplinary-page',
  imports: [FormsModule, DatePipe, RouterLink, PageHeader, StatusBadge, EmptyState, FormDialog, Skeleton, Tabs, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Disciplinary" subtitle="Show-cause notices, replies and enquiries, and the decision that follows. Nobody is terminated before they have been heard.">
      <button type="button" class="btn btn--primary" (click)="openIssue()">Issue show-cause</button>
    </gggs-page-header>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    <gggs-tabs [tabs]="tabs" [selected]="tab()" (selectedChange)="tab.set($event); load()" />
    @if (notices(); as list) {
      <section class="panel stack">
        @for (notice of list; track notice.id) {
          <div class="xp__row">
            <span><strong>{{ notice.employeeName }}</strong> <span class="muted">{{ notice.employeeCode }}</span><br /><span class="muted">{{ notice.allegation }}</span></span>
            <span>Issued {{ notice.issuedOn | date: 'd MMM y' }}<br /><span class="muted">{{ notice.repliedOn ? 'Replied ' + (notice.repliedOn | date: 'd MMM') : 'Reply due ' + (notice.replyDueOn | date: 'd MMM') }}</span></span>
            <span>
              @if (notice.enquiryOfficerName) {
                Enquiry: {{ notice.enquiryOfficerName }}{{ notice.enquiryCompletedOn ? ' · findings in' : '' }}
              }
              @if (notice.outcome !== 'None') {
                {{ humanize(notice.outcome) }}{{ notice.decisionNote ? ': ' + notice.decisionNote : '' }}
              }
            </span>
            <span class="toolbar">
              <button type="button" class="btn btn--sm" (click)="open(notice)">{{ notice.status === 'Closed' ? 'View' : 'Review' }}</button>
              @if (notice.separationCaseId) {
                <a class="btn btn--sm btn--link" [routerLink]="['/separation/cases', notice.separationCaseId]">Case</a>
              }
            </span>
            <gggs-status-badge [status]="notice.status" />
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-file-edit" [title]="tab() === 'open' ? 'No open notices' : 'Nothing decided yet'" />
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }

    <gggs-form-dialog [(visible)]="issueOpen" title="Issue a show-cause notice" description="The notice is generated as a letter and the employee is asked to reply in PeopleOS." saveLabel="Issue" [busy]="busy()" [error]="formError()" [saveDisabled]="!employee() || !allegation().trim()" (save)="issue()">
      <div class="stack">
        <label class="field"><span class="field__label">Employee</span><gggs-employee-select [ngModel]="employee()" (ngModelChange)="employee.set($event)" /></label>
        <label class="field"><span class="field__label">What is alleged</span><textarea rows="4" maxlength="2000" [value]="allegation()" (input)="allegation.set($any($event.target).value)"></textarea></label>
        <label class="field"><span class="field__label">Days to reply</span><input type="number" min="1" max="30" [value]="replyDays()" (input)="replyDays.set(+$any($event.target).value)" /></label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="reviewOpen" [title]="current()?.employeeName ?? 'Show-cause'" size="lg" [saveLabel]="saveLabel()" [busy]="busy()" [error]="formError()" [saveDisabled]="!canSave()" (save)="act()">
      @if (current(); as n) {
        <div class="stack">
          <div class="xp__grid">
            <div class="xp__wide"><span class="muted">Allegation</span><br />{{ n.allegation }}</div>
            <div><span class="muted">Issued</span><br />{{ n.issuedOn | date: 'd MMM y' }}</div>
            <div><span class="muted">Reply due</span><br />{{ n.replyDueOn | date: 'd MMM y' }}</div>
            <div class="xp__wide"><span class="muted">Reply</span><br />{{ n.reply ?? (n.heard ? 'No reply; the time to reply has passed.' : 'Not yet.') }}</div>
            @if (n.enquiryOfficerName) {
              <div class="xp__wide"><span class="muted">Enquiry by {{ n.enquiryOfficerName }}</span><br />{{ n.enquiryFindings ?? 'Findings not yet recorded.' }}</div>
            }
            @if (n.status === 'Closed') {
              <div class="xp__wide"><span class="muted">Decision</span><br />{{ humanize(n.outcome) }} on {{ n.decidedOn | date: 'd MMM y' }}{{ n.decisionNote ? ': ' + n.decisionNote : '' }}</div>
            }
          </div>
          @if (n.status !== 'Closed') {
            <label class="field">
              <span class="field__label">Next step</span>
              <select [value]="step()" (change)="step.set($any($event.target).value)">
                @if (n.status !== 'UnderEnquiry') {
                  <option value="enquiry">Hold a domestic enquiry</option>
                }
                @if (n.status === 'UnderEnquiry') {
                  <option value="findings">Record the enquiry's findings</option>
                }
                <option value="Warning">Warn</option>
                <option value="Dropped">Drop the matter</option>
                <option value="Termination" [disabled]="!n.heard">Terminate{{ n.heard ? '' : ' (after the reply or its date)' }}</option>
              </select>
            </label>
            @switch (step()) {
              @case ('enquiry') {
                <label class="field"><span class="field__label">Enquiry officer</span><gggs-employee-select [ngModel]="officer()" (ngModelChange)="officer.set($event)" /></label>
              }
              @case ('findings') {
                <label class="field"><span class="field__label">Findings</span><textarea rows="4" maxlength="4000" [value]="text()" (input)="text.set($any($event.target).value)"></textarea></label>
              }
              @default {
                <label class="field"><span class="field__label">Note</span><textarea rows="2" maxlength="2000" [value]="text()" (input)="text.set($any($event.target).value)"></textarea></label>
                @if (step() === 'Termination') {
                  <div class="form-grid">
                    <label class="field"><span class="field__label">Last working day</span><input type="date" [value]="lastDay()" (change)="lastDay.set($any($event.target).value)" /></label>
                    <label class="field"><span class="field__label">Days paid in lieu of notice</span><input type="number" min="0" max="180" [value]="payInLieu()" (input)="payInLieu.set(+$any($event.target).value)" /></label>
                    <label class="xp__flag"><input type="checkbox" [checked]="disableAccess()" (change)="disableAccess.set($any($event.target).checked)" /> Switch off their sign-in now</label>
                  </div>
                }
              }
            }
          }
        </div>
      }
    </gggs-form-dialog>
  `,
})
export class DisciplinaryPage {
  private readonly api = inject(SeparationApi);

  protected readonly humanize = humanize;
  protected readonly tabs: readonly TabItem[] = [
    { key: 'open', label: 'Open' },
    { key: 'closed', label: 'Decided' },
  ];
  protected readonly tab = signal('open');
  protected readonly notices = signal<readonly ShowCause[] | null>(null);
  protected readonly issueOpen = signal(false);
  protected readonly employee = signal<string | null>(null);
  protected readonly allegation = signal('');
  protected readonly replyDays = signal(7);
  protected readonly reviewOpen = signal(false);
  protected readonly current = signal<ShowCause | null>(null);
  protected readonly step = signal<string>('Warning');
  protected readonly officer = signal<string | null>(null);
  protected readonly text = signal('');
  protected readonly lastDay = signal(today());
  protected readonly payInLieu = signal(0);
  protected readonly disableAccess = signal(true);
  protected readonly saveLabel = computed(() => (this.current()?.status === 'Closed' ? 'Close' : this.step() === 'enquiry' ? 'Start enquiry' : this.step() === 'findings' ? 'Save findings' : 'Decide'));
  protected readonly canSave = computed(() => {
    const notice = this.current();
    if (!notice || notice.status === 'Closed') {
      return true;
    }

    return this.step() === 'enquiry' ? !!this.officer() : this.step() === 'findings' ? !!this.text().trim() : this.step() !== 'Termination' || notice.heard;
  });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.api.showCauses(this.tab() === 'open').then(
      (list) => this.notices.set(list),
      (error: unknown) => this.error.set(message(error, 'The notices could not be loaded.')),
    );
  }

  protected openIssue(): void {
    this.employee.set(null);
    this.allegation.set('');
    this.replyDays.set(7);
    this.formError.set(null);
    this.issueOpen.set(true);
  }

  protected issue(): Promise<void> {
    return this.run(async () => {
      await this.api.issueShowCause({ employeeId: this.employee() ?? '', allegation: this.allegation().trim(), replyDays: this.replyDays() });
      this.issueOpen.set(false);
      this.tab.set('open');
      this.load();
    });
  }

  protected open(notice: ShowCause): void {
    this.current.set(notice);
    this.step.set(notice.status === 'UnderEnquiry' ? (notice.enquiryCompletedOn ? 'Warning' : 'findings') : 'Warning');
    this.officer.set(null);
    this.text.set('');
    this.lastDay.set(today());
    this.payInLieu.set(0);
    this.disableAccess.set(true);
    this.formError.set(null);
    this.reviewOpen.set(true);
  }

  protected act(): Promise<void> {
    const notice = this.current();
    if (!notice || notice.status === 'Closed') {
      this.reviewOpen.set(false);
      return Promise.resolve();
    }

    return this.run(async () => {
      const step = this.step();
      const updated =
        step === 'enquiry'
          ? await this.api.startEnquiry(notice.id, this.officer() ?? '')
          : step === 'findings'
            ? await this.api.recordFindings(notice.id, this.text().trim())
            : await this.api.decideShowCause(notice.id, {
                outcome: step as ShowCauseOutcome,
                note: this.text().trim() || null,
                lastWorkingDay: step === 'Termination' ? this.lastDay() : null,
                payInLieuDays: step === 'Termination' ? this.payInLieu() : null,
                disableAccessNow: step === 'Termination' && this.disableAccess(),
              });
      if (updated.status === 'Closed') {
        this.reviewOpen.set(false);
      } else {
        this.open(updated);
      }

      this.load();
    });
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await work();
    } catch (error) {
      this.formError.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Unexplained absences long enough to suggest absconding: notices, then return, dismiss or separate. */
@Component({
  selector: 'gggs-absconding-page',
  imports: [DatePipe, RouterLink, PageHeader, StatusBadge, EmptyState, Skeleton, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Absconding" subtitle="People absent without leave for days on end, found from attendance each night. Send notices before treating anyone as having left.">
      <button type="button" class="btn" [disabled]="busy()" (click)="detect()">Check attendance now</button>
    </gggs-page-header>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    @if (info()) {
      <p class="alert alert--success" role="status">{{ info() }}</p>
    }
    <gggs-tabs [tabs]="tabs" [selected]="tab()" (selectedChange)="tab.set($event); load()" />
    @if (alerts(); as list) {
      <section class="panel stack">
        @for (alert of list; track alert.id) {
          <div class="xp__row">
            <span><strong>{{ alert.employeeName }}</strong> <span class="muted">{{ alert.employeeCode }}</span></span>
            <span>{{ alert.workingDaysAbsent }} working days since {{ alert.firstAbsentDay | date: 'd MMM' }}<br /><span class="muted">last at work {{ alert.lastWorkedDay | date: 'd MMM y' }}</span></span>
            <span>
              @if (alert.noticesSent) {
                {{ alert.noticesSent }} notice{{ alert.noticesSent === 1 ? '' : 's' }} · respond by {{ alert.respondBy | date: 'd MMM' }}
              } @else {
                <span class="muted">No notice yet</span>
              }
              {{ alert.note ? ' · ' + alert.note : '' }}
            </span>
            <span class="toolbar">
              @if (alert.status === 'Open') {
                @if (alert.canSendNotice) {
                  <button type="button" class="btn btn--sm" [disabled]="busy()" (click)="notice(alert)">{{ alert.noticesSent === 2 ? 'Send final notice' : 'Send notice' }}</button>
                }
                <button type="button" class="btn btn--sm btn--link" [disabled]="busy()" (click)="close(alert, true)">Returned</button>
                <button type="button" class="btn btn--sm btn--link" [disabled]="busy()" (click)="close(alert, false)">Dismiss</button>
                @if (alert.canSeparate) {
                  <button type="button" class="btn btn--sm btn--danger" [disabled]="busy()" (click)="separate(alert)">Treat as absconding</button>
                }
              }
              @if (alert.separationCaseId) {
                <a class="btn btn--sm btn--link" [routerLink]="['/separation/cases', alert.separationCaseId]">Case</a>
              }
            </span>
            <gggs-status-badge [status]="alert.status" />
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-user-minus" [title]="tab() === 'open' ? 'No absences to look into' : 'Nothing closed yet'" description="Alerts appear when someone who has been at work is then absent without leave for the number of working days set in the separation policy." />
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }
  `,
})
export class AbscondingPage {
  private readonly api = inject(SeparationApi);

  protected readonly tabs: readonly TabItem[] = [
    { key: 'open', label: 'Open' },
    { key: 'closed', label: 'Closed' },
  ];
  protected readonly tab = signal('open');
  protected readonly alerts = signal<readonly AbsenceAlert[] | null>(null);
  protected readonly busy = signal(false);
  protected readonly info = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.api.absences(this.tab() === 'open').then(
      (list) => this.alerts.set(list),
      (error: unknown) => this.error.set(message(error, 'The absences could not be loaded.')),
    );
  }

  protected detect(): Promise<void> {
    return this.run(async () => {
      const { raised } = await this.api.detectAbsences();
      this.info.set(raised ? `${raised} new absence${raised === 1 ? '' : 's'} found.` : 'No new absences found.');
    });
  }

  protected notice(alert: AbsenceAlert): Promise<void> {
    return this.run(async () => {
      await this.api.sendAbsenceNotice(alert.id);
      this.info.set(`Notice sent to ${alert.employeeName ?? 'the employee'}; it is in their documents.`);
    });
  }

  protected close(alert: AbsenceAlert, returned: boolean): Promise<void> {
    const note = prompt(returned ? 'What did they say? (optional)' : 'Why is this not a concern? (optional)');
    return note === null ? Promise.resolve() : this.run(async () => void (await this.api.closeAbsence(alert.id, returned, note.trim() || null)));
  }

  protected separate(alert: AbsenceAlert): Promise<void> {
    return confirm(`Treat ${alert.employeeName ?? 'this employee'} as having left on ${alert.lastWorkedDay}? A separation is opened and completed overnight.`)
      ? this.run(async () => void (await this.api.separateAbsence(alert.id)))
      : Promise.resolve();
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.info.set(null);
    try {
      await work();
      this.load();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Retrenching several people at once, with notice or pay in lieu and compensation in their settlement. */
@Component({
  selector: 'gggs-retrenchment-page',
  imports: [FormsModule, RouterLink, PageHeader, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header
      title="Retrenchment"
      subtitle="Cut positions under the Industrial Relations Code, 2020: one month's notice or wages in lieu, and fifteen days' average pay per completed year as compensation."
    />
    <section class="panel stack">
      <div class="toolbar">
        <label class="field"><span class="field__label">Add employee</span><gggs-employee-select [ngModel]="adding()" (ngModelChange)="add($event)" /></label>
      </div>
      @for (person of people(); track person.id) {
        <div class="toolbar">
          <span>{{ person.label }}</span>
          <button type="button" class="btn btn--sm btn--link" [attr.aria-label]="'Remove ' + person.label" (click)="remove(person.id)">Remove</button>
        </div>
      } @empty {
        <p class="muted">Add the people whose positions are being cut.</p>
      }
      <div class="form-grid">
        <label class="field form-grid__full"><span class="field__label">Reason</span><input type="text" maxlength="1000" placeholder="Closure of the support unit" [value]="reason()" (input)="reason.set($any($event.target).value)" /></label>
        <label class="field"><span class="field__label">Notice given on</span><input type="date" [value]="noticeDate()" (change)="noticeDate.set($any($event.target).value)" /></label>
        <label class="field"><span class="field__label">Last working day</span><input type="date" [value]="lastDay()" (change)="lastDay.set($any($event.target).value)" /></label>
        <label class="xp__flag"><input type="checkbox" [checked]="payInLieu()" (change)="payInLieu.set($any($event.target).checked)" /> Pay wages in lieu of any notice not given</label>
      </div>
      <p class="muted">{{ noticeHint() }}</p>
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
      <div class="form-actions">
        <button type="button" class="btn btn--danger" [disabled]="busy() || !people().length || !reason().trim()" (click)="retrench()">Retrench {{ people().length || '' }}</button>
      </div>
    </section>
    @if (results().length) {
      <section class="panel stack">
        <h2 class="xp__heading">Result</h2>
        @for (result of results(); track result.employeeId) {
          <div class="toolbar">
            <strong>{{ result.employeeName ?? result.employeeId }}</strong>
            @if (result.caseId) {
              <span>Retrenched{{ result.payInLieuDays ? ', ' + result.payInLieuDays + ' days paid in lieu of notice' : '' }}</span>
              <a class="btn btn--sm btn--link" [routerLink]="['/separation/cases', result.caseId]">Case</a>
            } @else {
              <span class="xp__danger">{{ result.error }}</span>
            }
          </div>
        }
      </section>
    }
  `,
})
export class RetrenchmentPage {
  private readonly api = inject(SeparationApi);
  private readonly directory = inject(EmployeeDirectoryApi);

  protected readonly adding = signal<string | null>(null);
  protected readonly people = signal<readonly { id: string; label: string }[]>([]);
  protected readonly reason = signal('');
  protected readonly noticeDate = signal(today());
  protected readonly lastDay = signal(inDays(30));
  protected readonly payInLieu = signal(false);
  protected readonly results = signal<readonly RetrenchmentResult[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly noticeHint = computed(() => {
    const given = Math.round((Date.parse(this.lastDay()) - Date.parse(this.noticeDate())) / 86_400_000);
    if (Number.isNaN(given)) {
      return '';
    }

    return given >= 30 ? `${given} days' notice: the one month the law asks for is given.` : `${given} days' notice: ${30 - given} days short of a month${this.payInLieu() ? ', paid in lieu' : ' - tick pay in lieu or give more notice'}.`;
  });

  protected add(id: string | null): void {
    if (id && !this.people().some((person) => person.id === id)) {
      this.people.set([...this.people(), { id, label: `Employee ${this.people().length + 1}` }]);
      void this.name(id);
    }

    setTimeout(() => this.adding.set(null));
  }

  protected remove(id: string): void {
    this.people.set(this.people().filter((person) => person.id !== id));
  }

  protected async retrench(): Promise<void> {
    if (!confirm(`Retrench ${this.people().length} employee(s)? Each gets a separation case and a retrenchment notice.`)) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      this.results.set(
        await this.api.retrench({ employeeIds: this.people().map((person) => person.id), reason: this.reason().trim(), noticeDate: this.noticeDate(), lastWorkingDay: this.lastDay(), payInLieuOfNotice: this.payInLieu() }),
      );
      this.people.set(this.people().filter((person) => !this.results().some((result) => result.employeeId === person.id && result.caseId)));
    } catch (error) {
      this.error.set(message(error, 'The retrenchment could not be carried out.'));
    } finally {
      this.busy.set(false);
    }
  }

  /** Shows the chosen person's name and code once the directory answers. */
  private async name(id: string): Promise<void> {
    const profile = await this.directory.get(id).catch(() => null);
    if (profile) {
      this.people.set(this.people().map((person) => (person.id === id ? { ...person, label: `${profile.displayName} (${profile.employeeCode})` } : person)));
    }
  }
}
