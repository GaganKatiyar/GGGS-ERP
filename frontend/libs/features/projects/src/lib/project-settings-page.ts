import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { ProjectHealthThresholds, ProjectRole } from '@gggs/shared/models';
import { FormDialog, PageHeader, StatusBadge } from '@gggs/shared/ui';

type Thresholds = { -readonly [K in keyof ProjectHealthThresholds]: ProjectHealthThresholds[K] };

interface ThresholdField {
  readonly key: keyof ProjectHealthThresholds;
  readonly label: string;
  readonly step?: number;
}

/** Project roles (spec §42) and the health thresholds every project is judged by (spec §78). */
@Component({
  selector: 'gggs-project-settings-page',
  imports: [RouterLink, PageHeader, StatusBadge, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Project settings" subtitle="Roles people play on projects, and how project health is judged.">
      <a class="btn" routerLink="/projects">Projects</a>
    </gggs-page-header>

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
      <p class="field__error" role="alert">{{ error() }}</p>
    }

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
      grid-template-columns: repeat(auto-fit, minmax(26rem, 1fr));
      gap: var(--gggs-space-3);
      align-items: start;
    }

    .settings__heading {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .settings__hint {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .settings__role {
      display: grid;
      grid-template-columns: 1fr auto auto;
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
      const [roles, thresholds] = await Promise.all([this.api.roles(true), this.api.thresholds()]);
      this.roles.set(roles);
      this.thresholds.set({ ...thresholds });
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Settings could not be loaded.');
    }
  }
}
