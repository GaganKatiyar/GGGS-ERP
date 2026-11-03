import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { SETTINGS_SECTIONS, VisibleNavigation, type NavItem, type SettingsSection } from '@gggs/core/layout';
import { EmptyState, PageHeader } from '@gggs/shared/ui';
import { map } from 'rxjs';

interface SectionCard {
  readonly section: SettingsSection;
  readonly pages: readonly NavItem[];
}

/**
 * The Settings hub: every master and setting the user may open, one card per category, with a
 * search across them. The cards come from the navigation manifest's Settings group, filtered by the
 * same rule as the rail, so a page appears here exactly when its route would let the user in.
 */
@Component({
  selector: 'gggs-settings-home-page',
  imports: [RouterLink, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Settings" subtitle="Every master list and setting in one place. Changes here shape what everyone else sees." />

    @if (cards().length === 0) {
      <gggs-empty-state icon="pi pi-cog" title="Nothing to set up" description="Your role does not include any settings. An administrator can give you access." />
    } @else {
      <label class="settings__search">
        <i class="pi pi-search" aria-hidden="true"></i>
        <span class="sr-only">Search settings</span>
        <input type="search" placeholder="Search settings, e.g. holidays, grades, roles" autocomplete="off" [value]="query()" (input)="query.set($any($event.target).value)" />
      </label>

      @if (shown().length === 0) {
        <p class="muted" role="status">No settings match “{{ query().trim() }}”.</p>
      }

      <div class="settings__grid">
        @for (card of shown(); track card.section.key) {
          <section
            class="elevated settings-card"
            [id]="'settings-' + card.section.key"
            [class.settings-card--focus]="card.section.key === focused()"
            [attr.aria-labelledby]="'settings-title-' + card.section.key"
          >
            <div class="settings-card__head">
              <span class="settings-card__icon" aria-hidden="true"><i [class]="card.section.icon"></i></span>
              <div>
                <h2 [id]="'settings-title-' + card.section.key">{{ card.section.label }}</h2>
                <p>{{ card.section.description }}</p>
              </div>
            </div>
            <ul class="settings-card__links">
              @for (page of card.pages; track page.route) {
                <li>
                  <a class="settings-card__link" [routerLink]="page.route">
                    <i [class]="page.icon" aria-hidden="true"></i>
                    <span>{{ page.label }}</span>
                    <i class="pi pi-chevron-right settings-card__chevron" aria-hidden="true"></i>
                  </a>
                </li>
              }
            </ul>
          </section>
        }
      </div>
    }
  `,
  styles: `
    .settings__search {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-2);
      max-width: 32rem;
      margin-block-end: var(--gggs-space-5);
      padding-inline: var(--gggs-space-3);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-lg);
      background: var(--gggs-color-surface-raised);
      box-shadow: var(--gggs-shadow-1);
      color: var(--gggs-color-text-muted);

      &:focus-within {
        border-color: var(--gggs-color-primary);
        box-shadow: var(--gggs-focus-ring);
      }

      input {
        flex: 1;
        min-width: 0;
        min-height: 2.75rem;
        border: 0;
        background: transparent;
        color: var(--gggs-color-text);
        font: inherit;
        outline: none;
      }
    }

    .settings__grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(19rem, 100%), 1fr));
      align-items: start;
      gap: var(--gggs-space-5);
    }

    .settings-card {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-3);
      padding: var(--gggs-space-5);
      border-radius: var(--gggs-radius-xl);
      scroll-margin-block-start: 6rem;
      transition: box-shadow var(--gggs-transition), border-color var(--gggs-transition);
    }

    .settings-card--focus {
      border-color: var(--gggs-color-primary);
      box-shadow: var(--gggs-shadow-3), 0 0 0 3px var(--gggs-color-primary-subtle);
    }

    .settings-card__head {
      display: flex;
      align-items: flex-start;
      gap: var(--gggs-space-3);

      h2 {
        margin: 0;
        font-family: var(--gggs-font-family-display);
        font-size: var(--gggs-font-size-lg);
      }

      p {
        margin: var(--gggs-space-1) 0 0;
        color: var(--gggs-color-text-muted);
        font-size: var(--gggs-font-size-sm);
      }
    }

    .settings-card__icon {
      display: grid;
      flex: 0 0 2.75rem;
      place-items: center;
      width: 2.75rem;
      height: 2.75rem;
      border-radius: var(--gggs-radius-lg);
      background: var(--gggs-gradient-tile);
      box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--gggs-color-primary) 18%, transparent);
      color: var(--gggs-color-primary);
      font-size: var(--gggs-font-size-lg);
    }

    .settings-card__links {
      display: flex;
      flex-direction: column;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .settings-card__link {
      display: flex;
      align-items: center;
      gap: var(--gggs-space-3);
      min-height: 2.75rem;
      padding-inline: var(--gggs-space-2);
      border-radius: var(--gggs-radius-md);
      color: var(--gggs-color-text);
      text-decoration: none;
      transition: background-color var(--gggs-transition-fast), color var(--gggs-transition-fast);

      .pi:first-child {
        width: 1.25rem;
        color: var(--gggs-color-text-muted);
        text-align: center;
      }

      span {
        flex: 1;
      }

      &:hover {
        background: var(--gggs-color-surface-hover);
        color: var(--gggs-color-primary);
      }

      &:focus-visible {
        outline: none;
        box-shadow: var(--gggs-focus-ring);
      }
    }

    .settings-card__chevron {
      color: var(--gggs-color-text-subtle);
      font-size: var(--gggs-font-size-xs);
    }
  `,
})
export class SettingsHomePage {
  private readonly navigation = inject(VisibleNavigation);

  protected readonly query = signal('');
  /** The category a module's gear link or a breadcrumb asked for (`?section=`), highlighted and scrolled to. */
  protected readonly focused = toSignal(inject(ActivatedRoute).queryParamMap.pipe(map((params) => params.get('section'))), { initialValue: null });

  protected readonly cards = computed<readonly SectionCard[]>(() => {
    const settings = this.navigation.items().find((group) => group.route === '/settings')?.children ?? [];
    return SETTINGS_SECTIONS.map((section) => ({ section, pages: settings.filter((page) => page.section === section.key) })).filter((card) => card.pages.length > 0);
  });

  /** A search keeps the cards whose name matches whole, and otherwise only the matching pages in each. */
  protected readonly shown = computed<readonly SectionCard[]>(() => {
    const term = this.query().trim().toLowerCase();
    if (!term) {
      return this.cards();
    }

    return this.cards()
      .map((card) => (card.section.label.toLowerCase().includes(term) ? card : { ...card, pages: card.pages.filter((page) => page.label.toLowerCase().includes(term)) }))
      .filter((card) => card.pages.length > 0);
  });

  constructor() {
    effect(() => {
      const key = this.focused();
      if (key && this.cards().some((card) => card.section.key === key)) {
        // After the cards render.
        setTimeout(() => document.getElementById(`settings-${key}`)?.scrollIntoView({ block: 'start', behavior: 'smooth' }));
      }
    });
  }
}
