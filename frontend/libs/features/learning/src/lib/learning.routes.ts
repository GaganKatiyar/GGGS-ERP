import type { Routes } from '@angular/router';
import { CourseListPage } from './courses/course-list-page';
import { EmployeeEnrollmentsPage } from './enrollments/employee-enrollments-page';

/** Lazily loaded by the shell at `/learning/*`. */
export const LEARNING_ROUTES: Routes = [
  { path: '', component: CourseListPage, title: 'Courses' },
  { path: 'enrollments', component: EmployeeEnrollmentsPage, title: 'Enrollments' },
];
