import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import type { HireDraftItem } from '@gggs/shared/models';
import { EmptyState, PageHeader, Skeleton } from '@gggs/shared/ui';
import { HIRE_STEP_LABELS } from './hire-employee-page';

const FILE_LABELS: Readonly<Record<string, string>> = { photo: 'Photo', AADHAAR: 'Aadhaar', PAN: 'PAN', PASSPORT: 'Passport', OFFER_LETTER: 'Offer letter' };

/**
 * Hire forms saved part-way. A draft is not an employee - it has no code and shows nowhere else - until
 * someone opens it, finishes it and presses Hire, which removes it from here.
 */
@Component({
  selector: 'gggs-hire-drafts-page',
  imports: [DatePipe, RouterLink, PageHeader, Skeleton, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Draft hires" subtitle="Hire forms saved part-way. Open one to finish it; it becomes an employee only when you press Hire.">
      <a class="btn" routerLink="..">Back to the directory</a>
      <a class="btn btn--primary" routerLink="../new"><i class="pi pi-user-plus" aria-hidden="true"></i> New hire</a>
    </gggs-page-header>

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (loading()) {
      <gggs-skeleton [lines]="5" />
    } @else if (!drafts().length) {
      <gggs-empty-state icon="pi pi-save" title="No drafts" description="Press Save as draft on the Hire form to keep what you have typed and finish later." />
    } @else {
      <section class="panel drafts__panel">
        <div class="drafts__scroll">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col">Stopped at</th>
                <th scope="col">Files</th>
                <th scope="col">Last saved</th>
                <th scope="col"><span class="drafts__hidden">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (draft of drafts(); track draft.id) {
                <tr>
                  <td>
                    <a [routerLink]="['../new']" [queryParams]="{ draft: draft.id }">{{ draft.displayName }}</a>
                    @if (draft.offerId) { <span class="muted drafts__small">From an accepted offer</span> }
                  </td>
                  <td>Step {{ draft.step + 1 }} · {{ stepLabel(draft.step) }}</td>
                  <td>{{ fileList(draft) }}</td>
                  <td class="drafts__nowrap">{{ draft.modifiedDate ?? draft.createdDate | date: 'd MMM, h:mm a' }}</td>
                  <td class="drafts__nowrap">
                    <a class="btn btn--sm btn--primary" [routerLink]="['../new']" [queryParams]="{ draft: draft.id }">Continue</a>
                    <button type="button" class="btn btn--sm" [disabled]="deleting() === draft.id" (click)="remove(draft)">{{ deleting() === draft.id ? 'Deleting…' : 'Delete' }}</button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .drafts__panel { padding: 0; overflow: hidden; }
    .drafts__scroll { overflow-x: auto; }
    .drafts__small { display: block; font-size: var(--gggs-font-size-sm); }
    .drafts__nowrap { white-space: nowrap; }
    .drafts__nowrap .btn + .btn { margin-inline-start: var(--gggs-space-2); }
    .drafts__hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
  `,
})
export class HireDraftsPage {
  private readonly api = inject(EmployeeDirectoryApi);

  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly drafts = signal<readonly HireDraftItem[]>([]);
  protected readonly deleting = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected stepLabel(step: number): string {
    return HIRE_STEP_LABELS[step] ?? HIRE_STEP_LABELS[0];
  }

  protected fileList(draft: HireDraftItem): string {
    return draft.files.map((slot) => FILE_LABELS[slot] ?? slot).join(', ') || '—';
  }

  protected async remove(draft: HireDraftItem): Promise<void> {
    if (!confirm(`Delete the draft for ${draft.displayName}? What was typed and the files kept with it are removed.`)) {
      return;
    }
    this.deleting.set(draft.id);
    this.error.set(null);
    try {
      await this.api.deleteHireDraft(draft.id);
      this.drafts.update((rows) => rows.filter((row) => row.id !== draft.id));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The draft could not be deleted.');
    } finally {
      this.deleting.set(null);
    }
  }

  private async load(): Promise<void> {
    try {
      this.drafts.set(await this.api.hireDrafts());
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Drafts could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
