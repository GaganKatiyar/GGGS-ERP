import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { SeparationApi } from '@gggs/shared/experience';
import type { NoticeRule, SeparationPolicy } from '@gggs/shared/models';
import { EmptyState, PageHeader, Skeleton } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);
const codes = (text: string) => text.split(/[\s,;]+/).map((code) => code.trim().toUpperCase()).filter(Boolean);

/**
 * The separation policy: notice periods (with rules by employment type and probation), what may
 * happen during notice, leave encashment, and the statutory settlement deadlines.
 */
@Component({
  selector: 'gggs-separation-policy-page',
  imports: [PageHeader, Skeleton, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Separation policy" subtitle="Notice periods, early release and buyout, leave during notice and settlement deadlines." />
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }
    @if (saved()) {
      <p class="alert alert--success" role="status">Policy saved.</p>
    }

    @if (draft(); as p) {
      <form class="panel stack" (submit)="$event.preventDefault(); save()">
        <section class="form-section">
          <h2 class="form-section__title">Notice</h2>
          <p class="form-section__description">The notice period when no rule below matches. A more specific rule always wins.</p>
          <div class="form-grid">
            <label class="field"><span class="field__label">Confirmed employees (days)</span><input type="number" min="0" max="180" [value]="p.defaultNoticeDays" (input)="patch({ defaultNoticeDays: +$any($event.target).value })" /></label>
            <label class="field"><span class="field__label">On probation (days)</span><input type="number" min="0" max="180" [value]="p.probationNoticeDays" (input)="patch({ probationNoticeDays: +$any($event.target).value })" /></label>
            <label class="field"><input type="checkbox" [checked]="p.buyoutAllowed" (change)="patch({ buyoutAllowed: $any($event.target).checked })" /><span class="field__label">Employees may buy out unserved notice</span></label>
            <label class="field"><input type="checkbox" [checked]="p.gardenLeaveAllowed" (change)="patch({ gardenLeaveAllowed: $any($event.target).checked })" /><span class="field__label">Garden leave may be used</span></label>
          </div>
        </section>

        <section class="form-section">
          <h2 class="form-section__title">Leave</h2>
          <div class="form-grid">
            <label class="field"><span class="field__label">Blocked while on notice (leave codes)</span><input type="text" [value]="p.blockedLeaveTypeCodes.join(', ')" (change)="patch({ blockedLeaveTypeCodes: codes($any($event.target).value) })" /><span class="field__hint">For example EL. Comma-separated.</span></label>
            <label class="field"><span class="field__label">Encashed in the settlement (leave codes)</span><input type="text" [value]="p.encashableLeaveTypeCodes.join(', ')" (change)="patch({ encashableLeaveTypeCodes: codes($any($event.target).value) })" /></label>
            <label class="field"><span class="field__label">Encashment cap (days)</span><input type="number" min="0" max="365" [value]="p.encashmentCapDays ?? ''" placeholder="No cap" (input)="patch({ encashmentCapDays: $any($event.target).value === '' ? null : +$any($event.target).value })" /></label>
          </div>
        </section>

        <section class="form-section">
          <h2 class="form-section__title">Deadlines and thresholds</h2>
          <p class="form-section__description">Indian law: the full and final settlement within 2 working days of the last day (Code on Wages s.17(2)); gratuity within 30 days (Payment of Gratuity Act s.7).</p>
          <div class="form-grid">
            <label class="field"><span class="field__label">Settlement due (working days)</span><input type="number" min="1" max="60" [value]="p.settlementDueWorkingDays" (input)="patch({ settlementDueWorkingDays: +$any($event.target).value })" /></label>
            <label class="field"><span class="field__label">Gratuity due (days)</span><input type="number" min="1" max="90" [value]="p.gratuityDueDays" (input)="patch({ gratuityDueDays: +$any($event.target).value })" /></label>
            <label class="field"><span class="field__label">Retirement age</span><input type="number" min="50" max="75" [value]="p.retirementAge" (input)="patch({ retirementAge: +$any($event.target).value })" /></label>
            <label class="field"><span class="field__label">Absconding alert after (days absent)</span><input type="number" min="2" max="60" [value]="p.abscondingDays" (input)="patch({ abscondingDays: +$any($event.target).value })" /></label>
          </div>
        </section>

        <div class="form-actions">
          <button type="submit" class="btn btn--primary" [disabled]="busy()">Save policy</button>
        </div>
      </form>

      <section class="panel stack">
        <div class="panel__header">
          <div>
            <h2 class="panel__title">Notice rules</h2>
            <p class="panel__subtitle">A different notice period for an employment type or for people on probation.</p>
          </div>
        </div>
        @if (rules().length) {
          <div class="table-wrap">
            <table class="table">
              <thead><tr><th>Employment type</th><th>Probation</th><th class="num">Notice (days)</th><th class="actions"><span class="sr-only">Actions</span></th></tr></thead>
              <tbody>
                @for (rule of rules(); track rule.id) {
                  <tr>
                    <td>{{ rule.employmentTypeCode ?? 'Any' }}</td>
                    <td>{{ rule.onProbation === true ? 'On probation' : rule.onProbation === false ? 'Confirmed' : 'Either' }}</td>
                    <td class="num">{{ rule.noticeDays }}</td>
                    <td class="actions"><button type="button" class="btn btn--sm btn--danger" (click)="remove(rule)">Remove</button></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else {
          <gggs-empty-state icon="pi pi-sliders-h" title="No rules" description="Everyone gets the default notice period." />
        }
        <div class="toolbar">
          <label class="field"><span class="field__label">Employment type</span><input type="text" placeholder="FULL_TIME" [value]="ruleType()" (input)="ruleType.set($any($event.target).value)" /></label>
          <label class="field">
            <span class="field__label">Probation</span>
            <select [value]="ruleProbation()" (change)="ruleProbation.set($any($event.target).value)">
              <option value="">Either</option>
              <option value="yes">On probation</option>
              <option value="no">Confirmed</option>
            </select>
          </label>
          <label class="field"><span class="field__label">Notice (days)</span><input type="number" min="0" max="180" [value]="ruleDays()" (input)="ruleDays.set(+$any($event.target).value)" /></label>
          <button type="button" class="btn" [disabled]="busy()" (click)="add()">Add rule</button>
        </div>
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="8" />
    }
  `,
})
export class SeparationPolicyPage {
  private readonly api = inject(SeparationApi);
  protected readonly codes = codes;
  protected readonly draft = signal<SeparationPolicy | null>(null);
  protected readonly rules = signal<readonly NoticeRule[]>([]);
  protected readonly ruleType = signal('');
  protected readonly ruleProbation = signal('');
  protected readonly ruleDays = signal(30);
  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected patch(change: Partial<SeparationPolicy>): void {
    const current = this.draft();
    if (current) {
      this.draft.set({ ...current, ...change });
      this.saved.set(false);
    }
  }

  protected save(): Promise<void> {
    const draft = this.draft();
    return draft ? this.run(async () => {
      this.draft.set(await this.api.updatePolicy(draft));
      this.saved.set(true);
    }) : Promise.resolve();
  }

  protected add(): Promise<void> {
    const probation = this.ruleProbation();
    return this.run(async () => {
      await this.api.addNoticeRule({
        gradeId: null,
        employmentTypeCode: this.ruleType().trim() || null,
        onProbation: probation === 'yes' ? true : probation === 'no' ? false : null,
        noticeDays: this.ruleDays(),
      });
      this.ruleType.set('');
      this.ruleProbation.set('');
      this.rules.set(await this.api.noticeRules());
    });
  }

  protected remove(rule: NoticeRule): Promise<void> {
    return this.run(async () => {
      await this.api.removeNoticeRule(rule.id);
      this.rules.set(await this.api.noticeRules());
    });
  }

  private async load(): Promise<void> {
    try {
      const [policy, rules] = await Promise.all([this.api.policy(), this.api.noticeRules()]);
      this.draft.set(policy);
      this.rules.set(rules);
    } catch (error) {
      this.error.set(message(error, 'The policy could not be loaded.'));
    }
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await work();
    } catch (error) {
      this.error.set(message(error, 'That did not work.'));
    } finally {
      this.busy.set(false);
    }
  }
}
