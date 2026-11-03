import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { DataHubApi } from '@gggs/shared/insight';
import type { Automation, AutomationCondition, AutomationConditionOperator, AutomationConnection, AutomationInput, AutomationTestOutcome, AutomationTrigger } from '@gggs/shared/models';
import type { BadgeVariant } from '@gggs/shared/ui';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { CONDITION_OPERATORS, insertPlaceholder, renderTemplate, sampleFields, unknownPlaceholders } from './studio-model';

const STATUS_VARIANTS: Readonly<Record<string, BadgeVariant>> = { succeeded: 'success', pending: 'primary', failed: 'warning', abandoned: 'danger' };

/** "When this happens, only if these hold, do that": automations on business events, built in three steps. */
@Component({
  selector: 'gggs-automations-page',
  imports: [DatePipe, RouterLink, PageHeader, EmptyState, FormDialog, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Automations" subtitle="When something happens in Nuts and Bolts, act in another tool: post to Slack when someone joins, tell a channel when payroll is done.">
      @if (canManage) {
        <button type="button" class="btn btn--primary" (click)="open(null)"><i class="pi pi-plus" aria-hidden="true"></i> New automation</button>
      }
    </gggs-page-header>

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (loading()) {
      <gggs-skeleton [lines]="5" />
    } @else if (automations().length === 0) {
      <gggs-empty-state icon="pi pi-sitemap" title="No automations yet" description="Connect a tool under Connectors, then make your first automation here." />
    } @else {
      <div class="panel panel--flush table-wrap" tabindex="0" role="region" aria-label="Automations">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">Automation</th>
              <th scope="col">When</th>
              <th scope="col">Do</th>
              <th scope="col">Last delivery</th>
              <th scope="col">Status</th>
              <th scope="col"><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (automation of automations(); track automation.id) {
              <tr>
                <td><strong>{{ automation.name }}</strong></td>
                <td>
                  {{ automation.triggerLabel }}
                  @if (automation.conditions.length) {
                    <span class="automations__meta">{{ automation.conditions.length }} condition(s)</span>
                  }
                </td>
                <td>{{ automation.operationLabel }}<span class="automations__meta">{{ automation.connectionName }}</span></td>
                <td>
                  @if (automation.lastStatus; as last) {
                    <a [routerLink]="['../deliveries']" [queryParams]="{ automation: automation.id }" [attr.aria-label]="'Deliveries of ' + automation.name">
                      <gggs-status-badge [status]="last" [variants]="variants" />
                    </a>
                    <span class="automations__meta">{{ automation.lastDeliveredAt | date: 'd MMM, HH:mm' }}</span>
                  } @else {
                    Not yet
                  }
                </td>
                <td><gggs-status-badge [status]="automation.isActive ? 'Active' : 'Inactive'" /></td>
                <td class="automations__actions">
                  @if (canManage) {
                    <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Edit ' + automation.name" (click)="open(automation)">Edit</button>
                    <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="(automation.isActive ? 'Switch off ' : 'Switch on ') + automation.name" (click)="toggle(automation)">{{ automation.isActive ? 'Switch off' : 'Switch on' }}</button>
                    <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Delete ' + automation.name" (click)="remove(automation)">Delete</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }

    <gggs-form-dialog [(visible)]="dialogOpen" size="lg" [title]="editingId() ? 'Edit automation' : 'New automation'" [busy]="busy()" [error]="formError()" [saveDisabled]="!canSave()" (save)="save()">
      <label class="field">
        <span class="field__label">Name *</span>
        <input type="text" maxlength="120" placeholder="Welcome new joiners" [value]="name()" (input)="name.set($any($event.target).value)" />
      </label>

      <fieldset class="builder__step">
        <legend>1. When</legend>
        <label class="field">
          <span class="field__label">This happens *</span>
          <select (change)="chooseTrigger($any($event.target).value)">
            <option value="" [selected]="!triggerCode()">Choose…</option>
            @for (group of triggerGroups(); track group.category) {
              <optgroup [label]="group.category">
                @for (option of group.triggers; track option.code) {
                  <option [value]="option.code" [selected]="option.code === triggerCode()">{{ option.label }}</option>
                }
              </optgroup>
            }
          </select>
        </label>
        @if (trigger(); as chosen) {
          <p class="muted builder__note">{{ chosen.description }}</p>
        }
      </fieldset>

      <fieldset class="builder__step" [disabled]="!trigger()">
        <legend>2. Only if <span class="muted">(optional)</span></legend>
        @for (condition of conditions(); track $index; let index = $index) {
          <div class="builder__condition">
            <select aria-label="Field" (change)="setCondition(index, { field: $any($event.target).value })">
              @for (field of trigger()?.fields ?? []; track field.key) {
                <option [value]="field.key" [selected]="field.key === condition.field">{{ field.label }}</option>
              }
            </select>
            <select aria-label="Comparison" (change)="setCondition(index, { operator: $any($event.target).value })">
              @for (operator of operators; track operator.value) {
                <option [value]="operator.value" [selected]="operator.value === condition.operator">{{ operator.label }}</option>
              }
            </select>
            <input type="text" aria-label="Value" [value]="condition.value" (input)="setCondition(index, { value: $any($event.target).value })" />
            <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Remove condition ' + (index + 1)" (click)="removeCondition(index)"><i class="pi pi-times" aria-hidden="true"></i></button>
          </div>
        }
        <div><button type="button" class="btn btn--sm" [disabled]="!trigger() || conditions().length >= 10" (click)="addCondition()"><i class="pi pi-plus" aria-hidden="true"></i> Add a condition</button></div>
      </fieldset>

      <fieldset class="builder__step" [disabled]="!trigger()">
        <legend>3. Do</legend>
        @if (connections().length === 0) {
          <p class="alert alert--warning"><i class="pi pi-info-circle" aria-hidden="true"></i><span>Nothing to act through yet. <a routerLink="../connectors">Connect Slack, Teams or a webhook</a> first.</span></p>
        }
        <div class="builder__pair">
          <label class="field">
            <span class="field__label">Through *</span>
            <select (change)="chooseConnection($any($event.target).value)">
              <option value="" [selected]="!connectionId()">Choose…</option>
              @for (option of connections(); track option.id) {
                <option [value]="option.id" [selected]="option.id === connectionId()">{{ option.name }} ({{ option.adapterName }}){{ option.isActive ? '' : ' - switched off' }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span class="field__label">Action *</span>
            <select [disabled]="!connection()" (change)="chooseOperation($any($event.target).value)">
              @for (option of connection()?.operations ?? []; track option.key) {
                <option [value]="option.key" [selected]="option.key === operationKey()">{{ option.label }}</option>
              }
            </select>
          </label>
        </div>

        @for (parameter of operation()?.parameters ?? []; track parameter.key) {
          <div class="field">
            <label class="field__label" [attr.for]="'parameter-' + parameter.key">{{ parameter.label }}{{ parameter.required ? ' *' : '' }}</label>
            <textarea
              #box
              [id]="'parameter-' + parameter.key"
              [rows]="parameter.key === 'text' || parameter.key === 'message' || parameter.key === 'body' ? 3 : 1"
              [placeholder]="parameter.hint ?? ''"
              [value]="parameters()[parameter.key] || ''"
              (input)="setParameter(parameter.key, $any($event.target).value)"
            ></textarea>
            <div class="builder__under">
              <select [attr.aria-label]="'Insert a field into ' + parameter.label" (change)="insert(parameter.key, box, $any($event.target))">
                <option value="">Insert a field…</option>
                @for (field of trigger()?.fields ?? []; track field.key) {
                  <option [value]="field.key">{{ field.label }}</option>
                }
              </select>
              @if (parameters()[parameter.key]) {
                <span class="builder__preview"><span class="muted">Preview:</span> {{ preview(parameter.key) }}</span>
              }
            </div>
            @if (unknown(parameter.key).length) {
              <p class="builder__warn" role="status">{{ trigger()?.label }} has no {{ unknown(parameter.key).join(', ') }}: that part will come out empty.</p>
            }
          </div>
        }

        @if (operation()) {
          <div class="builder__test">
            <button type="button" class="btn btn--sm" [disabled]="!canSave() || testing()" (click)="test()"><i class="pi pi-send" aria-hidden="true"></i> {{ testing() ? 'Sending…' : 'Send a test' }}</button>
            <span class="muted">Sends once with example data, so it really arrives.</span>
          </div>
          @if (tested(); as outcome) {
            <p class="alert" [class.alert--danger]="!outcome.succeeded" [class.alert--success]="outcome.succeeded" role="status">{{ outcome.message }}</p>
          }
        }
      </fieldset>

      <label class="builder__active"><input type="checkbox" [checked]="isActive()" (change)="isActive.set($any($event.target).checked)" /> Switched on</label>
    </gggs-form-dialog>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .automations__meta { display: block; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); }
    .automations__actions { white-space: nowrap; }
    .builder__step { display: flex; flex-direction: column; gap: var(--gggs-space-3); min-width: 0; margin: 0; padding: var(--gggs-space-3) var(--gggs-space-4) var(--gggs-space-4); border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-md); }
    .builder__step legend { padding: 0 var(--gggs-space-2); font-weight: var(--gggs-font-weight-semibold); }
    .builder__note, .builder__warn { margin: 0; }
    .builder__warn { color: var(--gggs-color-warning); font-size: var(--gggs-font-size-xs); }
    .builder__condition { display: grid; grid-template-columns: minmax(0, 1.2fr) minmax(0, .7fr) minmax(0, 1.2fr) auto; gap: var(--gggs-space-2); align-items: center; }
    .builder__pair { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(16rem, 100%), 1fr)); gap: var(--gggs-space-3); }
    .builder__step textarea[rows='1'] { min-height: 0; resize: none; }
    .builder__under { display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-3); margin-top: var(--gggs-space-1); }
    .builder__under select { width: auto; max-width: 14rem; }
    .builder__preview { flex: 1 1 14rem; min-width: 0; overflow-wrap: anywhere; font-size: var(--gggs-font-size-xs); }
    .builder__test { display: flex; flex-wrap: wrap; align-items: center; gap: var(--gggs-space-3); }
    .builder__active { display: inline-flex; align-items: center; gap: var(--gggs-space-2); }
    @media (max-width: 40rem) {
      .builder__condition { grid-template-columns: minmax(0, 1fr) auto; }
      .builder__condition select { grid-column: 1 / -1; }
    }
  `,
})
export class AutomationsPage {
  private readonly api = inject(DataHubApi);

  protected readonly canManage = inject(PermissionStore).has('datahub.integration.manage');
  protected readonly variants = STATUS_VARIANTS;
  protected readonly operators = CONDITION_OPERATORS;
  protected readonly loading = signal(true);
  protected readonly automations = signal<readonly Automation[]>([]);
  protected readonly triggers = signal<readonly AutomationTrigger[]>([]);
  protected readonly connections = signal<readonly AutomationConnection[]>([]);
  protected readonly error = signal<string | null>(null);

  protected readonly dialogOpen = signal(false);
  protected readonly busy = signal(false);
  protected readonly testing = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly tested = signal<AutomationTestOutcome | null>(null);
  protected readonly editingId = signal<string | null>(null);
  protected readonly name = signal('');
  protected readonly triggerCode = signal('');
  protected readonly conditions = signal<readonly AutomationCondition[]>([]);
  protected readonly connectionId = signal('');
  protected readonly operationKey = signal('');
  protected readonly parameters = signal<Readonly<Record<string, string>>>({});
  protected readonly isActive = signal(true);

  protected readonly triggerGroups = computed(() => {
    const groups = new Map<string, AutomationTrigger[]>();
    for (const trigger of this.triggers()) {
      groups.set(trigger.category, [...(groups.get(trigger.category) ?? []), trigger]);
    }
    return [...groups].map(([category, triggers]) => ({ category, triggers }));
  });
  protected readonly trigger = computed(() => this.triggers().find((trigger) => trigger.code === this.triggerCode()));
  protected readonly connection = computed(() => this.connections().find((connection) => connection.id === this.connectionId()));
  protected readonly operation = computed(() => this.connection()?.operations.find((operation) => operation.key === this.operationKey()));
  private readonly samples = computed(() => sampleFields(this.trigger()));
  protected readonly canSave = computed(() => {
    const operation = this.operation();
    return !!this.name().trim() && !!this.trigger() && !!operation && operation.parameters.every((parameter) => !parameter.required || !!this.parameters()[parameter.key]?.trim());
  });

  constructor() {
    void this.load();
    this.api.automationTriggers().then((triggers) => this.triggers.set(triggers), () => undefined);
  }

  protected async open(automation: Automation | null): Promise<void> {
    this.connections.set(await this.api.automationConnections().catch(() => []));
    this.editingId.set(automation?.id ?? null);
    this.name.set(automation?.name ?? '');
    this.triggerCode.set(automation?.triggerCode ?? '');
    this.conditions.set(automation?.conditions ?? []);
    this.connectionId.set(automation?.connectionId ?? '');
    this.operationKey.set(automation?.operation ?? '');
    this.parameters.set(automation?.parameters ?? {});
    this.isActive.set(automation?.isActive ?? true);
    this.formError.set(null);
    this.tested.set(null);
    this.dialogOpen.set(true);
  }

  protected chooseTrigger(code: string): void {
    this.triggerCode.set(code);

    // Each trigger has its own fields, so conditions on the last one's no longer mean anything.
    this.conditions.set([]);
  }

  protected chooseConnection(id: string): void {
    this.connectionId.set(id);
    this.chooseOperation(this.connection()?.operations[0]?.key ?? '');
  }

  protected chooseOperation(key: string): void {
    this.operationKey.set(key);
    this.tested.set(null);
  }

  protected addCondition(): void {
    const field = this.trigger()?.fields[0]?.key;
    if (field) {
      this.conditions.update((rows) => [...rows, { field, operator: 'Equals' as AutomationConditionOperator, value: '' }]);
    }
  }

  protected setCondition(index: number, change: Partial<AutomationCondition>): void {
    this.conditions.update((rows) => rows.map((row, at) => (at === index ? { ...row, ...change } : row)));
  }

  protected removeCondition(index: number): void {
    this.conditions.update((rows) => rows.filter((_, at) => at !== index));
  }

  protected setParameter(key: string, value: string): void {
    this.parameters.update((values) => ({ ...values, [key]: value }));
  }

  protected insert(key: string, box: HTMLTextAreaElement, menu: HTMLSelectElement): void {
    if (!menu.value) {
      return;
    }
    const result = insertPlaceholder(this.parameters()[key] ?? '', menu.value, box.selectionStart);
    this.setParameter(key, result.text);
    menu.value = '';
    box.value = result.text;
    box.focus();
    box.setSelectionRange(result.caret, result.caret);
  }

  protected preview(key: string): string {
    return renderTemplate(this.parameters()[key] ?? '', this.samples());
  }

  protected unknown(key: string): readonly string[] {
    return unknownPlaceholders(this.parameters()[key] ?? '', this.trigger());
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await this.api.saveAutomation(this.editingId(), this.input());
      this.dialogOpen.set(false);
      await this.load();
    } catch (error) {
      this.formError.set(error instanceof ApiError ? error.message : 'The automation could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async test(): Promise<void> {
    this.testing.set(true);
    this.tested.set(null);
    try {
      this.tested.set(await this.api.testAutomation(this.input()));
    } catch (error) {
      this.tested.set({ succeeded: false, message: error instanceof ApiError ? error.message : 'The test could not be sent.', parameters: {} });
    } finally {
      this.testing.set(false);
    }
  }

  protected async toggle(automation: Automation): Promise<void> {
    this.error.set(null);
    try {
      await this.api.saveAutomation(automation.id, {
        name: automation.name,
        triggerCode: automation.triggerCode,
        conditions: automation.conditions,
        connectionId: automation.connectionId,
        operation: automation.operation,
        parameters: automation.parameters,
        isActive: !automation.isActive,
      });
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The automation could not be changed.');
    }
  }

  protected async remove(automation: Automation): Promise<void> {
    if (!confirm(`Delete "${automation.name}"? Anything it is still retrying stops.`)) {
      return;
    }
    this.error.set(null);
    try {
      await this.api.deleteAutomation(automation.id);
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The automation could not be deleted.');
    }
  }

  private input(): AutomationInput {
    const allowed = new Set(this.operation()?.parameters.map((parameter) => parameter.key));
    return {
      name: this.name().trim(),
      triggerCode: this.triggerCode(),
      conditions: this.conditions(),
      connectionId: this.connectionId(),
      operation: this.operationKey(),
      parameters: Object.fromEntries(Object.entries(this.parameters()).filter(([key]) => allowed.has(key))),
      isActive: this.isActive(),
    };
  }

  private async load(): Promise<void> {
    try {
      this.automations.set(await this.api.automations());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Automations could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
