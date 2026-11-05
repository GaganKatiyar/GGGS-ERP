import { formatDate } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, LOCALE_ID, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MyPhotoStore, myPhotoUrl } from '@gggs/core/auth';
import { ApiError } from '@gggs/core/http';
import { PermissionStore } from '@gggs/core/permissions';
import { EmployeeDirectoryApi } from '@gggs/shared/employee-reference';
import type { Address, EmployeeProfile } from '@gggs/shared/models';
import { EmptyState, Skeleton, StatusBadge, Tabs, type TabItem } from '@gggs/shared/ui';
import { PhotoCropDialog } from './photo-crop-dialog';
import { PHOTO_ACCEPT, cropPhoto, decodePhoto, type SquareCrop } from './profile-photo';
import { initialsOf, nextAnniversary, profileCompleteness, tenureLabel, type ProfileGap } from './profile-summary';
import { EducationTab } from './tabs/education-tab';
import { ExperienceTab } from './tabs/experience-tab';
import { HistoryTab } from './tabs/history-tab';
import { MyDocumentsTab } from './tabs/my-documents-tab';
import { PersonalTab } from './tabs/personal-tab';
import { ProfileCoverTiles, type CoverTile } from './profile-cover-tiles';

interface Field {
  readonly label: string;
  readonly value: string | null | undefined;
}

/** How many missing details the side card lists before "and N more". */
const GAPS_SHOWN = 5;

/** The ring's circumference at r = 32, for the completeness meter's dash. */
const RING = 2 * Math.PI * 32;

/**
 * The signed-in employee's own record, read through the self-service route - it needs no employee
 * permission, only an account linked to an employee. The photo is the employee's own to change
 * (self.profile.edit); the other details are edited by HR, through the same dialogs as Employee 360.
 */
@Component({
  selector: 'gggs-my-profile-page',
  imports: [StatusBadge, Skeleton, EmptyState, Tabs, PersonalTab, ProfileCoverTiles, EducationTab, ExperienceTab, HistoryTab, MyDocumentsTab, PhotoCropDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error()) {
      <gggs-empty-state icon="pi pi-user" title="No employee record" [description]="error()!" />
    } @else if (!profile()) {
      <gggs-skeleton [lines]="8" />
    } @else {
      @let p = profile()!;
      @let done = completeness();
      <section class="profile-hero elevated rise-in" aria-label="Profile summary">
        <div class="profile-hero__cover">
          <span class="profile-hero__shape profile-hero__shape--a" aria-hidden="true"></span>
          <span class="profile-hero__shape profile-hero__shape--b" aria-hidden="true"></span>
          <span class="profile-hero__shape profile-hero__shape--c" aria-hidden="true"></span>
          <gggs-profile-cover-tiles [tiles]="coverTiles()" />
        </div>

        <div class="profile-hero__body">
          <div class="profile-photo">
            <div class="profile-photo__ring" [class.profile-photo__ring--busy]="photoBusy()">
              <div class="profile-photo__frame">
                @if (photoUrl(); as url) {
                  <img class="profile-photo__image" [src]="url" [alt]="'Photo of ' + p.displayName" width="512" height="512" />
                } @else {
                  <span class="profile-photo__initials" aria-hidden="true">{{ initials() }}</span>
                }
              </div>
            </div>
            @if (canEditPhoto) {
              <button type="button" class="profile-photo__camera" [attr.aria-label]="p.photoId ? 'Change photo' : 'Add photo'" [title]="p.photoId ? 'Change photo' : 'Add photo'" [disabled]="photoBusy()" (click)="photoInput.click()">
                <i class="pi" [class.pi-camera]="!photoBusy()" [class.pi-spin]="photoBusy()" [class.pi-spinner]="photoBusy()" aria-hidden="true"></i>
              </button>
            }
          </div>

          <div class="profile-hero__identity">
            <div class="profile-hero__title">
              <h1>{{ p.displayName }}</h1>
              <gggs-status-badge [status]="p.status" />
            </div>
            <p class="profile-hero__role">{{ roleLine() }}</p>
            <ul class="profile-hero__chips" aria-label="At a glance">
              <li><i class="pi pi-id-card" aria-hidden="true"></i>{{ p.employeeCode }}</li>
              @if (p.assignment.location; as location) {
                <li><i class="pi pi-map-marker" aria-hidden="true"></i>{{ location.name }}</li>
              }
              <li><i class="pi pi-building" aria-hidden="true"></i>{{ p.assignment.workMode.label }}</li>
              <li><i class="pi pi-briefcase" aria-hidden="true"></i>{{ p.assignment.employmentType.label }}</li>
            </ul>
          </div>

          <div class="profile-hero__actions">
            @if (canEditPhoto) {
              <button #photoButton type="button" class="btn" [disabled]="photoBusy()" (click)="photoInput.click()">
                <i class="pi pi-camera" aria-hidden="true"></i> {{ p.photoId ? 'Change photo' : 'Add photo' }}
              </button>
              @if (p.photoId) {
                <button type="button" class="btn btn--ghost" [disabled]="photoBusy()" (click)="removePhoto()">Remove photo</button>
              }
            }
            @if (canEditDetails) {
              <button type="button" class="btn btn--primary" (click)="editor.openPersonal()">
                <i class="pi pi-pencil" aria-hidden="true"></i> Edit personal details
              </button>
            }
          </div>
          <!-- Opened by the photo buttons; hidden from the tab order, since they are its keyboard route. -->
          <input #photoInput type="file" class="sr-only" tabindex="-1" aria-hidden="true" [accept]="photoAccept" (change)="onPhotoChosen($event)" />
          <gggs-photo-crop-dialog [picture]="cropping()" [busy]="photoBusy()" [error]="cropError()" (confirmed)="usePhoto($event)" (cancelled)="closeCrop()" />
        </div>

        @if (notice(); as message) {
          <p class="alert alert--success profile-hero__message" role="status">{{ message }}</p>
        }
        @if (photoError(); as message) {
          <p class="alert alert--danger profile-hero__message" role="alert">{{ message }}</p>
        }
      </section>

      <div class="profile-layout">
        <section class="elevated profile-details" aria-label="Details">
          <div class="profile-details__bar">
            <gggs-tabs [tabs]="sections" [(selected)]="section" />
            @if (canEditDetails && (section() === 'personal' || section() === 'contact')) {
              <button type="button" class="btn btn--sm profile-details__edit" (click)="section() === 'personal' ? editor.openPersonal() : editor.openContact()">
                <i class="pi pi-pencil" aria-hidden="true"></i> Edit
              </button>
            }
          </div>

          <div role="tabpanel" [attr.aria-label]="sectionLabel()">
            @switch (section()) {
              @case ('education') {
                <gggs-education-tab [editable]="canEditPhoto" [openOnStart]="addingEducation()" (counted)="educationCounted($event)" />
              }
              @case ('experience') {
                <gggs-experience-tab [editable]="canEditPhoto" />
              }
              @case ('history') {
                <gggs-history-tab />
              }
              @case ('documents') {
                <gggs-my-documents-tab />
              }
              @default {
            <dl class="profile-fields">
              @for (field of fields(); track field.label) {
                <div class="profile-field">
                  <dt>{{ field.label }}</dt>
                  <dd>
                    @if (field.value) {
                      {{ field.value }}
                    } @else {
                      <span class="profile-field__empty">Not added</span>
                    }
                  </dd>
                </div>
              }
            </dl>
            <p class="profile-details__hint"><i class="pi pi-info-circle" aria-hidden="true"></i>{{ hint() }}</p>
              }
            }
          </div>
        </section>

        <aside class="profile-side">
          <section class="elevated profile-complete" aria-labelledby="profile-complete-title">
            <div class="profile-complete__head">
              <svg class="profile-ring" viewBox="0 0 80 80" role="img" [attr.aria-label]="done.percent + '% complete'">
                <circle class="profile-ring__track" cx="40" cy="40" r="32" />
                @if (done.percent > 0) {
                  <circle class="profile-ring__value" cx="40" cy="40" r="32" [attr.stroke-dasharray]="ringDash()" transform="rotate(-90 40 40)" />
                }
                <text x="40" y="46" text-anchor="middle">{{ done.percent }}%</text>
              </svg>
              <div>
                <h2 id="profile-complete-title">{{ done.gaps.length ? 'Complete your profile' : 'Your profile is complete' }}</h2>
                <p>
                  {{ done.gaps.length ? 'HR uses these for benefits, emergencies and statutory forms.' : 'Everything HR needs from you is here.' }}
                </p>
              </div>
            </div>
            @if (done.gaps.length) {
              <ul class="profile-complete__list">
                @for (gap of done.gaps.slice(0, gapsShown); track gap.label) {
                  <li>
                    <span class="profile-complete__dot" aria-hidden="true"></span>
                    <span class="profile-complete__label">{{ gap.label }}</span>
                    @if (canFill(gap)) {
                      <button type="button" class="btn btn--link btn--sm" [attr.aria-label]="'Add ' + gap.label.toLowerCase()" (click)="fill(gap, editor, photoInput)">Add</button>
                    }
                  </li>
                }
              </ul>
              @if (done.gaps.length > gapsShown) {
                <p class="profile-complete__more">and {{ done.gaps.length - gapsShown }} more</p>
              }
              @if (hrOnlyGaps()) {
                <p class="profile-complete__more">{{ canFillAny() ? 'Ask HR to add the details without an Add button.' : 'Ask HR to add these for you.' }}</p>
              }
            }
          </section>

          <section class="elevated profile-line" aria-labelledby="profile-line-title">
            <h2 id="profile-line-title">Reporting line</h2>
            @if (p.assignment.manager; as manager) {
              <div class="profile-line__person">
                <span class="profile-avatar" aria-hidden="true">{{ initialsOf(manager.name) }}</span>
                <span><strong>{{ manager.name }}</strong><span class="profile-line__meta">Manager</span></span>
              </div>
              <span class="profile-line__link" aria-hidden="true"></span>
            }
            <div class="profile-line__person profile-line__person--you">
              <span class="profile-avatar profile-avatar--you" aria-hidden="true">
                @if (photoUrl(); as url) {
                  <img [src]="url" alt="" width="40" height="40" />
                } @else {
                  {{ initials() }}
                }
              </span>
              <span><strong>You</strong><span class="profile-line__meta">{{ p.assignment.designation ?? p.assignment.job?.name ?? p.displayName }}</span></span>
            </div>
            @if (!p.assignment.manager) {
              <p class="profile-line__meta">No manager is on record.</p>
            }
          </section>
        </aside>
      </div>

      <gggs-personal-tab #editor [profile]="p" [panels]="false" (changed)="load()" />
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-5);
    }

    .profile-hero {
      overflow: hidden;
      border-radius: var(--gggs-radius-xl);
    }

    .profile-hero__cover {
      position: relative;
      min-height: 8.5rem;
      overflow: hidden;
      background: var(--gggs-gradient-brand);
    }

    /* Three outlined planes at different depths: light catching glass, not decoration to read. */
    .profile-hero__shape {
      position: absolute;
      border: 1px solid color-mix(in srgb, #fff 16%, transparent);
      background: linear-gradient(160deg, color-mix(in srgb, #fff 12%, transparent), transparent 70%);
    }

    .profile-hero__shape--a {
      top: -4rem;
      right: 8%;
      width: 14rem;
      height: 14rem;
      border-radius: 2.75rem;
      transform: rotate(28deg);
    }

    .profile-hero__shape--b {
      top: 2.5rem;
      right: 24%;
      width: 8.5rem;
      height: 8.5rem;
      border-radius: 2rem;
      transform: rotate(14deg);
      background: linear-gradient(160deg, color-mix(in srgb, var(--gggs-color-accent-bright) 35%, transparent), transparent 70%);
    }

    .profile-hero__shape--c {
      top: 4.5rem;
      right: -2.5rem;
      width: 10rem;
      height: 10rem;
      border-radius: 50%;
      background: none;
    }

    .profile-hero__body {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: var(--gggs-space-4) var(--gggs-space-6);
      padding: 0 var(--gggs-space-6) var(--gggs-space-5);
    }

    .profile-photo {
      position: relative;
      flex: 0 0 auto;
      margin-block-start: -4.5rem;
    }

    .profile-photo__ring {
      width: 8.75rem;
      height: 8.75rem;
      padding: 5px;
      box-sizing: border-box;
      border-radius: 50%;
      background: linear-gradient(145deg, var(--gggs-color-accent-bright), var(--gggs-color-primary) 55%, var(--gggs-color-primary-hover));
      box-shadow: 0 22px 40px -14px color-mix(in srgb, var(--gggs-color-primary) 60%, transparent), var(--gggs-shadow-2);
    }

    .profile-photo__ring--busy {
      opacity: 0.6;
    }

    .profile-photo__frame {
      display: grid;
      place-items: center;
      width: 100%;
      height: 100%;
      overflow: hidden;
      box-sizing: border-box;
      border: 4px solid var(--gggs-color-surface-raised);
      border-radius: 50%;
      background: var(--gggs-gradient-tile);
    }

    .profile-photo__image,
    .profile-avatar img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    .profile-photo__initials {
      color: var(--gggs-color-primary);
      font-family: var(--gggs-font-family-display);
      font-size: 2.75rem;
      font-weight: var(--gggs-font-weight-bold);
    }

    .profile-photo__camera {
      position: absolute;
      right: 0.25rem;
      bottom: 0.375rem;
      display: grid;
      place-items: center;
      width: 2.75rem;
      height: 2.75rem;
      padding: 0;
      border: 3px solid var(--gggs-color-surface-raised);
      border-radius: 50%;
      background: var(--gggs-color-text);
      color: var(--gggs-color-surface-raised);
      box-shadow: var(--gggs-shadow-2);
      cursor: pointer;

      &:focus-visible {
        outline: none;
        box-shadow: var(--gggs-focus-ring);
      }

      &:disabled {
        cursor: progress;
      }
    }

    .profile-hero__identity {
      display: flex;
      flex: 1 1 20rem;
      flex-direction: column;
      gap: var(--gggs-space-2);
      min-width: 0;
      padding-block-start: var(--gggs-space-4);
    }

    .profile-hero__title {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--gggs-space-3);

      h1 {
        margin: 0;
        font-family: var(--gggs-font-family-display);
        font-size: var(--gggs-font-size-2xl);
        font-weight: var(--gggs-font-weight-bold);
        letter-spacing: -0.02em;
        line-height: 1.15;
      }
    }

    .profile-hero__role {
      margin: 0;
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-md);
    }

    .profile-hero__chips {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
      margin: var(--gggs-space-1) 0 0;
      padding: 0;
      list-style: none;

      li {
        display: inline-flex;
        align-items: center;
        gap: var(--gggs-space-2);
        min-height: 1.875rem;
        padding: 0 var(--gggs-space-3);
        border-radius: var(--gggs-radius-md);
        background: var(--gggs-color-surface-sunken);
        box-shadow: inset 0 0 0 1px var(--gggs-color-border);
        font-size: var(--gggs-font-size-sm);
      }

      .pi {
        color: var(--gggs-color-text-muted);
        font-size: var(--gggs-font-size-xs);
      }
    }

    .profile-hero__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--gggs-space-2);
    }

    .profile-hero__message {
      margin: 0 var(--gggs-space-6) var(--gggs-space-5);
    }

    .profile-layout {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      gap: var(--gggs-space-5);
    }

    .profile-side h2 {
      margin: 0;
      font-family: var(--gggs-font-family-display);
      font-size: var(--gggs-font-size-lg);
    }

    .profile-details {
      flex: 999 1 34rem;
      min-width: 0;
      padding: var(--gggs-space-2) var(--gggs-space-5) var(--gggs-space-5);
    }

    .profile-details__bar {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);

      gggs-tabs {
        flex: 1 1 auto;
      }
    }

    .profile-details__edit {
      flex: 0 0 auto;
      margin-block-end: var(--gggs-space-4);
    }

    .profile-fields {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(14rem, 100%), 1fr));
      gap: var(--gggs-space-1) var(--gggs-space-3);
      margin: 0;
    }

    .profile-field {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-1);
      padding: var(--gggs-space-3);

      dt {
        color: var(--gggs-color-text-muted);
        font-size: var(--gggs-font-size-xs);
        font-weight: var(--gggs-font-weight-semibold);
      }

      dd {
        margin: 0;
        font-size: var(--gggs-font-size-md);
        font-weight: var(--gggs-font-weight-semibold);
        overflow-wrap: anywhere;
      }
    }

    .profile-field__empty {
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-base);
      font-style: italic;
      font-weight: var(--gggs-font-weight-regular);
    }

    .profile-details__hint {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-2);
      margin: var(--gggs-space-4) 0 0;
      padding: var(--gggs-space-3);
      border-radius: var(--gggs-radius-lg);
      background: var(--gggs-color-surface-sunken);
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-sm);
    }

    .profile-side {
      display: flex;
      flex: 1 1 18rem;
      flex-direction: column;
      gap: var(--gggs-space-5);
      min-width: 0;
    }

    .profile-complete,
    .profile-line {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-3);
      padding: var(--gggs-space-5);
    }

    .profile-complete__head {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-4);

      p {
        margin: var(--gggs-space-1) 0 0;
        color: var(--gggs-color-text-muted);
        font-size: var(--gggs-font-size-sm);
      }
    }

    .profile-ring {
      flex: 0 0 4rem;
      width: 4rem;
      height: 4rem;

      circle {
        fill: none;
        stroke-width: 8;
      }

      text {
        fill: var(--gggs-color-text);
        font-family: var(--gggs-font-family-display);
        font-size: 1.125rem;
        font-weight: var(--gggs-font-weight-bold);
      }
    }

    .profile-ring__track {
      stroke: var(--gggs-color-surface-sunken);
    }

    .profile-ring__value {
      stroke: var(--gggs-color-primary);
      stroke-linecap: round;
    }

    .profile-complete__list {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-1);
      margin: 0;
      padding: 0;
      list-style: none;

      li {
        display: flex;
        align-items: center;
        gap: var(--gggs-space-3);
        min-height: 2.75rem;
        padding: 0 var(--gggs-space-1) 0 var(--gggs-space-3);
        border-radius: var(--gggs-radius-md);
        background: var(--gggs-color-surface-sunken);
      }
    }

    .profile-complete__dot {
      width: 0.5rem;
      height: 0.5rem;
      border-radius: 50%;
      background: var(--gggs-color-primary);
    }

    .profile-complete__label {
      flex: 1 1 auto;
      font-weight: var(--gggs-font-weight-medium);
    }

    .profile-complete__more {
      margin: 0;
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-sm);
    }

    .profile-line {
      gap: 0;

      h2 {
        margin-block-end: var(--gggs-space-3);
      }
    }

    .profile-line__person {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);

      strong,
      .profile-line__meta {
        display: block;
      }
    }

    .profile-line__person--you {
      margin-inline: calc(-1 * var(--gggs-space-2));
      padding: var(--gggs-space-2);
      border-radius: var(--gggs-radius-lg);
      background: var(--gggs-color-primary-subtle);
    }

    .profile-line__meta {
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-sm);
    }

    .profile-line__link {
      width: 2px;
      height: 1.125rem;
      margin-inline-start: 1.1875rem;
      background: linear-gradient(180deg, var(--gggs-color-border), var(--gggs-color-primary));
    }

    .profile-avatar {
      display: grid;
      flex: 0 0 2.5rem;
      place-items: center;
      width: 2.5rem;
      height: 2.5rem;
      overflow: hidden;
      border-radius: 50%;
      background: var(--gggs-gradient-tile);
      box-shadow: var(--gggs-shadow-2), inset 0 0 0 1px color-mix(in srgb, var(--gggs-color-primary) 22%, transparent);
      color: var(--gggs-color-primary);
      font-size: var(--gggs-font-size-sm);
      font-weight: var(--gggs-font-weight-bold);
    }

    @media (max-width: 40rem) {
      .profile-hero__body {
        padding-inline: var(--gggs-space-4);
      }

      .profile-hero__actions {
        width: 100%;
      }
    }
  `,
})
export class MyProfilePage {
  private readonly api = inject(EmployeeDirectoryApi);
  private readonly photos = inject(MyPhotoStore);
  private readonly locale = inject(LOCALE_ID);
  private readonly permissions = inject(PermissionStore);

  protected readonly profile = signal<EmployeeProfile | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly photoError = signal<string | null>(null);
  protected readonly photoBusy = signal(false);
  /** The picture being cropped; the crop window is open while there is one. */
  protected readonly cropping = signal<ImageBitmap | null>(null);
  protected readonly cropError = signal<string | null>(null);
  private readonly injector = inject(Injector);
  private readonly photoButton = viewChild<ElementRef<HTMLElement>>('photoButton');
  /** What had focus when the picture was chosen, so closing the crop window can put it back. */
  private photoOpener: HTMLElement | null = null;

  /** The photo is the employee's own; everything else on the page is HR's to edit. */
  protected readonly canEditPhoto = this.permissions.has('self.profile.edit');
  protected readonly canEditDetails = this.permissions.has('employee.profile.edit');

  protected readonly photoAccept = PHOTO_ACCEPT;
  protected readonly gapsShown = GAPS_SHOWN;
  protected readonly initialsOf = initialsOf;

  protected readonly sections: readonly TabItem[] = [
    { key: 'work', label: 'Work' },
    { key: 'personal', label: 'Personal' },
    { key: 'contact', label: 'Contact' },
    { key: 'bank', label: 'Bank & tax' },
    { key: 'education', label: 'Education' },
    { key: 'experience', label: 'Experience' },
    { key: 'history', label: 'History' },
    ...(this.permissions.has('self.document.view') && this.permissions.hasFeature('Documents') ? [{ key: 'documents', label: 'Documents' }] : []),
  ];
  /** A home-page nudge can open a tab directly: /me/profile?section=education. */
  protected readonly section = signal(this.initialSection());
  /** How many education entries there are; unknown until read, and then counted towards completeness. */
  private readonly educationCount = signal<number | undefined>(undefined);
  /** Set when "Add" on the completeness card should open the education dialog as the tab appears. */
  protected readonly addingEducation = signal(false);

  protected readonly photoUrl = computed(() => {
    const id = this.profile()?.photoId;
    return id ? myPhotoUrl(id) : null;
  });
  protected readonly initials = computed(() => initialsOf(this.profile()?.displayName));
  protected readonly completeness = computed(() => profileCompleteness(this.profile()!, this.educationCount()));
  /** Service, manager, anniversary and completeness: the cover's tiles. */
  protected readonly coverTiles = computed<readonly CoverTile[]>(() => {
    const p = this.profile()!;
    const anniversary = this.anniversary();
    const done = this.completeness();
    return [
      { label: 'Length of service', value: this.tenure(), hint: `Since ${this.date(p.joiningDate)}` },
      { label: 'Reports to', value: p.assignment.manager?.name ?? 'No manager', hint: p.assignment.department?.name },
      {
        label: 'Next anniversary',
        value: formatDate(anniversary.date, 'mediumDate', this.locale),
        hint: `${anniversary.years} ${anniversary.years === 1 ? 'year' : 'years'} · ${anniversary.when}`,
      },
      { label: 'Profile', value: `${done.percent}% complete`, progress: done.percent },
    ];
  });
  protected readonly ringDash = computed(() => `${(this.completeness().percent / 100) * RING} ${RING}`);
  protected readonly tenure = computed(() => tenureLabel(this.profile()!.joiningDate, new Date()));
  protected readonly anniversary = computed(() => nextAnniversary(this.profile()!.joiningDate, new Date()));

  protected readonly roleLine = computed(() => {
    const assignment = this.profile()!.assignment;
    return [assignment.designation ?? assignment.job?.name, assignment.department?.name].filter(Boolean).join(' · ') || 'No role on record';
  });

  protected readonly sectionLabel = computed(() => this.sections.find((tab) => tab.key === this.section())?.label ?? '');

  protected readonly fields = computed<readonly Field[]>(() => {
    const p = this.profile()!;
    const a = p.assignment;
    switch (this.section()) {
      case 'personal':
        return [
          { label: 'Full name', value: [p.firstName, p.middleName, p.lastName].filter(Boolean).join(' ') },
          { label: 'Preferred name', value: p.preferredName },
          { label: 'Gender', value: p.gender?.label },
          { label: 'Date of birth', value: this.date(p.dateOfBirth) },
          { label: 'Marital status', value: p.maritalStatus?.label },
          { label: 'Nationality', value: p.nationality?.label },
          { label: 'Blood group', value: p.bloodGroup?.label },
        ];
      case 'contact':
        return [
          { label: 'Official email', value: p.officialEmail },
          { label: 'Personal email', value: p.personalEmail },
          { label: 'Mobile', value: p.mobilePhone },
          { label: 'Alternate phone', value: p.alternatePhone },
          { label: 'Current address', value: this.address(p.currentAddress) },
          { label: 'Permanent address', value: this.address(p.permanentAddress) },
        ];
      case 'bank':
        return [
          { label: 'PAN', value: p.sensitive.panHint },
          { label: 'UAN', value: p.sensitive.uanHint },
          { label: 'Bank', value: p.sensitive.bankName },
          { label: 'Bank account', value: p.sensitive.bankAccountHint },
        ];
      default:
        return [
          { label: 'Designation', value: a.designation ?? a.job?.name },
          { label: 'Department', value: a.department?.name },
          { label: 'Manager', value: a.manager?.name },
          { label: 'Location', value: a.location?.name },
          { label: 'Joined', value: this.date(p.joiningDate) },
          { label: 'Employment type', value: a.employmentType.label },
          { label: 'Work mode', value: a.workMode.label },
          { label: 'Employee code', value: p.employeeCode },
          ...(p.probationEndDate && !p.confirmationDate ? [{ label: 'Probation ends', value: this.date(p.probationEndDate) }] : []),
          ...(p.confirmationDate ? [{ label: 'Confirmed on', value: this.date(p.confirmationDate) }] : []),
          ...(p.contractEndDate ? [{ label: 'Contract ends', value: this.date(p.contractEndDate) }] : []),
        ];
    }
  });

  protected readonly hint = computed(() => {
    switch (this.section()) {
      case 'personal':
      case 'contact':
        return this.canEditDetails
          ? 'Keep these current so HR can reach you and fill in statutory forms.'
          : 'To add or correct anything here, contact HR.';
      case 'bank':
        return 'Shown masked for your safety. Only HR and Payroll see the full numbers.';
      default:
        return 'Work details come from HR. To correct anything here, contact HR.';
    }
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.cropping()?.close());
    void this.load();
    this.api.education().then((entries) => this.educationCount.set(entries.length), () => undefined);
  }

  protected educationCounted(count: number): void {
    this.educationCount.set(count);
    this.addingEducation.set(false);
  }

  protected async load(): Promise<void> {
    try {
      const profile = await this.api.me();
      this.profile.set(profile);
      this.photos.set(profile.photoId ?? null);
    } catch (error) {
      this.error.set(
        error instanceof ApiError && error.status === 404
          ? 'Your account is not linked to an employee record. Ask HR to link it.'
          : error instanceof ApiError
            ? error.message
            : 'Try again in a moment.',
      );
    }
  }

  protected async onPhotoChosen(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    // Cleared so choosing the same file again still fires a change.
    input.value = '';
    if (!file) {
      return;
    }

    this.notice.set(null);
    this.photoError.set(null);
    this.cropError.set(null);
    this.photoOpener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    try {
      this.cropping.set(await decodePhoto(file));
    } catch {
      this.photoError.set('That picture could not be used. Choose a JPEG, PNG or WebP photo.');
    }
  }

  /** Crops and uploads. A failure keeps the window open, so the person can retry without choosing the picture again. */
  protected async usePhoto(crop: SquareCrop): Promise<void> {
    const picture = this.cropping();
    if (!picture) {
      return;
    }

    this.photoBusy.set(true);
    this.cropError.set(null);
    try {
      const cropped = await cropPhoto(picture, crop);
      const photoId = await this.api.uploadMyPhoto(cropped, cropped.type === 'image/webp' ? 'photo.webp' : 'photo.jpg');
      this.setPhoto(photoId);
      this.closeCrop();
      this.notice.set('Your photo is updated.');
    } catch (error) {
      this.cropError.set(error instanceof ApiError ? error.message : 'The photo could not be saved. Try again.');
    } finally {
      this.photoBusy.set(false);
    }
  }

  /** Closes the crop window and lets go of the picture, which can be large. */
  protected closeCrop(): void {
    const picture = this.cropping();
    this.cropping.set(null);
    picture?.close();

    // Once rendered, because the photo buttons are disabled during an upload. "Add" on the
    // completeness card has gone by then if this added the photo, so the photo button stands in.
    const opener = this.photoOpener;
    afterNextRender(
      () => {
        const target = opener?.isConnected && opener !== document.body ? opener : this.photoButton()?.nativeElement;
        target?.focus();
      },
      { injector: this.injector },
    );
  }

  protected async removePhoto(): Promise<void> {
    if (!confirm('Remove your profile photo? Your initials will show instead.')) {
      return;
    }

    await this.runPhoto(async () => {
      await this.api.removeMyPhoto();
      this.setPhoto(null);
      return 'Your photo is removed.';
    }, 'The photo could not be removed. Try again.');
  }

  protected canFill(gap: ProfileGap): boolean {
    // The photo and education are the employee's own to add; the rest is HR's.
    return gap.section === 'photo' || gap.section === 'education' ? this.canEditPhoto : this.canEditDetails;
  }

  protected canFillAny(): boolean {
    return this.completeness().gaps.some((gap) => this.canFill(gap));
  }

  /** Whether some missing details are HR's to add - the employee needs telling who to ask. */
  protected hrOnlyGaps(): boolean {
    return this.completeness().gaps.some((gap) => !this.canFill(gap));
  }

  protected fill(gap: ProfileGap, editor: PersonalTab, photoInput: HTMLInputElement): void {
    if (gap.section === 'photo') {
      photoInput.click();
    } else if (gap.section === 'education') {
      this.addingEducation.set(true);
      this.section.set('education');
    } else if (gap.section === 'personal') {
      editor.openPersonal();
    } else {
      editor.openContact();
    }
  }

  private async runPhoto(change: () => Promise<string>, fallback: string): Promise<void> {
    this.photoBusy.set(true);
    this.notice.set(null);
    this.photoError.set(null);
    try {
      this.notice.set(await change());
    } catch (error) {
      this.photoError.set(error instanceof ApiError ? error.message : fallback);
    } finally {
      this.photoBusy.set(false);
    }
  }

  private setPhoto(photoId: string | null): void {
    this.profile.update((profile) => (profile ? { ...profile, photoId: photoId ?? undefined } : profile));
    this.photos.set(photoId);
  }

  private initialSection(): string {
    const asked = inject(ActivatedRoute).snapshot.queryParamMap.get('section');
    return asked && this.sections.some((tab) => tab.key === asked) ? asked : 'work';
  }

  private date(value: string | null | undefined): string | null {
    return value ? formatDate(value, 'mediumDate', this.locale) : null;
  }

  private address(address: Address | undefined): string | null {
    return address ? [address.line1, address.line2, address.city, address.stateCode, address.postalCode, address.countryCode].filter(Boolean).join(', ') || null : null;
  }
}
