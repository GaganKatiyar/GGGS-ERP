import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import type { BillingPreview } from '@gggs/shared/models';
import { MoneyPipe, ProjectFinancialsApi } from '@gggs/shared/money';
import { FormDialog, localIsoDate } from '@gggs/shared/ui';
import { monthBefore, monthBounds } from './billing-period';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

/**
 * Builds a draft invoice from the work: a preview of what the period has earned that is not yet invoiced,
 * and what could not go on it and why. Nothing is saved until the invoice is prepared.
 */
@Component({
  selector: 'gggs-prepare-invoice-dialog',
  imports: [MoneyPipe, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-form-dialog [(visible)]="visible" title="Prepare invoice" size="lg" saveLabel="Prepare draft" [busy]="busy()" [error]="error()" [saveDisabled]="!preview()?.lines?.length" (save)="prepare()">
      <div class="stack">
        <div class="bill__grid">
          <label class="field">
            <span class="field__label">From</span>
            <input type="date" [value]="from()" (change)="setFrom($any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">To</span>
            <input type="date" [value]="to()" (change)="setTo($any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">Due date</span>
            <input type="date" [value]="dueDate()" (change)="dueDate.set($any($event.target).value)" />
          </label>
          <label class="field">
            <span class="field__label">Tax</span>
            <input type="number" min="0" [value]="tax()" (input)="tax.set(+$any($event.target).value)" />
          </label>
        </div>

        @if (preview(); as p) {
          @if (p.billingType === 'FixedPrice' || p.billingType === 'Milestone') {
            <p class="muted bill__note">This engagement bills what it has earned so far, whatever the period.</p>
          }
          @if (p.lines.length) {
            <div class="bill__scroll" tabindex="0" role="region" aria-label="Invoice lines">
              <table class="table">
                <thead>
                  <tr><th scope="col">Line</th><th scope="col" class="bill__num">Amount</th></tr>
                </thead>
                <tbody>
                  @for (line of p.lines; track $index) {
                    <tr><td>{{ line.description }}</td><td class="bill__num">{{ line.amount | gggsMoney: p.currency }}</td></tr>
                  }
                </tbody>
                <tfoot>
                  <tr><th scope="row">Before tax</th><td class="bill__num"><strong>{{ p.amount | gggsMoney: p.currency }}</strong></td></tr>
                </tfoot>
              </table>
            </div>
          } @else {
            <p class="muted">Nothing in that period is ready to invoice.</p>
          }
          @if (p.heldByCap > 0) {
            <p class="alert alert--warning" role="status"><i class="pi pi-info-circle" aria-hidden="true"></i><span>{{ p.heldByCap | gggsMoney: p.currency }} held back: it would go over the not-to-exceed amount.</span></p>
          }
          @if (p.unpricedHours > 0) {
            <p class="alert alert--warning" role="status"><i class="pi pi-info-circle" aria-hidden="true"></i><span>{{ p.unpricedHours }} approved hours left out: those people have no billing rate.</span></p>
          }
          @if (p.typedInvoices.length) {
            <p class="alert alert--warning" role="status"><i class="pi pi-info-circle" aria-hidden="true"></i><span>Check this does not repeat {{ p.typedInvoices.join(', ') }}. They were entered by hand, so the app cannot tell what they covered.</span></p>
          }
        }
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .bill__grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
      gap: var(--gggs-space-3);
    }

    .bill__note {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .bill__scroll {
      overflow-x: auto;
    }

    .bill__num {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }
  `,
})
export class PrepareInvoiceDialog {
  private readonly api = inject(ProjectFinancialsApi);

  readonly visible = model(false);
  readonly projectId = input.required<string>();
  /** The month to start from, as yyyy-MM; the month just finished if not given. */
  readonly month = input<string | null>(null);
  readonly prepared = output();

  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly dueDate = signal('');
  protected readonly tax = signal(0);
  protected readonly preview = signal<BillingPreview | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly period = computed(() => `${this.from()}|${this.to()}`);
  private asked = 0;

  constructor() {
    effect(() => {
      if (this.visible()) {
        untracked(() => this.reset());
      }
    });
  }

  protected setFrom(day: string): void {
    this.from.set(day);
    void this.load();
  }

  protected setTo(day: string): void {
    this.to.set(day);
    void this.load();
  }

  protected async prepare(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.prepareInvoice(this.projectId(), { from: this.from(), to: this.to(), dueDate: this.dueDate() || null, taxAmount: this.tax() });
      this.visible.set(false);
      this.prepared.emit();
    } catch (error) {
      this.error.set(message(error, 'The invoice could not be prepared.'));
    } finally {
      this.busy.set(false);
    }
  }

  private reset(): void {
    const { from, to } = monthBounds(this.month() ?? monthBefore(localIsoDate()));
    this.from.set(from);
    this.to.set(to);
    this.dueDate.set('');
    this.tax.set(0);
    this.preview.set(null);
    this.error.set(null);
    void this.load();
  }

  private async load(): Promise<void> {
    this.preview.set(null);
    this.error.set(null);
    if (!this.from() || !this.to()) {
      return;
    }

    // Only the latest period counts, however the answers come back.
    const asked = ++this.asked;
    const period = this.period();
    try {
      const preview = await this.api.billingPreview(this.projectId(), this.from(), this.to());
      if (asked === this.asked && period === this.period()) {
        this.preview.set(preview);
      }
    } catch (error) {
      if (asked === this.asked) {
        this.error.set(message(error, 'The preview could not be loaded.'));
      }
    }
  }
}
