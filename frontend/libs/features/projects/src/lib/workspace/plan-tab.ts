import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi, isoToday } from '@gggs/shared/delivery';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { Milestone, MilestoneStatus, Phase, PhaseStatus } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, StatusBadge } from '@gggs/shared/ui';

/** The plan: phases (spec §43) and milestones (spec §46), each with dates, owners and progress. */
@Component({
  selector: 'gggs-plan-tab',
  imports: [DatePipe, DecimalPipe, ReactiveFormsModule, FormDialog, EmptyState, StatusBadge, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="plan">
      <section class="panel stack">
        <div class="toolbar">
          <h2 class="plan__heading">Phases</h2>
          @if (canManage()) {
            <button type="button" class="btn btn--primary btn--sm" (click)="editPhase(null)">Add phase</button>
          }
        </div>
        @for (phase of phases(); track phase.id) {
          <div class="plan__item">
            <div class="plan__row">
              <strong>{{ phase.name }}</strong>
              <span class="muted">{{ phase.startDate ? (phase.startDate | date: 'd MMM y') : '?' }} – {{ phase.endDate ? (phase.endDate | date: 'd MMM y') : '?' }}</span>
              <gggs-status-badge [status]="phase.status" />
            </div>
            <div class="plan__row">
              <span class="plan__bar"><span [style.width.%]="phase.measuredProgress"></span></span>
              <span>{{ phase.measuredProgress | number: '1.0-0' }}%{{ phase.taskCount ? ' of ' + phase.taskCount + ' tasks' : '' }}</span>
              <span class="muted">{{ phase.ownerName ?? '' }}</span>
              @if (canManage()) {
                <button type="button" class="btn btn--link btn--sm" (click)="editPhase(phase)">Edit</button>
              }
            </div>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-sitemap" title="No phases yet" description="Break the plan into stages such as discovery, build and launch." />
        }
      </section>

      <section class="panel stack">
        <div class="toolbar">
          <h2 class="plan__heading">Milestones</h2>
          @if (canManageMilestones()) {
            <button type="button" class="btn btn--primary btn--sm" (click)="editMilestone(null)">Add milestone</button>
          }
        </div>
        @for (milestone of milestones(); track milestone.id) {
          <div class="plan__item">
            <div class="plan__row">
              <strong>{{ milestone.name }}</strong>
              <span [class.plan__late]="milestone.daysOverdue > 0">
                {{ milestone.plannedDate | date: 'd MMM y' }}
                @if (milestone.daysOverdue > 0) {
                  · {{ milestone.daysOverdue }}d late
                }
                @if (milestone.actualDate) {
                  · achieved {{ milestone.actualDate | date: 'd MMM' }}
                }
              </span>
              <gggs-status-badge [status]="milestone.status" />
            </div>
            <div class="plan__row">
              <span class="plan__bar"><span [style.width.%]="milestone.measuredPercent"></span></span>
              <span>{{ milestone.measuredPercent | number: '1.0-0' }}%{{ milestone.isBillable ? ' · billable' : '' }}</span>
              <span class="muted">{{ milestone.ownerName ?? '' }}</span>
              @if (canManageMilestones()) {
                <span class="plan__actions">
                  @if (milestone.status === 'Planned' || milestone.status === 'InProgress') {
                    <button type="button" class="btn btn--link btn--sm" (click)="achieve(milestone)">Achieved</button>
                  }
                  <button type="button" class="btn btn--link btn--sm" (click)="editMilestone(milestone)">Edit</button>
                </span>
              }
            </div>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-flag" title="No milestones yet" />
        }
      </section>
    </div>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="phaseOpen" [title]="editingPhase() ? 'Edit phase' : 'Add a phase'" [busy]="busy()" [error]="dialogError()" (save)="savePhase()">
      <div class="form-grid">
        <label class="field form-grid__full">
          <span class="field__label">Name *</span>
          <input type="text" maxlength="100" [value]="phaseName()" (input)="phaseName.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Starts</span>
          <input type="date" [value]="phaseStart()" (input)="phaseStart.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Ends</span>
          <input type="date" [value]="phaseEnd()" (input)="phaseEnd.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Status</span>
          <select [value]="phaseStatus()" (change)="phaseStatus.set($any($event.target).value)">
            @for (status of phaseStatuses; track status) {
              <option [value]="status">{{ humanize(status) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Progress % <span class="muted">(when it has no tasks)</span></span>
          <input type="number" min="0" max="100" [value]="phaseProgress()" (input)="phaseProgress.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Budget</span>
          <input type="number" min="0" step="1000" [value]="phaseBudget() ?? ''" (input)="phaseBudget.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Owner</span>
          <gggs-employee-select [formControl]="phaseOwner" ariaLabel="Owner" />
        </label>
      </div>
      @if (editingPhase()) {
        <button type="button" class="btn btn--link btn--sm" (click)="deletePhase()">Delete phase</button>
      }
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="milestoneOpen" [title]="editingMilestone() ? 'Edit milestone' : 'Add a milestone'" [busy]="busy()" [error]="dialogError()" (save)="saveMilestone()">
      <div class="form-grid">
        <label class="field form-grid__full">
          <span class="field__label">Name *</span>
          <input type="text" maxlength="200" [value]="milestoneName()" (input)="milestoneName.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Planned date *</span>
          <input type="date" [value]="milestoneDate()" (input)="milestoneDate.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Status</span>
          <select [value]="milestoneStatus()" (change)="milestoneStatus.set($any($event.target).value)">
            @for (status of milestoneStatuses; track status) {
              <option [value]="status">{{ humanize(status) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Phase</span>
          <select [value]="milestonePhase()" (change)="milestonePhase.set($any($event.target).value)">
            <option value="">None</option>
            @for (phase of phases(); track phase.id) {
              <option [value]="phase.id">{{ phase.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Percent complete <span class="muted">(when it has no tasks)</span></span>
          <input type="number" min="0" max="100" [value]="milestonePercent()" (input)="milestonePercent.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Owner</span>
          <gggs-employee-select [formControl]="milestoneOwner" ariaLabel="Owner" />
        </label>
        <label class="field">
          <span><input type="checkbox" [checked]="milestoneBillable()" (change)="milestoneBillable.set($any($event.target).checked)" /> Invoiced when achieved</span>
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Description</span>
          <textarea rows="2" maxlength="2000" [value]="milestoneDescription()" (input)="milestoneDescription.set($any($event.target).value)"></textarea>
        </label>
      </div>
      @if (editingMilestone()) {
        <button type="button" class="btn btn--link btn--sm" (click)="deleteMilestone()">Delete milestone</button>
      }
    </gggs-form-dialog>
  `,
  styles: `
    .plan {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(24rem, 1fr));
      gap: var(--gggs-space-3);
      align-items: start;
    }

    .plan__heading {
      margin-inline-end: auto;
      margin-block: 0;
      font-size: var(--gggs-font-size-md);
    }

    .plan__item {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-1);
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .plan__row {
      display: grid;
      grid-template-columns: 1fr auto auto auto;
      gap: var(--gggs-space-3);
      align-items: center;
    }

    .plan__bar {
      display: block;
      height: 0.4rem;
      border-radius: 999px;
      background: var(--gggs-color-border);
      overflow: hidden;
    }

    .plan__bar span {
      display: block;
      height: 100%;
      background: var(--gggs-color-primary);
    }

    .plan__late {
      color: var(--gggs-color-danger);
    }

    .plan__actions {
      white-space: nowrap;
    }
  `,
})
export class PlanTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();
  readonly canManage = input(false);
  readonly canManageMilestones = input(false);

  protected readonly humanize = humanize;
  protected readonly phaseStatuses: readonly PhaseStatus[] = ['NotStarted', 'InProgress', 'Completed', 'OnHold'];
  protected readonly milestoneStatuses: readonly MilestoneStatus[] = ['Planned', 'InProgress', 'Achieved', 'Cancelled'];
  protected readonly phases = signal<readonly Phase[]>([]);
  protected readonly milestones = signal<readonly Milestone[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  protected readonly phaseOpen = signal(false);
  protected readonly editingPhase = signal<Phase | null>(null);
  protected readonly phaseName = signal('');
  protected readonly phaseStart = signal('');
  protected readonly phaseEnd = signal('');
  protected readonly phaseStatus = signal<PhaseStatus>('NotStarted');
  protected readonly phaseProgress = signal(0);
  protected readonly phaseBudget = signal<number | null>(null);
  protected readonly phaseOwner = new FormControl<string | null>(null);

  protected readonly milestoneOpen = signal(false);
  protected readonly editingMilestone = signal<Milestone | null>(null);
  protected readonly milestoneName = signal('');
  protected readonly milestoneDate = signal(isoToday());
  protected readonly milestoneStatus = signal<MilestoneStatus>('Planned');
  protected readonly milestonePhase = signal('');
  protected readonly milestonePercent = signal(0);
  protected readonly milestoneBillable = signal(false);
  protected readonly milestoneDescription = signal('');
  protected readonly milestoneOwner = new FormControl<string | null>(null);

  constructor() {
    effect(() => {
      void this.load(this.projectId());
    });
  }

  protected editPhase(phase: Phase | null): void {
    this.editingPhase.set(phase);
    this.phaseName.set(phase?.name ?? '');
    this.phaseStart.set(phase?.startDate ?? '');
    this.phaseEnd.set(phase?.endDate ?? '');
    this.phaseStatus.set(phase?.status ?? 'NotStarted');
    this.phaseProgress.set(phase?.progress ?? 0);
    this.phaseBudget.set(phase?.budget ?? null);
    this.phaseOwner.setValue(phase?.ownerEmployeeId ?? null);
    this.dialogError.set(null);
    this.phaseOpen.set(true);
  }

  protected async savePhase(): Promise<void> {
    if (!this.phaseName().trim()) {
      this.dialogError.set('Give the phase a name.');
      return;
    }

    await this.dialog(async () => {
      await this.api.savePhase(this.projectId(), this.editingPhase()?.id ?? null, {
        name: this.phaseName().trim(),
        startDate: this.phaseStart() || null,
        endDate: this.phaseEnd() || null,
        status: this.phaseStatus(),
        progress: this.phaseProgress(),
        budget: this.phaseBudget(),
        ownerEmployeeId: this.phaseOwner.value,
        sortOrder: this.editingPhase()?.sortOrder ?? null,
      });
      this.phaseOpen.set(false);
    });
  }

  protected async deletePhase(): Promise<void> {
    const phase = this.editingPhase();
    if (phase) {
      await this.dialog(async () => {
        await this.api.deletePhase(this.projectId(), phase.id);
        this.phaseOpen.set(false);
      });
    }
  }

  protected editMilestone(milestone: Milestone | null): void {
    this.editingMilestone.set(milestone);
    this.milestoneName.set(milestone?.name ?? '');
    this.milestoneDate.set(milestone?.plannedDate ?? isoToday());
    this.milestoneStatus.set(milestone?.status ?? 'Planned');
    this.milestonePhase.set(milestone?.phaseId ?? '');
    this.milestonePercent.set(milestone?.percentComplete ?? 0);
    this.milestoneBillable.set(milestone?.isBillable ?? false);
    this.milestoneDescription.set(milestone?.description ?? '');
    this.milestoneOwner.setValue(milestone?.ownerEmployeeId ?? null);
    this.dialogError.set(null);
    this.milestoneOpen.set(true);
  }

  protected async saveMilestone(): Promise<void> {
    if (!this.milestoneName().trim() || !this.milestoneDate()) {
      this.dialogError.set('Give the milestone a name and a planned date.');
      return;
    }

    await this.dialog(async () => {
      await this.api.saveMilestone(this.projectId(), this.editingMilestone()?.id ?? null, {
        name: this.milestoneName().trim(),
        plannedDate: this.milestoneDate(),
        status: this.milestoneStatus(),
        phaseId: this.milestonePhase() || null,
        percentComplete: this.milestonePercent(),
        isBillable: this.milestoneBillable(),
        description: this.milestoneDescription() || null,
        ownerEmployeeId: this.milestoneOwner.value,
      });
      this.milestoneOpen.set(false);
    });
  }

  protected async deleteMilestone(): Promise<void> {
    const milestone = this.editingMilestone();
    if (milestone) {
      await this.dialog(async () => {
        await this.api.deleteMilestone(this.projectId(), milestone.id);
        this.milestoneOpen.set(false);
      });
    }
  }

  protected async achieve(milestone: Milestone): Promise<void> {
    this.error.set(null);
    try {
      await this.api.achieveMilestone(this.projectId(), milestone.id, null);
      await this.load(this.projectId());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The milestone could not be updated.');
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
    this.error.set(null);
    try {
      const [phases, milestones] = await Promise.all([this.api.phases(projectId), this.api.milestones(projectId)]);
      this.phases.set(phases);
      this.milestones.set(milestones);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The plan could not be loaded.');
    }
  }
}
