import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiError } from '@gggs/core/http';
import { HasPermissionDirective } from '@gggs/core/permissions';
import type { NotificationTemplate } from '@gggs/shared/models';
import { EMAIL_MODULES } from '@gggs/shared/models';
import { NotificationsApi } from '@gggs/shared/notifications';
import { EmptyState, FormDialog, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

/**
 * The wording of every notification Nuts and Bolts sends, grouped by the module it belongs to, and the
 * channels it goes out on. The set of notifications is fixed - each is sent by some module - but what
 * they say is the organization's. Who the email comes from is the module's sender (Studio > Email
 * senders); one template may borrow another sender.
 */
@Component({
  selector: 'gggs-notification-templates-page',
  imports: [ReactiveFormsModule, RouterLink, HasPermissionDirective, PageHeader, FormDialog, EmptyState, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Email templates" subtitle="What each notification says, grouped by module, and whether it goes in-app, by email or by text message." />
    <p class="muted templates__note">Who each module's emails come from, with its signature and header colour, is set under <a routerLink="/studio/email-senders">Email senders</a>.</p>

    @if (loading()) {
      <gggs-skeleton [lines]="6" />
    } @else if (templates().length === 0) {
      <gggs-empty-state title="No templates" />
    } @else {
      <div class="templates__modules" role="group" aria-label="Module">
        <button type="button" class="templates__pill" [class.templates__pill--on]="!module()" [attr.aria-pressed]="!module()" (click)="module.set(null)">All · {{ templates().length }}</button>
        @for (entry of modules(); track entry.key) {
          <button type="button" class="templates__pill" [class.templates__pill--on]="module() === entry.key" [attr.aria-pressed]="module() === entry.key" (click)="module.set(entry.key)">
            {{ entry.name }} · {{ entry.count }}
          </button>
        }
      </div>

      <section class="panel templates__panel">
        <div class="templates__scroll">
          <table class="table">
            <thead>
              <tr>
                <th scope="col">Notification</th>
                <th scope="col">Module</th>
                <th scope="col">Subject</th>
                <th scope="col">In-app</th>
                <th scope="col">Email</th>
                <th scope="col">Text</th>
                <th scope="col">Status</th>
                <th scope="col"><span class="templates__hidden">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (template of shown(); track template.id) {
                <tr>
                  <td>{{ template.name }} <span class="muted"><code>{{ template.code }}</code></span></td>
                  <td>
                    {{ moduleName(template.moduleKey) }}
                    @if (template.senderKey) {
                      <span class="muted templates__small">Sends as {{ senderName(template.senderKey) }}</span>
                    }
                  </td>
                  <td>{{ template.subject }}</td>
                  <td>{{ template.sendInApp ? 'Yes' : 'No' }}</td>
                  <td>{{ template.sendEmail ? 'Yes' : 'No' }}</td>
                  <td>{{ template.sendSms ? 'Yes' : 'No' }}</td>
                  <td><gggs-status-badge [status]="template.isActive ? 'Active' : 'Inactive'" /></td>
                  <td>
                    <button type="button" class="btn btn--link btn--sm" *gggsHasPermission="'notification.template.manage'" (click)="openEdit(template)">Edit</button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    }
    @if (listError()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ listError() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="dialogOpen" [title]="editing()?.name ?? 'Edit notification'" size="lg" [busy]="busy()" [error]="error()" (save)="save()">
      <div class="form-grid" [formGroup]="form">
        <label class="field form-grid__full">
          <span class="field__label">Sender</span>
          <select formControlName="senderKey">
            <option value="">Its module's sender ({{ moduleName(editing()?.moduleKey ?? 'other') }})</option>
            <option value="default">The default sender</option>
            @for (entry of allModules; track entry.key) {
              @if (entry.key !== editing()?.moduleKey) {
                <option [value]="entry.key">{{ entry.name }}'s sender</option>
              }
            }
          </select>
          <span class="field__hint">Leave it on the module's sender unless this one email must come from someone else.</span>
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Subject *</span>
          <input type="text" formControlName="subject" maxlength="300" />
        </label>
        <label class="field form-grid__full">
          <span class="field__label">Message *</span>
          <textarea rows="6" formControlName="body" maxlength="4000"></textarea>
          <span class="field__hint">Placeholders in double braces are filled in when it is sent; {{ recipientPlaceholder }} is always available. The sender's signature is added below the message.</span>
        </label>
        <label class="field">
          <span><input type="checkbox" formControlName="sendInApp" /> In-app</span>
        </label>
        <label class="field">
          <span><input type="checkbox" formControlName="sendEmail" /> Email</span>
        </label>
        <label class="field">
          <span><input type="checkbox" formControlName="sendSms" /> Text message</span>
          <span class="field__hint">Sends the message above, once a gateway is chosen under Studio, Text messages. Each one costs money and reaches a personal phone.</span>
        </label>
        <label class="field">
          <span><input type="checkbox" formControlName="isActive" /> Send this notification</span>
        </label>
      </div>
    </gggs-form-dialog>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--gggs-space-4); }
    .templates__note { margin: 0; }
    .templates__modules { display: flex; flex-wrap: wrap; gap: var(--gggs-space-2); }
    .templates__pill { min-height: 2.75rem; padding: 0 var(--gggs-space-4); border-radius: 999px; border: 1px solid var(--gggs-color-border); background: var(--gggs-color-surface); color: var(--gggs-color-text); font: inherit; cursor: pointer; }
    .templates__pill--on { background: var(--gggs-color-text); border-color: var(--gggs-color-text); color: var(--gggs-color-surface); font-weight: var(--gggs-font-weight-semibold); }
    .templates__panel { padding: 0; overflow: hidden; }
    .templates__scroll { overflow-x: auto; }
    .templates__small { display: block; font-size: var(--gggs-font-size-sm); }
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
  protected readonly allModules = EMAIL_MODULES;
  protected readonly templates = signal<readonly NotificationTemplate[]>([]);
  protected readonly module = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly listError = signal<string | null>(null);
  protected readonly dialogOpen = signal(false);
  protected readonly editing = signal<NotificationTemplate | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The modules that have templates, in Studio's order, with how many each has. */
  protected readonly modules = computed(() =>
    EMAIL_MODULES.map((entry) => ({ ...entry, count: this.templates().filter((template) => template.moduleKey === entry.key).length })).filter((entry) => entry.count > 0),
  );
  protected readonly shown = computed(() => {
    const module = this.module();
    return module ? this.templates().filter((template) => template.moduleKey === module) : this.templates();
  });

  protected readonly form = new FormGroup({
    senderKey: new FormControl('', { nonNullable: true }),
    subject: new FormControl('', { nonNullable: true, validators: Validators.required }),
    body: new FormControl('', { nonNullable: true, validators: Validators.required }),
    sendInApp: new FormControl(true, { nonNullable: true }),
    sendEmail: new FormControl(true, { nonNullable: true }),
    sendSms: new FormControl(false, { nonNullable: true }),
    isActive: new FormControl(true, { nonNullable: true }),
  });

  constructor() {
    void this.load();
  }

  protected moduleName(key: string): string {
    return EMAIL_MODULES.find((entry) => entry.key === key)?.name ?? 'Everything else';
  }

  protected senderName(key: string): string {
    return key === 'default' ? 'the default sender' : `${this.moduleName(key)}'s sender`;
  }

  protected openEdit(template: NotificationTemplate): void {
    this.editing.set(template);
    this.form.reset({
      senderKey: template.senderKey ?? '',
      subject: template.subject,
      body: template.body,
      sendInApp: template.sendInApp,
      sendEmail: template.sendEmail,
      sendSms: template.sendSms,
      isActive: template.isActive,
    });
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
      const value = this.form.getRawValue();
      await this.api.updateTemplate(editing.id, { ...value, senderKey: value.senderKey || null });
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
