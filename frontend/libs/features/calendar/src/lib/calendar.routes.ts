import type { Routes } from '@angular/router';
import { CalendarPage } from './calendar-page';

/** Lazily loaded by the shell at `/calendar`. */
export const CALENDAR_ROUTES: Routes = [{ path: '', component: CalendarPage, title: 'HR calendar' }];
