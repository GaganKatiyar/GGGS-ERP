import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { ComplianceDashboardPage, ComplianceFilingsPage, ComplianceRegistrationsPage, StatutoryRulesPage } from './compliance-pages';

/** Lazily loaded by the shell at `/compliance/*`. */
export const COMPLIANCE_ROUTES: Routes = [
  { path: '', component: ComplianceDashboardPage, title: 'Compliance' },
  { path: 'rules', component: StatutoryRulesPage, title: 'Statutory rules', ...withPermissions(['compliance.rule.view']) },
  { path: 'filings', component: ComplianceFilingsPage, title: 'Filing calendar', ...withPermissions(['compliance.filing.view']) },
  { path: 'registrations', component: ComplianceRegistrationsPage, title: 'Licences and certificates', ...withPermissions(['compliance.filing.view']) },
];
