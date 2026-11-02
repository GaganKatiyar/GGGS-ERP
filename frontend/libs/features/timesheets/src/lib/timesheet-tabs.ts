import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/** The two faces of one's timesheet: this week to fill in, and the weeks already done. */
@Component({
  selector: 'gggs-timesheet-tabs',
  imports: [RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="ts-tabs" aria-label="My timesheet">
      <a class="ts-tabs__tab" routerLink="/me/timesheet" routerLinkActive="ts-tabs__tab--active" ariaCurrentWhenActive="page" [routerLinkActiveOptions]="{ exact: true }">My timesheet</a>
      <a class="ts-tabs__tab" routerLink="/me/timesheet/bank" routerLinkActive="ts-tabs__tab--active" ariaCurrentWhenActive="page">Timesheet bank</a>
    </nav>
  `,
  styles: `
    .ts-tabs {
      display: flex;
      gap: var(--gggs-space-4);
      margin-block-end: var(--gggs-space-3);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .ts-tabs__tab {
      padding-block: var(--gggs-space-2);
      border-block-end: 2px solid transparent;
      color: var(--gggs-color-text-muted);
      text-decoration: none;
    }

    .ts-tabs__tab--active {
      border-block-end-color: var(--gggs-color-primary);
      color: var(--gggs-color-text);
      font-weight: var(--gggs-font-weight-semibold);
    }
  `,
})
export class TimesheetTabs {}
