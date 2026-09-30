import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button } from '@gggs/shared/ui';
import type { JobApplicationListItem } from '@gggs/shared/models';
import { RecruitmentApi } from '../data/recruitment-api';

/**
 * Applications against one posting - embedded under its row in `JobPostingsPanel` rather than
 * their own route, matching how postings themselves are embedded under the requisition. Includes
 * a manual "record an application" form for the internal recruiter-entry path this v1 slice
 * covers (see `Candidate`'s own remarks on what a public apply flow would still need).
 */
@Component({
  selector: 'gggs-job-applications-panel',
  imports: [ReactiveFormsModule, HasPermissionDirective, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (applications(); as items) {
      @if (items.length > 0) {
        <table class="applications-table">
          <thead>
            <tr>
              <th scope="col">Candidate</th>
              <th scope="col">Stage</th>
              <th scope="col"></th>
            </tr>
          </thead>
          <tbody>
            @for (application of items; track application.id) {
              <tr>
                <td>{{ application.candidateDisplayName }}</td>
                <td>{{ application.stage }}</td>
                <td class="application-actions" *gggsHasPermission="'recruitment.application.manage'">
                  @if (application.stage !== 'Hired' && application.stage !== 'Rejected') {
                    <gggs-button size="sm" (click)="advance(application.id)" [disabled]="isActing()">Advance</gggs-button>
                    <gggs-button size="sm" (click)="reject(application.id)" [disabled]="isActing()">Reject</gggs-button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      } @else {
        <p class="muted">No applications yet.</p>
      }
    } @else {
      <p class="muted">Loading…</p>
    }

    @if (postingIsPublished()) {
      <form
        *gggsHasPermission="'recruitment.application.manage'"
        [formGroup]="form"
        (ngSubmit)="submit()"
        novalidate
        class="apply-form"
      >
        <label class="field">
          <span class="field__label">First name</span>
          <input type="text" formControlName="firstName" maxlength="100" required />
        </label>

        <label class="field">
          <span class="field__label">Last name (optional)</span>
          <input type="text" formControlName="lastName" maxlength="100" />
        </label>

        <label class="field">
          <span class="field__label">Email</span>
          <input type="email" formControlName="email" maxlength="256" required />
        </label>

        <label class="field">
          <span class="field__label">Phone (optional)</span>
          <input type="text" formControlName="phone" maxlength="20" />
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Recording…' : 'Record application' }}
        </gggs-button>
      </form>
    } @else {
      <p class="muted" *gggsHasPermission="'recruitment.application.manage'">
        The posting must be Published before an application can be recorded against it.
      </p>
    }
  `,
  styles: `
    .applications-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .applications-table th,
    .applications-table td {
      text-align: left;
      padding: 0.375rem 0.625rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.8125rem;
    }

    .application-actions {
      display: flex;
      gap: 0.5rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 0.75rem;
      font-size: 0.8125rem;
    }

    .apply-form {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      align-items: end;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.8125rem;
    }

    .field__label {
      font-weight: 600;
    }

    .field__error {
      grid-column: 1 / -1;
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.8125rem;
    }
  `,
})
export class JobApplicationsPanel {
  private readonly api = inject(RecruitmentApi);
  private readonly formBuilder = inject(FormBuilder);

  readonly postingId = input.required<string>();
  readonly postingIsPublished = input.required<boolean>();

  protected readonly applications = signal<readonly JobApplicationListItem[] | null>(null);
  protected readonly isActing = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: [''],
    email: ['', [Validators.required, Validators.email]],
    phone: [''],
  });

  constructor() {
    void this.load();
  }

  protected async advance(applicationId: string): Promise<void> {
    await this.act(() => this.api.advanceApplication(applicationId));
  }

  protected async reject(applicationId: string): Promise<void> {
    await this.act(() => this.api.rejectApplication(applicationId));
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
      await this.api.applyToPosting(this.postingId(), {
        firstName: value.firstName ?? '',
        lastName: value.lastName || undefined,
        email: value.email ?? '',
        phone: value.phone || undefined,
      });

      this.form.reset();
      await this.load();
    } catch {
      this.errorMessage.set('The application could not be recorded. Check the details and try again.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async act(run: () => Promise<unknown>): Promise<void> {
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
    this.applications.set(await this.api.listApplications(this.postingId()));
  }
}
