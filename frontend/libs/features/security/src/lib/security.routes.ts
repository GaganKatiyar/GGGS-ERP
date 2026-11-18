import type { Routes } from '@angular/router';
import { IdentityProvidersPage } from './identity-providers-page';
import { PasswordPolicyPage } from './password-policy-page';

/** Lazily loaded by the shell at `/admin/identity-providers`. */
export const IDENTITY_PROVIDER_ROUTES: Routes = [{ path: '', component: IdentityProvidersPage, title: 'Single sign-on' }];

/** Lazily loaded by the shell at `/admin/password-policy`. */
export const PASSWORD_POLICY_ROUTES: Routes = [{ path: '', component: PasswordPolicyPage, title: 'Password policy' }];
