import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EmployeeDirectoryApi, profileCompleteness, type ProfileGap } from '@gggs/shared/employee-reference';
import type { EmployeeProfile } from '@gggs/shared/models';

interface Nudge {
  readonly section: ProfileGap['section'];
  readonly icon: string;
  readonly title: string;
  readonly text: string;
}

const NUDGES: readonly Nudge[] = [
  { section: 'photo', icon: 'pi pi-camera', title: 'Make your profile picture-perfect', text: 'Add a photo so colleagues recognise you across Nuts and Bolts.' },
  { section: 'personal', icon: 'pi pi-id-card', title: 'Enrich your personal details', text: 'HR needs these for benefits, emergencies and statutory forms.' },
  { section: 'contact', icon: 'pi pi-phone', title: 'Keep your contact details current', text: 'So HR can reach you, and payslips and letters find you.' },
  { section: 'education', icon: 'pi pi-book', title: 'Add your education', text: 'Your qualifications help HR match you to roles, learning and projects.' },
];

/** The cards shown at once; the rest wait until one is done. */
const SHOWN = 3;

/**
 * Up to three cards on the home page for what is still missing from the employee's own profile -
 * the same gaps My profile counts - each opening My profile. Nothing shows once it is complete.
 */
@Component({
  selector: 'gggs-profile-nudges',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (nudges().length) {
      <section class="nudges" aria-labelledby="nudges-title">
        <h2 id="nudges-title" class="nudges__title">Complete your profile <span class="muted">· {{ percent() }}% done</span></h2>
        <div class="nudges__grid">
          @for (nudge of nudges(); track nudge.section) {
            <a class="nudge elevated" routerLink="/me/profile" [queryParams]="nudge.section === 'education' ? { section: 'education' } : null">
              <span class="nudge__icon" aria-hidden="true"><i [class]="nudge.icon"></i></span>
              <strong>{{ nudge.title }}</strong>
              <span class="muted">{{ nudge.text }}</span>
              <span class="nudge__missing">Missing: {{ missing(nudge.section) }}</span>
            </a>
          }
        </div>
      </section>
    }
  `,
  styles: `
    .nudges {
      margin-block-start: var(--gggs-space-6);
    }

    .nudges__title {
      margin: 0 0 var(--gggs-space-3);
      font-size: var(--gggs-font-size-lg);
    }

    .nudges__grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(16rem, 100%), 1fr));
      gap: var(--gggs-space-4);
    }

    .nudge {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-2);
      padding: var(--gggs-space-5);
      color: inherit;
      text-decoration: none;
      transition: transform var(--gggs-transition), box-shadow var(--gggs-transition);

      &:hover {
        transform: translateY(var(--gggs-lift));
        box-shadow: var(--gggs-shadow-3);
      }
    }

    .nudge__icon {
      display: grid;
      place-items: center;
      width: 2.5rem;
      height: 2.5rem;
      border-radius: 50%;
      background: var(--gggs-gradient-tile);
      color: var(--gggs-color-primary);
    }

    .nudge__missing {
      margin-block-start: auto;
      color: var(--gggs-color-primary);
      font-size: var(--gggs-font-size-sm);
      font-weight: var(--gggs-font-weight-semibold);
    }
  `,
})
export class ProfileNudges {
  private readonly profile = signal<EmployeeProfile | null>(null);
  private readonly educationCount = signal<number | undefined>(undefined);
  private readonly completeness = computed(() => {
    const profile = this.profile();
    return profile ? profileCompleteness(profile, this.educationCount()) : null;
  });

  protected readonly percent = computed(() => this.completeness()?.percent ?? 0);
  protected readonly nudges = computed(() => NUDGES.filter((nudge) => this.completeness()?.gaps.some((gap) => gap.section === nudge.section)).slice(0, SHOWN));

  constructor() {
    // Quietly nothing if the profile cannot be read: a nudge is never worth an error on the home page.
    const api = inject(EmployeeDirectoryApi);
    void api.me().then((profile) => this.profile.set(profile), () => this.profile.set(null));
    void api.education().then((entries) => this.educationCount.set(entries.length), () => undefined);
  }

  protected missing(section: ProfileGap['section']): string {
    return (this.completeness()?.gaps ?? [])
      .filter((gap) => gap.section === section)
      .map((gap) => gap.label.toLowerCase())
      .join(', ');
  }
}
