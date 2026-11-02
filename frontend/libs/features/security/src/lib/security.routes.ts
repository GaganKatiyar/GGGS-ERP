import type { Routes } from '@angular/router';
import { IdentityProvidersPage } from './identity-providers-page';

/** Lazily loaded by the shell at `/admin/identity-providers`. */
export const IDENTITY_PROVIDER_ROUTES: Routes = [{ path: '', component: IdentityProvidersPage, title: 'Single sign-on' }];
