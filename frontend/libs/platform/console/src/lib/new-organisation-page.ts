import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  ALL_MODULES,
  PlatformApi,
  moduleName,
  type CreateOrganisationBody,
  type PlanView,
} from '@gggs/platform/data-access';
import { PageHeader, Skeleton } from '@gggs/shared/ui';
import { messageOf } from './platform-permissions';

const STEPS = ['Organisation', 'Plan & modules', 'Administrator', 'Review'] as const;

/** Lowercase letters, digits and hyphens, 3-32 long, as the backend's tenant code rule. */
const CODE_PATTERN = /^[a-z0-9](?:[a-z0-9-]{1,30})[a-z0-9]$/;

/**
 * Creating an organisation in four steps. On the last one the server provisions it, applies the plan
 * and add-ons, activates it, and emails the administrator their invitation.
 */
@Component({
  selector: 'gggs-console-new-organisation-page',
  imports: [FormsModule, RouterLink, PageHeader, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="New organisation" subtitle="Set up a company on Nuts and Bolts. Their administrator gets an email to sign in." />

    <ol class="steps" aria-label="Steps">
      @for (name of steps; track name; let i = $index) {
        <li [class.steps__item--done]="i < step()" [class.steps__item--current]="i === step()" class="steps__item">
          <span class="steps__dot">{{ i < step() ? '✓' : i + 1 }}</span>{{ name }}
        </li>
      }
    </ol>

    <section class="panel stack">
      @switch (step()) {
        @case (0) {
          <div class="form-grid">
            <label class="field form-grid__full">
              <span class="field__label">Organisation name *</span>
              <input [(ngModel)]="form.name" (ngModelChange)="suggestCode()" maxlength="200" />
            </label>
            <label class="field">
              <span class="field__label">Organisation code *</span>
              <input [(ngModel)]="form.code" (ngModelChange)="codeTouched = true" maxlength="32" />
              <span class="field__hint">Used to sign in (and later as their web address). 3–32 lowercase letters, digits or hyphens.</span>
            </label>
            <label class="field">
              <span class="field__label">Legal name</span>
              <input [(ngModel)]="form.legalName" maxlength="300" />
            </label>
            <label class="field">
              <span class="field__label">Country</span>
              <select [(ngModel)]="form.countryCode">
                <option value="IN">India</option>
                <option value="AE">United Arab Emirates</option>
                <option value="GB">United Kingdom</option>
                <option value="US">United States</option>
                <option value="SG">Singapore</option>
              </select>
            </label>
            <label class="field">
              <span class="field__label">Data region</span>
              <select [(ngModel)]="form.dataRegion">
                <option value="IN">India</option>
                <option value="EU">Europe</option>
                <option value="US">United States</option>
                <option value="ME">Middle East</option>
              </select>
              <span class="field__hint">Where their data is kept. Cannot be changed later.</span>
            </label>
            <label class="field">
              <span class="field__label">Time zone</span>
              <input [(ngModel)]="form.defaultTimeZone" maxlength="64" />
            </label>
            <label class="field">
              <span class="field__label">Currency</span>
              <input [(ngModel)]="form.defaultCurrency" maxlength="3" />
            </label>
          </div>
        }
        @case (1) {
          @if (plans(); as list) {
            <div class="plans">
              @for (plan of list; track plan.code) {
                <button type="button" class="plan" [class.plan--chosen]="form.planCode === plan.code" (click)="choosePlan(plan)">
                  <strong>{{ plan.name }}</strong>
                  <span class="muted text-sm">{{ plan.maxEmployees ? 'Up to ' + plan.maxEmployees + ' employees' : 'Any number of employees' }}</span>
                  <span class="text-sm">{{ plan.features.length }} modules</span>
                </button>
              }
            </div>
            <div class="form-grid">
              <label class="field">
                <span class="field__label">Start as</span>
                <select [(ngModel)]="trial">
                  <option [ngValue]="true">Free trial</option>
                  <option [ngValue]="false">Paid subscription</option>
                </select>
              </label>
              @if (trial) {
                <label class="field">
                  <span class="field__label">Trial length (days)</span>
                  <input type="number" [(ngModel)]="trialDays" min="1" max="90" />
                </label>
              }
              <label class="field">
                <span class="field__label">Seat limit</span>
                <input type="number" [(ngModel)]="form.seatLimit" min="1" [attr.max]="chosenPlan()?.maxEmployees ?? null" placeholder="The plan's limit" />
              </label>
              <label class="field">
                <span class="field__label">Billing reference</span>
                <input [(ngModel)]="form.billingReference" maxlength="100" placeholder="e.g. a contract or invoice number" />
              </label>
            </div>
            @if (addOnChoices().length) {
              <fieldset class="addons">
                <legend class="field__label">Add-on modules (not in {{ chosenPlan()?.name }})</legend>
                @for (code of addOnChoices(); track code) {
                  <label><input type="checkbox" [checked]="form.addOns.includes(code)" (change)="toggleAddOn(code)" /> {{ name(code) }}</label>
                }
              </fieldset>
            }
          } @else {
            <gggs-skeleton [lines]="4" />
          }
        }
        @case (2) {
          <label class="field admin">
            <span class="field__label">Administrator's email *</span>
            <input type="email" [(ngModel)]="form.administratorEmail" maxlength="256" />
            <span class="field__hint">They get an email with a link to choose a password and sign in as the organisation's administrator. They can then invite everyone else.</span>
          </label>
        }
        @case (3) {
          <dl class="definition-list">
            <dt>Organisation</dt><dd>{{ form.name }} <span class="subtle">({{ form.code }})</span></dd>
            @if (form.legalName) { <dt>Legal name</dt><dd>{{ form.legalName }}</dd> }
            <dt>Region</dt><dd>{{ form.countryCode }} · data in {{ form.dataRegion }} · {{ form.defaultTimeZone }} · {{ form.defaultCurrency }}</dd>
            <dt>Plan</dt><dd>{{ chosenPlan()?.name }} · {{ trial ? trialDays + '-day trial' : 'Paid' }}@if (form.seatLimit) { · {{ form.seatLimit }} seats }</dd>
            <dt>Add-ons</dt><dd>{{ form.addOns.length ? addOnNames() : 'None' }}</dd>
            <dt>Administrator</dt><dd>{{ form.administratorEmail }}</dd>
          </dl>
        }
      }

      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }

      <div class="toolbar">
        @if (step() === 0) {
          <a class="btn btn--secondary" routerLink="/organisations">Cancel</a>
        } @else {
          <button type="button" class="btn btn--secondary" (click)="back()" [disabled]="busy()">Back</button>
        }
        <span class="toolbar__spacer"></span>
        @if (step() < steps.length - 1) {
          <button type="button" class="btn btn--primary" (click)="next()">Next</button>
        } @else {
          <button type="button" class="btn btn--primary" (click)="create()" [disabled]="busy()">{{ busy() ? 'Creating…' : 'Create organisation' }}</button>
        }
      </div>
    </section>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .steps { display: flex; flex-wrap: wrap; gap: var(--gggs-space-4); margin: 0; padding: 0; list-style: none; }
    .steps__item { display: flex; align-items: center; gap: var(--gggs-space-2); color: var(--gggs-color-text-muted); }
    .steps__item--current { color: var(--gggs-color-text); font-weight: 600; }
    .steps__dot { display: grid; place-items: center; width: 1.6rem; height: 1.6rem; border-radius: 50%; border: 1px solid var(--gggs-color-border-strong); font-size: var(--gggs-font-size-xs); }
    .steps__item--current .steps__dot, .steps__item--done .steps__dot { background: #4f46e5; border-color: #4f46e5; color: #fff; }
    .plans { display: grid; grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr)); gap: var(--gggs-space-3); }
    .plan { display: flex; flex-direction: column; gap: var(--gggs-space-1); padding: var(--gggs-space-4); border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-md); background: var(--gggs-color-surface-raised); text-align: left; font: inherit; color: inherit; cursor: pointer; }
    .plan--chosen { border-color: #4f46e5; box-shadow: 0 0 0 2px #4f46e5 inset; }
    .addons { display: grid; grid-template-columns: repeat(auto-fill, minmax(14rem, 1fr)); gap: var(--gggs-space-2); border: 0; padding: 0; margin: 0; }
    .addons legend { margin-bottom: var(--gggs-space-2); }
    .admin { max-width: 32rem; }
  `,
})
export class ConsoleNewOrganisationPage {
  private readonly api = inject(PlatformApi);
  private readonly router = inject(Router);

  protected readonly steps = STEPS;
  protected readonly step = signal(0);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly plans = signal<PlanView[] | null>(null);
  private readonly planCode = signal('');

  protected codeTouched = false;
  protected trial = true;
  protected trialDays = 30;
  protected readonly form: CreateOrganisationBody = {
    code: '',
    name: '',
    legalName: '',
    countryCode: 'IN',
    dataRegion: 'IN',
    defaultTimeZone: 'Asia/Kolkata',
    defaultCurrency: 'INR',
    defaultLocale: 'en-IN',
    planCode: '',
    isolationTier: 'Pooled',
    trialDays: 30,
    seatLimit: null,
    billingReference: null,
    administratorEmail: '',
    addOns: [],
  };

  protected readonly chosenPlan = computed(() => this.plans()?.find((plan) => plan.code === this.planCode()) ?? null);
  protected readonly addOnChoices = computed(() => {
    const plan = this.chosenPlan();
    return plan ? ALL_MODULES.filter((code) => !plan.features.includes(code)) : [];
  });

  constructor() {
    this.api.plans().then(
      (view) => {
        const active = view.plans.filter((plan) => plan.isActive);
        this.plans.set(active);
        const middle = active.find((plan) => plan.code === 'PROFESSIONAL') ?? active[0];
        if (middle) this.choosePlan(middle);
      },
      (error: unknown) => this.error.set(messageOf(error, 'The plans could not be loaded.')),
    );
  }

  protected name(code: string): string {
    return moduleName(code);
  }

  protected addOnNames(): string {
    return this.form.addOns.map(moduleName).join(', ');
  }

  protected suggestCode(): void {
    if (this.codeTouched) return;
    this.form.code = this.form.name
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 32)
      .replace(/-+$/g, '');
  }

  protected choosePlan(plan: PlanView): void {
    this.form.planCode = plan.code;
    this.planCode.set(plan.code);
    this.form.addOns = this.form.addOns.filter((code) => !plan.features.includes(code));
  }

  protected toggleAddOn(code: string): void {
    this.form.addOns = this.form.addOns.includes(code)
      ? this.form.addOns.filter((existing) => existing !== code)
      : [...this.form.addOns, code];
  }

  protected back(): void {
    this.error.set(null);
    this.step.update((value) => Math.max(0, value - 1));
  }

  protected next(): void {
    const problem = this.problemWith(this.step());
    this.error.set(problem);
    if (!problem) this.step.update((value) => value + 1);
  }

  protected async create(): Promise<void> {
    for (let index = 0; index < STEPS.length - 1; index++) {
      const problem = this.problemWith(index);
      if (problem) {
        this.step.set(index);
        this.error.set(problem);
        return;
      }
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const created = await this.api.createOrganisation({
        ...this.form,
        code: this.form.code.trim(),
        name: this.form.name.trim(),
        legalName: this.form.legalName?.trim() || null,
        billingReference: this.form.billingReference?.trim() || null,
        trialDays: this.trial ? this.trialDays : null,
        administratorEmail: this.form.administratorEmail.trim(),
      });
      await this.router.navigate(['/organisations', created.id], { queryParams: { created: 1 } });
    } catch (error) {
      this.error.set(messageOf(error, 'The organisation could not be created.'));
    } finally {
      this.busy.set(false);
    }
  }

  private problemWith(step: number): string | null {
    switch (step) {
      case 0:
        if (!this.form.name.trim()) return 'Enter the organisation name.';
        if (!CODE_PATTERN.test(this.form.code.trim())) return 'The code must be 3–32 lowercase letters, digits or hyphens, not starting or ending with a hyphen.';
        return null;
      case 1: {
        if (!this.form.planCode) return 'Choose a plan.';
        const max = this.chosenPlan()?.maxEmployees;
        if (this.form.seatLimit !== null && (this.form.seatLimit < 1 || (max && this.form.seatLimit > max))) {
          return `The seat limit must be between 1 and ${max ?? 'the plan limit'}.`;
        }
        if (this.trial && (this.trialDays < 1 || this.trialDays > 90)) return 'A trial lasts 1 to 90 days.';
        return null;
      }
      case 2:
        return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this.form.administratorEmail.trim()) ? null : 'Enter the administrator’s email address.';
      default:
        return null;
    }
  }
}
