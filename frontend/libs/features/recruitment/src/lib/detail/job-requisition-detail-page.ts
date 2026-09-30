import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { JobRequisition, JobRequisitionStatus } from '@gggs/shared/models';
import { RecruitmentApi } from '../data/recruitment-api';
import { JobPostingsPanel } from './job-postings-panel';

/** A single requisition: its details plus whichever status action currently applies. */
@Component({
  selector: 'gggs-job-requisition-detail-page',
  imports: [RouterLink, HasPermissionDirective, PageHeader, Card, Button, JobPostingsPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (requisition(); as r) {
      <gggs-page-header [title]="r.title" [subtitle]="r.requisitionNo">
        <a routerLink="/recruitment/requisitions" class="btn">Back</a>
      </gggs-page-header>

      <gggs-card title="Details">
        <dl class="detail-grid">
          <dt>Status</dt>
          <dd>{{ r.status }}</dd>
          <dt>Openings</dt>
          <dd>{{ r.openings }}</dd>
          <dt>Hiring manager</dt>
          <dd>{{ r.hiringManagerDisplayName ?? '—' }}</dd>
          @if (r.justification) {
            <dt>Justification</dt>
            <dd>{{ r.justification }}</dd>
          }
        </dl>

        <div class="actions" *gggsHasPermission="'recruitment.requisition.manage'">
          @if (canOpen()) {
            <gggs-button (click)="act('open')" [disabled]="isActing()">Open</gggs-button>
          }
          @if (canHold()) {
            <gggs-button (click)="act('hold')" [disabled]="isActing()">Put on hold</gggs-button>
          }
          @if (canResume()) {
            <gggs-button (click)="act('resume')" [disabled]="isActing()">Resume</gggs-button>
          }
          @if (canClose()) {
            <gggs-button (click)="act('close')" [disabled]="isActing()">Close</gggs-button>
          }
          @if (canCancel()) {
            <gggs-button (click)="act('cancel')" [disabled]="isActing()">Cancel</gggs-button>
          }
        </div>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }
      </gggs-card>

      <gggs-card title="Postings">
        <gggs-job-postings-panel [requisitionId]="r.id" [requisitionIsOpen]="r.status === 'Open'" />
      </gggs-card>
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    .detail-grid {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 0.5rem 1rem;
      margin: 0 0 1rem;
    }

    .detail-grid dt {
      font-weight: 600;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .detail-grid dd {
      margin: 0;
    }

    .actions {
      display: flex;
      gap: 0.75rem;
    }

    .field__error {
      margin: 0.75rem 0 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .btn {
      padding: 0.5rem 1rem;
      font-weight: 600;
      border-radius: 0.375rem;
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      background: var(--gggs-color-surface-raised, #ffffff);
      text-decoration: none;
      color: inherit;
      cursor: pointer;
    }
  `,
})
export class JobRequisitionDetailPage {
  private readonly api = inject(RecruitmentApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly requisition = signal<JobRequisition | null>(null);
  protected readonly isActing = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  private readonly status = computed<JobRequisitionStatus | null>(() => this.requisition()?.status ?? null);

  protected readonly canOpen = computed(() => this.status() === 'Draft');
  protected readonly canHold = computed(() => this.status() === 'Open');
  protected readonly canResume = computed(() => this.status() === 'OnHold');
  protected readonly canClose = computed(() => this.status() === 'Open' || this.status() === 'OnHold');
  protected readonly canCancel = computed(() => this.status() === 'Draft' || this.status() === 'Open' || this.status() === 'OnHold');

  constructor() {
    void this.load();
  }

  protected async act(action: 'open' | 'hold' | 'resume' | 'close' | 'cancel'): Promise<void> {
    const id = this.requisition()?.id;
    if (!id || this.isActing()) {
      return;
    }

    this.isActing.set(true);
    this.errorMessage.set(null);

    try {
      const updated = await this.runAction(id, action);
      this.requisition.set(updated);
    } catch {
      this.errorMessage.set('That action could not be completed. Please try again.');
    } finally {
      this.isActing.set(false);
    }
  }

  private runAction(id: string, action: 'open' | 'hold' | 'resume' | 'close' | 'cancel'): Promise<JobRequisition> {
    switch (action) {
      case 'open':
        return this.api.open(id);
      case 'hold':
        return this.api.putOnHold(id);
      case 'resume':
        return this.api.resume(id);
      case 'close':
        return this.api.close(id);
      case 'cancel':
        return this.api.cancel(id);
    }
  }

  private async load(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }

    this.requisition.set(await this.api.get(id));
  }
}
