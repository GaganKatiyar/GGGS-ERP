import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import type { SearchHit, SearchResults } from '@gggs/shared/models';
import { firstValueFrom } from 'rxjs';

const KIND_LABELS: Record<string, string> = {
  employee: 'People',
  project: 'Projects',
  client: 'Clients',
  candidate: 'Candidates',
  requisition: 'Requisitions',
  asset: 'Assets',
  article: 'Help articles',
};

const KIND_ICONS: Record<string, string> = {
  employee: 'pi pi-user',
  project: 'pi pi-briefcase',
  client: 'pi pi-building',
  candidate: 'pi pi-id-card',
  requisition: 'pi pi-file',
  asset: 'pi pi-desktop',
  article: 'pi pi-book',
};

/** The header's global search: type two letters and results from every module appear, grouped by kind. */
@Component({
  selector: 'gggs-global-search',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown)': 'shortcut($event)', '(document:click)': 'outside($event)' },
  template: `
    <div class="search" role="search">
      <i class="pi pi-search search__icon" aria-hidden="true"></i>
      <input
        #box
        type="search"
        class="search__input"
        placeholder="Search people, projects, clients... ( / )"
        aria-label="Search"
        role="combobox"
        aria-controls="global-search-results"
        [attr.aria-expanded]="open()"
        [value]="term()"
        (input)="type($any($event.target).value)"
        (focus)="open.set(term().length >= 2)"
        (keydown)="keys($event)"
      />
      @if (open()) {
        <div id="global-search-results" class="search__panel" role="listbox">
          @if (busy()) {
            <p class="search__empty">Searching...</p>
          } @else if (!hits().length) {
            <p class="search__empty">Nothing found for "{{ term() }}".</p>
          }
          @for (group of groups(); track group.kind) {
            <p class="search__group">{{ label(group.kind) }}</p>
            @for (hit of group.hits; track hit.kind + hit.id) {
              <button type="button" role="option" class="search__hit" [class.search__hit--active]="hit === active()" [attr.aria-selected]="hit === active()" (click)="go(hit)">
                <i [class]="icon(hit.kind)" aria-hidden="true"></i>
                <span class="search__text">
                  <strong>{{ hit.title }}</strong>
                  @if (hit.subtitle) {
                    <span class="search__sub">{{ hit.subtitle }}</span>
                  }
                </span>
              </button>
            }
          }
        </div>
      }
    </div>
  `,
  styles: `
    .search {
      position: relative;
      width: min(28rem, 40vw);
    }

    .search__icon {
      position: absolute;
      inset-inline-start: 0.6rem;
      inset-block-start: 50%;
      transform: translateY(-50%);
      color: var(--gggs-color-text-muted);
    }

    .search__input {
      width: 100%;
      padding-inline-start: 2rem;
    }

    .search__panel {
      position: absolute;
      inset-inline: 0;
      inset-block-start: calc(100% + 0.25rem);
      max-height: 26rem;
      overflow-y: auto;
      z-index: 1000;
      background: var(--gggs-color-surface);
      border: 1px solid var(--gggs-color-border);
      border-radius: var(--gggs-radius-md, 0.5rem);
      box-shadow: 0 8px 24px rgb(0 0 0 / 0.12);
      padding: var(--gggs-space-1);
    }

    .search__group {
      margin: var(--gggs-space-2) var(--gggs-space-2) var(--gggs-space-1);
      font-size: var(--gggs-font-size-sm);
      color: var(--gggs-color-text-muted);
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }

    .search__hit {
      display: flex;
      gap: var(--gggs-space-2);
      align-items: center;
      width: 100%;
      padding: var(--gggs-space-2);
      border: 0;
      background: transparent;
      color: inherit;
      text-align: start;
      border-radius: var(--gggs-radius-sm, 0.25rem);
      cursor: pointer;
    }

    .search__hit:hover,
    .search__hit--active {
      background: var(--gggs-color-surface-hover, rgb(0 0 0 / 0.05));
    }

    .search__text {
      display: grid;
    }

    .search__sub,
    .search__empty {
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-sm);
    }

    .search__empty {
      margin: var(--gggs-space-2);
    }

    @media (max-width: 48rem) {
      .search {
        width: 100%;
      }
    }
  `,
})
export class GlobalSearch {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);
  private timer: ReturnType<typeof setTimeout> | undefined;
  private request = 0;

  protected readonly term = signal('');
  protected readonly hits = signal<readonly SearchHit[]>([]);
  protected readonly busy = signal(false);
  protected readonly open = signal(false);
  protected readonly activeIndex = signal(0);
  protected readonly active = computed(() => this.ordered()[this.activeIndex()]);
  protected readonly groups = computed(() => {
    const groups: { kind: string; hits: SearchHit[] }[] = [];
    for (const hit of this.hits()) {
      let group = groups.find((candidate) => candidate.kind === hit.kind);
      if (!group) {
        group = { kind: hit.kind, hits: [] };
        groups.push(group);
      }

      group.hits.push(hit);
    }

    return groups;
  });
  private readonly ordered = computed(() => this.groups().flatMap((group) => group.hits));

  protected label(kind: string): string {
    return KIND_LABELS[kind] ?? kind;
  }

  protected icon(kind: string): string {
    return KIND_ICONS[kind] ?? 'pi pi-circle';
  }

  protected type(value: string): void {
    this.term.set(value);
    clearTimeout(this.timer);
    if (value.trim().length < 2) {
      this.hits.set([]);
      this.open.set(false);
      return;
    }

    this.open.set(true);
    this.busy.set(true);
    this.timer = setTimeout(() => void this.run(value.trim()), 250);
  }

  protected keys(event: KeyboardEvent): void {
    const count = this.ordered().length;
    if (event.key === 'ArrowDown' && count) {
      event.preventDefault();
      this.activeIndex.update((index) => (index + 1) % count);
    } else if (event.key === 'ArrowUp' && count) {
      event.preventDefault();
      this.activeIndex.update((index) => (index - 1 + count) % count);
    } else if (event.key === 'Enter' && this.active()) {
      event.preventDefault();
      this.go(this.active()!);
    } else if (event.key === 'Escape') {
      this.open.set(false);
    }
  }

  protected go(hit: SearchHit): void {
    this.open.set(false);
    void this.router.navigateByUrl(hit.link);
  }

  /** "/" focuses the search box from anywhere, unless the user is typing in a field. */
  protected shortcut(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    if (event.key === '/' && !(target && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)))) {
      event.preventDefault();
      this.host.nativeElement.querySelector('input')?.focus();
    }
  }

  protected outside(event: MouseEvent): void {
    if (!this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  private async run(term: string): Promise<void> {
    const request = ++this.request;
    try {
      const results = await firstValueFrom(this.http.get<SearchResults>('/api/v1/search', { params: { q: term } }));
      if (request === this.request) {
        this.hits.set(results.hits);
        this.activeIndex.set(0);
      }
    } catch {
      if (request === this.request) {
        this.hits.set([]);
      }
    } finally {
      if (request === this.request) {
        this.busy.set(false);
      }
    }
  }
}
