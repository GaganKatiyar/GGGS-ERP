import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { BoardPage } from './portfolio/board-page';
import { MyTasksPage } from './portfolio/my-tasks-page';
import { ChangeRequestsPage, MilestonesPage, RaidPage, SprintsPage } from './portfolio/registers-pages';
import { ProjectSettingsPage } from './project-settings-page';
import { ProjectsPage } from './projects-page';
import { ProjectWorkspacePage } from './workspace/project-workspace-page';

/** Lazily loaded by the shell at `/projects/*`. The cross-project registers come before `:id`. */
export const PROJECTS_ROUTES: Routes = [
  { path: '', component: ProjectsPage, title: 'Projects' },
  { path: 'tasks', component: MyTasksPage, title: 'Tasks', ...withPermissions(['project.task.view']) },
  { path: 'board', component: BoardPage, title: 'Board', ...withPermissions(['project.task.view']) },
  { path: 'milestones', component: MilestonesPage, title: 'Milestones', ...withPermissions(['project.milestone.view']) },
  { path: 'sprints', component: SprintsPage, title: 'Sprints', ...withPermissions(['project.sprint.view']) },
  { path: 'change-requests', component: ChangeRequestsPage, title: 'Change requests', ...withPermissions(['project.change-request.view']) },
  { path: 'raid', component: RaidPage, title: 'RAID log', ...withPermissions(['project.raid.view']) },
  { path: 'settings', component: ProjectSettingsPage, title: 'Project settings', ...withPermissions(['project.settings.manage']) },
  { path: ':id', component: ProjectWorkspacePage, title: 'Project' },
];
