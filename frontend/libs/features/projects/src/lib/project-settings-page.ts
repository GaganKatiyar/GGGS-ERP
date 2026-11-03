import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { DefaultProjectRow, ProjectHealthThresholds, ProjectRole, ProjectTemplate } from '@gggs/shared/models';
import { BILLING_MODEL_LABELS } from '@gggs/shared/models';
import { FormDialog, PageHeader, StatusBadge } from '@gggs/shared/ui';
import { CodeLinkSetupPanel } from './code-link-setup';
import { activitiesToText, columnsToText, phasesToText, textToActivities, textToColumns, textToPhases } from './template-text';

type Thresholds = { -readonly [K in keyof ProjectHealthThresholds]: ProjectHealthThresholds[K] };

interface ThresholdField {
  readonly key: keyof ProjectHealthThresholds;
  readonly label: string;
  readonly step?: number;
}

/** Default projects, project roles (spec §42) and the health thresholds every project is judged by (spec §78). */
@Component({
  selector: 'gggs-project-settings-page',
  imports: [RouterLink, PageHeader, StatusBadge, FormDialog, CodeLinkSetupPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Project settings" subtitle="Where each department books its time, the roles people play on projects, and how project health is judged.">
      <a class="btn" routerLink="/projects">Projects</a>
    </gggs-page-header>

    <section class="panel stack settings__defaults">
      <div class="toolbar">
        <h2 class="settings__heading">Default projects</h2>
        <button type="button" class="btn btn--primary btn--sm" [disabled]="busy()" (click)="ensureDefaults()">Create missing</button>
      </div>
      <p class="muted settings__hint">
        Everyone can book time to their department's default project from the day they join, without being allocated to it. A department without its own uses the nearest parent's, then the organisation's. Missing ones are also created overnight.
      </p>
      <div class="settings__scroll" tabindex="0" role="region" aria-label="Default projects">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">Department</th>
              <th scope="col">Default project</th>
              <th scope="col">Run by</th>
            </tr>
          </thead>
          <tbody>
            @for (row of defaults(); track row.departmentId ?? 'organisation') {
              <tr>
                <td>{{ row.name }}</td>
                <td>
                  @if (row.projectId) {
                    <a [routerLink]="['/projects', row.projectId]">{{ row.projectCode }} · {{ row.projectName }}</a>
                    @if (row.isInherited) {
                      <span class="muted"> (inherited)</span>
                    }
                  } @else {
                    <span class="muted">None yet</span>
                  }
                </td>
                <td>{{ row.managerName ?? '—' }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </section>

    <section class="panel stack settings__defaults">
      <h2 class="settings__heading">Project templates</h2>
      <p class="muted settings__hint">What a new project can start with, for each engagement model: its phases, its board, and the shared work anyone on it can book time to. Changes apply to new projects only.</p>
      <div class="settings__scroll" tabindex="0" role="region" aria-label="Project templates">
        <table class="table">
          <thead>
            <tr>
              <th scope="col">Template</th>
              <th scope="col">Engagement model</th>
              <th scope="col">Phases</th>
              <th scope="col">Board columns</th>
              <th scope="col" aria-label="Actions"></th>
            </tr>
          </thead>
          <tbody>
            @for (template of templates(); track template.id) {
              <tr>
                <td>{{ template.name }}</td>
                <td>{{ modelLabels[template.billingType] }}</td>
                <td class="muted">{{ template.definition.phases.join(', ') || '—' }}</td>
                <td>{{ template.definition.columns.length }}</td>
                <td><button type="button" class="btn btn--link btn--sm" (click)="editTemplate(template)">Edit</button></td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </section>

    <gggs-code-link-setup />

    <div class="settings">
      <section class="panel stack">
        <div class="toolbar">
          <h2 class="settings__heading">Project roles</h2>
          <button type="button" class="btn btn--primary btn--sm" (click)="editRole(null)">Add role</button>
        </div>
        @for (role of roles(); track role.id) {
          <div class="settings__role">
            <div>
              <strong>{{ role.name }}</strong> <span class="muted">{{ role.code }}</span>
              <div class="muted settings__skills">{{ role.skills.join(', ') }}</div>
            </div>
            <gggs-status-badge [status]="role.isActive ? 'Active' : 'Inactive'" />
            <span class="settings__actions">
              <button type="button" class="btn btn--link btn--sm" (click)="editRole(role)">Edit</button>
              <button type="button" class="btn btn--link btn--sm" (click)="toggleRole(role)">{{ role.isActive ? 'Retire' : 'Restore' }}</button>
            </span>
          </div>
        }
      </section>

      <section class="panel stack">
        <h2 class="settings__heading">Health thresholds</h2>
        <p class="muted settings__hint">
          Each dimension turns amber, then red, at these values. Overall is a weighted average (green 0, amber 1, red 3); two red dimensions always read red.
        </p>
        @if (thresholds(); as t) {
          <div class="form-grid">
            @for (field of fields; track field.key) {
              <label class="field">
                <span class="field__label">{{ field.label }}</span>
                <input type="number" min="0" [step]="field.step ?? 1" [value]="t[field.key]" (input)="setThreshold(field.key, +$any($event.target).value)" />
              </label>
            }
          </div>
          <div class="toolbar">
            <button type="button" class="btn btn--primary btn--sm" [disabled]="busy()" (click)="saveThresholds()">Save thresholds</button>
          </div>
        }
        @if (message()) {
          <p class="settings__message" role="status">{{ message() }}</p>
        }
      </section>
    </div>
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="templateOpen" [title]="'Edit ' + (editingTemplate()?.name ?? 'template')" size="lg" [busy]="busy()" [error]="dialogError()" (save)="saveTemplate()">
      <div class="stack">
        <label class="field">
          <span class="field__label">Name *</span>
          <input type="text" maxlength="100" [value]="templateName()" (input)="templateName.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Phases <span class="muted">(one to a line)</span></span>
          <textarea rows="5" [value]="templatePhases()" (input)="templatePhases.set($any($event.target).value)"></textarea>
        </label>
        <label class="field">
          <span class="field__label">Board columns <span class="muted">(one to a line: Name | Backlog, Todo, InProgress or Done)</span></span>
          <textarea rows="6" [value]="templateColumns()" (input)="templateColumns.set($any($event.target).value)"></textarea>
        </label>
        <label class="field">
          <span class="field__label">Shared work <span class="muted">(one activity to a line: Activity: task, task)</span></span>
          <textarea rows="4" [value]="templateActivities()" (input)="templateActivities.set($any($event.target).value)"></textarea>
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="roleOpen" [title]="editing() ? 'Edit ' + editing()?.name : 'Add a project role'" [busy]="busy()" [error]="dialogError()" (save)="saveRole()">
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Code *</span>
          <input type="text" maxlength="32" [disabled]="!!editing()" [value]="code()" (input)="code.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Name *</span>
          <input type="text" maxlength="100" [value]="name()" (input)="name.set($any($event.target).value)" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Skills it calls for <span class="muted">(comma-separated)</span></span>
          <input type="text" [value]="skills()" (input)="skills.set($any($event.target).value)" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Description</span>
          <input type="text" maxlength="500" [value]="description()" (input)="description.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .settings {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(26rem, 100%), 1fr));
      gap: var(--gggs-space-3);
      align-items: start;
    }

    .settings__defaults {
      margin-block-end: var(--gggs-space-3);
    }

    .settings__scroll {
      overflow-x: auto;
    }

    .settings__heading {
      margin-inline-end: auto;
      margin-block: 0;
      font-size: var(--gggs-font-size-md);
    }

    .settings__hint {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .settings__role {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto auto;
      gap: var(--gggs-space-3);
      align-items: center;
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .settings__skills {
      font-size: var(--gggs-font-size-xs);
    }

    .settings__actions {
      white-space: nowrap;
    }

    .settings__message {
      color: var(--gggs-color-success);
    }
  `,
})
export class ProjectSettingsPage {
  private readonly api = inject(ProjectsApi);

  protected readonly fields: readonly ThresholdField[] = [
    { key: 'scheduleAmberGap', label: 'Schedule amber: progress behind time by (points)' },
    { key: 'scheduleRedGap', label: 'Schedule red: behind by (points)' },
    { key: 'milestoneRedOverdueDays', label: 'Schedule red: a milestone late by (days)' },
    { key: 'resourceEndingSoonDays', label: 'Resources amber: someone leaves within (days)' },
    { key: 'scopeAmberPendingChanges', label: 'Scope amber: changes awaiting decision' },
    { key: 'scopeRedPendingChanges', label: 'Scope red: changes awaiting decision' },
    { key: 'scopeRedScheduleImpactDays', label: 'Scope red: days added by approved changes' },
    { key: 'qualityAmberOpenBugs', label: 'Quality amber: open bugs' },
    { key: 'qualityRedOpenBugs', label: 'Quality red: open bugs' },
    { key: 'riskAmberScore', label: 'Risk amber: highest open risk score (of 25)' },
    { key: 'riskRedScore', label: 'Risk red: highest open risk score' },
    { key: 'issueRedOverdueCount', label: 'Issues red: overdue issues' },
    { key: 'overallAmberScore', label: 'Overall amber at weighted score', step: 0.1 },
    { key: 'overallRedScore', label: 'Overall red at weighted score', step: 0.1 },
  ];

  protected readonly modelLabels = BILLING_MODEL_LABELS;
  protected readonly templates = signal<readonly ProjectTemplate[]>([]);
  protected readonly templateOpen = signal(false);
  protected readonly editingTemplate = signal<ProjectTemplate | null>(null);
  protected readonly templateName = signal('');
  protected readonly templatePhases = signal('');
  protected readonly templateColumns = signal('');
  protected readonly templateActivities = signal('');
  protected readonly defaults = signal<readonly DefaultProjectRow[]>([]);
  protected readonly roles = signal<readonly ProjectRole[]>([]);
  protected readonly thresholds = signal<Thresholds | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly roleOpen = signal(false);
  protected readonly editing = signal<ProjectRole | null>(null);
  protected readonly code = signal('');
  protected readonly name = signal('');
  protected readonly skills = signal('');
  protected readonly description = signal('');
  protected readonly dialogError = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected editTemplate(template: ProjectTemplate): void {
    this.editingTemplate.set(template);
    this.templateName.set(template.name);
    this.templatePhases.set(phasesToText(template.definition.phases));
    this.templateColumns.set(columnsToText(template.definition.columns));
    this.templateActivities.set(activitiesToText(template.definition.activities));
    this.dialogError.set(null);
    this.templateOpen.set(true);
  }

  protected async saveTemplate(): Promise<void> {
    const template = this.editingTemplate();
    if (!template) {
      return;
    }

    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await this.api.saveTemplate(template.id, {
        name: this.templateName().trim(),
        phases: textToPhases(this.templatePhases()),
        columns: textToColumns(this.templateColumns()),
        activities: textToActivities(this.templateActivities()),
      });
      this.templateOpen.set(false);
      this.templates.set(await this.api.templates());
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The template could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async ensureDefaults(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      this.defaults.set(await this.api.ensureDefaultProjects());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The default projects could not be created.');
    } finally {
      this.busy.set(false);
    }
  }

  protected setThreshold(key: keyof ProjectHealthThresholds, value: number): void {
    this.thresholds.update((current) => (current ? { ...current, [key]: value } : current));
  }

  protected async saveThresholds(): Promise<void> {
    const thresholds = this.thresholds();
    if (!thresholds) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      this.thresholds.set({ ...(await this.api.saveThresholds(thresholds)) });
      this.message.set('Thresholds saved. Health is judged against them from now on.');
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The thresholds could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  protected editRole(role: ProjectRole | null): void {
    this.editing.set(role);
    this.code.set(role?.code ?? '');
    this.name.set(role?.name ?? '');
    this.skills.set(role?.skills.join(', ') ?? '');
    this.description.set(role?.description ?? '');
    this.dialogError.set(null);
    this.roleOpen.set(true);
  }

  protected async saveRole(): Promise<void> {
    if (!this.code().trim() || !this.name().trim()) {
      this.dialogError.set('Give the role a code and a name.');
      return;
    }

    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await this.api.saveRole(this.editing()?.id ?? null, {
        code: this.code().trim(),
        name: this.name().trim(),
        description: this.description() || null,
        skills: this.skills().split(',').map((skill) => skill.trim()).filter((skill) => skill.length > 0),
        sortOrder: this.editing()?.sortOrder ?? null,
      });
      this.roleOpen.set(false);
      await this.load();
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'The role could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async toggleRole(role: ProjectRole): Promise<void> {
    try {
      await this.api.setRoleActive(role.id, !role.isActive);
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The role could not be changed.');
    }
  }

  private async load(): Promise<void> {
    try {
      const [defaults, roles, thresholds, templates] = await Promise.all([this.api.defaultProjects(), this.api.roles(true), this.api.thresholds(), this.api.templates()]);
      this.defaults.set(defaults);
      this.templates.set(templates);
      this.roles.set(roles);
      this.thresholds.set({ ...thresholds });
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Settings could not be loaded.');
    }
  }
}
