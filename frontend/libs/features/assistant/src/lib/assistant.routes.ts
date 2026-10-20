import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { AssistantPage } from './assistant-page';
import { UsagePage } from './usage-page';

/** Lazily loaded by the shell at `/ai`. */
export const ASSISTANT_ROUTES: Routes = [
  { path: '', component: AssistantPage, title: 'AI assistants' },
  { path: 'usage', ...withPermissions(['ai.usage.view']), component: UsagePage, title: 'Assistant usage' },
];
