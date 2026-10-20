import type { Routes } from '@angular/router';
import { IdentityProvidersPage } from './identity-providers-page';
import { MySecurityPage } from './my-security-page';

/** Lazily loaded by the shell at `/me/security`. */
export const MY_SECURITY_ROUTES: Routes = [{ path: '', component: MySecurityPage, title: 'Sign-in security' }];

/** Lazily loaded by the shell at `/admin/identity-providers`. */
export const IDENTITY_PROVIDER_ROUTES: Routes = [{ path: '', component: IdentityProvidersPage, title: 'Single sign-on' }];
