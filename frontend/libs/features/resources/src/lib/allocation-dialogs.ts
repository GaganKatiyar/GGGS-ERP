import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi, ResourcesApi, isoToday } from '@gggs/shared/delivery';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { AllocationItem, AllocationOutcome, ProjectItem, ProjectRole } from '@gggs/shared/models';
import { FormDialog } from '@gggs/shared/ui';

export const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/** The outcome in words - over-allocation is allowed but never silent (spec §57). */
export function outcomeText(outcome: AllocationOutcome): string {
  return outcome.overallocated
    ? `Allocated, but this takes the person to ${outcome.peakAllocationPercent}% at the busiest point - over-allocated.`
    : `Allocated. Peak allocation ${outcome.peakAllocationPercent}%.`;
}

/**
 * Allocate a person to a project (spec §57): who, where, how much and for how long. Saving with
 * a percentage that takes them past 100% is allowed - the caller is told, and the calendar shows it.
 */
@Component({
  selector: 'gggs-allocate-dialog',
  imports: [FormsModule, FormDialog, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog [(visible)]="visible" title="Allocate to a project" saveLabel="Allocate" [busy]="busy()" [error]="error()" [saveDisabled]="!person() || !projectId() || !startDate() || percent() <= 0" (save)="save()">
      @if (!employeeId()) {
        <label class="field">
          <span class="field__label">Person</span>
          <gggs-employee-select ariaLabel="Person" [ngModel]="person()" (ngModelChange)="person.set($event)" />
        </label>
      }
      <label class="field">
        <span class="field__label">Project</span>
        <select [value]="projectId()" (change)="pickProject($any($event.target).value)">
          <option value="">Choose a project</option>
          @for (project of projects(); track project.id) {
            <option [value]="project.id">{{ project.code }} · {{ project.name }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Role</span>
        <select [value]="roleId()" (change)="roleId.set($any($event.target).value)">
          <option value="">Team member</option>
          @for (role of roles(); track role.id) {
            <option [value]="role.id">{{ role.name }}</option>
          }
        </select>
      </label>
      <div class="alloc__grid">
        <label class="field">
          <span class="field__label">From</span>
          <input type="date" [value]="startDate()" (change)="startDate.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Until (optional)</span>
          <input type="date" [value]="endDate()" (change)="endDate.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Allocation %</span>
          <input type="number" min="5" max="100" step="5" [value]="percent()" (input)="percent.set(+$any($event.target).value)" />
        </label>
      </div>
      <label class="alloc__flag">
        <input type="checkbox" [checked]="billable()" (change)="billable.set($any($event.target).checked)" />
        <span>Billable to the client</span>
      </label>
    </gggs-form-dialog>
  `,
  styles: `
    .alloc__grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
      gap: var(--gggs-space-3);
    }

    .alloc__flag {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
    }
  `,
})
export class AllocateDialog {
  private readonly resources = inject(ResourcesApi);
  private readonly projectsApi = inject(ProjectsApi);

  readonly visible = model(false);
  /** Fixed when allocating from a person's profile; otherwise picked in the dialog. */
  readonly employeeId = input<string | null>(null);
  readonly projectPreset = input<string | null>(null);
  readonly allocated = output<AllocationOutcome>();

  protected readonly projects = signal<readonly ProjectItem[]>([]);
  protected readonly roles = signal<readonly ProjectRole[]>([]);
  protected readonly person = signal<string | null>(null);
  protected readonly projectId = signal('');
  protected readonly roleId = signal('');
  protected readonly startDate = signal(isoToday());
  protected readonly endDate = signal('');
  protected readonly percent = signal(100);
  protected readonly billable = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      if (!this.visible()) {
        return;
      }

      this.person.set(this.employeeId());
      this.projectId.set(this.projectPreset() ?? '');
      this.roleId.set('');
      this.startDate.set(isoToday());
      this.endDate.set('');
      this.percent.set(100);
      this.error.set(null);
      if (!this.projects().length) {
        Promise.all([this.projectsApi.list({ pageSize: 100 }), this.projectsApi.roles()]).then(
          ([page, roles]) => {
            this.projects.set(page.items);
            this.roles.set(roles);
          },
          (error: unknown) => this.error.set(message(error, 'Projects could not be loaded.')),
        );
      }
    });
  }

  protected pickProject(id: string): void {
    this.projectId.set(id);
    const project = this.projects().find((candidate) => candidate.id === id);
    if (project) {
      this.billable.set(project.billingType !== 'NonBillable');
    }
  }

  protected async save(): Promise<void> {
    const employeeId = this.person();
    if (!employeeId) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      const outcome = await this.resources.allocate({
        projectId: this.projectId(),
        employeeId,
        projectRoleId: this.roleId() || null,
        startDate: this.startDate(),
        endDate: this.endDate() || null,
        allocationPercent: this.percent(),
        billable: this.billable(),
      });
      this.visible.set(false);
      this.allocated.emit(outcome);
    } catch (error) {
      this.error.set(message(error, 'The allocation could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Change, release or transfer an existing allocation. */
@Component({
  selector: 'gggs-allocation-edit-dialog',
  imports: [FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog [(visible)]="visible" [title]="title()" [saveLabel]="saveLabel()" [busy]="busy()" [error]="error()" [saveDisabled]="mode() === 'transfer' && (!toProject() || !reason().trim())" (save)="save()">
      @if (allocation(); as a) {
        <p class="muted">{{ a.projectCode }} · {{ a.projectName }} - {{ a.roleName }}, {{ a.allocationPercent }}%</p>
        <div class="toolbar">
          <label class="alloc__flag"><input type="radio" name="alloc-mode" [checked]="mode() === 'change'" (change)="mode.set('change')" /> Change</label>
          <label class="alloc__flag"><input type="radio" name="alloc-mode" [checked]="mode() === 'release'" (change)="mode.set('release')" /> Release</label>
          <label class="alloc__flag"><input type="radio" name="alloc-mode" [checked]="mode() === 'transfer'" (change)="mode.set('transfer')" /> Transfer</label>
        </div>
        @switch (mode()) {
          @case ('change') {
            <div class="alloc__grid">
              <label class="field">
                <span class="field__label">From</span>
                <input type="date" [value]="startDate()" (change)="startDate.set($any($event.target).value)" />
              </label>
              <label class="field">
                <span class="field__label">Until</span>
                <input type="date" [value]="endDate()" (change)="endDate.set($any($event.target).value)" />
              </label>
              <label class="field">
                <span class="field__label">Allocation %</span>
                <input type="number" min="5" max="100" step="5" [value]="percent()" (input)="percent.set(+$any($event.target).value)" />
              </label>
            </div>
            <label class="alloc__flag">
              <input type="checkbox" [checked]="billable()" (change)="billable.set($any($event.target).checked)" />
              <span>Billable</span>
            </label>
          }
          @case ('release') {
            <label class="field">
              <span class="field__label">Last day on the project</span>
              <input type="date" [value]="releaseOn()" (change)="releaseOn.set($any($event.target).value)" />
            </label>
          }
          @case ('transfer') {
            <p class="muted">The move goes for approval. Once approved, the person leaves this project the day before and joins the new one.</p>
            <label class="field">
              <span class="field__label">To project</span>
              <select [value]="toProject()" (change)="toProject.set($any($event.target).value)">
                <option value="">Choose a project</option>
                @for (project of projects(); track project.id) {
                  @if (project.id !== a.projectId) {
                    <option [value]="project.id">{{ project.code }} · {{ project.name }}</option>
                  }
                }
              </select>
            </label>
            <div class="alloc__grid">
              <label class="field">
                <span class="field__label">Effective</span>
                <input type="date" [value]="releaseOn()" (change)="releaseOn.set($any($event.target).value)" />
              </label>
              <label class="field">
                <span class="field__label">Allocation %</span>
                <input type="number" min="5" max="100" step="5" [value]="percent()" (input)="percent.set(+$any($event.target).value)" />
              </label>
            </div>
            <label class="field">
              <span class="field__label">Reason</span>
              <textarea rows="2" maxlength="500" [value]="reason()" (input)="reason.set($any($event.target).value)"></textarea>
            </label>
          }
        }
      }
    </gggs-form-dialog>
  `,
  styles: `
    .alloc__grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
      gap: var(--gggs-space-3);
    }

    .alloc__flag {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
    }
  `,
})
export class AllocationEditDialog {
  private readonly resources = inject(ResourcesApi);
  private readonly projectsApi = inject(ProjectsApi);

  readonly visible = model(false);
  readonly allocation = input<AllocationItem | null>(null);
  readonly changed = output<string>();

  protected readonly mode = signal<'change' | 'release' | 'transfer'>('change');
  protected readonly startDate = signal('');
  protected readonly endDate = signal('');
  protected readonly percent = signal(100);
  protected readonly billable = signal(true);
  protected readonly releaseOn = signal(isoToday());
  protected readonly toProject = signal('');
  protected readonly reason = signal('');
  protected readonly projects = signal<readonly ProjectItem[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected title(): string {
    return this.mode() === 'release' ? 'Release from the project' : this.mode() === 'transfer' ? 'Transfer to another project' : 'Change the allocation';
  }

  protected saveLabel(): string {
    return this.mode() === 'release' ? 'Release' : this.mode() === 'transfer' ? 'Request transfer' : 'Save';
  }

  constructor() {
    effect(() => {
      const allocation = this.allocation();
      if (!this.visible() || !allocation) {
        return;
      }

      this.mode.set('change');
      this.startDate.set(allocation.startDate);
      this.endDate.set(allocation.endDate ?? '');
      this.percent.set(allocation.allocationPercent);
      this.billable.set(allocation.billable);
      this.releaseOn.set(isoToday());
      this.toProject.set('');
      this.reason.set('');
      this.error.set(null);
      if (!this.projects().length) {
        this.projectsApi.list({ pageSize: 100 }).then(
          (page) => this.projects.set(page.items),
          () => undefined,
        );
      }
    });
  }

  protected async save(): Promise<void> {
    const allocation = this.allocation();
    if (!allocation) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      let done: string;
      switch (this.mode()) {
        case 'change': {
          const outcome = await this.resources.updateAllocation(allocation.memberId, {
            allocationPercent: this.percent(),
            startDate: this.startDate(),
            endDate: this.endDate() || null,
            billable: this.billable(),
          });
          done = outcomeText(outcome).replace('Allocated', 'Updated');
          break;
        }
        case 'release':
          await this.resources.release(allocation.memberId, this.releaseOn());
          done = 'Released.';
          break;
        default:
          await this.resources.requestTransfer({
            fromMemberId: allocation.memberId,
            toProjectId: this.toProject(),
            allocationPercent: this.percent(),
            effectiveDate: this.releaseOn(),
            billable: allocation.billable,
            reason: this.reason().trim(),
          });
          done = 'Transfer requested.';
      }
      this.visible.set(false);
      this.changed.emit(done);
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}
