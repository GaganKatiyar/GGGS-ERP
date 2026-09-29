import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { Button, Card, FormField, PageHeader } from '@gggs/shared/ui';
import type { WorkflowDefinition, WorkflowDefinitionVersion } from '@gggs/shared/models';
import { WorkflowApi } from '../workflow-api';

/**
 * Workflow definition administration - `workflow.definition.manage`'s screen, `/admin/workflows`
 * in the navigation manifest. v1 has no visual step editor (architecture.md §13.3's own remarks:
 * that lands in Phase 6) - a draft version's steps are pasted in as JSON, matching the shape
 * `DevSeed`'s own seeded definitions use.
 */
@Component({
  selector: 'gggs-workflow-definitions-page',
  imports: [ReactiveFormsModule, PageHeader, Card, Button, FormField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header
      title="Workflow definitions"
      subtitle="Approval chains for each module's request-and-approve flow."
    />

    <gggs-card title="New definition" class="section">
      <form [formGroup]="definitionForm" (ngSubmit)="createDefinition()" novalidate class="inline-form">
        <gggs-form-field label="Code" required>
          <input type="text" formControlName="code" maxlength="50" required />
        </gggs-form-field>
        <gggs-form-field label="Name" required>
          <input type="text" formControlName="name" maxlength="150" required />
        </gggs-form-field>
        <gggs-form-field label="Entity type" required>
          <input type="text" formControlName="entityType" maxlength="50" required />
        </gggs-form-field>
        @if (definitionErrorMessage()) {
          <p class="field-error" role="alert">{{ definitionErrorMessage() }}</p>
        }
        <gggs-button type="submit" [loading]="isSavingDefinition()">Create</gggs-button>
      </form>
    </gggs-card>

    <gggs-card title="Definitions">
      @if (definitions().length === 0) {
        <p class="muted">No workflow definitions yet.</p>
      } @else {
        <table class="definitions-table">
          <thead>
            <tr>
              <th scope="col">Code</th>
              <th scope="col">Name</th>
              <th scope="col">Entity type</th>
              <th scope="col"></th>
            </tr>
          </thead>
          <tbody>
            @for (definition of definitions(); track definition.id) {
              <tr [class.definitions-table__row--selected]="selectedDefinition()?.id === definition.id">
                <td>{{ definition.code }}</td>
                <td>{{ definition.name }}</td>
                <td>{{ definition.entityType }}</td>
                <td>
                  <gggs-button variant="ghost" size="sm" (click)="selectDefinition(definition)">
                    Versions
                  </gggs-button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
    </gggs-card>

    @if (selectedDefinition(); as definition) {
      <gggs-card title="Versions of {{ definition.code }}">
        @if (versions().length > 0) {
          <table class="definitions-table">
            <thead>
              <tr>
                <th scope="col">Version</th>
                <th scope="col">Status</th>
                <th scope="col"></th>
              </tr>
            </thead>
            <tbody>
              @for (version of versions(); track version.id) {
                <tr>
                  <td>{{ version.version }}</td>
                  <td>{{ version.status }}</td>
                  <td>
                    @if (version.status === 'Draft') {
                      <gggs-button
                        size="sm"
                        [loading]="isPublishing() === version.id"
                        (click)="publish(version)"
                      >
                        Publish
                      </gggs-button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No versions yet.</p>
        }

        <form [formGroup]="versionForm" (ngSubmit)="createVersion()" novalidate class="version-form">
          <gggs-form-field label="Definition JSON" required>
            <textarea formControlName="definitionJson" rows="8" required></textarea>
          </gggs-form-field>
          @if (versionErrorMessage()) {
            <p class="field-error" role="alert">{{ versionErrorMessage() }}</p>
          }
          <gggs-button type="submit" [loading]="isSavingVersion()">Create draft version</gggs-button>
        </form>
      </gggs-card>
    }
  `,
  styles: `
    .section {
      margin-block-end: 1rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0;
    }

    .inline-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
      flex-wrap: wrap;
    }

    .field-error {
      flex-basis: 100%;
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .definitions-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .definitions-table th,
    .definitions-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .definitions-table__row--selected {
      background: var(--gggs-color-surface-sunken, #f5f6f8);
    }

    .version-form {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      align-items: flex-start;
    }

    .version-form textarea {
      width: 100%;
      font-family: monospace;
      font-size: 0.8125rem;
    }
  `,
})
export class WorkflowDefinitionsPage {
  private readonly api = inject(WorkflowApi);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly definitions = signal<readonly WorkflowDefinition[]>([]);
  protected readonly selectedDefinition = signal<WorkflowDefinition | null>(null);
  protected readonly versions = signal<readonly WorkflowDefinitionVersion[]>([]);
  protected readonly isSavingDefinition = signal(false);
  protected readonly definitionErrorMessage = signal<string | null>(null);
  protected readonly isSavingVersion = signal(false);
  protected readonly versionErrorMessage = signal<string | null>(null);
  protected readonly isPublishing = signal<string | null>(null);

  protected readonly definitionForm = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.maxLength(50)]],
    name: ['', [Validators.required, Validators.maxLength(150)]],
    entityType: ['', [Validators.required, Validators.maxLength(50)]],
  });

  protected readonly versionForm = this.formBuilder.nonNullable.group({
    definitionJson: [DEFAULT_DEFINITION_JSON, Validators.required],
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.definitions.set(await this.api.listDefinitions());
  }

  protected async selectDefinition(definition: WorkflowDefinition): Promise<void> {
    this.selectedDefinition.set(definition);
    this.versions.set(await this.api.listVersions(definition.id));
  }

  protected async createDefinition(): Promise<void> {
    if (this.definitionForm.invalid || this.isSavingDefinition()) {
      this.definitionForm.markAllAsTouched();
      return;
    }

    this.isSavingDefinition.set(true);
    this.definitionErrorMessage.set(null);

    try {
      const definition = await this.api.createDefinition(this.definitionForm.getRawValue());
      this.definitions.update((definitions) => [...definitions, definition]);
      this.definitionForm.reset();
    } catch (error) {
      this.definitionErrorMessage.set(
        error instanceof ApiError ? error.message : 'The definition could not be created.',
      );
    } finally {
      this.isSavingDefinition.set(false);
    }
  }

  protected async createVersion(): Promise<void> {
    const definition = this.selectedDefinition();
    if (!definition || this.versionForm.invalid || this.isSavingVersion()) {
      this.versionForm.markAllAsTouched();
      return;
    }

    this.isSavingVersion.set(true);
    this.versionErrorMessage.set(null);

    try {
      const version = await this.api.createVersion(
        definition.id,
        this.versionForm.getRawValue().definitionJson,
      );
      this.versions.update((versions) => [version, ...versions]);
      this.versionForm.reset({ definitionJson: DEFAULT_DEFINITION_JSON });
    } catch (error) {
      this.versionErrorMessage.set(
        error instanceof ApiError ? error.message : 'The version could not be created.',
      );
    } finally {
      this.isSavingVersion.set(false);
    }
  }

  protected async publish(version: WorkflowDefinitionVersion): Promise<void> {
    this.isPublishing.set(version.id);

    try {
      const published = await this.api.publishVersion(version.id);
      this.versions.update((versions) =>
        versions.map((existing) => (existing.id === published.id ? published : existing)),
      );
    } finally {
      this.isPublishing.set(null);
    }
  }
}

const DEFAULT_DEFINITION_JSON = `{
  "steps": [
    {
      "key": "manager",
      "name": "Reporting manager",
      "approvers": { "type": "ReportingManager", "level": 1 },
      "mode": "Any"
    }
  ]
}`;
