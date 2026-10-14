import { ChangeDetectionStrategy, Component, inject, input, signal, type OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import type { OrgChartNode } from '@gggs/shared/models';
import { EmptyState, PageHeader, Skeleton } from '@gggs/shared/ui';

/** One person on the chart and, recursively, the people who report to them. */
@Component({
  selector: 'gggs-org-chart-node',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let n = node();
    <div class="chart-node">
      <div class="chart-node__card">
        <a class="chart-node__name" [routerLink]="['/employees', n.id]">{{ n.displayName }}</a>
        <span class="muted chart-node__meta">{{ n.designation ?? '' }}{{ n.designation && n.departmentName ? ' · ' : '' }}{{ n.departmentName ?? '' }}</span>
        @if (n.directReportCount) {
          <button type="button" class="btn btn--link btn--sm" [attr.aria-expanded]="expanded()" (click)="toggle()">
            {{ expanded() ? 'Hide' : 'Show' }} {{ n.directReportCount }} {{ n.directReportCount === 1 ? 'report' : 'reports' }}
          </button>
        }
      </div>
      @if (expanded() && reports().length) {
        <div class="chart-node__reports">
          @for (child of reports(); track child.id) {
            <gggs-org-chart-node [node]="child" />
          }
        </div>
      }
    </div>
  `,
  styles: `
    .chart-node__card {
      display: inline-flex;
      flex-direction: column;
      gap: 0.125rem;
      min-width: 14rem;
      padding: var(--gggs-space-2) var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-raised);
    }

    .chart-node__name {
      font-weight: var(--gggs-font-weight-semibold);
    }

    .chart-node__meta {
      font-size: var(--gggs-font-size-xs);
    }

    .chart-node__reports {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-2);
      margin-block-start: var(--gggs-space-2);
      margin-inline-start: var(--gggs-space-5, 1.5rem);
      padding-inline-start: var(--gggs-space-3);
      border-inline-start: 2px solid var(--gggs-color-border);
    }
  `,
})
export class OrgChartNodeView {
  private readonly api = inject(EmployeeDirectoryApi);

  readonly node = input.required<OrgChartNode>();

  protected readonly expanded = signal(false);
  /** Reports the server sent with the node, or fetched when it was cut off by the depth limit. */
  protected readonly reports = signal<readonly OrgChartNode[]>([]);

  protected async toggle(): Promise<void> {
    if (this.expanded()) {
      this.expanded.set(false);
      return;
    }

    const node = this.node();
    if (node.reports.length) {
      this.reports.set(node.reports);
    } else if (!this.reports().length) {
      const [self] = await this.api.orgChart(node.id, 2);
      this.reports.set(self?.reports ?? []);
    }
    this.expanded.set(true);
  }
}

/**
 * The reporting hierarchy from the top (or from one person, via `?root=`), opened a level at a
 * time so a large company stays readable.
 */
@Component({
  selector: 'gggs-org-chart-page',
  imports: [RouterLink, PageHeader, Skeleton, EmptyState, OrgChartNodeView],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Org chart" subtitle="Who reports to whom.">
      <a class="btn" routerLink="..">Back to directory</a>
    </gggs-page-header>

    @if (error()) {
      <gggs-empty-state icon="pi pi-exclamation-circle" title="The org chart could not be loaded" [description]="error()!" />
    } @else if (!roots()) {
      <gggs-skeleton [lines]="6" />
    } @else if (roots()!.length === 0) {
      <gggs-empty-state icon="pi pi-sitemap" title="Nobody to show yet" description="Hire employees and set their managers to build the chart." />
    } @else {
      <div class="org-chart stack" aria-label="Reporting hierarchy">
        @for (root of roots(); track root.id) {
          <gggs-org-chart-node [node]="root" />
        }
      </div>
    }
  `,
})
export class OrgChartPage implements OnInit {
  private readonly api = inject(EmployeeDirectoryApi);

  /** Bound from the `root` query parameter. */
  readonly root = input<string | undefined>(undefined);

  protected readonly roots = signal<readonly OrgChartNode[] | null>(null);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.api.orgChart(this.root(), 2).then(
      (roots) => this.roots.set(roots),
      (error: unknown) => this.error.set(error instanceof ApiError ? error.message : 'Try again in a moment.'),
    );
  }
}
