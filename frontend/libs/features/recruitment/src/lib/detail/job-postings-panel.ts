import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button } from '@gggs/shared/ui';
import type { JobPosting, JobPostingListItem } from '@gggs/shared/models';
import { RecruitmentApi } from '../data/recruitment-api';
import { JobApplicationsPanel } from './job-applications-panel';

/**
 * Postings against one requisition - embedded in `JobRequisitionDetailPage` rather than its own
 * route, since a posting is always viewed in the context of the requisition it belongs to (there
 * is no standalone "all postings" screen, matching the API's own nested route shape).
 */
@Component({
  selector: 'gggs-job-postings-panel',
  imports: [ReactiveFormsModule, HasPermissionDirective, Button, JobApplicationsPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (postings(); as items) {
      @if (items.length > 0) {
        <table class="postings-table">
          <thead>
            <tr>
              <th scope="col">Title</th>
              <th scope="col">Slug</th>
              <th scope="col">Public</th>
              <th scope="col">Status</th>
              <th scope="col"></th>
            </tr>
          </thead>
          <tbody>
            @for (posting of items; track posting.id) {
              <tr>
                <td>{{ posting.title }}</td>
                <td>{{ posting.slug }}</td>
                <td>{{ posting.isPublic ? 'Yes' : 'No' }}</td>
                <td>{{ posting.status }}</td>
                <td class="posting-actions">
                  <gggs-button size="sm" (click)="toggleApplications(posting.id)">
                    {{ expandedPostingId() === posting.id ? 'Hide applications' : 'Applications' }}
                  </gggs-button>
                  <ng-container *gggsHasPermission="'recruitment.posting.manage'">
                    @if (posting.status === 'Draft') {
                      <gggs-button size="sm" (click)="publish(posting.id)" [disabled]="isActing()">Publish</gggs-button>
                    }
                    @if (posting.status === 'Published') {
                      <gggs-button size="sm" (click)="close(posting.id)" [disabled]="isActing()">Close</gggs-button>
                    }
                  </ng-container>
                </td>
              </tr>
              @if (expandedPostingId() === posting.id) {
                <tr>
                  <td colspan="5" class="applications-cell">
                    <gggs-job-applications-panel
                      [postingId]="posting.id"
                      [postingIsPublished]="posting.status === 'Published'"
                    />
                  </td>
                </tr>
              }
            }
          </tbody>
        </table>
      } @else {
        <p class="muted">No postings yet.</p>
      }
    } @else {
      <p class="muted">Loading…</p>
    }

    @if (requisitionIsOpen()) {
      <form
        *gggsHasPermission="'recruitment.posting.manage'"
        [formGroup]="form"
        (ngSubmit)="submit()"
        novalidate
        class="create-form"
      >
        <label class="field">
          <span class="field__label">Title</span>
          <input type="text" formControlName="title" maxlength="200" required />
        </label>

        <label class="field">
          <span class="field__label">Slug</span>
          <input type="text" formControlName="slug" maxlength="200" placeholder="senior-engineer-bangalore" required />
        </label>

        <label class="field field--full">
          <span class="field__label">Description</span>
          <textarea formControlName="descriptionHtml" rows="3" required></textarea>
        </label>

        <label class="field">
          <span class="field__label">Location (optional)</span>
          <input type="text" formControlName="location" maxlength="200" />
        </label>

        <label class="checkbox-field">
          <input type="checkbox" formControlName="isInternal" />
          <span>Internal</span>
        </label>

        <label class="checkbox-field">
          <input type="checkbox" formControlName="isPublic" />
          <span>Public</span>
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Creating…' : 'Create posting' }}
        </gggs-button>
      </form>
    } @else {
      <p class="muted" *gggsHasPermission="'recruitment.posting.manage'">
        The requisition must be Open before a posting can be created against it.
      </p>
    }
  `,
  styles: `
    .postings-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .postings-table th,
    .postings-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .posting-actions {
      display: flex;
      gap: 0.5rem;
    }

    .applications-cell {
      background: var(--gggs-color-surface-sunken, #f5f6f8);
      padding: 0.75rem 1rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 1rem;
    }

    .create-form {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      align-items: end;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
    }

    .field--full {
      grid-column: 1 / -1;
    }

    .field__label {
      font-weight: 600;
    }

    .field__error {
      grid-column: 1 / -1;
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }

    .checkbox-field {
      display: flex;
      align-items: center;
      gap: 0.375rem;
      font-size: 0.875rem;
    }
  `,
})
export class JobPostingsPanel {
  private readonly api = inject(RecruitmentApi);
  private readonly formBuilder = inject(FormBuilder);

  readonly requisitionId = input.required<string>();
  readonly requisitionIsOpen = input.required<boolean>();

  protected readonly postings = signal<readonly JobPostingListItem[] | null>(null);
  protected readonly isActing = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly expandedPostingId = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    slug: ['', Validators.required],
    descriptionHtml: ['', Validators.required],
    location: [''],
    isInternal: [true],
    isPublic: [false],
  });

  constructor() {
    void this.load();
  }

  protected toggleApplications(postingId: string): void {
    this.expandedPostingId.set(this.expandedPostingId() === postingId ? null : postingId);
  }

  protected async publish(postingId: string): Promise<void> {
    await this.act(() => this.api.publishPosting(this.requisitionId(), postingId));
  }

  protected async close(postingId: string): Promise<void> {
    await this.act(() => this.api.closePosting(this.requisitionId(), postingId));
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      await this.api.createPosting(this.requisitionId(), {
        title: value.title ?? '',
        descriptionHtml: value.descriptionHtml ?? '',
        location: value.location || undefined,
        isInternal: value.isInternal ?? false,
        isPublic: value.isPublic ?? false,
        slug: value.slug ?? '',
      });

      this.form.reset({ isInternal: true, isPublic: false });
      await this.load();
    } catch {
      this.errorMessage.set('The posting could not be created. Check the slug is unique and try again.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async act(run: () => Promise<JobPosting>): Promise<void> {
    if (this.isActing()) {
      return;
    }

    this.isActing.set(true);

    try {
      await run();
      await this.load();
    } catch {
      this.errorMessage.set('That action could not be completed. Please try again.');
    } finally {
      this.isActing.set(false);
    }
  }

  private async load(): Promise<void> {
    this.postings.set(await this.api.listPostings(this.requisitionId()));
  }
}
