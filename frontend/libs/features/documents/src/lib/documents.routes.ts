import type { Routes } from '@angular/router';
import { withPermissions } from '@gggs/core/permissions';
import { DocumentLibraryPage } from './document-library-page';
import { DocumentTemplatesPage } from './document-templates-page';
import { DocumentTypesPage } from './document-types-page';
import { MyDocumentsPage } from './my-documents-page';

/** Lazily loaded by the shell at `/documents/*`. */
export const DOCUMENTS_ROUTES: Routes = [
  { path: '', component: DocumentLibraryPage, title: 'Documents' },
  { path: 'types', component: DocumentTypesPage, title: 'Document types', ...withPermissions(['document.type.manage']) },
  { path: 'templates', component: DocumentTemplatesPage, title: 'Letter templates', ...withPermissions(['document.template.manage']) },
];

/** Mounted at `/me/documents`: self-service. */
export const MY_DOCUMENTS_ROUTES: Routes = [{ path: '', component: MyDocumentsPage, title: 'My documents' }];
