import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective, PermissionStore } from '@gggs/core/permissions';
import type { AcknowledgementReport, PolicyDetail, PolicyVersion } from '@gggs/shared/models';
import { POLICY_CATEGORY_LABELS } from '@gggs/shared/models';
import { EmptyState, FormDialog, KpiCard, PageHeader, Skeleton, StatusBadge, Tabs, type TabItem } from '@gggs/shared/ui';
import { PoliciesApi } from './policies-api';
import { PolicyFields, policyForm, toPolicyInput } from './policy-form';

const today = () => new Date().toISOString().slice(0, 10);

/**
 * One policy: the text in effect, the draft being prepared, every earlier version, and - for
 * those allowed - who has acknowledged it. Publishing goes through approval when the organization
 * has set one up for policies.
 */
@Component({
  selector: 'gggs-policy-detail-page',
  imports: [DatePipe, RouterLink, HasPermissionDirective, PageHeader, Tabs, Skeleton, EmptyState, StatusBadge, KpiCard, FormDialog, PolicyFields],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="This policy could not be shown" [description]="error()!" />
    } @else if (!detail()) {
      <gggs-skeleton [lines]="8" />
    } @else {
      @let p = detail()!.policy;
      <gggs-page-header [title]="p.title" [subtitle]="categories[p.category] + ' · ' + p.audience + (p.requiresAcknowledgement ? ' · acknowledgement required' : '')">
        <div class="toolbar">
          <ng-container *gggsHasPermission="'policy.policy.manage'">
            <button type="button" class="btn" (click)="openEdit()">Edit details</button>
            @if (!open()) {
              <button type="button" class="btn btn--primary" (click)="openDraft()">New version</button>
            }
            <button type="button" class="btn btn--link" (click)="setRetired(p.isActive)">{{ p.isActive ? 'Retire' : 'Reinstate' }}</button>
          </ng-container>
          <a class="btn" routerLink="/policies">All policies</a>
        </div>
      </gggs-page-header>

      <gggs-tabs [tabs]="tabs()" [(selected)]="tab" />

      <div class="policy__body">
        @switch (tab()) {
          @case ('text') {
            <div class="stack">
              @if (open(); as draft) {
                <section class="panel stack policy__draft">
                  <div class="toolbar">
                    <h2 class="policy__heading">Version {{ draft.number }}</h2>
                    <gggs-status-badge [status]="draft.status" />
                    <span class="muted">takes effect {{ draft.effectiveFrom | date: 'mediumDate' }}</span>
                  </div>
                  @if (draft.status === 'PendingApproval') {
                    <p class="muted">
                      Waiting for approval.
                      @if (draft.workflowInstanceId) {
                        <a [routerLink]="['/approvals/requests', draft.workflowInstanceId]">See where it stands</a>
                      }
                    </p>
                  }
                  @if (draft.changeSummary) {
                    <p><strong>What changed:</strong> {{ draft.changeSummary }}</p>
                  }
                  <pre class="policy__text">{{ draft.content }}</pre>
                  @if (draft.status === 'Draft') {
                    <div class="toolbar" *gggsHasPermission="'policy.policy.manage'">
                      <button type="button" class="btn" (click)="openEditDraft(draft)">Edit draft</button>
                      <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="publish(draft)">Publish</button>
                    </div>
                  }
                </section>
              }

              @if (published(); as current) {
                <section class="panel stack">
                  <div class="toolbar">
                    <h2 class="policy__heading">In effect: version {{ current.number }}</h2>
                    <span class="muted">since {{ current.effectiveFrom | date: 'mediumDate' }}</span>
                  </div>
                  <pre class="policy__text">{{ current.content }}</pre>
                </section>
              } @else if (!open()) {
                <gggs-empty-state title="No version yet" description="Write the first version to publish this policy." />
              }
              @if (actionError()) {
                <p class="field__error" role="alert">{{ actionError() }}</p>
              }
              @if (notice()) {
                <p class="policy__notice" role="status">{{ notice() }}</p>
              }
            </div>
          }
          @case ('history') {
            <section class="panel">
              <table class="policy__table">
                <thead>
                  <tr>
                    <th scope="col">Version</th>
                    <th scope="col">Status</th>
                    <th scope="col">Takes effect</th>
                    <th scope="col">Published</th>
                    <th scope="col">What changed</th>
                  </tr>
                </thead>
                <tbody>
                  @for (version of detail()!.versions; track version.id) {
                    <tr>
                      <td>v{{ version.number }}</td>
                      <td><gggs-status-badge [status]="version.status" /></td>
                      <td>{{ version.effectiveFrom | date: 'mediumDate' }}</td>
                      <td>{{ (version.publishedDate | date: 'medium') ?? '—' }}</td>
                      <td>{{ version.changeSummary ?? '—' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </section>
          }
          @case ('acknowledgements') {
            @if (report(); as r) {
              <div class="stack">
                <div class="kpi-row">
                  <gggs-kpi-card label="Version" icon="pi pi-file" [value]="'v' + r.versionNumber" />
                  <gggs-kpi-card label="In the audience" icon="pi pi-users" [value]="r.audienceCount" />
                  <gggs-kpi-card label="Acknowledged" icon="pi pi-check" [value]="r.acknowledgedCount" />
                  <gggs-kpi-card label="Coverage" icon="pi pi-percentage" [value]="coverage()" />
                </div>
                <section class="panel">
                  <table class="policy__table">
                    <thead>
                      <tr>
                        <th scope="col">Employee</th>
                        <th scope="col">Code</th>
                        <th scope="col">Acknowledged</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (row of r.rows; track row.employeeId) {
                        <tr>
                          <td><a [routerLink]="['/employees', row.employeeId]">{{ row.employeeName ?? 'Employee' }}</a></td>
                          <td>{{ row.employeeCode ?? '—' }}</td>
                          <td>
                            @if (row.acknowledgedDate) {
                              {{ row.acknowledgedDate | date: 'medium' }}
                            } @else {
                              <gggs-status-badge status="Pending" />
                            }
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </section>
              </div>
            } @else if (reportError()) {
              <gggs-empty-state icon="pi pi-info-circle" title="No acknowledgements yet" [description]="reportError()!" />
            } @else {
              <gggs-skeleton [lines]="5" />
            }
          }
        }
      </div>
    }

    <gggs-form-dialog [(visible)]="editOpen" title="Edit policy details" width="min(52rem, 96vw)" [busy]="busy()" [error]="formError()" (save)="saveDetails()">
      <gggs-policy-fields [form]="form" />
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="draftOpen" [title]="editingDraft() ? 'Edit version ' + editingDraft()!.number : 'New version'" width="min(52rem, 96vw)" [busy]="busy()" [error]="formError()" (save)="saveDraft()">
      <div class="stack">
        <label class="field">
          <span class="field__label">Text (Markdown) *</span>
          <textarea rows="14" [value]="draftContent()" (input)="draftContent.set($any($event.target).value)"></textarea>
        </label>
        <div class="form-grid">
          <label class="field">
            <span class="field__label">What changed</span>
            <input type="text" maxlength="500" [value]="draftSummary()" (input)="draftSummary.set($any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">Takes effect *</span>
            <input type="date" [value]="draftEffective()" (input)="draftEffective.set($any($event.target).value)" />
          </label>
        </div>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .policy__body {
      margin-block-start: var(--gggs-space-4);
    }

    .policy__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .policy__draft {
      border-color: var(--gggs-color-primary);
    }

    .policy__text {
      margin: 0;
      white-space: pre-wrap;
      font-family: inherit;
      line-height: 1.6;
    }

    .policy__notice {
      color: var(--gggs-color-success);
    }

    .policy__table {
      width: 100%;
      border-collapse: collapse;
    }

    .policy__table th,
    .policy__table td {
      padding: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
    }
  `,
})
export class PolicyDetailPage {
  private readonly api = inject(PoliciesApi);
  private readonly canSeeAcknowledgements = inject(PermissionStore).has('policy.acknowledgement.view');

  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  protected readonly categories = POLICY_CATEGORY_LABELS;
  protected readonly detail = signal<PolicyDetail | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly tab = signal('text');
  protected readonly report = signal<AcknowledgementReport | null>(null);
  protected readonly reportError = signal<string | null>(null);

  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  protected readonly editOpen = signal(false);
  protected readonly form = policyForm();
  protected readonly draftOpen = signal(false);
  protected readonly editingDraft = signal<PolicyVersion | null>(null);
  protected readonly draftContent = signal('');
  protected readonly draftSummary = signal('');
  protected readonly draftEffective = signal(today());

  protected readonly open = computed(() => this.detail()?.versions.find((version) => version.status === 'Draft' || version.status === 'PendingApproval') ?? null);
  protected readonly published = computed(() => this.detail()?.versions.find((version) => version.status === 'Published') ?? null);

  protected readonly tabs = computed<readonly TabItem[]>(() => [
    { key: 'text', label: 'Text' },
    { key: 'history', label: 'Versions', note: String(this.detail()?.versions.length ?? 0) },
    ...(this.canSeeAcknowledgements && this.detail()?.policy.requiresAcknowledgement ? [{ key: 'acknowledgements', label: 'Acknowledgements' }] : []),
  ]);

  protected readonly coverage = computed(() => {
    const report = this.report();
    return report && report.audienceCount > 0 ? `${Math.round((report.acknowledgedCount / report.audienceCount) * 100)}%` : '—';
  });

  constructor() {
    effect(() => void this.load(this.id()));
    effect(() => {
      if (this.tab() === 'acknowledgements') {
        void this.loadReport();
      }
    });
  }

  protected openEdit(): void {
    const policy = this.detail()?.policy;
    if (!policy) {
      return;
    }
    this.form.reset({
      code: policy.code,
      title: policy.title,
      summary: policy.summary ?? null,
      category: policy.category,
      requiresAcknowledgement: policy.requiresAcknowledgement,
      departmentIds: policy.audienceDepartmentIds,
      locationIds: policy.audienceLocationIds,
    });
    this.form.controls.code.disable();
    this.formError.set(null);
    this.editOpen.set(true);
  }

  protected openDraft(): void {
    this.editingDraft.set(null);
    this.draftContent.set(this.published()?.content ?? '');
    this.draftSummary.set('');
    this.draftEffective.set(today());
    this.formError.set(null);
    this.draftOpen.set(true);
  }

  protected openEditDraft(draft: PolicyVersion): void {
    this.editingDraft.set(draft);
    this.draftContent.set(draft.content);
    this.draftSummary.set(draft.changeSummary ?? '');
    this.draftEffective.set(draft.effectiveFrom);
    this.formError.set(null);
    this.draftOpen.set(true);
  }

  protected async saveDetails(): Promise<void> {
    if (this.form.invalid) {
      this.formError.set('Fill in the title.');
      return;
    }
    await this.run(async () => {
      await this.api.update(this.id(), toPolicyInput(this.form));
      this.editOpen.set(false);
    }, this.formError);
  }

  protected async saveDraft(): Promise<void> {
    if (!this.draftContent().trim() || !this.draftEffective()) {
      this.formError.set('Write the text and choose when it takes effect.');
      return;
    }
    const body = { content: this.draftContent(), changeSummary: this.draftSummary().trim() || null, effectiveFrom: this.draftEffective() };
    const editing = this.editingDraft();
    await this.run(async () => {
      if (editing) {
        await this.api.editDraft(this.id(), editing.id, body);
      } else {
        await this.api.addDraft(this.id(), body);
      }
      this.draftOpen.set(false);
    }, this.formError);
  }

  protected async publish(draft: PolicyVersion): Promise<void> {
    this.notice.set(null);
    await this.run(async () => {
      const result = await this.api.publish(this.id(), draft.id);
      this.notice.set(
        result.status === 'Published'
          ? `Version ${draft.number} is now in effect.`
          : `Version ${draft.number} has been sent for approval; it takes effect once approved.`,
      );
    }, this.actionError);
  }

  protected async setRetired(retired: boolean): Promise<void> {
    await this.run(() => this.api.setRetired(this.id(), retired), this.actionError);
  }

  private async run(call: () => Promise<void>, errorSignal: ReturnType<typeof signal<string | null>>): Promise<void> {
    this.busy.set(true);
    errorSignal.set(null);
    try {
      await call();
      await this.load(this.id());
    } catch (error) {
      errorSignal.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    this.error.set(null);
    try {
      this.detail.set(await this.api.get(id));
      this.report.set(null);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Try again in a moment.');
    }
  }

  private async loadReport(): Promise<void> {
    this.reportError.set(null);
    try {
      this.report.set(await this.api.acknowledgements(this.id()));
    } catch (error) {
      this.reportError.set(error instanceof ApiError ? error.message : 'The report could not be loaded.');
    }
  }
}
