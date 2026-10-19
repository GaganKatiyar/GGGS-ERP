import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { CertificatesPage, CoursesPage, EnrollmentsPage, LearningDashboardPage, MyLearningPage } from './learning-pages';

/** Lazily loaded by the shell at `/learning/*`. */
export const LEARNING_ROUTES: Routes = [
  { path: '', component: LearningDashboardPage, title: 'Learning', ...withPermissions(['learning.enrollment.view']) },
  { path: 'courses', component: CoursesPage, title: 'Courses', ...withPermissions(['learning.course.view']) },
  { path: 'enrollments', component: EnrollmentsPage, title: 'Enrollments', ...withPermissions(['learning.enrollment.view']) },
  { path: 'certificates', component: CertificatesPage, title: 'Certificates', ...withPermissions(['learning.certification.view']) },
];

/** Lazily loaded by the shell at `/me/learning`. */
export const MY_LEARNING_ROUTES: Routes = [{ path: '', component: MyLearningPage, title: 'My learning' }];
