import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import type { FinanceInvoice } from '@gggs/shared/models';
import { MoneyPipe, ProjectFinancialsApi } from '@gggs/shared/money';
import { FormDialog, localIsoDate } from '@gggs/shared/ui';

/**
 * A credit note against an invoice that has gone out. Crediting a line reverses it and puts what it billed
 * back up for billing; an amount just reduces the invoice, for a discount or a correction.
 */
@Component({
  selector: 'gggs-credit-note-dialog',
  imports: [MoneyPipe, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog [(visible)]="visible" [title]="'Credit note on ' + (invoice()?.number ?? '')" saveLabel="Raise credit note" [busy]="busy()" [error]="error()" [saveDisabled]="!canSave()" (save)="raise()">
      <div class="stack">
        @if (lines().length) {
          <fieldset class="cn__choice">
            <legend class="field__label">What to credit</legend>
            <label><input type="radio" name="credit-by" [checked]="by() === 'lines'" (change)="by.set('lines')" /> Lines</label>
            <label><input type="radio" name="credit-by" [checked]="by() === 'amount'" (change)="by.set('amount')" /> An amount</label>
          </fieldset>
        }
        @if (by() === 'lines') {
          @for (line of lines(); track line.id) {
            <label class="cn__line">
              <input type="checkbox" [checked]="chosen().has(line.id)" (change)="toggle(line.id, $any($event.target).checked)" />
              <span>{{ line.description }}</span>
              <span class="cn__num">{{ line.amount | gggsMoney: currency() }}</span>
            </label>
          }
          <p class="muted cn__note">Hours or months on these lines can be billed again, and the hours can be reopened.</p>
        } @else {
          <label class="field">
            <span class="field__label">Amount (before tax, up to {{ open() | gggsMoney: currency() }})</span>
            <input type="number" min="0" [max]="open()" [value]="amount() || ''" (input)="amount.set(+$any($event.target).value)" />
          </label>
        }
        <div class="cn__grid">
          <label class="field">
            <span class="field__label">Tax credited</span>
            <input type="number" min="0" [max]="invoice()?.taxAmount ?? 0" [value]="tax()" (input)="tax.set(+$any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">Date</span>
            <input type="date" [value]="date()" (change)="date.set($any($event.target).value)" />
          </label>
        </div>
        <label class="field">
          <span class="field__label">Reason</span>
          <input type="text" maxlength="500" [value]="reason()" (input)="reason.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .cn__choice {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-3);
      margin: 0;
      padding: 0;
      border: 0;
    }

    .cn__line {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr) auto;
      gap: var(--gggs-space-2);
      align-items: center;
    }

    .cn__num {
      font-variant-numeric: tabular-nums;
    }

    .cn__note {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .cn__grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      gap: var(--gggs-space-3);
    }
  `,
})
export class CreditNoteDialog {
  private readonly api = inject(ProjectFinancialsApi);

  readonly visible = model(false);
  readonly projectId = input.required<string>();
  readonly invoice = input<FinanceInvoice | null>(null);
  readonly currency = input<string>();
  readonly credited = output();

  protected readonly by = signal<'lines' | 'amount'>('lines');
  protected readonly chosen = signal<ReadonlySet<string>>(new Set());
  protected readonly amount = signal(0);
  protected readonly tax = signal(0);
  protected readonly date = signal('');
  protected readonly reason = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly lines = computed(() => (this.invoice()?.lines ?? []).filter((line) => !line.isCredited));
  protected readonly open = computed(() => {
    const invoice = this.invoice();
    return invoice ? invoice.amount - invoice.creditedAmount : 0;
  });
  private readonly selected = computed(() => this.lines().filter((line) => this.chosen().has(line.id)).reduce((sum, line) => sum + line.amount, 0));
  protected readonly canSave = computed(() => this.reason().trim().length > 0 && (this.by() === 'lines' ? this.chosen().size > 0 : this.amount() > 0 && this.amount() <= this.open()));

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => this.reset());
      }
    });

    // The tax follows what is being credited, until someone types over it.
    effect(() => {
      const invoice = this.invoice();
      const base = this.by() === 'lines' ? this.selected() : this.amount();
      if (invoice && invoice.amount > 0) {
        this.tax.set(Math.round((invoice.taxAmount * base * 100) / invoice.amount) / 100);
      }
    });
  }

  protected toggle(id: string, on: boolean): void {
    this.chosen.update((chosen) => {
      const next = new Set(chosen);
      if (on) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }

  protected async raise(): Promise<void> {
    const invoice = this.invoice();
    if (!invoice) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.creditInvoice(this.projectId(), invoice.id, {
        reason: this.reason().trim(),
        date: this.date(),
        lineIds: this.by() === 'lines' ? [...this.chosen()] : null,
        amount: this.by() === 'amount' ? this.amount() : null,
        taxAmount: this.tax(),
      });
      this.visible.set(false);
      this.credited.emit();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The credit note could not be raised.');
    } finally {
      this.busy.set(false);
    }
  }

  private reset(): void {
    const lines = this.lines();
    this.by.set(lines.length ? 'lines' : 'amount');
    this.chosen.set(new Set(lines.map((line) => line.id)));
    this.amount.set(lines.length ? 0 : this.open());
    this.date.set(localIsoDate());
    this.reason.set('');
    this.error.set(null);
  }
}
