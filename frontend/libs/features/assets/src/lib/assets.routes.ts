import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { AssetPage, AssetsPage, MyAssetsPage } from './assets-pages';

/** Lazily loaded by the shell at `/assets/*`. */
export const ASSETS_ROUTES: Routes = [
  { path: '', component: AssetsPage, title: 'Assets', ...withPermissions(['asset.asset.view']) },
  { path: ':id', component: AssetPage, title: 'Asset', ...withPermissions(['asset.asset.view']) },
];

/** Lazily loaded by the shell at `/me/assets`. */
export const MY_ASSETS_ROUTES: Routes = [{ path: '', component: MyAssetsPage, title: 'My assets' }];
