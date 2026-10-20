import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import type { NotificationTemplate } from '@gggs/shared/models';
import { NotificationsApi } from '@gggs/shared/notifications';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

/**
 * The wording of every notification PeopleOS sends, and the channels it goes out on. The set of
 * notifications is fixed - each is sent by some module - but what they say is the organization's.
 */
@Component({
  selector: 'gggs-notification-templates-page',
  imports: [ReactiveFormsModule, HasPermissionDirective, PageHeader, FormDialog, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Notifications" subtitle="What each notification says, and whether it goes in-app, by email, or both." />

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else if (templates().length === 0) {
      <gggs-empty-state title="No templates" />
    } @else {
      <section class="panel">
        <table class="templates__table">
          <thead>
            <tr>
              <th scope="col">Notification</th>
              <th scope="col">Subject</th>
              <th scope="col">In-app</th>
              <th scope="col">Email</th>
              <th scope="col">Status</th>
              <th scope="col"><span class="templates__hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (template of templates(); track template.id) {
              <tr>
                <td>{{ template.name }} <span class="muted"><code>{{ template.code }}</code></span></td>
                <td>{{ template.subject }}</td>
                <td>{{ template.sendInApp ? 'Yes' : 'No' }}</td>
                <td>{{ template.sendEmail ? 'Yes' : 'No' }}</td>
                <td><gggs-status-badge [status]="template.isActive ? 'Active' : 'Inactive'" /></td>
                <td>
                  <button type="button" class="btn btn--link btn--sm" *gggsHasPermission="'notification.template.manage'" (click)="openEdit(template)">Edit</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </section>
    }
    @if (listError()) {
      <p class="field__error" role="alert">{{ listError() }}</p>
    }

    <gggs-form-dialog [(visible)]="dialogOpen" [title]="editing()?.name ?? 'Edit notification'" size="lg" [busy]="busy()" [error]="error()" (save)="save()">
      <div class="form-grid" [formGroup]="form">
        <label class="field form-grid__full">
          <span class="field__label">Subject *</span>
          <input type="text" formControlName="subject" maxlength="300" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Message *</span>
          <textarea rows="6" formControlName="body" maxlength="4000"></textarea>
          <span class="field__hint">Placeholders in double braces are filled in when it is sent; {{ recipientPlaceholder }} is always available.</span>
        </label>
        <label class="field">
          <span><input type="checkbox" formControlName="sendInApp" /> In-app</span>
        </label>
        <label class="field">
          <span><input type="checkbox" formControlName="sendEmail" /> Email</span>
        </label>
        <label class="field">
          <span><input type="checkbox" formControlName="isActive" /> Send this notification</span>
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    .templates__table {
      width: 100%;
      border-collapse: collapse;
    }

    .templates__table th,
    .templates__table td {
      padding: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
      text-align: start;
    }

    .templates__hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
})
export class NotificationTemplatesPage {
  private readonly api = inject(NotificationsApi);

  protected readonly recipientPlaceholder = '{{recipientName}}';
  protected readonly templates = signal<readonly NotificationTemplate[]>([]);
  protected readonly loading = signal(true);
  protected readonly listError = signal<string | null>(null);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<NotificationTemplate | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    subject: new FormControl('', { nonNullable: true, validators: Validators.required }),
    body: new FormControl('', { nonNullable: true, validators: Validators.required }),
    sendInApp: new FormControl(true, { nonNullable: true }),
    sendEmail: new FormControl(true, { nonNullable: true }),
    isActive: new FormControl(true, { nonNullable: true }),
  });

  constructor() {
    void this.load();
  }

  protected openEdit(template: NotificationTemplate): void {
    this.editing.set(template);
    this.form.reset({ subject: template.subject, body: template.body, sendInApp: template.sendInApp, sendEmail: template.sendEmail, isActive: template.isActive });
    this.error.set(null);
    this.dialogOpen.set(true);
  }

  protected async save(): Promise<void> {
    const editing = this.editing();
    if (!editing || this.form.invalid) {
      this.error.set('Fill in the subject and the message.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.updateTemplate(editing.id, this.form.getRawValue());
      this.dialogOpen.set(false);
      await this.load();
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The notification could not be saved.');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.templates.set(await this.api.templates());
    } catch (error) {
      this.listError.set(error instanceof ApiError ? error.message : 'Notifications could not be loaded.');
    } finally {
      this.loading.set(false);
    }
  }
}
