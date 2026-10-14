import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import type { EmployeeProfile } from '@gggs/shared/models';
import { EmptyState, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';
import { PersonalTab } from './tabs/personal-tab';

/**
 * The signed-in employee's own record, read through the self-service route - it needs no employee
 * permission, only an account linked to an employee. Changes go through HR until self-service
 * change requests arrive with Workflow (M3).
 */
@Component({
  selector: 'gggs-my-profile-page',
  imports: [DatePipe, PageHeader, StatusBadge, Skeleton, EmptyState, PersonalTab],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error()) {
      <gggs-empty-state icon="pi pi-user" title="No employee record" [description]="error()!" />
    } @else if (!profile()) {
      <gggs-skeleton [lines]="8" />
    } @else {
      @let p = profile()!;
      <gggs-page-header title="My profile" [subtitle]="p.displayName + ' · ' + p.employeeCode">
        <gggs-status-badge [status]="p.status" />
      </gggs-page-header>

      <div class="stack">
        <section class="panel">
          <dl class="definition-list">
            <dt>Designation</dt><dd>{{ p.assignment.designation ?? '—' }}</dd>
            <dt>Department</dt><dd>{{ p.assignment.department?.name ?? '—' }}</dd>
            <dt>Manager</dt><dd>{{ p.assignment.manager?.name ?? '—' }}</dd>
            <dt>Location</dt><dd>{{ p.assignment.location?.name ?? '—' }}</dd>
            <dt>Joined</dt><dd>{{ p.joiningDate | date: 'mediumDate' }}</dd>
            <dt>Employment type</dt><dd>{{ p.assignment.employmentType.label }}</dd>
            <dt>Work mode</dt><dd>{{ p.assignment.workMode.label }}</dd>
            <dt>PAN</dt><dd>{{ p.sensitive.panHint ?? '—' }}</dd>
            <dt>Bank account</dt><dd>{{ p.sensitive.bankAccountHint ?? '—' }}</dd>
          </dl>
        </section>
        <gggs-personal-tab [profile]="p" (changed)="load()" />
        <p class="muted">To correct anything here, contact HR. Self-service change requests arrive with Workflow (M3).</p>
      </div>
    }
  `,
})
export class MyProfilePage {
  private readonly api = inject(EmployeeDirectoryApi);

  protected readonly profile = signal<EmployeeProfile | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(): Promise<void> {
    try {
      this.profile.set(await this.api.me());
    } catch (error) {
      this.error.set(
        error instanceof ApiError && error.status === 404
          ? 'Your account is not linked to an employee record. Ask HR to link it.'
          : error instanceof ApiError
            ? error.message
            : 'Try again in a moment.',
      );
    }
  }
}
