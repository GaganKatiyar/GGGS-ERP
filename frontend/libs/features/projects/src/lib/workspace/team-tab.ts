import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi, isoToday } from '@gggs/shared/delivery';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { ProjectRole, TeamMember } from '@gggs/shared/models';
import { EmptyState, FormDialog, StatusBadge } from '@gggs/shared/ui';

/** The project team (spec §41-42): who, in what role, for what share of their time, and at what rates. */
@Component({
  selector: 'gggs-team-tab',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, FormDialog, EmptyState, StatusBadge, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel stack">
      <div class="toolbar">
        <h2 class="team__heading">Team <span class="muted">{{ active().length }} on the project</span></h2>
        <label class="team__released"><input type="checkbox" [checked]="showReleased()" (change)="showReleased.set($any($event.target).checked)" /> Show released</label>
        @if (canManage()) {
          <button type="button" class="btn btn--primary btn--sm" (click)="edit(null)">Add member</button>
        }
      </div>
      <table class="team__table">
        <thead>
          <tr>
            <th scope="col">Person</th>
            <th scope="col">Role</th>
            <th scope="col">Allocation</th>
            <th scope="col">Dates</th>
            @if (seeRates()) {
              <th scope="col">Bill / cost rate</th>
            }
            <th scope="col">Status</th>
            @if (canManage()) {
              <th scope="col"><span class="team__hidden">Actions</span></th>
            }
          </tr>
        </thead>
        <tbody>
          @for (member of shown(); track member.id) {
            <tr>
              <td><a [routerLink]="['/employees', member.employeeId]">{{ member.employeeName }}</a> <span class="muted">{{ member.designation }}</span></td>
              <td>{{ member.roleName }}</td>
              <td>
                {{ member.allocationPercent }}%{{ member.billable ? '' : ' · non-billable' }}
                @if (member.overAllocated && member.status === 'Active') {
                  <span class="team__over" title="Allocated over 100% across projects">{{ member.totalAllocationPercent }}% overall</span>
                }
              </td>
              <td class="muted">{{ member.startDate | date: 'd MMM y' }} – {{ member.endDate ? (member.endDate | date: 'd MMM y') : 'ongoing' }}</td>
              @if (seeRates()) {
                <td>{{ member.billingRate ?? '—' }} / {{ member.costRate ?? '—' }}</td>
              }
              <td><gggs-status-badge [status]="member.status" /></td>
              @if (canManage()) {
                <td class="team__actions">
                  @if (member.status === 'Active') {
                    <button type="button" class="btn btn--link btn--sm" (click)="edit(member)">Edit</button>
                    <button type="button" class="btn btn--link btn--sm" (click)="release(member)">Release</button>
                  }
                </td>
              }
            </tr>
          } @empty {
            <tr>
              <td colspan="7"><gggs-empty-state icon="pi pi-users" title="Nobody on the team yet" /></td>
            </tr>
          }
        </tbody>
      </table>
      @if (error()) {
        <p class="field__error" role="alert">{{ error() }}</p>
      }
    </section>

    <gggs-form-dialog [(visible)]="formOpen" [title]="editing() ? 'Edit ' + editing()?.employeeName : 'Add to the team'" [busy]="busy()" [error]="dialogError()" (save)="save()">
      <div class="form-grid">
        <label class="field form-grid__full">
          <span class="field__label">Person *</span>
          <gggs-employee-select [formControl]="person" ariaLabel="Person" />
        </label>
        <label class="field">
          <span class="field__label">Role *</span>
          <select [value]="roleId()" (change)="roleId.set($any($event.target).value)">
            <option value="">Choose a role</option>
            @for (role of roles(); track role.id) {
              <option [value]="role.id">{{ role.name }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Allocation % *</span>
          <input type="number" min="1" max="100" [value]="allocation()" (input)="allocation.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">From *</span>
          <input type="date" [value]="startDate()" (input)="startDate.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Until</span>
          <input type="date" [value]="endDate()" (input)="endDate.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Billing rate / hour</span>
          <input type="number" min="0" [value]="billingRate() ?? ''" (input)="billingRate.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Cost rate / hour</span>
          <input type="number" min="0" [value]="costRate() ?? ''" (input)="costRate.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
        </label>
        <label class="field">
          <span><input type="checkbox" [checked]="billable()" (change)="billable.set($any($event.target).checked)" /> Billable</span>
        </label>
        @if (selectedRole()?.skills?.length) {
          <p class="muted form-grid__full">The role calls for: {{ selectedRole()?.skills?.join(', ') }}</p>
        }
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .team__heading {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .team__released {
      font-size: var(--gggs-font-size-sm);
    }

    .team__table {
      width: 100%;
      border-collapse: collapse;
    }

    .team__table th,
    .team__table td {
      padding: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
    }

    .team__over {
      display: block;
      font-size: var(--gggs-font-size-xs);
      color: var(--gggs-color-danger, #dc2626);
    }

    .team__actions {
      white-space: nowrap;
    }

    .team__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class TeamTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();
  readonly canManage = input(false);
  readonly seeRates = input(false);

  protected readonly members = signal<readonly TeamMember[]>([]);
  protected readonly roles = signal<readonly ProjectRole[]>([]);
  protected readonly showReleased = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formOpen = signal(false);
  protected readonly editing = signal<TeamMember | null>(null);
  protected readonly person = new FormControl<string | null>(null);
  protected readonly roleId = signal('');
  protected readonly allocation = signal(100);
  protected readonly startDate = signal(isoToday());
  protected readonly endDate = signal('');
  protected readonly billingRate = signal<number | null>(null);
  protected readonly costRate = signal<number | null>(null);
  protected readonly billable = signal(true);
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  protected readonly active = computed(() => this.members().filter((member) => member.status === 'Active'));
  protected readonly shown = computed(() => (this.showReleased() ? this.members() : this.active()));
  protected readonly selectedRole = computed(() => this.roles().find((role) => role.id === this.roleId()));

  constructor() {
    effect(() => {
      void this.load(this.projectId());
    });
  }

  protected edit(member: TeamMember | null): void {
    this.editing.set(member);
    this.person.setValue(member?.employeeId ?? null);
    if (member) {
      this.person.disable();
    } else {
      this.person.enable();
    }
    this.roleId.set(member?.projectRoleId ?? '');
    this.allocation.set(member?.allocationPercent ?? 100);
    this.startDate.set(member?.startDate ?? isoToday());
    this.endDate.set(member?.endDate ?? '');
    this.billingRate.set(member?.billingRate ?? null);
    this.costRate.set(member?.costRate ?? null);
    this.billable.set(member?.billable ?? true);
    this.dialogError.set(null);
    this.formOpen.set(true);
  }

  protected async save(): Promise<void> {
    const employeeId = this.person.value;
    if (!employeeId || !this.roleId()) {
      this.dialogError.set('Choose the person and their role.');
      return;
    }

    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await this.api.saveMember(this.projectId(), this.editing()?.id ?? null, {
        employeeId,
        projectRoleId: this.roleId(),
        allocationPercent: this.allocation(),
        startDate: this.startDate(),
        endDate: this.endDate() || null,
        billingRate: this.billingRate(),
        costRate: this.costRate(),
        billable: this.billable(),
      });
      this.formOpen.set(false);
      await this.load(this.projectId());
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The team could not be updated.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async release(member: TeamMember): Promise<void> {
    this.error.set(null);
    try {
      await this.api.releaseMember(this.projectId(), member.id, isoToday());
      await this.load(this.projectId());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The member could not be released.');
    }
  }

  private async load(projectId: string): Promise<void> {
    this.error.set(null);
    try {
      const [members, roles] = await Promise.all([this.api.team(projectId), this.api.roles()]);
      this.members.set(members);
      this.roles.set(roles);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The team could not be loaded.');
    }
  }
}
