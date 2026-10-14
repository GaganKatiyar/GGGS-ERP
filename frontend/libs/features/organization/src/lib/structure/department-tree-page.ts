import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { DepartmentNode } from '@gggs/shared/models';
import { OrgStructureApi } from '@gggs/shared/org-reference';
import { EmptyState, KpiCard, PageHeader, Skeleton } from '@gggs/shared/ui';

/** A department and its sub-departments, flattened with their depth for rendering. */
interface TreeRow {
  readonly node: DepartmentNode;
  readonly depth: number;
  readonly total: number;
}

/**
 * The department tree with headcounts: who sits where, at a glance. Each row shows the department's
 * own people and, for parents, everyone beneath it.
 */
@Component({
  selector: 'gggs-department-tree-page',
  imports: [PageHeader, KpiCard, EmptyState, Skeleton, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Organization structure" subtitle="Departments and the people in them.">
      <a class="btn" routerLink="../departments">Manage departments</a>
    </gggs-page-header>

    <div class="kpi-row tree__kpis">
      <gggs-kpi-card label="Departments" icon="pi pi-share-alt" [value]="activeCount()" />
      <gggs-kpi-card label="People placed" icon="pi pi-users" [value]="peopleCount()" />
      <gggs-kpi-card label="Top-level units" icon="pi pi-sitemap" [value]="tree().length" />
    </div>

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else if (rows().length === 0) {
      <gggs-empty-state title="No departments yet" description="Create departments to see the structure here." />
    } @else {
      <ul class="tree" aria-label="Departments">
        @for (row of rows(); track row.node.id) {
          <li class="tree__row" [style.padding-inline-start.rem]="row.depth * 1.5 + 0.75" [class.tree__row--inactive]="!row.node.isActive">
            <span class="tree__name">
              <i [class]="row.node.children.length ? 'pi pi-folder-open' : 'pi pi-folder'" aria-hidden="true"></i>
              {{ row.node.name }} <span class="muted">{{ row.node.code }}</span>
            </span>
            <span class="tree__count" [attr.aria-label]="row.node.headcount + ' people'">{{ row.node.headcount }}</span>
            @if (row.node.children.length) {
              <span class="tree__total muted">{{ row.total }} incl. sub-departments</span>
            }
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .tree__kpis {
      margin-block-end: var(--gggs-space-4);
    }

    .tree {
      margin: 0;
      padding: 0;
      list-style: none;
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md);
      background: var(--gggs-color-surface-raised);
    }

    .tree__row {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);
      padding-block: var(--gggs-space-2);
      padding-inline-end: var(--gggs-space-3);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .tree__row:last-child {
      border-block-end: 0;
    }

    .tree__row--inactive {
      opacity: 0.6;
    }

    .tree__name {
      display: flex;
      flex: 1;
      align-items: center;
      gap: var(--gggs-space-2);
    }

    .tree__count {
      min-width: 2.5rem;
      padding: 0 var(--gggs-space-2);
      border-radius: 999px;
      background: var(--gggs-color-primary-subtle);
      color: var(--gggs-color-primary);
      font-weight: var(--gggs-font-weight-semibold);
      text-align: center;
    }

    .tree__total {
      font-size: var(--gggs-font-size-xs);
    }
  `,
})
export class DepartmentTreePage {
  private readonly api = inject(OrgStructureApi);

  protected readonly loading = signal(true);
  protected readonly tree = signal<readonly DepartmentNode[]>([]);

  protected readonly rows = computed<readonly TreeRow[]>(() => {
    const rows: TreeRow[] = [];
    const total = (node: DepartmentNode): number => node.headcount + node.children.reduce((sum, child) => sum + total(child), 0);
    const walk = (nodes: readonly DepartmentNode[], depth: number) => {
      for (const node of nodes) {
        rows.push({ node, depth, total: total(node) });
        walk(node.children, depth + 1);
      }
    };
    walk(this.tree(), 0);
    return rows;
  });

  protected readonly activeCount = computed(() => this.rows().filter((row) => row.node.isActive).length);
  protected readonly peopleCount = computed(() => this.rows().reduce((sum, row) => sum + row.node.headcount, 0));

  constructor() {
    void this.api.departmentTree().then(
      (tree) => {
        this.tree.set(tree);
        this.loading.set(false);
      },
      () => this.loading.set(false),
    );
  }
}
