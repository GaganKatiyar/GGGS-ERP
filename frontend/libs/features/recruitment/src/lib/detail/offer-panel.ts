import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { OrgReferenceApi } from '@gggs/shared/org-reference';
import { Button } from '@gggs/shared/ui';
import type { DesignationListItem, Offer } from '@gggs/shared/models';
import { RecruitmentApi } from '../data/recruitment-api';

/**
 * The offer against one application - embedded under its row in `JobApplicationsPanel` rather
 * than its own route, matching how applications and postings are embedded further up. At most one
 * offer per application, so this shows either the existing offer and its actions, or (once the
 * application is at the Offer stage) a form to create the first and only one.
 */
@Component({
  selector: 'gggs-offer-panel',
  imports: [ReactiveFormsModule, HasPermissionDirective, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isLoading()) {
      <p class="muted">Loading…</p>
    } @else if (offer(); as o) {
      <dl class="offer-grid">
        <dt>Offer no.</dt>
        <dd>{{ o.offerNo }}</dd>
        <dt>Annual CTC</dt>
        <dd>{{ o.annualCtc }} {{ o.currency }}</dd>
        <dt>Joining date</dt>
        <dd>{{ o.joiningDate }}</dd>
        <dt>Status</dt>
        <dd>{{ o.status }}</dd>
      </dl>

      <div class="offer-actions" *gggsHasPermission="'recruitment.offer.manage'">
        @if (o.status === 'Draft') {
          <gggs-button size="sm" (click)="send()" [disabled]="isActing()">Send</gggs-button>
        }
        @if (o.status === 'Sent') {
          <gggs-button size="sm" (click)="accept()" [disabled]="isActing()">Accept</gggs-button>
          <gggs-button size="sm" (click)="decline()" [disabled]="isActing()">Decline</gggs-button>
        }
        @if (o.status === 'Draft' || o.status === 'Sent') {
          <gggs-button size="sm" (click)="revoke()" [disabled]="isActing()">Revoke</gggs-button>
        }
      </div>
    } @else if (applicationIsAtOfferStage()) {
      <form
        *gggsHasPermission="'recruitment.offer.manage'"
        [formGroup]="form"
        (ngSubmit)="submit()"
        novalidate
        class="create-form"
      >
        <label class="field">
          <span class="field__label">Designation</span>
          <select formControlName="designationId" required>
            <option value="" disabled>Choose one</option>
            @for (designation of designations(); track designation.id) {
              <option [value]="designation.id">{{ designation.title }}</option>
            }
          </select>
        </label>

        <label class="field">
          <span class="field__label">Annual CTC</span>
          <input type="number" formControlName="annualCtc" min="1" required />
        </label>

        <label class="field">
          <span class="field__label">Currency</span>
          <input type="text" formControlName="currency" maxlength="3" placeholder="INR" required />
        </label>

        <label class="field">
          <span class="field__label">Joining date</span>
          <input type="date" formControlName="joiningDate" required />
        </label>

        <label class="field">
          <span class="field__label">Expiry date</span>
          <input type="date" formControlName="expiryDate" required />
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Creating…' : 'Create offer' }}
        </gggs-button>
      </form>
    } @else {
      <p class="muted">No offer yet.</p>
    }
  `,
  styles: `
    .offer-grid {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 0.25rem 0.75rem;
      margin: 0 0 0.75rem;
      font-size: 0.8125rem;
    }

    .offer-grid dt {
      font-weight: 600;
      color: var(--gggs-color-text-muted, #5b6472);
    }

    .offer-grid dd {
      margin: 0;
    }

    .offer-actions {
      display: flex;
      gap: 0.5rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0;
      font-size: 0.8125rem;
    }

    .create-form {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
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
export class OfferPanel {
  private readonly api = inject(RecruitmentApi);
  private readonly orgApi = inject(OrgReferenceApi);
  private readonly formBuilder = inject(FormBuilder);

  readonly applicationId = input.required<string>();
  readonly applicationIsAtOfferStage = input.required<boolean>();

  protected readonly offer = signal<Offer | null>(null);
  protected readonly designations = signal<readonly DesignationListItem[]>([]);
  protected readonly isLoading = signal(true);
  protected readonly isActing = signal(false);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    designationId: ['', Validators.required],
    annualCtc: [0, [Validators.required, Validators.min(1)]],
    currency: ['INR', Validators.required],
    joiningDate: ['', Validators.required],
    expiryDate: ['', Validators.required],
  });

  constructor() {
    void this.load();
    void this.loadDesignations();
  }

  protected async send(): Promise<void> {
    await this.act((offerId) => this.api.sendOffer(offerId));
  }

  protected async accept(): Promise<void> {
    await this.act((offerId) => this.api.acceptOffer(offerId));
  }

  protected async decline(): Promise<void> {
    await this.act((offerId) => this.api.declineOffer(offerId));
  }

  protected async revoke(): Promise<void> {
    await this.act((offerId) => this.api.revokeOffer(offerId));
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
      const offer = await this.api.createOffer(this.applicationId(), {
        designationId: value.designationId ?? '',
        annualCtc: value.annualCtc ?? 0,
        currency: value.currency ?? 'INR',
        joiningDate: value.joiningDate ?? '',
        expiryDate: value.expiryDate ?? '',
      });

      this.offer.set(offer);
    } catch {
      this.errorMessage.set('The offer could not be created. Check the details and try again.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async act(run: (offerId: string) => Promise<Offer>): Promise<void> {
    const offerId = this.offer()?.id;
    if (!offerId || this.isActing()) {
      return;
    }

    this.isActing.set(true);
    this.errorMessage.set(null);

    try {
      this.offer.set(await run(offerId));
    } catch {
      this.errorMessage.set('That action could not be completed. Please try again.');
    } finally {
      this.isActing.set(false);
    }
  }

  private async load(): Promise<void> {
    this.isLoading.set(true);

    try {
      this.offer.set(await this.api.getOffer(this.applicationId()));
    } finally {
      this.isLoading.set(false);
    }
  }

  private async loadDesignations(): Promise<void> {
    this.designations.set(await this.orgApi.listDesignations());
  }
}
