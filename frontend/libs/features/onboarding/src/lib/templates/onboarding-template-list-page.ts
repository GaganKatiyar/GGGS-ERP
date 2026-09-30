import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { OnboardingTemplateListItem } from '@gggs/shared/models';
import { OnboardingApi } from '../data/onboarding-api';

/** Every onboarding template this tenant has defined, plus a form to add a new one. */
@Component({
  selector: 'gggs-onboarding-template-list-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Onboarding templates" subtitle="Reusable checklists for bringing someone on board.">
      <gggs-button link="cases/new" *gggsHasPermission="'onboarding.case.manage'">Start onboarding</gggs-button>
    </gggs-page-header>

    <gggs-card title="Templates">
      @if (templates(); as items) {
        @if (items.length > 0) {
          <ul class="template-list">
            @for (template of items; track template.id) {
              <li>
                <a (click)="open(template.id)" (keydown.enter)="open(template.id)" tabindex="0" role="link">
                  {{ template.name }}
                </a>
              </li>
            }
          </ul>
        } @else {
          <p class="muted">No templates yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }

      <form
        *gggsHasPermission="'onboarding.template.manage'"
        [formGroup]="form"
        (ngSubmit)="submit()"
        novalidate
        class="create-form"
      >
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" formControlName="name" maxlength="150" required />
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Creating…' : 'Create template' }}
        </gggs-button>
      </form>
    </gggs-card>
  `,
  styles: `
    .template-list {
      list-style: none;
      margin: 0 0 1rem;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
    }

    .template-list a {
      cursor: pointer;
      color: var(--gggs-color-primary, #1d4ed8);
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0 0 1rem;
    }

    .create-form {
      display: flex;
      gap: 0.75rem;
      align-items: end;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      font-size: 0.875rem;
    }

    .field__label {
      font-weight: 600;
    }

    .field__error {
      margin: 0;
      color: var(--gggs-color-danger, #b3261e);
      font-size: 0.875rem;
    }
  `,
})
export class OnboardingTemplateListPage {
  private readonly api = inject(OnboardingApi);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);

  protected readonly templates = signal<readonly OnboardingTemplateListItem[] | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
  });

  constructor() {
    void this.load();
  }

  protected open(templateId: string): void {
    void this.router.navigate(['/onboarding/templates', templateId]);
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);
    this.errorMessage.set(null);

    try {
      const template = await this.api.createTemplate({ name: this.form.getRawValue().name ?? '' });
      this.form.reset();
      await this.router.navigate(['/onboarding/templates', template.id]);
    } catch {
      this.errorMessage.set('The template could not be created. Please try again.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async load(): Promise<void> {
    this.templates.set(await this.api.listTemplates());
  }
}
