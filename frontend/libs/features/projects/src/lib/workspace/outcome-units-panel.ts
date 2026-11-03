import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import type { CommercialTerms, OutcomeUnits } from '@gggs/shared/models';
import { MoneyPipe, ProjectFinancialsApi } from '@gggs/shared/money';
import { FormDialog, localIsoDate } from '@gggs/shared/ui';

/** Units of outcome the client has accepted: what an outcome-priced engagement earns from. */
@Component({
  selector: 'gggs-outcome-units-panel',
  imports: [DatePipe, DecimalPipe, MoneyPipe, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel stack">
      <div class="toolbar">
        <h3 class="ou__heading">Accepted {{ unitName() }}s</h3>
        <span class="ou__spacer"></span>
        @if (canManage()) {
          <button type="button" class="btn btn--sm" [disabled]="!terms()?.unitPrice" (click)="open()"><i class="pi pi-plus" aria-hidden="true"></i> Record acceptance</button>
        }
      </div>
      @if (!terms()?.unitPrice) {
        <p class="muted ou__note">Set the unit and its price in the commercial terms first.</p>
      } @else {
        <p class="muted ou__note">
          {{ total() | number: '1.0-2' }} accepted{{ terms()?.targetUnits ? ' of ' + (terms()?.targetUnits | number: '1.0-2') + ' expected' : '' }}, at {{ terms()?.unitPrice | gggsMoney: currency() }} each today.
          Revenue counts what the client has accepted, at the price when they accepted it.
        </p>
      }
      <div class="ou__scroll" tabindex="0" role="region" aria-label="Accepted units">
        <table class="table">
          <thead>
            <tr><th scope="col">Accepted</th><th scope="col">What</th><th scope="col" class="ou__num">Quantity</th><th scope="col" class="ou__num">Price</th><th scope="col" class="ou__num">Amount</th><th scope="col">By</th><th scope="col"><span class="sr-only">Actions</span></th></tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.id) {
              <tr>
                <td>{{ row.acceptedOn | date: 'd MMM y' }}</td>
                <td>{{ row.reference }}@if (row.evidence) { <div class="muted ou__note">{{ row.evidence }}</div> }</td>
                <td class="ou__num">{{ row.quantity | number: '1.0-2' }}</td>
                <td class="ou__num">{{ row.unitPrice | gggsMoney: currency() }}</td>
                <td class="ou__num">{{ row.amount | gggsMoney: currency() }}</td>
                <td>{{ row.acceptedBy }}</td>
                <td class="ou__num">
                  @if (row.isBilled) {
                    <span class="muted ou__note">Invoiced</span>
                  } @else if (canManage()) {
                    <button type="button" class="btn btn--link btn--sm" [disabled]="busy()" (click)="remove(row)">Remove</button>
                  }
                </td>
              </tr>
            } @empty {
              <tr><td colspan="7" class="muted">Nothing accepted yet.</td></tr>
            }
          </tbody>
        </table>
      </div>
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </section>

    <gggs-form-dialog [(visible)]="adding" title="Record acceptance" saveLabel="Record" [busy]="busy()" [error]="formError()" [saveDisabled]="!canSave()" (save)="save()">
      <div class="ou__grid">
        <label class="field">
          <span class="field__label">Accepted on</span>
          <input type="date" [value]="acceptedOn()" (change)="acceptedOn.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">How many</span>
          <input type="number" min="0" step="any" [value]="quantity() || ''" (input)="quantity.set(+$any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">What was accepted</span>
          <input type="text" maxlength="200" placeholder="Release 4.2" [value]="reference()" (input)="reference.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Accepted by</span>
          <input type="text" maxlength="200" placeholder="Who at the client" [value]="acceptedBy()" (input)="acceptedBy.set($any($event.target).value)" />
        </label>
      </div>
      <label class="field">
        <span class="field__label">Evidence</span>
        <input type="text" maxlength="1000" placeholder="Sign-off email, evaluation results" [value]="evidence()" (input)="evidence.set($any($event.target).value)" />
      </label>
    </gggs-form-dialog>
  `,
  styles: `
    .ou__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .ou__spacer {
      flex: 1;
    }

    .ou__note {
      margin: 0;
      font-size: var(--gggs-font-size-sm);
    }

    .ou__scroll {
      overflow-x: auto;
    }

    .ou__num {
      text-align: end !important;
      font-variant-numeric: tabular-nums;
    }

    .ou__grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      gap: var(--gggs-space-3);
    }
  `,
})
export class OutcomeUnitsPanel {
  private readonly api = inject(ProjectFinancialsApi);

  readonly projectId = input.required<string>();
  readonly currency = input<string>();
  readonly terms = input<CommercialTerms | undefined>(undefined);
  readonly canManage = input(false);
  readonly changed = output();

  protected readonly rows = signal<readonly OutcomeUnits[]>([]);
  protected readonly adding = signal(false);
  protected readonly acceptedOn = signal('');
  protected readonly quantity = signal(0);
  protected readonly reference = signal('');
  protected readonly acceptedBy = signal('');
  protected readonly evidence = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  protected readonly unitName = computed(() => this.terms()?.unitName || 'unit');
  protected readonly total = computed(() => this.rows().reduce((sum, row) => sum + row.quantity, 0));
  protected readonly canSave = computed(() => this.quantity() > 0 && Boolean(this.reference().trim()) && Boolean(this.acceptedBy().trim()) && Boolean(this.acceptedOn()));

  constructor() {
    effect(() => {
      const id = this.projectId();
      untracked(() => {
        this.api.outcomeUnits(id).then(
          (rows) => this.rows.set(rows),
          (error: unknown) => this.error.set(error instanceof ApiError ? error.message : 'The accepted units could not be loaded.'),
        );
      });
    });
  }

  protected open(): void {
    this.acceptedOn.set(localIsoDate());
    this.quantity.set(0);
    this.reference.set('');
    this.acceptedBy.set('');
    this.evidence.set('');
    this.formError.set(null);
    this.adding.set(true);
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      this.rows.set(
        await this.api.acceptOutcomeUnits(this.projectId(), {
          acceptedOn: this.acceptedOn(),
          quantity: this.quantity(),
          reference: this.reference().trim(),
          acceptedBy: this.acceptedBy().trim(),
          evidence: this.evidence().trim() || null,
        }),
      );
      this.adding.set(false);
      this.changed.emit();
    } catch (error) {
      this.formError.set(error instanceof ApiError ? error.message : 'That could not be recorded.');
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(row: OutcomeUnits): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.rows.set(await this.api.removeOutcomeUnits(this.projectId(), row.id));
      this.changed.emit();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That could not be removed.');
    } finally {
      this.busy.set(false);
    }
  }
}
