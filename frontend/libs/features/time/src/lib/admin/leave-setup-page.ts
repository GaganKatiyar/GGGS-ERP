import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import type { LeaveType } from '@gggs/shared/models';
import { LeaveApi } from '@gggs/shared/time';
import { FormDialog, PageHeader, StatusBadge } from '@gggs/shared/ui';

/**
 * The leave types master (spec §20): the kinds of leave that exist, paid or not, in half days or
 * whole. The rules each follows live in leave policies, which HR assigns per engagement type.
 */
@Component({
  selector: 'gggs-leave-setup-page',
  imports: [RouterLink, HasPermissionDirective, PageHeader, StatusBadge, FormDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Leave types" subtitle="The kinds of leave that exist. Their rules are set in leave policies.">
      <div class="toolbar">
        <a class="btn" routerLink="../policies">Leave policies</a>
        <button type="button" class="btn" *gggsHasPermission="'leave.policy.manage'" (click)="openAccrual()">Run accrual</button>
      </div>
    </gggs-page-header>

    <div class="stack">
      <section class="panel stack">
        <div class="toolbar">
          <h2 class="setup__heading">Leave types</h2>
          <button type="button" class="btn btn--primary btn--sm" *gggsHasPermission="'leave.type.manage'" (click)="openType(null)">Add type</button>
        </div>
        <table class="table grid">
          <thead>
            <tr>
              <th scope="col">Type</th>
              <th scope="col">Paid</th>
              <th scope="col">Half days</th>
              <th scope="col">Status</th>
              <th scope="col"><span class="grid__hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (type of types(); track type.id) {
              <tr>
                <td><span class="setup__dot" [style.background]="type.color" aria-hidden="true"></span> <strong>{{ type.name }}</strong> <span class="muted">{{ type.code }}</span></td>
                <td>{{ type.isPaid ? 'Paid' : 'Unpaid' }}</td>
                <td>{{ type.allowHalfDay ? 'Allowed' : 'No' }}</td>
                <td><gggs-status-badge [status]="type.isActive ? 'Active' : 'Inactive'" /></td>
                <td class="grid__actions" *gggsHasPermission="'leave.type.manage'">
                  <button type="button" class="btn btn--link btn--sm" (click)="openType(type)">Edit</button>
                  <button type="button" class="btn btn--link btn--sm" (click)="toggleType(type)">{{ type.isActive ? 'Retire' : 'Restore' }}</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </section>

      @if (message()) {
        <p class="setup__message" role="status">{{ message() }}</p>
      }
      @if (error()) {
        <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
      }
    </div>

    <gggs-form-dialog [(visible)]="typeOpen" [title]="editingType() ? 'Edit leave type' : 'Add leave type'" [busy]="busy()" [error]="dialogError()" (save)="saveType()">
      <div class="form-grid">
        <label class="field">
          <span class="field__label">Code *</span>
          <input type="text" maxlength="16" [disabled]="!!editingType()" [value]="typeCode()" (input)="typeCode.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Name *</span>
          <input type="text" maxlength="100" [value]="typeName()" (input)="typeName.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span class="field__label">Colour</span>
          <input type="color" [value]="typeColor()" (input)="typeColor.set($any($event.target).value)" />
        </label>
        <label class="field">
          <span><input type="checkbox" [checked]="typePaid()" (change)="typePaid.set($any($event.target).checked)" /> Paid</span>
        </label>
        <label class="field">
          <span><input type="checkbox" [checked]="typeHalf()" (change)="typeHalf.set($any($event.target).checked)" /> Half days allowed</span>
        </label>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="accrualOpen" title="Run accrual" saveLabel="Run" [busy]="busy()" [error]="dialogError()" (save)="runAccrual()">
      <div class="stack">
        <p class="muted">Accrual runs by itself on the first of each month, by each employee's leave policy. Running a month again adds nothing that was already credited.</p>
        <label class="field">
          <span class="field__label">Month</span>
          <input type="month" [value]="accrualMonth()" (input)="accrualMonth.set($any($event.target).value)" />
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .setup__heading {
      margin-inline-end: auto;
      margin-block: 0;
      font-size: var(--gggs-font-size-md);
    }

    .setup__dot {
      display: inline-block;
      width: 0.6rem;
      height: 0.6rem;
      border-radius: 50%;
    }

    .setup__message {
      color: var(--gggs-color-success);
    }

    .grid__actions {
      white-space: nowrap;
    }

    .grid__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class LeaveSetupPage {
  private readonly api = inject(LeaveApi);

  protected readonly types = signal<readonly LeaveType[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly dialogError = signal<string | null>(null);

  protected readonly typeOpen = signal(false);
  protected readonly editingType = signal<LeaveType | null>(null);
  protected readonly typeCode = signal('');
  protected readonly typeName = signal('');
  protected readonly typeColor = signal('#1E88E5');
  protected readonly typePaid = signal(true);
  protected readonly typeHalf = signal(true);

  protected readonly accrualOpen = signal(false);
  protected readonly accrualMonth = signal(new Date().toISOString().slice(0, 7));

  constructor() {
    void this.load();
  }

  protected openType(type: LeaveType | null): void {
    this.editingType.set(type);
    this.typeCode.set(type?.code ?? '');
    this.typeName.set(type?.name ?? '');
    this.typeColor.set(type?.color ?? '#1E88E5');
    this.typePaid.set(type?.isPaid ?? true);
    this.typeHalf.set(type?.allowHalfDay ?? true);
    this.dialogError.set(null);
    this.typeOpen.set(true);
  }

  protected async saveType(): Promise<void> {
    if (!this.typeCode().trim() || !this.typeName().trim()) {
      this.dialogError.set('Give the type a code and a name.');
      return;
    }
    await this.inDialog(async () => {
      await this.api.saveType(this.editingType()?.id ?? null, {
        code: this.typeCode().trim(),
        name: this.typeName().trim(),
        color: this.typeColor(),
        isPaid: this.typePaid(),
        allowHalfDay: this.typeHalf(),
      });
      this.typeOpen.set(false);
    });
  }

  protected async toggleType(type: LeaveType): Promise<void> {
    await this.inPage(() => this.api.setTypeActive(type.id, !type.isActive));
  }

  protected openAccrual(): void {
    this.dialogError.set(null);
    this.accrualOpen.set(true);
  }

  protected async runAccrual(): Promise<void> {
    const [year, month] = this.accrualMonth().split('-').map(Number);
    await this.inDialog(async () => {
      const result = await this.api.runAccrual(year, month);
      this.accrualOpen.set(false);
      this.message.set(`Accrual ${result.period}: ${result.accrued} credit(s), ${result.lapsed} lapse(s).`);
    });
  }

  private async inDialog(call: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.dialogError.set(null);
    try {
      await call();
      await this.load();
    } catch (error) {
      this.dialogError.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private async inPage(call: () => Promise<void>): Promise<void> {
    this.error.set(null);
    try {
      await call();
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'That did not work. Try again.');
    }
  }

  private async load(): Promise<void> {
    try {
      this.types.set(await this.api.types(true));
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'Leave types could not be loaded.');
    }
  }
}
