import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PageHeader } from '@gggs/shared/ui';
import { map } from 'rxjs';
import { HelpArticle } from './help-article';
import { HelpCenter } from './help-center';
import { findTopic, searchTopics } from './help-engine';
import type { HelpAudience, HelpTopic } from './help-model';

type AudienceFilter = 'all' | HelpAudience;

const FILTERS: readonly (readonly [AudienceFilter, string])[] = [
  ['all', 'Everything'],
  ['employee', 'For me'],
  ['manager', 'Managers'],
  ['admin', 'HR & admins'],
];

/**
 * The Help & Wiki (`/help`): a guide to every screen the person can open, searchable, with the
 * help assistant one click away. `?topic=me.leave` opens a page directly.
 */
@Component({
  selector: 'gggs-help-page',
  imports: [RouterLink, PageHeader, HelpArticle],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <gggs-page-header title="Help & Wiki" subtitle="Simple guides to every screen you can use. Search, pick a topic, or ask the assistant.">
      <button type="button" class="btn btn--primary" (click)="center.openAssistant()"><i class="pi pi-comments" aria-hidden="true"></i> Ask for help</button>
    </gggs-page-header>

    <section class="panel stack help-hero">
      <label class="help-search">
        <i class="pi pi-search" aria-hidden="true"></i>
        <span class="sr-only">Search help</span>
        <input type="search" placeholder="Search help, for example: apply leave, payslip, approve" autocomplete="off" [value]="query()" (input)="query.set($any($event.target).value)" />
      </label>
      <div class="help-filters">
        <div class="help-chips" role="group" aria-label="Show help for">
          @for (option of filters; track option[0]) {
            <button type="button" class="help-chip" [attr.aria-pressed]="audience() === option[0]" (click)="audience.set(option[0])">{{ option[1] }}</button>
          }
        </div>
        @if (center.canShowAll()) {
          <label class="help-toggle text-sm">
            <input type="checkbox" [checked]="center.showAll()" (change)="center.showAll.set($any($event.target).checked)" />
            Show every screen (to help others)
          </label>
        }
      </div>
    </section>

    @if (!center.content()) {
      <p class="muted" role="status">Loading help...</p>
    } @else {
      @if (!query().trim() && popular().length) {
        <nav class="help-popular" aria-label="Popular tasks">
          @for (item of popular(); track item.topic.id) {
            <a class="help-popular__item" [routerLink]="[]" [queryParams]="{ topic: item.topic.id }">
              <strong>{{ item.label }}</strong>
              <span class="muted text-sm">{{ item.topic.group }}</span>
            </a>
          }
        </nav>
      }

      <div class="help-layout">
        <nav class="panel help-toc" aria-label="Help topics">
          @if (query().trim()) {
            <p class="help-toc__group">{{ list().length }} {{ list().length === 1 ? 'result' : 'results' }}</p>
          }
          @for (group of groups(); track group.name) {
            @if (!query().trim()) {
              <p class="help-toc__group">{{ group.name }}</p>
            }
            @for (t of group.topics; track t.id) {
              <a class="help-toc__item" [routerLink]="[]" [queryParams]="{ topic: t.id }" [attr.aria-current]="t.id === selectedId() ? 'true' : null">
                <span>{{ t.title }}</span>
                @if (query().trim()) {
                  <span class="muted text-xs">{{ t.group }}</span>
                } @else if (!t.guide) {
                  <span class="muted text-xs">short</span>
                }
              </a>
            }
          } @empty {
            <p class="muted text-sm help-toc__empty">No help page matches "{{ query().trim() }}". Try other words, or <button type="button" class="btn btn--link btn--sm" (click)="center.openAssistant()">ask the assistant</button>.</p>
          }
        </nav>

        <div class="panel">
          @if (selected(); as topic) {
            <gggs-help-article [topic]="topic" />
          }
        </div>
      </div>
    }
  `,
  styles: `
    :host { display: grid; gap: var(--gggs-space-4); }
    .help-search { display: flex; align-items: center; gap: var(--gggs-space-2); max-width: 40rem; padding: 0 var(--gggs-space-3); border: 1px solid var(--gggs-color-border-strong); border-radius: var(--gggs-radius-md); background: var(--gggs-color-surface-raised); }
    .help-search input { flex: 1; min-width: 0; border: 0; background: transparent; padding: var(--gggs-space-3) 0; font-size: var(--gggs-font-size-md); outline: none; box-shadow: none; }
    .help-search:focus-within { outline: 2px solid var(--gggs-color-focus); outline-offset: 1px; }
    .help-filters { display: flex; flex-wrap: wrap; gap: var(--gggs-space-3); justify-content: space-between; align-items: center; }
    .help-chips { display: flex; flex-wrap: wrap; gap: var(--gggs-space-2); }
    .help-chip { padding: 0.25rem 0.8rem; border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-pill); background: var(--gggs-color-surface-raised); color: var(--gggs-color-text); font-size: var(--gggs-font-size-sm); cursor: pointer; }
    .help-chip:hover { background: var(--gggs-color-surface-hover); }
    .help-chip[aria-pressed='true'] { border-color: var(--gggs-color-primary); background: var(--gggs-color-primary-subtle); color: var(--gggs-color-accent); font-weight: var(--gggs-font-weight-medium); }
    .help-toggle { display: inline-flex; gap: var(--gggs-space-2); align-items: center; }
    .help-popular { display: grid; grid-template-columns: repeat(auto-fill, minmax(11rem, 1fr)); gap: var(--gggs-space-3); }
    .help-popular__item { display: grid; gap: 2px; padding: var(--gggs-space-3); border: 1px solid var(--gggs-color-border); border-radius: var(--gggs-radius-md); background: var(--gggs-color-surface-raised); color: var(--gggs-color-text); text-decoration: none; }
    .help-popular__item:hover { border-color: var(--gggs-color-border-strong); background: var(--gggs-color-surface-hover); }
    .help-layout { display: grid; grid-template-columns: minmax(14rem, 18rem) minmax(0, 1fr); gap: var(--gggs-space-4); align-items: start; }
    .help-toc { display: grid; gap: 2px; max-height: calc(100vh - 12rem); overflow-y: auto; position: sticky; top: var(--gggs-space-4); }
    .help-toc__group { margin: var(--gggs-space-3) 0 var(--gggs-space-1); padding: 0 var(--gggs-space-2); color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); text-transform: uppercase; letter-spacing: 0.06em; font-weight: var(--gggs-font-weight-semibold); }
    .help-toc__group:first-child { margin-top: 0; }
    .help-toc__item { display: flex; justify-content: space-between; gap: var(--gggs-space-2); padding: var(--gggs-space-1) var(--gggs-space-2); border-radius: var(--gggs-radius-sm); color: var(--gggs-color-text); text-decoration: none; font-size: var(--gggs-font-size-sm); }
    .help-toc__item:hover { background: var(--gggs-color-surface-hover); }
    .help-toc__item[aria-current='true'] { background: var(--gggs-color-surface-selected); color: var(--gggs-color-accent); font-weight: var(--gggs-font-weight-semibold); }
    .help-toc__empty { padding: var(--gggs-space-2); }
    @media (max-width: 56rem) {
      .help-layout { grid-template-columns: minmax(0, 1fr); }
      .help-toc { position: static; max-height: 18rem; }
    }
  `,
})
export class HelpPage {
  protected readonly center = inject(HelpCenter);
  private readonly route = inject(ActivatedRoute);

  protected readonly filters = FILTERS;
  protected readonly query = signal(this.route.snapshot.queryParamMap.get('q') ?? '');
  protected readonly audience = signal<AudienceFilter>('all');
  private readonly topicParam = toSignal(this.route.queryParamMap.pipe(map((params) => params.get('topic'))), { initialValue: null });

  /** The pages for the chosen audience; general pages always stay. */
  private readonly forAudience = computed(() => {
    const audience = this.audience();
    return this.center.topics().filter((topic) => audience === 'all' || topic.audience === audience || topic.audience === 'everyone');
  });

  protected readonly list = computed(() => (this.query().trim() ? searchTopics(this.query(), this.forAudience()) : this.forAudience()));

  protected readonly groups = computed(() => {
    const list = this.list();
    if (this.query().trim()) {
      return list.length ? [{ name: 'Results', topics: list }] : [];
    }
    const groups: { name: string; topics: HelpTopic[] }[] = [];
    for (const topic of list) {
      let group = groups.find((candidate) => candidate.name === topic.group);
      if (!group) {
        group = { name: topic.group, topics: [] };
        groups.push(group);
      }
      group.topics.push(topic);
    }
    return groups;
  });

  protected readonly selected = computed(() => {
    const all = this.center.topics();
    return findTopic(this.topicParam(), all) ?? (this.query().trim() ? this.list()[0] : undefined) ?? findTopic('getting-started', all) ?? all[0];
  });

  protected readonly selectedId = computed(() => this.selected()?.id ?? null);

  protected readonly popular = computed(() => {
    const content = this.center.content();
    const topics = this.center.topics();
    return (content?.popular ?? [])
      .map(([key, label]) => ({ label, topic: findTopic(key, topics) }))
      .filter((item): item is { label: string; topic: HelpTopic } => !!item.topic);
  });

  constructor() {
    void this.center.load();
    // The page explains itself; the panel would only repeat it.
    this.center.panelOpen.set(false);
  }
}
