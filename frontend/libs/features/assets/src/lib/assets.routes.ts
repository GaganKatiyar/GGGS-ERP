import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { AssetCategoryListPage } from './categories/asset-category-list-page';
import { AssetRegisterPage } from './register/asset-register-page';
import { MyAssetsPage } from './my-assets/my-assets-page';

/** Lazily loaded by the shell at `/assets/*`. */
export const ASSETS_ROUTES: Routes = [
  { path: '', component: AssetCategoryListPage, title: 'Asset categories' },
  {
    path: 'register',
    component: AssetRegisterPage,
    title: 'Assets',
    ...withPermissions(['asset.asset.view']),
  },
];

/** Mounted directly at `/me/assets` - see `NAVIGATION`'s "My Space" entry. */
export const MY_ASSETS_ROUTES: Routes = [{ path: '', component: MyAssetsPage, title: 'My assets' }];
