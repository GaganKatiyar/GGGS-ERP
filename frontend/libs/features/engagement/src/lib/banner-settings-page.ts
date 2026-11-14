import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { FeatureBanner, FeatureBannerApi } from '@gggs/shared/experience';
import type { BannerKind, BannerLayout, FeatureBannerInput, FeatureBannerItem, MyFeatureBanner, OrgUnit } from '@gggs/shared/models';
import { BANNER_KINDS } from '@gggs/shared/models';
import { OrgStructureApi } from '@gggs/shared/org-reference';
import { EmptyState, FormDialog, localIsoDate, PageHeader, Skeleton, StatusBadge } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

const STATUS_VARIANTS = { live: 'success', scheduled: 'primary', draft: 'neutral', ended: 'neutral' } as const;

/** What the picker accepts; the API checks the bytes again. */
const IMAGE_ACCEPT = 'image/jpeg,image/png,image/webp';
/** Wider than the banner ever shows it on a high-density screen, and well under the API's 3 MB limit. */
const IMAGE_MAX_WIDTH = 1600;

/** Scales a chosen picture down in the browser, the right way up. Rejects when the file is not a picture it can read. */
async function scalePicture(file: File): Promise<Blob> {
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  const scale = Math.min(1, IMAGE_MAX_WIDTH / bitmap.width);
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  canvas.getContext('2d')!.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  return new Promise((resolve, reject) => canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('No picture'))), 'image/jpeg', 0.85));
}

const blank = (position: number): FeatureBannerInput => ({
  title: '',
  body: null,
  kind: 'Announcement',
  actionLabel: null,
  actionLink: null,
  secondaryLabel: null,
  secondaryLink: null,
  startsOn: localIsoDate(),
  endsOn: null,
  position,
  audienceDepartmentIds: [],
  audienceLocationIds: [],
  layout: 'FullPicture',
});

/** Settings > Dashboard banner: the slides on everyone's home dashboard, their dates and audience. */
@Component({
  selector: 'gggs-banner-settings-page',
  imports: [DatePipe, EmptyState, FeatureBanner, FormDialog, PageHeader, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Dashboard banner" subtitle="The slides people see at the top of their home dashboard, for the dates and people you choose.">
      <button type="button" class="btn btn--primary" (click)="create()"><i class="pi pi-plus" aria-hidden="true"></i> New slide</button>
    </gggs-page-header>

    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    @if (slides(); as rows) {
      @if (rows.length) {
        <section class="panel banner-table">
          <table>
            <thead>
              <tr>
                <th scope="col">Order</th>
                <th scope="col">Slide</th>
                <th scope="col">Shows</th>
                <th scope="col">Audience</th>
                <th scope="col">Status</th>
                <th scope="col" class="actions"><span class="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows; track row.id) {
                <tr>
                  <td>{{ row.position }}</td>
                  <td>
                    <div class="banner-table__slide">
                      @if (row.imageUrl) {
                        <img class="banner-table__thumb" [src]="row.imageUrl" alt="" />
                      }
                      <div>
                        <strong>{{ row.title }}</strong>
                        <div class="muted">{{ row.layout === 'FullPicture' ? 'Full picture' : kindLabel(row.kind) }}</div>
                      </div>
                    </div>
                  </td>
                  <td>
                    {{ row.startsOn | date: 'd MMM y' }}
                    @if (row.endsOn) {
                      to {{ row.endsOn | date: 'd MMM y' }}
                    } @else {
                      onward
                    }
                  </td>
                  <td>{{ audience(row) }}</td>
                  <td><gggs-status-badge [status]="status(row)" [variants]="statusVariants" /></td>
                  <td class="actions">
                    <div class="toolbar">
                      <button type="button" class="btn btn--sm" (click)="edit(row)">Edit</button>
                      <button type="button" class="btn btn--sm" [disabled]="busy()" (click)="togglePublished(row)">{{ row.isPublished ? 'Unpublish' : 'Publish' }}</button>
                      <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Delete ' + row.title" (click)="deleting.set(row); deleteOpen.set(true)"><i class="pi pi-trash" aria-hidden="true"></i></button>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </section>
      } @else {
        <section class="panel">
          <gggs-empty-state icon="pi pi-megaphone" title="No slides yet" description="Until you publish a slide, the dashboard shows built-in tips about the product. Your first published slide replaces them." />
        </section>
      }
    } @else if (!error()) {
      <gggs-skeleton [lines]="4" />
    }

    <gggs-form-dialog [(visible)]="formOpen" [title]="editingId() ? 'Edit slide' : 'New slide'" saveLabel="Save" [busy]="busy()" [error]="formError()" [saveDisabled]="!canSave()" (save)="save()" width="min(56rem, 96vw)">
      <div class="stack">
        <fieldset class="banner-types">
          <legend>Slide type</legend>
          @for (type of types; track type.value) {
            <label class="banner-type" [class.banner-type--on]="form().layout === type.value">
              <input type="radio" name="banner-layout" [value]="type.value" [checked]="form().layout === type.value" (change)="patch({ layout: type.value })" />
              <span><strong>{{ type.label }}</strong><span class="banner-type__hint">{{ type.hint }}</span></span>
            </label>
          }
        </fieldset>
        <gggs-feature-banner [slides]="preview()" [preview]="true" />
        <div class="xp__grid">
          @if (isFull()) {
            <label class="field xp__wide">
              <span class="field__label">What the picture shows</span>
              <input type="text" maxlength="120" placeholder="Diwali greetings from everyone at GGGS" [value]="form().title" (input)="patch({ title: $any($event.target).value })" />
              <span class="field__hint">Read out to people using a screen reader, and shown if the picture cannot load. It does not appear on the slide.</span>
            </label>
          } @else {
            <label class="field xp__wide">
              <span class="field__label">Headline</span>
              <input type="text" maxlength="120" [value]="form().title" (input)="patch({ title: $any($event.target).value })" />
            </label>
            <label class="field xp__wide">
              <span class="field__label">Sentence below it</span>
              <textarea rows="2" maxlength="300" [value]="form().body ?? ''" (input)="patch({ body: $any($event.target).value || null })"></textarea>
            </label>
          }
          <div class="field xp__wide">
            <label class="field__label" for="banner-picture">{{ isFull() ? 'Picture (required)' : 'Picture (optional)' }}</label>
            <div class="banner-picture">
              <input id="banner-picture" type="file" [accept]="imageAccept" (change)="choosePicture($event)" />
              @if (previewImage()) {
                <button type="button" class="btn btn--sm" (click)="removePicture()">Remove picture</button>
              }
            </div>
            @if (isFull()) {
              <p class="field__hint">JPEG, PNG or WebP. Best at 1800 × 470 pixels: other shapes are cropped to fit. A phone shows the picture smaller, so keep any words in it large and near the middle.</p>
            } @else {
              <p class="field__hint">JPEG, PNG or WebP. A wide picture works best, at least 1200 pixels across. Without one, the slide uses the drawing for what it is about.</p>
            }
          </div>
          @if (!isFull()) {
            <label class="field">
              <span class="field__label">About</span>
              <select [value]="form().kind" (change)="patch({ kind: $any($event.target).value })">
                @for (kind of kinds; track kind.value) {
                  <option [value]="kind.value" [selected]="kind.value === form().kind">{{ kind.label }}</option>
                }
              </select>
            </label>
          }
          <label class="field">
            <span class="field__label">Order (lower numbers come first)</span>
            <input type="number" step="1" [value]="form().position" (input)="patch({ position: +$any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">First day</span>
            <input type="date" [value]="form().startsOn" (change)="patch({ startsOn: $any($event.target).value })" />
          </label>
          <label class="field">
            <span class="field__label">Last day{{ !isFull() && form().kind === 'Deadline' ? ' (the due date)' : ' (optional)' }}</span>
            <input type="date" [value]="form().endsOn ?? ''" (change)="patch({ endsOn: $any($event.target).value || null })" />
          </label>
          @if (isFull()) {
            <label class="field xp__wide">
              <span class="field__label">Clicking the picture goes to (optional)</span>
              <input type="text" maxlength="300" placeholder="/me/leave or https://…" [value]="form().actionLink ?? ''" (input)="patch({ actionLink: $any($event.target).value || null })" />
            </label>
          } @else {
            <label class="field">
              <span class="field__label">Button wording</span>
              <input type="text" maxlength="40" placeholder="Apply for leave" [value]="form().actionLabel ?? ''" (input)="patch({ actionLabel: $any($event.target).value || null })" />
            </label>
            <label class="field">
              <span class="field__label">Button goes to</span>
              <input type="text" maxlength="300" placeholder="/me/leave or https://…" [value]="form().actionLink ?? ''" (input)="patch({ actionLink: $any($event.target).value || null })" />
            </label>
            <label class="field">
              <span class="field__label">Second link wording (optional)</span>
              <input type="text" maxlength="40" [value]="form().secondaryLabel ?? ''" (input)="patch({ secondaryLabel: $any($event.target).value || null })" />
            </label>
            <label class="field">
              <span class="field__label">Second link goes to</span>
              <input type="text" maxlength="300" placeholder="/calendar" [value]="form().secondaryLink ?? ''" (input)="patch({ secondaryLink: $any($event.target).value || null })" />
            </label>
          }
          <fieldset class="xp__card xp__wide">
            <legend>Who sees it (nothing ticked means everyone)</legend>
            <p class="muted">Departments</p>
            <div class="xp__grid">
              @for (department of departments(); track department.id) {
                <label class="xp__flag"><input type="checkbox" [checked]="form().audienceDepartmentIds.includes(department.id)" (change)="toggle('audienceDepartmentIds', department.id, $any($event.target).checked)" /> {{ department.name }}</label>
              }
            </div>
            <p class="muted">Locations</p>
            <div class="xp__grid">
              @for (location of locations(); track location.id) {
                <label class="xp__flag"><input type="checkbox" [checked]="form().audienceLocationIds.includes(location.id)" (change)="toggle('audienceLocationIds', location.id, $any($event.target).checked)" /> {{ location.name }}</label>
              }
            </div>
          </fieldset>
        </div>
      </div>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="deleteOpen" title="Delete this slide?" saveLabel="Delete" [danger]="true" [busy]="busy()" [error]="formError()" (save)="remove()">
      <p>"{{ deleting()?.title }}" leaves the dashboard for everyone. To take it down for now and keep it, use Unpublish instead.</p>
    </gggs-form-dialog>
  `,
  styles: `
    .banner-table {
      overflow-x: auto;
    }

    .banner-table__slide {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);
    }

    .banner-table__thumb {
      flex: none;
      width: 4rem;
      height: 2.5rem;
      border-radius: var(--gggs-radius-sm);
      object-fit: cover;
    }

    .banner-types {
      display: grid;
      gap: var(--gggs-space-3);
      grid-template-columns: repeat(auto-fit, minmax(min(15rem, 100%), 1fr));
      margin: 0;
      padding: 0;
      border: 0;
    }

    .banner-types legend {
      margin-block-end: var(--gggs-space-1);
      padding: 0;
      font-weight: var(--gggs-font-weight-medium);
    }

    .banner-type {
      display: flex;
      align-items: flex-start;
      gap: var(--gggs-space-3);
      padding: var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border-strong);
      border-radius: var(--gggs-radius-lg);
      cursor: pointer;
    }

    .banner-type--on {
      border-color: var(--gggs-color-primary);
      background: var(--gggs-color-primary-subtle);
      box-shadow: 0 0 0 1px var(--gggs-color-primary);
    }

    .banner-type strong {
      display: block;
    }

    .banner-type__hint {
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-sm);
    }

    .banner-picture {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--gggs-space-3);
    }
  `,
})
export class BannerSettingsPage {
  private readonly api = inject(FeatureBannerApi);

  protected readonly kinds = BANNER_KINDS;
  protected readonly types: readonly { readonly value: BannerLayout; readonly label: string; readonly hint: string }[] = [
    { value: 'FullPicture', label: 'Full picture', hint: 'The picture fills the slide. No text to write.' },
    { value: 'PictureWithText', label: 'Picture with text', hint: 'Headline, sentence and button beside a picture.' },
  ];
  protected readonly imageAccept = IMAGE_ACCEPT;
  /** The chosen picture, sent once the slide is saved; and whether to drop the one it has. */
  private readonly pendingImage = signal<{ readonly blob: Blob; readonly url: string } | null>(null);
  private readonly savedImage = signal<string | null>(null);
  private readonly dropImage = signal(false);
  protected readonly previewImage = computed(() => this.pendingImage()?.url ?? (this.dropImage() ? null : this.savedImage()));
  protected readonly isFull = computed(() => this.form().layout === 'FullPicture');
  /** A full picture needs its picture and a description of it; a slide with text needs its headline. */
  protected readonly canSave = computed(() => Boolean(this.form().title.trim()) && (!this.isFull() || Boolean(this.previewImage())));
  protected readonly statusVariants = STATUS_VARIANTS;
  protected readonly slides = signal<readonly FeatureBannerItem[] | null>(null);
  protected readonly departments = signal<readonly OrgUnit[]>([]);
  protected readonly locations = signal<readonly OrgUnit[]>([]);
  protected readonly formOpen = signal(false);
  protected readonly deleteOpen = signal(false);
  protected readonly deleting = signal<FeatureBannerItem | null>(null);
  protected readonly editingId = signal<string | null>(null);
  protected readonly form = signal<FeatureBannerInput>(blank(10));
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  /** The slide as it will look, with a placeholder until there is a headline. */
  protected readonly preview = computed<readonly MyFeatureBanner[]>(() => {
    const form = this.form();
    return [
      {
        id: 'preview',
        title: form.title.trim() || (form.layout === 'FullPicture' ? 'Choose a picture for this slide' : 'Your headline'),
        body: form.body,
        kind: form.kind,
        actionLabel: form.actionLabel,
        actionLink: form.actionLink,
        secondaryLabel: form.secondaryLabel,
        secondaryLink: form.secondaryLink,
        endsOn: form.endsOn,
        imageUrl: this.previewImage(),
        layout: form.layout,
      },
    ];
  });

  constructor() {
    const org = inject(OrgStructureApi);
    org.active('departments').then((rows) => this.departments.set(rows), () => undefined);
    org.active('locations').then((rows) => this.locations.set(rows), () => undefined);
    void this.load();
  }

  protected kindLabel(kind: BannerKind): string {
    return (BANNER_KINDS.find((option) => option.value === kind)?.label ?? kind).split(' (')[0]!;
  }

  protected audience(row: FeatureBannerItem): string {
    const named = [
      ...row.audienceDepartmentIds.map((id) => this.departments().find((unit) => unit.id === id)?.name),
      ...row.audienceLocationIds.map((id) => this.locations().find((unit) => unit.id === id)?.name),
    ].filter(Boolean);
    return row.audienceDepartmentIds.length + row.audienceLocationIds.length === 0 ? 'Everyone' : named.join(', ') || 'Selected people';
  }

  protected status(row: FeatureBannerItem): string {
    if (!row.isPublished) {
      return 'Draft';
    }

    const today = localIsoDate();
    if (row.startsOn > today) {
      return 'Scheduled';
    }

    return row.endsOn && row.endsOn < today ? 'Ended' : 'Live';
  }

  protected create(): void {
    const highest = Math.max(0, ...(this.slides() ?? []).map((row) => row.position));
    this.editingId.set(null);
    this.form.set(blank(highest + 10));
    this.resetPicture(null);
    this.formError.set(null);
    this.formOpen.set(true);
  }

  protected edit(row: FeatureBannerItem): void {
    this.editingId.set(row.id);
    this.resetPicture(row.imageUrl ?? null);
    this.form.set({
      title: row.title,
      body: row.body,
      kind: row.kind,
      actionLabel: row.actionLabel,
      actionLink: row.actionLink,
      secondaryLabel: row.secondaryLabel,
      secondaryLink: row.secondaryLink,
      startsOn: row.startsOn,
      endsOn: row.endsOn,
      position: row.position,
      audienceDepartmentIds: row.audienceDepartmentIds,
      audienceLocationIds: row.audienceLocationIds,
      layout: row.layout,
    });
    this.formError.set(null);
    this.formOpen.set(true);
  }

  protected async choosePicture(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    this.formError.set(null);
    try {
      const blob = await scalePicture(file);
      this.setPending({ blob, url: URL.createObjectURL(blob) });
      this.dropImage.set(false);
    } catch {
      input.value = '';
      this.formError.set('That file could not be read as a picture. Choose a JPEG, PNG or WebP file.');
    }
  }

  protected removePicture(): void {
    this.setPending(null);
    this.dropImage.set(true);
    const input = document.getElementById('banner-picture') as HTMLInputElement | null;
    if (input) {
      input.value = '';
    }
  }

  protected patch(change: Partial<FeatureBannerInput>): void {
    this.form.update((form) => ({ ...form, ...change }));
  }

  protected toggle(field: 'audienceDepartmentIds' | 'audienceLocationIds', id: string, on: boolean): void {
    this.form.update((form) => ({ ...form, [field]: on ? [...form[field], id] : form[field].filter((existing) => existing !== id) }));
  }

  protected async save(): Promise<void> {
    await this.run(async () => {
      // A published slide cannot become a full picture until it has one, so an existing slide gets its picture first.
      const pending = this.pendingImage();
      const existing = this.editingId();
      if (existing && pending) {
        await this.api.uploadImage(existing, pending.blob, 'banner.jpg');
      }

      const saved = await this.api.save(existing, this.form());
      // A failed upload below leaves the slide saved: trying again must update it, not add another.
      this.editingId.set(saved.id);
      if (!existing && pending) {
        await this.api.uploadImage(saved.id, pending.blob, 'banner.jpg');
      } else if (!pending && this.dropImage() && this.savedImage()) {
        await this.api.removeImage(saved.id);
      }

      this.resetPicture(null);
      this.formOpen.set(false);
    }, this.formError, 'The slide could not be saved.');
  }

  protected async togglePublished(row: FeatureBannerItem): Promise<void> {
    await this.run(() => this.api.setPublished(row.id, !row.isPublished), this.error, 'The slide could not be changed.');
  }

  protected async remove(): Promise<void> {
    const row = this.deleting();
    if (row) {
      await this.run(async () => {
        await this.api.delete(row.id);
        this.deleteOpen.set(false);
      }, this.formError, 'The slide could not be deleted.');
    }
  }

  private setPending(next: { readonly blob: Blob; readonly url: string } | null): void {
    const current = this.pendingImage();
    if (current) {
      URL.revokeObjectURL(current.url);
    }

    this.pendingImage.set(next);
  }

  private resetPicture(saved: string | null): void {
    this.setPending(null);
    this.savedImage.set(saved);
    this.dropImage.set(false);
    const input = document.getElementById('banner-picture') as HTMLInputElement | null;
    if (input) {
      input.value = '';
    }
  }

  private async run(action: () => Promise<unknown>, errors: typeof this.error, fallback: string): Promise<void> {
    this.busy.set(true);
    errors.set(null);
    try {
      await action();
      await this.load();
    } catch (error) {
      errors.set(message(error, fallback));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.slides.set(await this.api.list());
    } catch (error) {
      this.error.set(message(error, 'The slides could not be loaded.'));
    }
  }
}
