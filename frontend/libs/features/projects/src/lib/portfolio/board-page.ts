import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ProjectsApi } from '@gggs/shared/delivery';
import type { ProjectDetail, ProjectItem } from '@gggs/shared/models';
import { EmptyState, PageHeader } from '@gggs/shared/ui';
import { ProjectBoard } from '../workspace/project-board';

/** The board for any project in reach, chosen from a picker (spec §45). */
@Component({
  selector: 'gggs-board-page',
  imports: [RouterLink, PageHeader, EmptyState, ProjectBoard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Board" subtitle="Move work across a project's columns.">
      @if (detail(); as d) {
        <a class="btn" [routerLink]="['/projects', d.project.id]">Open {{ d.project.code }}</a>
      }
    </gggs-page-header>

    <section class="panel toolbar board-page__picker">
      <label class="field board-page__project">
        <span class="field__label">Project</span>
        <select [value]="projectId() ?? ''" (change)="choose($any($event.target).value)">
          <option value="">Choose a project</option>
          @for (project of projects(); track project.id) {
            <option [value]="project.id">{{ project.code }} · {{ project.name }}</option>
          }
        </select>
      </label>
    </section>

    @if (detail(); as d) {
      <gggs-project-board [projectId]="d.project.id" [canEdit]="d.can.manageTasks" />
    } @else {
      <gggs-empty-state icon="pi pi-th-large" title="Choose a project to see its board" />
    }
  `,
  styles: `
    .board-page__picker {
      margin-block-end: var(--gggs-space-3);
    }

    .board-page__project {
      min-width: 20rem;
    }
  `,
})
export class BoardPage {
  private readonly api = inject(ProjectsApi);
  private readonly router = inject(Router);

  /** Bound from the `project` query parameter. */
  readonly project = input<string | undefined>(undefined);

  protected readonly projects = signal<readonly ProjectItem[]>([]);
  protected readonly projectId = signal<string | null>(null);
  protected readonly detail = signal<ProjectDetail | null>(null);

  constructor() {
    void this.api.list({ pageSize: 200, sort: 'name' }).then(async (page) => {
      this.projects.set(page.items);
      const initial = this.project() ?? (page.items.length === 1 ? page.items[0].id : undefined);
      if (initial) {
        await this.load(initial);
      }
    });
  }

  protected async choose(id: string): Promise<void> {
    void this.router.navigate([], { queryParams: { project: id || null }, replaceUrl: true });
    if (id) {
      await this.load(id);
    } else {
      this.projectId.set(null);
      this.detail.set(null);
    }
  }

  private async load(id: string): Promise<void> {
    this.projectId.set(id);
    this.detail.set(await this.api.get(id));
  }
}
