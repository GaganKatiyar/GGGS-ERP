import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { Requirement, RequirementPriority, RequirementStatus, RequirementType } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, StatusBadge } from '@gggs/shared/ui';

/** Requirements (spec §49), each traced to the tasks that deliver it. */
@Component({
  selector: 'gggs-requirements-tab',
  imports: [ReactiveFormsModule, FormDialog, EmptyState, StatusBadge, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel stack">
      <div class="toolbar">
        <h2 class="req__heading">Requirements</h2>
        @if (canEdit()) {
          <button type="button" class="btn btn--primary btn--sm" (click)="open(null)">Add requirement</button>
        }
      </div>
      @for (requirement of requirements(); track requirement.id) {
        <button type="button" class="req__row" (click)="open(requirement)">
          <strong>{{ requirement.code }}</strong>
          <span>{{ requirement.title }} <span class="muted">· {{ humanize(requirement.type) }}</span></span>
          <span class="req__priority">{{ requirement.priority === 'Wont' ? "Won't" : requirement.priority }}</span>
          <span class="muted">{{ requirement.doneTaskCount }}/{{ requirement.taskCount }} tasks done</span>
          <gggs-status-badge [status]="requirement.status" />
        </button>
      } @empty {
        <gggs-empty-state icon="pi pi-list-check" title="No requirements yet" description="Capture what the client needs, then link tasks to trace delivery." />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>

    <gggs-form-dialog [(visible)]="formOpen" [title]="editing() ? editing()?.code + ' · ' + editing()?.title : 'New requirement'" size="lg" [busy]="busy()" [error]="dialogError()" [saveDisabled]="!canEdit()" (save)="save()">
      <div class="form-grid">
        <label class="field form-grid__full">
          <span class="field__label">Title *</span>
          <input type="text" maxlength="300" [value]="title()" (input)="title.set($any($event.target).value)" />
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
          <span class="field__label">Priority (MoSCoW)</span>
          <select [value]="priority()" (change)="priority.set($any($event.target).value)">
            <option value="Must">Must</option>
            <option value="Should">Should</option>
            <option value="Could">Could</option>
            <option value="Wont">Won't (this time)</option>
          </select>
        </label>
        <label class="field">
          <span class="field__label">Status</span>
          <select [value]="status()" (change)="status.set($any($event.target).value)">
            @for (option of statuses; track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Owner</span>
          <gggs-employee-select [formControl]="owner" ariaLabel="Owner" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Source</span>
          <input type="text" maxlength="200" placeholder="Workshop, RFP, regulation…" [value]="source()" (input)="source.set($any($event.target).value)" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Description</span>
          <textarea rows="3" maxlength="8000" [value]="description()" (input)="description.set($any($event.target).value)"></textarea>
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Acceptance criteria</span>
          <textarea rows="3" maxlength="4000" [value]="criteria()" (input)="criteria.set($any($event.target).value)"></textarea>
        </label>
      </div>
      @if (editing() && canEdit()) {
        <button type="button" class="btn btn--link btn--sm req__delete" (click)="remove()">Delete requirement</button>
      }
    </gggs-form-dialog>
  `,
  styles: `
    .req__heading {
      margin-inline-end: auto;
      margin-block: 0;
      font-size: var(--gggs-font-size-md);
    }

    .req__row {
      display: grid;
      grid-template-columns: 6rem 1fr 5rem 9rem 8rem;
      gap: var(--gggs-space-3);
      align-items: center;
      padding: var(--gggs-space-2) 0;
      border: 0;
      border-block-end: 1px solid var(--gggs-color-border);
      background: transparent;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .req__priority {
      font-weight: var(--gggs-font-weight-semibold);
    }

    .req__delete {
      margin-block-start: var(--gggs-space-3);
    }
  `,
})
export class RequirementsTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();
  readonly canEdit = input(false);

  protected readonly humanize = humanize;
  protected readonly types: readonly RequirementType[] = ['Business', 'Functional', 'NonFunctional', 'Technical', 'Compliance'];
  protected readonly statuses: readonly RequirementStatus[] = ['Draft', 'Approved', 'InProgress', 'Implemented', 'Verified', 'Rejected', 'Deferred'];
  protected readonly requirements = signal<readonly Requirement[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly formOpen = signal(false);
  protected readonly editing = signal<Requirement | null>(null);
  protected readonly title = signal('');
  protected readonly type = signal<RequirementType>('Functional');
  protected readonly priority = signal<RequirementPriority>('Must');
  protected readonly status = signal<RequirementStatus>('Draft');
  protected readonly source = signal('');
  protected readonly description = signal('');
  protected readonly criteria = signal('');
  protected readonly owner = new FormControl<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  constructor() {
    effect(() => {
      void this.load(this.projectId());
    });
  }

  protected open(requirement: Requirement | null): void {
    this.editing.set(requirement);
    this.title.set(requirement?.title ?? '');
    this.type.set(requirement?.type ?? 'Functional');
    this.priority.set(requirement?.priority ?? 'Must');
    this.status.set(requirement?.status ?? 'Draft');
    this.source.set(requirement?.source ?? '');
    this.description.set(requirement?.description ?? '');
    this.criteria.set(requirement?.acceptanceCriteria ?? '');
    this.owner.setValue(requirement?.ownerEmployeeId ?? null);
    this.dialogError.set(null);
    this.formOpen.set(true);
  }

  protected async save(): Promise<void> {
    if (!this.title().trim()) {
      this.dialogError.set('Give the requirement a title.');
      return;
    }

    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await this.api.saveRequirement(this.projectId(), this.editing()?.id ?? null, {
        title: this.title().trim(),
        type: this.type(),
        priority: this.priority(),
        status: this.status(),
        source: this.source() || null,
        description: this.description() || null,
        acceptanceCriteria: this.criteria() || null,
        ownerEmployeeId: this.owner.value,
      });
      this.formOpen.set(false);
      await this.load(this.projectId());
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The requirement could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const requirement = this.editing();
    if (!requirement) {
      return;
    }

    try {
      await this.api.deleteRequirement(this.projectId(), requirement.id);
      this.formOpen.set(false);
      await this.load(this.projectId());
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The requirement could not be deleted.');
    }
  }

  private async load(projectId: string): Promise<void> {
    this.error.set(null);
    try {
      this.requirements.set(await this.api.requirements(projectId));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Requirements could not be loaded.');
    }
  }
}
