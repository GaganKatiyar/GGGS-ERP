import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { MyRewardsPage, RewardsPage } from './rewards-pages';

/** Lazily loaded by the shell at `/rewards`. */
export const REWARDS_ROUTES: Routes = [
  { path: '', component: RewardsPage, title: 'Rewards', ...withPermissions(['reward.recognition.view', 'reward.program.manage', 'reward.catalog.manage', 'reward.redemption.manage']) },
];

/** Lazily loaded by the shell at `/me/rewards`. */
export const MY_REWARDS_ROUTES: Routes = [{ path: '', component: MyRewardsPage, title: 'My rewards' }];
