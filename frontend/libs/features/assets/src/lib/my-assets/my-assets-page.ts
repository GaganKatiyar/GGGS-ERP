import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Card, PageHeader } from '@gggs/shared/ui';
import type { AssetAssignment } from '@gggs/shared/models';
import { AssetsApi } from '../data/assets-api';

/** Self-service, read-only: which assets are currently assigned to the signed-in employee.
 * Registering, assigning or returning an asset is always an admin action. */
@Component({
  selector: 'gggs-my-assets-page',
  imports: [PageHeader, Card],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My assets" subtitle="Equipment currently assigned to you." />

    <gggs-card title="Assigned to me">
      @if (assignments(); as items) {
        @if (items.length > 0) {
          <table class="assignments-table">
            <thead>
              <tr>
                <th scope="col">Asset</th>
                <th scope="col">Assigned</th>
              </tr>
            </thead>
            <tbody>
              @for (assignment of items; track assignment.id) {
                <tr>
                  <td>{{ assignment.assetId }}</td>
                  <td>{{ assignment.assignedDate }}</td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">No assets currently assigned to you.</p>
        }
      } @else {
        <p class="muted">Loading…</p>
      }
    </gggs-card>
  `,
  styles: `
    .assignments-table {
      width: 100%;
      border-collapse: collapse;
    }

    .assignments-table th,
    .assignments-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid var(--gggs-color-border, #e2e5ea);
      font-size: 0.875rem;
    }

    .muted {
      color: var(--gggs-color-text-muted, #5b6472);
      margin: 0;
    }
  `,
})
export class MyAssetsPage {
  private readonly api = inject(AssetsApi);

  protected readonly assignments = signal<readonly AssetAssignment[] | null>(null);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.assignments.set((await this.api.listMyAssignments()).filter((x) => x.returnedDate === null));
  }
}
