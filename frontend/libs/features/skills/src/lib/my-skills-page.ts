import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { SkillsApi } from '@gggs/shared/delivery';
import type { EmployeeSkillItem, SkillItem, SkillProfile } from '@gggs/shared/models';
import { SKILL_LEVEL_LABELS, humanize } from '@gggs/shared/models';
import { EmptyState, FormDialog, PageHeader, Skeleton } from '@gggs/shared/ui';

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback);

interface SkillForm {
  skillId: string;
  currentLevel: number;
  targetLevel: number | null;
  yearsExperience: number | null;
  isPrimary: boolean;
}

interface CertificationForm {
  name: string;
  issuer: string;
  credentialId: string;
  issuedOn: string;
  expiresOn: string;
  skillId: string;
}

/**
 * My skills (spec §60-61): the skills I rate myself on, with the level I am aiming for, and my
 * certifications. Resource managers match people to project requests from this.
 */
@Component({
  selector: 'gggs-my-skills-page',
  imports: [DatePipe, PageHeader, EmptyState, FormDialog, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="My skills" subtitle="Keep your skills current: they decide which projects you are matched to.">
      <button type="button" class="btn btn--primary" (click)="editSkill(null)"><i class="pi pi-plus" aria-hidden="true"></i> Add skill</button>
    </gggs-page-header>

    @if (profile(); as p) {
      <section class="panel stack">
        <h2 class="my-skills__heading">Skills</h2>
        @for (skill of p.skills; track skill.id) {
          <div class="my-skills__row">
            <span><strong>{{ skill.skillName }}</strong> <span class="muted">{{ skill.category }}</span>{{ skill.isPrimary ? ' · primary' : '' }}</span>
            <span class="my-skills__level" [attr.aria-label]="'Level ' + skill.currentLevel + ' of 5'">
              @for (dot of dots; track dot) {
                <span class="my-skills__dot" [class.my-skills__dot--on]="dot <= skill.currentLevel" [class.my-skills__dot--target]="dot > skill.currentLevel && dot <= (skill.targetLevel ?? 0)"></span>
              }
              {{ levels[skill.currentLevel] }}
            </span>
            <span class="muted">{{ skill.targetLevel ? 'aiming for ' + levels[skill.targetLevel] : '' }}{{ skill.yearsExperience ? ' · ' + skill.yearsExperience + ' yrs' : '' }}</span>
            <span class="muted">{{ humanize(skill.source) }} · {{ skill.lastAssessedOn | date: 'd MMM y' }}</span>
            <span>
              <button type="button" class="btn btn--link btn--sm" (click)="editSkill(skill)">Edit</button>
              <button type="button" class="btn btn--link btn--sm" (click)="removeSkill(skill)">Remove</button>
            </span>
          </div>
        } @empty {
          <gggs-empty-state icon="pi pi-bolt" title="No skills yet" description="Add the skills you work with and how strong you are in each." />
        }
      </section>

      <section class="panel stack">
        <div class="toolbar">
          <h2 class="my-skills__heading">Certifications</h2>
          <span class="my-skills__spacer"></span>
          <button type="button" class="btn btn--sm" (click)="openCertification()"><i class="pi pi-plus" aria-hidden="true"></i> Add certification</button>
        </div>
        @for (certification of p.certifications; track certification.id) {
          <div class="my-skills__row">
            <strong>{{ certification.name }}</strong>
            <span class="muted">{{ certification.issuer ?? '' }}{{ certification.credentialId ? ' · ' + certification.credentialId : '' }}</span>
            <span>{{ certification.issuedOn ? (certification.issuedOn | date: 'MMM y') : '' }}</span>
            <span [class.my-skills__expired]="!certification.isCurrent">{{ certification.expiresOn ? (certification.isCurrent ? 'expires ' : 'expired ') + (certification.expiresOn | date: 'd MMM y') : 'no expiry' }}</span>
            <span><button type="button" class="btn btn--link btn--sm" (click)="removeCertification(certification.id)">Remove</button></span>
          </div>
        } @empty {
          <p class="muted">No certifications recorded.</p>
        }
      </section>
    } @else if (!error()) {
      <gggs-skeleton [lines]="6" />
    }
    @if (error()) {
      <p class="alert alert--danger" role="alert"><i class="pi pi-exclamation-circle" aria-hidden="true"></i><span>{{ error() }}</span></p>
    }

    <gggs-form-dialog [(visible)]="skillOpen" [title]="skillForm().skillId && editingSkill() ? 'Edit skill' : 'Add skill'" [busy]="busy()" [error]="formError()" [saveDisabled]="!skillForm().skillId" (save)="saveSkill()">
      <label class="field">
        <span class="field__label">Skill</span>
        <select [value]="skillForm().skillId" [disabled]="editingSkill()" (change)="patchSkill({ skillId: $any($event.target).value })">
          <option value="">Choose a skill</option>
          @for (skill of available(); track skill.id) {
            <option [value]="skill.id">{{ skill.name }} ({{ skill.category }})</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Your level</span>
        <select [value]="skillForm().currentLevel" (change)="patchSkill({ currentLevel: +$any($event.target).value })">
          @for (dot of dots; track dot) {
            <option [value]="dot">{{ dot }} · {{ levels[dot] }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Level you are aiming for</span>
        <select [value]="skillForm().targetLevel ?? ''" (change)="patchSkill({ targetLevel: $any($event.target).value ? +$any($event.target).value : null })">
          <option value="">No target</option>
          @for (dot of dots; track dot) {
            <option [value]="dot">{{ dot }} · {{ levels[dot] }}</option>
          }
        </select>
      </label>
      <label class="field">
        <span class="field__label">Years of experience</span>
        <input type="number" min="0" max="50" step="0.5" [value]="skillForm().yearsExperience ?? ''" (input)="patchSkill({ yearsExperience: $any($event.target).value === '' ? null : +$any($event.target).value })" />
      </label>
      <label class="my-skills__flag">
        <input type="checkbox" [checked]="skillForm().isPrimary" (change)="patchSkill({ isPrimary: $any($event.target).checked })" />
        <span>One of my primary skills</span>
      </label>
    </gggs-form-dialog>

    <gggs-form-dialog [(visible)]="certificationOpen" title="Add certification" [busy]="busy()" [error]="formError()" [saveDisabled]="!certificationForm().name.trim()" (save)="saveCertification()">
      <label class="field">
        <span class="field__label">Name</span>
        <input type="text" maxlength="200" [value]="certificationForm().name" (input)="patchCertification('name', $any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Issuer</span>
        <input type="text" maxlength="200" [value]="certificationForm().issuer" (input)="patchCertification('issuer', $any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Credential ID</span>
        <input type="text" maxlength="100" [value]="certificationForm().credentialId" (input)="patchCertification('credentialId', $any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Issued on</span>
        <input type="date" [value]="certificationForm().issuedOn" (change)="patchCertification('issuedOn', $any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Expires on</span>
        <input type="date" [value]="certificationForm().expiresOn" (change)="patchCertification('expiresOn', $any($event.target).value)" />
      </label>
      <label class="field">
        <span class="field__label">Related skill</span>
        <select [value]="certificationForm().skillId" (change)="patchCertification('skillId', $any($event.target).value)">
          <option value="">None</option>
          @for (skill of catalog(); track skill.id) {
            <option [value]="skill.id">{{ skill.name }}</option>
          }
        </select>
      </label>
    </gggs-form-dialog>
  `,
  styles: `
    .my-skills__heading {
      margin: 0;
      font-size: var(--gggs-font-size-md);
    }

    .my-skills__spacer {
      flex: 1;
    }

    .my-skills__row {
      display: grid;
      grid-template-columns: minmax(0, 2fr) minmax(0, 1.6fr) minmax(0, 1.4fr) minmax(0, 1.4fr) 8rem;
      gap: var(--gggs-space-3);
      align-items: center;
      padding-block: var(--gggs-space-2);
      border-block-end: 1px solid var(--gggs-color-border);
    }

    .my-skills__level {
      display: inline-flex;
      gap: 3px;
      align-items: center;
    }

    .my-skills__dot {
      width: 0.6rem;
      height: 0.6rem;
      border-radius: 50%;
      background: var(--gggs-color-border);
    }

    .my-skills__dot--on {
      background: var(--gggs-color-primary);
    }

    .my-skills__dot--target {
      background: var(--gggs-color-primary-subtle);
      outline: 1px dashed var(--gggs-color-primary);
    }

    .my-skills__expired {
      color: var(--gggs-color-danger);
    }

    .my-skills__flag {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
    }

    @media (max-width: 56rem) {
      .my-skills__row {
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
      }
    }
  `,
})
export class MySkillsPage {
  private readonly api = inject(SkillsApi);

  protected readonly dots = [1, 2, 3, 4, 5];
  protected readonly levels = SKILL_LEVEL_LABELS;
  protected readonly humanize = humanize;
  protected readonly profile = signal<SkillProfile | null>(null);
  protected readonly catalog = signal<readonly SkillItem[]>([]);
  protected readonly skillOpen = signal(false);
  protected readonly editingSkill = signal(false);
  protected readonly skillForm = signal<SkillForm>({ skillId: '', currentLevel: 2, targetLevel: null, yearsExperience: null, isPrimary: false });
  protected readonly certificationOpen = signal(false);
  protected readonly certificationForm = signal<CertificationForm>({ name: '', issuer: '', credentialId: '', issuedOn: '', expiresOn: '', skillId: '' });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);

  /** Skills not yet on the profile - plus the one being edited. */
  protected readonly available = computed(() => {
    const held = new Set(this.profile()?.skills.map((skill) => skill.skillId) ?? []);
    return this.catalog().filter((skill) => !held.has(skill.id) || (this.editingSkill() && skill.id === this.skillForm().skillId));
  });

  constructor() {
    Promise.all([this.api.mine(), this.api.myCatalog()]).then(
      ([profile, catalog]) => {
        this.profile.set(profile);
        this.catalog.set(catalog);
      },
      (error: unknown) => this.error.set(error instanceof ApiError && error.status === 422 ? 'Your account is not linked to an employee record.' : message(error, 'Your skills could not be loaded.')),
    );
  }

  protected editSkill(skill: EmployeeSkillItem | null): void {
    this.editingSkill.set(!!skill);
    this.skillForm.set(
      skill
        ? { skillId: skill.skillId, currentLevel: skill.currentLevel, targetLevel: skill.targetLevel ?? null, yearsExperience: skill.yearsExperience ?? null, isPrimary: skill.isPrimary }
        : { skillId: '', currentLevel: 2, targetLevel: null, yearsExperience: null, isPrimary: false },
    );
    this.formError.set(null);
    this.skillOpen.set(true);
  }

  protected patchSkill(change: Partial<SkillForm>): void {
    this.skillForm.update((form) => ({ ...form, ...change }));
  }

  protected openCertification(): void {
    this.certificationForm.set({ name: '', issuer: '', credentialId: '', issuedOn: '', expiresOn: '', skillId: '' });
    this.formError.set(null);
    this.certificationOpen.set(true);
  }

  protected patchCertification(field: keyof CertificationForm, value: string): void {
    this.certificationForm.update((form) => ({ ...form, [field]: value }));
  }

  protected async saveSkill(): Promise<void> {
    const form = this.skillForm();
    await this.save(() => this.api.assessMine({ ...form, source: 'Self' }), () => this.skillOpen.set(false));
  }

  protected async saveCertification(): Promise<void> {
    const form = this.certificationForm();
    await this.save(
      () =>
        this.api.addMyCertification({
          name: form.name.trim(),
          issuer: form.issuer.trim() || null,
          credentialId: form.credentialId.trim() || null,
          issuedOn: form.issuedOn || null,
          expiresOn: form.expiresOn || null,
          skillId: form.skillId || null,
        }),
      () => this.certificationOpen.set(false),
    );
  }

  protected async removeSkill(skill: EmployeeSkillItem): Promise<void> {
    await this.save(() => this.api.removeMine(skill.skillId));
  }

  protected async removeCertification(id: string): Promise<void> {
    await this.save(() => this.api.removeMyCertification(id));
  }

  private async save(action: () => Promise<SkillProfile>, done?: () => void): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    this.error.set(null);
    try {
      this.profile.set(await action());
      done?.();
    } catch (error) {
      const text = message(error, 'That did not work.');
      if (done) {
        this.formError.set(text);
      } else {
        this.error.set(text);
      }
    } finally {
      this.busy.set(false);
    }
  }
}
