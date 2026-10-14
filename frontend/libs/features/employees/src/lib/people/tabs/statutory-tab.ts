import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import { LookupSelect } from '@gggs/shared/master-data';
import type { BankDetails, EmployeeProfile, StatutoryDetails } from '@gggs/shared/models';
import { LOOKUP_LISTS } from '@gggs/shared/models';
import { FormDialog } from '@gggs/shared/ui';

/**
 * Statutory identifiers and bank details. Everyone who can open the profile sees masked hints;
 * the values themselves are fetched on demand, under their own permission, and every reveal is
 * audited on the server. Values are never kept after the tab closes.
 */
@Component({
  selector: 'gggs-statutory-tab',
  imports: [ReactiveFormsModule, HasPermissionDirective, FormDialog, LookupSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let s = profile().sensitive;
    <div class="stack">
      <section class="panel stack">
        <div class="toolbar">
          <h2 class="statutory__heading">Statutory identifiers</h2>
          @if (!statutory()) {
            <button type="button" class="btn btn--sm" *gggsHasPermission="'employee.identity-document.view'" (click)="revealStatutory()">
              <i class="pi pi-eye" aria-hidden="true"></i> Reveal
            </button>
          } @else {
            <button type="button" class="btn btn--sm" (click)="statutory.set(null)"><i class="pi pi-eye-slash" aria-hidden="true"></i> Hide</button>
          }
          <button type="button" class="btn btn--sm" *gggsHasPermission="'employee.identity-document.manage'" (click)="openStatutory()">Update</button>
        </div>
        <dl class="definition-list">
          <dt>PAN</dt><dd class="statutory__value">{{ statutory()?.pan ?? s.panHint ?? '—' }}</dd>
          <dt>UAN</dt><dd class="statutory__value">{{ statutory()?.uan ?? s.uanHint ?? '—' }}</dd>
          <dt>ESIC number</dt><dd class="statutory__value">{{ statutory()?.esicNumber ?? s.esicHint ?? '—' }}</dd>
          <dt>National ID</dt><dd class="statutory__value">{{ statutory()?.nationalId ?? s.nationalIdHint ?? '—' }}</dd>
          <dt>Professional tax state</dt><dd>{{ s.professionalTaxStateCode ?? '—' }}</dd>
        </dl>
      </section>

      <section class="panel stack">
        <div class="toolbar">
          <h2 class="statutory__heading">Bank account</h2>
          @if (!bank()) {
            <button type="button" class="btn btn--sm" *gggsHasPermission="'employee.bank.view'" (click)="revealBank()"><i class="pi pi-eye" aria-hidden="true"></i> Reveal</button>
          } @else {
            <button type="button" class="btn btn--sm" (click)="bank.set(null)"><i class="pi pi-eye-slash" aria-hidden="true"></i> Hide</button>
          }
          <button type="button" class="btn btn--sm" *gggsHasPermission="'employee.bank.manage'" (click)="openBank()">Update</button>
        </div>
        <dl class="definition-list">
          <dt>Bank</dt><dd>{{ bank()?.bankName ?? s.bankName ?? '—' }}</dd>
          <dt>IFSC</dt><dd class="statutory__value">{{ bank()?.ifsc ?? s.bankIfsc ?? '—' }}</dd>
          <dt>Account holder</dt><dd>{{ bank()?.accountHolder ?? '—' }}</dd>
          <dt>Account number</dt><dd class="statutory__value">{{ bank()?.accountNumber ?? s.bankAccountHint ?? '—' }}</dd>
        </dl>
      </section>

      @if (revealError()) {
        <p class="field__error" role="alert">{{ revealError() }}</p>
      }
    </div>

    <gggs-form-dialog [(visible)]="statutoryOpen" title="Update statutory identifiers" [busy]="busy()" [error]="error()" (save)="saveStatutory()">
      <div class="form-grid" [formGroup]="statutoryForm">
        <p class="muted form-grid__full">Leave a field blank to keep what is stored. Values are encrypted at rest.</p>
        <label class="field">
          <span class="field__label">PAN</span>
          <input type="text" formControlName="pan" maxlength="10" autocomplete="off" placeholder="ABCDE1234F" />
        </label>
        <label class="field">
          <span class="field__label">UAN</span>
          <input type="text" formControlName="uan" maxlength="12" autocomplete="off" inputmode="numeric" />
        </label>
        <label class="field">
          <span class="field__label">ESIC number</span>
          <input type="text" formControlName="esicNumber" maxlength="17" autocomplete="off" inputmode="numeric" />
        </label>
        <label class="field">
          <span class="field__label">National ID</span>
          <input type="text" formControlName="nationalId" maxlength="32" autocomplete="off" />
        </label>
        <label class="field">
          <span class="field__label">Professional tax state</span>
          <gggs-lookup-select [list]="lists.state" parent="IN" formControlName="professionalTaxStateCode" ariaLabel="Professional tax state" />
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="bankOpen" title="Update bank account" [busy]="busy()" [error]="error()" (save)="saveBank()">
      <div class="form-grid" [formGroup]="bankForm">
        <p class="muted form-grid__full">Leave a field blank to keep what is stored. The account number is encrypted at rest.</p>
        <label class="field">
          <span class="field__label">Bank name</span>
          <input type="text" formControlName="bankName" maxlength="200" />
        </label>
        <label class="field">
          <span class="field__label">IFSC</span>
          <input type="text" formControlName="ifsc" maxlength="11" autocomplete="off" placeholder="HDFC0001234" />
        </label>
        <label class="field">
          <span class="field__label">Account holder</span>
          <input type="text" formControlName="accountHolder" maxlength="200" />
        </label>
        <label class="field">
          <span class="field__label">Account number</span>
          <input type="text" formControlName="accountNumber" maxlength="34" autocomplete="off" inputmode="numeric" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .statutory__heading {
      flex: 1;
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .statutory__value {
      font-family: var(--gggs-font-mono, monospace);
    }
  `,
})
export class StatutoryTab {
  private readonly api = inject(EmployeeDirectoryApi);

  readonly profile = input.required<EmployeeProfile>();
  readonly changed = output<void>();

  protected readonly lists = LOOKUP_LISTS;
  protected readonly statutory = signal<StatutoryDetails | null>(null);
  protected readonly bank = signal<BankDetails | null>(null);
  protected readonly revealError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly statutoryOpen = signal(false);
  protected readonly bankOpen = signal(false);

  protected readonly statutoryForm = new FormGroup({
    pan: new FormControl<string | null>(null),
    uan: new FormControl<string | null>(null),
    esicNumber: new FormControl<string | null>(null),
    nationalId: new FormControl<string | null>(null),
    professionalTaxStateCode: new FormControl<string | null>(null),
  });

  protected readonly bankForm = new FormGroup({
    bankName: new FormControl<string | null>(null),
    ifsc: new FormControl<string | null>(null),
    accountHolder: new FormControl<string | null>(null),
    accountNumber: new FormControl<string | null>(null),
  });

  constructor() {
    // A different employee, or fresh data after a save: drop whatever was revealed.
    effect(() => {
      this.profile();
      this.statutory.set(null);
      this.bank.set(null);
    });
  }

  protected async revealStatutory(): Promise<void> {
    await this.reveal(async () => this.statutory.set(await this.api.revealStatutory(this.profile().id)));
  }

  protected async revealBank(): Promise<void> {
    await this.reveal(async () => this.bank.set(await this.api.revealBank(this.profile().id)));
  }

  protected openStatutory(): void {
    this.statutoryForm.reset({ professionalTaxStateCode: this.profile().sensitive.professionalTaxStateCode ?? null });
    this.error.set(null);
    this.statutoryOpen.set(true);
  }

  protected openBank(): void {
    this.bankForm.reset({ bankName: this.profile().sensitive.bankName ?? null, ifsc: this.profile().sensitive.bankIfsc ?? null });
    this.error.set(null);
    this.bankOpen.set(true);
  }

  protected async saveStatutory(): Promise<void> {
    const value = this.statutoryForm.getRawValue();
    await this.run(
      () =>
        this.api.updateStatutory(this.profile().id, {
          pan: StatutoryTab.keepOrSet(value.pan)?.toUpperCase() ?? null,
          uan: StatutoryTab.keepOrSet(value.uan),
          esicNumber: StatutoryTab.keepOrSet(value.esicNumber),
          nationalId: StatutoryTab.keepOrSet(value.nationalId),
          professionalTaxStateCode: value.professionalTaxStateCode ?? '',
        }),
      () => this.statutoryOpen.set(false),
    );
  }

  protected async saveBank(): Promise<void> {
    const value = this.bankForm.getRawValue();
    await this.run(
      () =>
        this.api.updateBank(this.profile().id, {
          bankName: StatutoryTab.keepOrSet(value.bankName),
          ifsc: StatutoryTab.keepOrSet(value.ifsc)?.toUpperCase() ?? null,
          accountHolder: StatutoryTab.keepOrSet(value.accountHolder),
          accountNumber: StatutoryTab.keepOrSet(value.accountNumber),
        }),
      () => this.bankOpen.set(false),
    );
  }

  /** The API reads null as "leave as it is"; a blank field therefore sends null, not an empty string. */
  private static keepOrSet(text: string | null): string | null {
    return text?.trim() ? text.trim() : null;
  }

  private async reveal(call: () => Promise<void>): Promise<void> {
    this.revealError.set(null);
    try {
      await call();
    } catch (error) {
      this.revealError.set(error instanceof ApiError ? error.message : 'The details could not be shown.');
    }
  }

  private async run(call: () => Promise<void>, close: () => void): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await call();
      close();
      this.changed.emit();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The details could not be saved. Try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
