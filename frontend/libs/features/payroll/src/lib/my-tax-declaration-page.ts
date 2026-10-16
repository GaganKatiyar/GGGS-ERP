import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import type { DeclarationItem, DeclarationView, TaxRegime } from '@gggs/shared/models';
import { MoneyPipe, PayrollApi } from '@gggs/shared/money';
import { PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { financialYearLabel, financialYearOf, message, thisMonth } from './payroll-shared';

interface ItemDraft {
  section: string;
  description: string;
  declared: number;
}

const SECTION_HINTS: Readonly<Record<string, string>> = {
  '80C': 'PPF, ELSS, life insurance, tuition fees, home-loan principal',
  '80CCD(1B)': 'Your own NPS contribution, over 80C',
  '80D': 'Health insurance for yourself and family',
  '80D-PARENTS': 'Health insurance for your parents',
  '80TTA': 'Savings-account interest',
  '24(b)': 'Interest on a home loan for a house you live in',
  '80E': 'Interest on an education loan',
  '80G': 'Donations to approved charities',
};

/**
 * My tax declaration (Form 12BB): choose a regime - with the year's tax under each to compare - then
 * declare investments, rent and previous income for payroll to project TDS from and verify.
 */
@Component({
  selector: 'gggs-my-tax-declaration-page',
  imports: [DatePipe, MoneyPipe, PageHeader, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Tax declaration" [subtitle]="'What payroll deducts income tax on for ' + yearLabel(year()) + '.'" />
    @if (view(); as v) {
      <section class="panel stack">
        <div class="toolbar">
          <gggs-status-badge [status]="v.status" />
          @if (v.submittedDate) {
            <span class="muted">Submitted {{ v.submittedDate | date: 'd MMM y' }}</span>
          }
          @if (v.verifiedDate) {
            <span class="muted">· verified {{ v.verifiedDate | date: 'd MMM y' }}</span>
          }
        </div>
        @if (v.verifierNotes) {
          <p>Payroll's note: {{ v.verifierNotes }}</p>
        }
        @if (v.comparison; as c) {
          <div class="pay__grid">
            <button type="button" class="regime" [class.regime--on]="regime() === 'New'" [disabled]="locked()" (click)="regime.set('New')">
              <strong>New regime</strong>
              <span>{{ c.newRegimeTax | gggsMoney }} tax a year</span>
              <span class="muted">Lower rates, no deductions</span>
              @if (c.better === 'New') {
                <span class="regime__best">Lower for you</span>
              }
            </button>
            <button type="button" class="regime" [class.regime--on]="regime() === 'Old'" [disabled]="locked()" (click)="regime.set('Old')">
              <strong>Old regime</strong>
              <span>{{ c.oldRegimeTax | gggsMoney }} tax a year</span>
              <span class="muted">With what you have declared below</span>
              @if (c.better === 'Old') {
                <span class="regime__best">Lower for you</span>
              }
            </button>
          </div>
          <p class="muted">Estimates on your current salary; save after changing your declaration to update them.</p>
        } @else {
          <div class="toolbar">
            <label class="pay__flag"><input type="radio" name="regime" [checked]="regime() === 'New'" [disabled]="locked()" (change)="regime.set('New')" /> New regime</label>
            <label class="pay__flag"><input type="radio" name="regime" [checked]="regime() === 'Old'" [disabled]="locked()" (change)="regime.set('Old')" /> Old regime</label>
          </div>
        }
      </section>

      @if (regime() === 'Old') {
        <section class="panel stack">
          <h2 class="pay__heading">Deductions</h2>
          <table class="pay__table">
            <thead><tr><th scope="col">Section</th><th scope="col">What</th><th scope="col" class="pay__num">Amount</th><th scope="col"><span class="muted">Remove</span></th></tr></thead>
            <tbody>
              @for (item of items(); track $index; let index = $index) {
                <tr>
                  <td>
                    <select aria-label="Section" [value]="item.section" [disabled]="locked()" (change)="patch(index, { section: $any($event.target).value })">
                      @for (section of sections(); track section) {
                        <option [value]="section">{{ section }}</option>
                      }
                    </select>
                    @if (limit(item.section); as cap) {
                      <span class="muted"> up to {{ cap | gggsMoney }}</span>
                    }
                  </td>
                  <td><input type="text" aria-label="Description" maxlength="200" [placeholder]="hint(item.section)" [value]="item.description" [disabled]="locked()" (input)="patch(index, { description: $any($event.target).value })" /></td>
                  <td class="pay__num"><input type="number" aria-label="Amount" min="0" step="100" [value]="item.declared || ''" [disabled]="locked()" (input)="patch(index, { declared: +$any($event.target).value })" /></td>
                  <td>
                    @if (!locked()) {
                      <button type="button" class="btn btn--link btn--sm" aria-label="Remove" (click)="remove(index)"><i class="pi pi-times" aria-hidden="true"></i></button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
          @if (!locked()) {
            <button type="button" class="btn btn--sm" (click)="add()"><i class="pi pi-plus" aria-hidden="true"></i> Add a deduction</button>
          }
          <p class="muted">Your provident fund already counts towards 80C; payroll adds it.</p>

          <h2 class="pay__heading">House rent</h2>
          <div class="pay__grid">
            <label class="field">
              <span class="field__label">Rent a month</span>
              <input type="number" min="0" step="500" [value]="rent() ?? ''" [disabled]="locked()" (input)="rent.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
            </label>
            <label class="field">
              <span class="field__label">Landlord's PAN (rent over ₹1 lakh a year)</span>
              <input type="text" maxlength="10" [value]="landlordPan()" [disabled]="locked()" (input)="landlordPan.set($any($event.target).value.toUpperCase())" />
            </label>
          </div>
          <label class="pay__flag">
            <input type="checkbox" [checked]="metro()" [disabled]="locked()" (change)="metro.set($any($event.target).checked)" />
            <span>I live in Mumbai, Delhi, Kolkata or Chennai</span>
          </label>
        </section>
      }

      <section class="panel stack">
        <h2 class="pay__heading">Other income this year</h2>
        <div class="pay__grid">
          <label class="field">
            <span class="field__label">Salary from a previous employer</span>
            <input type="number" min="0" [value]="previousIncome() ?? ''" [disabled]="locked()" (input)="previousIncome.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">TDS deducted there</span>
            <input type="number" min="0" [value]="previousTds() ?? ''" [disabled]="locked()" (input)="previousTds.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">Other income to tax with salary</span>
            <input type="number" min="0" [value]="otherIncome() ?? ''" [disabled]="locked()" (input)="otherIncome.set($any($event.target).value === '' ? null : +$any($event.target).value)" />
          </label>
        </div>
        @if (!locked()) {
          <div class="toolbar">
            <button type="button" class="btn" [disabled]="busy()" (click)="save()">Save</button>
            <button type="button" class="btn btn--primary" [disabled]="busy()" (click)="submit()">Submit to payroll</button>
          </div>
        } @else {
          <p class="muted">Payroll has verified this declaration. Ask them to reopen it if something changes.</p>
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
    @if (notice()) {
      <p class="pay__notice" role="status">{{ notice() }}</p>
    }
    @if (error()) {
      <p class="field__error" role="alert">{{ error() }}</p>
    }
  `,
  styleUrl: './payroll.css',
  styles: `
    .regime {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-1);
      padding: var(--gggs-space-3);
      border: 2px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: none;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .regime--on {
      border-color: var(--gggs-color-primary);
      background: var(--gggs-color-primary-subtle);
    }

    .regime__best {
      color: var(--gggs-color-success);
      font-weight: var(--gggs-font-weight-semibold);
    }
  `,
})
export class MyTaxDeclarationPage {
  private readonly api = inject(PayrollApi);

  protected readonly yearLabel = financialYearLabel;
  protected readonly year = signal(financialYearOf(thisMonth()));
  protected readonly view = signal<DeclarationView | null>(null);
  protected readonly regime = signal<TaxRegime>('New');
  protected readonly items = signal<readonly ItemDraft[]>([]);
  protected readonly rent = signal<number | null>(null);
  protected readonly metro = signal(false);
  protected readonly landlordPan = signal('');
  protected readonly otherIncome = signal<number | null>(null);
  protected readonly previousIncome = signal<number | null>(null);
  protected readonly previousTds = signal<number | null>(null);
  protected readonly busy = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly locked = computed(() => this.view()?.status === 'Verified');
  protected readonly sections = computed(() => [...new Set([...Object.keys(this.view()?.sectionLimits ?? {}), ...Object.keys(SECTION_HINTS)])]);

  constructor() {
    this.api.myDeclaration().then(
      (view) => this.apply(view),
      (error: unknown) => this.error.set(message(error, 'Your declaration could not be loaded.')),
    );
  }

  protected limit(section: string): number | undefined {
    return this.view()?.sectionLimits[section];
  }

  protected hint(section: string): string {
    return SECTION_HINTS[section] ?? '';
  }

  protected add(): void {
    this.items.update((items) => [...items, { section: '80C', description: '', declared: 0 }]);
  }

  protected patch(index: number, change: Partial<ItemDraft>): void {
    this.items.update((items) => items.map((item, at) => (at === index ? { ...item, ...change } : item)));
  }

  protected remove(index: number): void {
    this.items.update((items) => items.filter((_, at) => at !== index));
  }

  protected async save(): Promise<boolean> {
    return this.run(async () => {
      const old = this.regime() === 'Old';
      const items: DeclarationItem[] = old ? this.items().filter((item) => item.declared > 0).map((item) => ({ ...item })) : [];
      this.apply(
        await this.api.saveMyDeclaration(this.year(), {
          regime: this.regime(),
          items,
          monthlyRent: old ? this.rent() : null,
          metroCity: old && this.metro(),
          landlordPan: old ? this.landlordPan() || null : null,
          otherIncome: this.otherIncome(),
          previousEmployerIncome: this.previousIncome(),
          previousEmployerTds: this.previousTds(),
        }),
      );
      this.notice.set('Saved.');
    });
  }

  protected async submit(): Promise<void> {
    if (await this.save()) {
      await this.run(async () => {
        this.apply(await this.api.submitMyDeclaration(this.year()));
        this.notice.set('Submitted to payroll.');
      });
    }
  }

  private apply(view: DeclarationView): void {
    this.view.set(view);
    this.year.set(view.financialYear);
    this.regime.set(view.regime);
    this.items.set(view.items.map((item) => ({ section: item.section, description: item.description, declared: item.declared })));
    this.rent.set(view.monthlyRent ?? null);
    this.metro.set(view.metroCity);
    this.landlordPan.set(view.landlordPan ?? '');
    this.otherIncome.set(view.otherIncome ?? null);
    this.previousIncome.set(view.previousEmployerIncome ?? null);
    this.previousTds.set(view.previousEmployerTds ?? null);
  }

  private async run(action: () => Promise<void>): Promise<boolean> {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    try {
      await action();
      return true;
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
