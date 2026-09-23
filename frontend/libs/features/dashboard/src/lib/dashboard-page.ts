import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { SessionStore } from '@gggs/core/auth';
import { HasPermissionDirective } from '@gggs/core/permissions';

/**
 * Employee dashboard.
 *
 * Phase 0 scaffold: it proves the shell, the session and permission-gated rendering work end to
 * end. The widget framework from the architecture (each widget with its own permission, feature
 * flag and independent load) arrives with the real dashboards in Phase 2.
 */
@Component({
  selector: 'gggs-dashboard-page',
  imports: [HasPermissionDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="dashboard">
      <h1 class="dashboard__title">
        Welcome back, {{ session.displayName() }}
      </h1>

      <div class="dashboard__grid">
        <article class="card" *gggsHasPermission="'self.attendance.view'">
          <h2 class="card__title">Today</h2>
          <p class="card__body">Attendance summary appears here.</p>
        </article>

        <article class="card" *gggsHasPermission="'self.leave.view'">
          <h2 class="card__title">Leave balance</h2>
          <p class="card__body">Your balances by leave type appear here.</p>
        </article>

        <article class="card" *gggsHasPermission="'workflow.task.view'">
          <h2 class="card__title">Pending approvals</h2>
          <p class="card__body">Requests waiting on you appear here.</p>
        </article>
      </div>
    </section>
  `,
  styles: `
    .dashboard__title {
      margin-block: 0 1rem;
      font-size: 1.375rem;
    }

    .dashboard__grid {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr));
    }

    .card {
      padding: 1rem;
      background: var(--gggs-color-surface-raised, #ffffff);
      border: 1px solid var(--gggs-color-border, #e2e5ea);
      border-radius: 0.5rem;
    }

    .card__title {
      margin-block: 0 0.5rem;
      font-size: 1rem;
    }

    .card__body {
      margin: 0;
      color: var(--gggs-color-text-muted, #5b6472);
    }
  `,
})
export class DashboardPage {
  protected readonly session = inject(SessionStore);
}
