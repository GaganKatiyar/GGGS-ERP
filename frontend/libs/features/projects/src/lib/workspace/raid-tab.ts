import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { RaidItem, RaidKind, RaidStatus, Severity, WorkPriority } from '@gggs/shared/models';
import { humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, StatusBadge, Tabs, type TabItem } from '@gggs/shared/ui';

/** The RAID log (spec §51-53): risks, assumptions, issues and dependencies in one register. */
@Component({
  selector: 'gggs-raid-tab',
  imports: [DatePipe, ReactiveFormsModule, FormDialog, EmptyState, StatusBadge, Tabs, EmployeeSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel stack">
      <div class="toolbar">
        <h2 class="raid__heading">RAID log</h2>
        <label class="raid__closed"><input type="checkbox" [checked]="showClosed()" (change)="showClosed.set($any($event.target).checked)" /> Show closed</label>
        @if (canEdit()) {
          <button type="button" class="btn btn--primary btn--sm" (click)="edit(null)">Log {{ kindLabel(kind()).toLowerCase() }}</button>
        }
      </div>
      <gggs-tabs [tabs]="tabs()" [(selected)]="kind" />
      @for (item of shown(); track item.id) {
        <button type="button" class="raid__row" (click)="edit(item)">
          <strong>{{ item.code }}</strong>
          <span>{{ item.title }}</span>
          @if (item.kind === 'Risk') {
            <span class="raid__score" [class.raid__score--high]="(item.score ?? 0) >= 15" [class.raid__score--mid]="(item.score ?? 0) >= 8 && (item.score ?? 0) < 15">{{ item.score }} <span class="muted">({{ item.probability }}×{{ item.impact }})</span></span>
          } @else if (item.kind === 'Issue') {
            <gggs-status-badge [status]="item.severity ?? ''" [variants]="severityVariants" />
          } @else {
            <span></span>
          }
          <span class="muted">{{ item.ownerName ?? '' }}</span>
          <span [class.raid__late]="item.isOverdue">{{ item.dueDate ? (item.dueDate | date: 'd MMM') : '' }}</span>
          <gggs-status-badge [status]="item.status" />
        </button>
      } @empty {
        <gggs-empty-state icon="pi pi-exclamation-triangle" [title]="'No ' + kindLabel(kind()).toLowerCase() + 's logged'" />
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>

    <gggs-form-dialog [(visible)]="formOpen" [title]="editing() ? editing()?.code + ' · ' + editing()?.title : 'Log a ' + kindLabel(kind()).toLowerCase()" size="lg" [busy]="busy()" [error]="dialogError()" [saveDisabled]="!canEdit()" (save)="save()">
      <div class="form-grid">
        <label class="field form-grid__full">
          <span class="field__label">Title *</span>
          <input type="text" maxlength="300" [value]="title()" (input)="title.set($any($event.target).value)" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Description</span>
          <textarea rows="2" maxlength="4000" [value]="description()" (input)="description.set($any($event.target).value)"></textarea>
        </label>
        @if (formKind() === 'Risk') {
          <label class="field">
            <span class="field__label">Probability (1-5) *</span>
            <input type="number" min="1" max="5" [value]="probability()" (input)="probability.set(+$any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">Impact (1-5) *</span>
            <input type="number" min="1" max="5" [value]="impact()" (input)="impact.set(+$any($event.target).value)" />
          </label>
          <p class="muted form-grid__full">Score {{ probability() * impact() }} of 25.</p>
        }
        @if (formKind() === 'Issue') {
          <label class="field">
            <span class="field__label">Severity *</span>
            <select [value]="severity()" (change)="severity.set($any($event.target).value)">
              <option value="Low">Low</option>
              <option value="Medium">Medium</option>
              <option value="High">High</option>
              <option value="Critical">Critical</option>
            </select>
          </label>
        }
        <label class="field">
          <span class="field__label">Priority</span>
          <select [value]="priority()" (change)="priority.set($any($event.target).value)">
            <option value="Low">Low</option>
            <option value="Medium">Medium</option>
            <option value="High">High</option>
            <option value="Critical">Critical</option>
          </select>
        </label>
        <label class="field">
          <span class="field__label">Status</span>
          <select [value]="status()" (change)="status.set($any($event.target).value)">
            @for (option of statuses(); track option) {
              <option [value]="option">{{ humanize(option) }}</option>
            }
          </select>
        </label>
        <label class="field">
          <span class="field__label">Category</span>
          <input type="text" maxlength="100" [value]="category()" (input)="category.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Owner</span>
          <gggs-employee-select [formControl]="owner" ariaLabel="Owner" />
        </label>
        <label class="field">
          <span class="field__label">Assigned to</span>
          <gggs-employee-select [formControl]="assignee" ariaLabel="Assigned to" />
        </label>
        <label class="field">
          <span class="field__label">Due</span>
          <input type="date" [value]="dueDate()" (input)="dueDate.set($any($event.target).value)" />
        </label>
        @if (formKind() === 'Risk') {
          <label class="field form-grid__full">
            <span class="field__label">Mitigation</span>
            <textarea rows="2" maxlength="4000" [value]="mitigation()" (input)="mitigation.set($any($event.target).value)"></textarea>
          </label>
          <label class="field form-grid__full">
            <span class="field__label">Contingency plan</span>
            <textarea rows="2" maxlength="4000" [value]="contingency()" (input)="contingency.set($any($event.target).value)"></textarea>
          </label>
        }
        @if (formKind() === 'Issue') {
          <label class="field form-grid__full">
            <span class="field__label">Root cause</span>
            <textarea rows="2" maxlength="4000" [value]="rootCause()" (input)="rootCause.set($any($event.target).value)"></textarea>
          </label>
        }
        <label class="field form-grid__full">
          <span class="field__label">Resolution</span>
          <textarea rows="2" maxlength="4000" [value]="resolution()" (input)="resolution.set($any($event.target).value)"></textarea>
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .raid__heading {
      margin-inline-end: auto;
      margin-block: 0;
      font-size: var(--gggs-font-size-md);
    }

    .raid__closed {
      font-size: var(--gggs-font-size-sm);
    }

    .raid__row {
      display: grid;
      grid-template-columns: 5rem 1fr 7rem 9rem 5rem 7rem;
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

    .raid__score {
      font-weight: var(--gggs-font-weight-semibold);
    }

    .raid__score--mid {
      color: var(--gggs-color-warning);
    }

    .raid__score--high,
    .raid__late {
      color: var(--gggs-color-danger);
    }
  `,
})
export class RaidTab {
  private readonly api = inject(ProjectsApi);

  readonly projectId = input.required<string>();
  readonly canEdit = input(false);

  protected readonly humanize = humanize;
  protected readonly severityVariants = { low: 'neutral', medium: 'primary', high: 'warning', critical: 'danger' } as const;
  protected readonly items = signal<readonly RaidItem[]>([]);
  protected readonly kind = signal<string>('Risk');
  protected readonly showClosed = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly formOpen = signal(false);
  protected readonly editing = signal<RaidItem | null>(null);
  protected readonly formKind = signal<RaidKind>('Risk');
  protected readonly title = signal('');
  protected readonly description = signal('');
  protected readonly category = signal('');
  protected readonly probability = signal(3);
  protected readonly impact = signal(3);
  protected readonly severity = signal<Severity>('Medium');
  protected readonly priority = signal<WorkPriority>('Medium');
  protected readonly status = signal<RaidStatus>('Open');
  protected readonly dueDate = signal('');
  protected readonly mitigation = signal('');
  protected readonly contingency = signal('');
  protected readonly rootCause = signal('');
  protected readonly resolution = signal('');
  protected readonly owner = new FormControl<string | null>(null);
  protected readonly assignee = new FormControl<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  protected readonly tabs = computed<readonly TabItem[]>(() =>
    (['Risk', 'Assumption', 'Issue', 'Dependency'] as const).map((kind) => {
      const open = this.items().filter((item) => item.kind === kind && (item.status === 'Open' || item.status === 'InProgress')).length;
      return { key: kind, label: `${this.kindLabel(kind)}s`, note: open ? String(open) : undefined };
    }),
  );

  protected readonly shown = computed(() =>
    this.items().filter((item) => item.kind === this.kind() && (this.showClosed() || item.status === 'Open' || item.status === 'InProgress' || item.status === 'Accepted')),
  );

  protected readonly statuses = computed<readonly RaidStatus[]>(() =>
    this.formKind() === 'Risk' ? ['Open', 'InProgress', 'Accepted', 'Resolved', 'Closed'] : ['Open', 'InProgress', 'Resolved', 'Closed'],
  );

  constructor() {
    effect(() => {
      void this.load(this.projectId());
    });
  }

  protected kindLabel(kind: string): string {
    return kind;
  }

  protected edit(item: RaidItem | null): void {
    this.editing.set(item);
    this.formKind.set(item?.kind ?? (this.kind() as RaidKind));
    this.title.set(item?.title ?? '');
    this.description.set(item?.description ?? '');
    this.category.set(item?.category ?? '');
    this.probability.set(item?.probability ?? 3);
    this.impact.set(item?.impact ?? 3);
    this.severity.set(item?.severity ?? 'Medium');
    this.priority.set(item?.priority ?? 'Medium');
    this.status.set(item?.status ?? 'Open');
    this.dueDate.set(item?.dueDate ?? '');
    this.mitigation.set(item?.mitigation ?? '');
    this.contingency.set(item?.contingencyPlan ?? '');
    this.rootCause.set(item?.rootCause ?? '');
    this.resolution.set(item?.resolution ?? '');
    this.owner.setValue(item?.ownerEmployeeId ?? null);
    this.assignee.setValue(item?.assigneeEmployeeId ?? null);
    this.dialogError.set(null);
    this.formOpen.set(true);
  }

  protected async save(): Promise<void> {
    if (!this.title().trim()) {
      this.dialogError.set('Give it a title.');
      return;
    }

    const kind = this.formKind();
    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await this.api.saveRaid(this.projectId(), this.editing()?.id ?? null, {
        kind,
        title: this.title().trim(),
        description: this.description() || null,
        category: this.category() || null,
        probability: kind === 'Risk' ? this.probability() : null,
        impact: kind === 'Risk' ? this.impact() : null,
        severity: kind === 'Issue' ? this.severity() : null,
        priority: this.priority(),
        status: this.status(),
        dueDate: this.dueDate() || null,
        mitigation: this.mitigation() || null,
        contingencyPlan: this.contingency() || null,
        rootCause: this.rootCause() || null,
        resolution: this.resolution() || null,
        ownerEmployeeId: this.owner.value,
        assigneeEmployeeId: this.assignee.value,
      });
      this.formOpen.set(false);
      await this.load(this.projectId());
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The entry could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(projectId: string): Promise<void> {
    this.error.set(null);
    try {
      this.items.set(await this.api.raid(projectId));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The RAID log could not be loaded.');
    }
  }
}
