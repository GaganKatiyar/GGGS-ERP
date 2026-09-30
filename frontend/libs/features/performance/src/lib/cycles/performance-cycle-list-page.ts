import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HasPermissionDirective } from '@gggs/core/permissions';
import { Button, Card, PageHeader } from '@gggs/shared/ui';
import type { PerformanceCycleListItem } from '@gggs/shared/models';
import { PerformanceApi } from '../data/performance-api';

/** Every performance cycle this tenant has defined, plus a form to add a new one. */
@Component({
  selector: 'gggs-performance-cycle-list-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, Card, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Performance cycles" subtitle="Goals and reviews are raised against a cycle." />

    <gggs-card title="Cycles">
      @if (cycles(); as items) {
        @if (items.length > 0) {
          <table class="cycles-table">
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col">Start</th>
                <th scope="col">End</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              @for (cycle of items; track cycle.id) {
                <tr (click)="open(cycle.id)" class="cycle-row">
                  <td>{{ cycle.name }}</td>
                  <td>{{ cycle.startDate }}</td>
                  <td>{{ cycle.endDate }}</td>
                  <td>{{ cycle.status }}</td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No cycles yet.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }

      <form
        *gggsHasPermission="'performance.cycle.manage'"
        [formGroup]="form"
        (ngSubmit)="submit()"
        novalidate
        class="create-form"
      >
        <label class="field">
          <span class="field__label">Name</span>
          <input type="text" formControlName="name" maxlength="150" required />
        </label>

        <label class="field">
          <span class="field__label">Start date</span>
          <input type="date" formControlName="startDate" required />
        </label>

        <label class="field">
          <span class="field__label">End date</span>
          <input type="date" formControlName="endDate" required />
        </label>

        @if (errorMessage()) {
          <p class="field__error" role="alert">{{ errorMessage() }}</p>
        }

        <gggs-button type="submit" size="sm" [disabled]="isSaving()">
          {{ isSaving() ? 'Creating…' : 'Create cycle' }}
        </gggs-button>
      </form>
    </gggs-card>
  `,
  styles: `
    .cycles-table {
      width: 100%;
      border-collapse: collapse;
      margin-block-end: 1rem;
    }

    .cycles-table th,
    .cycles-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .cycle-row {
      cursor: pointer;
    }

    .cycle-row:hover {
      background: var(--gggs-color-surface-sunken, #f5f6f8);
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
export class PerformanceCycleListPage {
  private readonly api = inject(PerformanceApi);
  private readonly formBuilder = inject(FormBuilder);
  private readonly router = inject(Router);

  protected readonly cycles = signal<readonly PerformanceCycleListItem[] | null>(null);
  protected readonly isSaving = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    startDate: ['', Validators.required],
    endDate: ['', Validators.required],
  });

  constructor() {
    void this.load();
  }

  protected open(cycleId: string): void {
    void this.router.navigate(['/performance/cycles', cycleId]);
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
      const cycle = await this.api.createCycle({
        name: value.name ?? '',
        startDate: value.startDate ?? '',
        endDate: value.endDate ?? '',
      });

      this.form.reset();
      await this.router.navigate(['/performance/cycles', cycle.id]);
    } catch {
      this.errorMessage.set('The cycle could not be created. Please try again.');
    } finally {
      this.isSaving.set(false);
    }
  }

  private async load(): Promise<void> {
    this.cycles.set(await this.api.listCycles());
  }
}
