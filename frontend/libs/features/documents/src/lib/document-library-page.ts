import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HasPermissionDirective, PermissionStore } from '@gggs/core/permissions';
import { DocumentsApi, EmployeeDocuments, documentContentUrl } from '@gggs/shared/documents';
import { EmployeeSelect } from '@gggs/shared/employee-reference';
import type { DocumentItem } from '@gggs/shared/models';
import { EmptyState, PageHeader, StatusBadge } from '@gggs/shared/ui';

/**
 * The document library: pick a person to see and manage their documents, and - for those with
 * organization-wide access - what is about to expire.
 */
@Component({
  selector: 'gggs-document-library-page',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, HasPermissionDirective, PageHeader, EmployeeSelect, EmployeeDocuments, EmptyState, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Documents" subtitle="Identity proofs, certificates, letters and contracts.">
      <div class="toolbar">
        <a class="btn" routerLink="templates" *gggsHasPermission="'document.template.manage'"><i class="pi pi-file-edit" aria-hidden="true"></i> Letter templates</a>
        <a class="btn" routerLink="types" *gggsHasPermission="'document.type.manage'"><i class="pi pi-tags" aria-hidden="true"></i> Document types</a>
      </div>
    </gggs-page-header>

    <div class="stack">
      <section class="panel">
        <label class="field library__picker">
          <span class="field__label">Employee</span>
          <gggs-employee-select [formControl]="employee" ariaLabel="Employee" placeholder="Search for a person" />
        </label>
      </section>

      @if (employee.value; as employeeId) {
        <gggs-employee-documents [employeeId]="employeeId" [canUpload]="canUpload" [canArchive]="canArchive" />
      } @else {
        <gggs-empty-state icon="pi pi-search" title="Choose someone" description="Search for an employee to see their documents." />
      }

      @if (tenantWide) {
        <section class="panel stack">
          <h2 class="library__heading">Expiring within 30 days</h2>
          @if (expiring().length === 0) {
            <p class="muted">Nothing is about to expire.</p>
          } @else {
            <table class="library__table">
              <thead>
                <tr>
                  <th scope="col">Document</th>
                  <th scope="col">Belongs to</th>
                  <th scope="col">Expires</th>
                  <th scope="col"><span class="library__hidden">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (document of expiring(); track document.id) {
                  <tr>
                    <td>{{ document.title }} <span class="muted">· {{ document.typeName }}</span></td>
                    <td>
                      @if (document.ownerId) {
                        <a [routerLink]="['/employees', document.ownerId]">{{ document.ownerName ?? 'Employee' }}</a>
                      } @else {
                        Organization
                      }
                    </td>
                    <td>
                      @if (document.isExpired) {
                        <gggs-status-badge status="Expired" />
                      } @else {
                        {{ document.expiresOn | date: 'mediumDate' }}
                      }
                    </td>
                    <td><a class="btn btn--link btn--sm" [href]="contentUrl(document)" download>Download</a></td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .library__picker {
      max-width: 28rem;
    }

    .library__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .library__table {
      width: 100%;
      border-collapse: collapse;
    }

    .library__table th,
    .library__table td {
      padding: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
    }

    .library__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class DocumentLibraryPage {
  private readonly api = inject(DocumentsApi);
  private readonly permissions = inject(PermissionStore);

  protected readonly canUpload = this.permissions.has('document.file.upload');
  protected readonly canArchive = this.permissions.has('document.file.delete');
  protected readonly tenantWide = this.permissions.scopeFor('document.file.view') === 'Tenant';

  protected readonly employee = new FormControl<string | null>(null);
  protected readonly expiring = signal<readonly DocumentItem[]>([]);

  constructor() {
    if (this.tenantWide) {
      void this.api.list({ expiringWithinDays: 30, pageSize: 100 }).then(
        (page) => this.expiring.set(page.items),
        () => this.expiring.set([]),
      );
    }
  }

  protected contentUrl(document: DocumentItem): string {
    return documentContentUrl(document.id, document.currentVersion);
  }
}
