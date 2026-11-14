import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** Raise this whenever `public/app-art.svg` changes, or browsers keep showing the old file. */
const ART_VERSION = 1;

/**
 * Which drawing each menu icon shows. The menu keeps its icon names because the rail draws them as
 * line icons, so a page's picture follows from its icon. Close relatives share a drawing.
 */
export const ICON_ART: Readonly<Record<string, string>> = {
  'pi pi-address-book': 'idcard',
  'pi pi-angle-double-up': 'career',
  'pi pi-arrow-right-arrow-left': 'arrows',
  'pi pi-arrow-up-right': 'career',
  'pi pi-bars': 'levels',
  'pi pi-bell': 'bell',
  'pi pi-bolt': 'bolt',
  'pi pi-book': 'book',
  'pi pi-briefcase': 'briefcase',
  'pi pi-building': 'building',
  'pi pi-bullseye': 'target',
  'pi pi-calendar': 'calendar',
  'pi pi-calendar-clock': 'calendar-clock',
  'pi pi-calendar-minus': 'calendar-minus',
  'pi pi-chart-bar': 'chart',
  'pi pi-chart-line': 'chart-line',
  'pi pi-chart-pie': 'chart-pie',
  'pi pi-check-circle': 'check-circle',
  'pi pi-check-square': 'approvals',
  'pi pi-clipboard': 'checklist',
  'pi pi-clock': 'clock',
  'pi pi-clone': 'clone',
  'pi pi-cog': 'gear',
  'pi pi-comment': 'chat',
  'pi pi-comments': 'chat',
  'pi pi-compass': 'compass',
  'pi pi-database': 'database',
  'pi pi-desktop': 'desktop',
  'pi pi-envelope': 'envelope',
  'pi pi-exclamation-triangle': 'warning',
  'pi pi-eye': 'eye',
  'pi pi-file': 'file',
  'pi pi-file-check': 'file-check',
  'pi pi-file-edit': 'file-edit',
  'pi pi-flag': 'flag',
  'pi pi-folder': 'folder',
  'pi pi-folder-open': 'folder',
  'pi pi-gauge': 'gauge',
  'pi pi-gift': 'gift',
  'pi pi-globe': 'globe',
  'pi pi-heart': 'heart',
  'pi pi-history': 'history',
  'pi pi-home': 'home',
  'pi pi-id-card': 'idcard',
  'pi pi-inbox': 'inbox',
  'pi pi-info-circle': 'info',
  'pi pi-key': 'key',
  'pi pi-link': 'link',
  'pi pi-list': 'list',
  'pi pi-list-check': 'checklist',
  'pi pi-lock': 'lock',
  'pi pi-map-marker': 'pin',
  'pi pi-megaphone': 'megaphone',
  'pi pi-money-bill': 'money',
  'pi pi-objects-column': 'board',
  'pi pi-pause-circle': 'pause',
  'pi pi-pencil': 'pencil',
  'pi pi-percentage': 'percent',
  'pi pi-play': 'play',
  'pi pi-plus-circle': 'plus',
  'pi pi-question-circle': 'helpdesk',
  'pi pi-receipt': 'receipt',
  'pi pi-replay': 'replay',
  'pi pi-send': 'send',
  'pi pi-share-alt': 'share',
  'pi pi-sign-in': 'enter',
  'pi pi-sign-out': 'exit',
  'pi pi-sitemap': 'sitemap',
  'pi pi-sliders-h': 'sliders',
  'pi pi-sliders-v': 'sliders',
  'pi pi-sort-amount-up': 'levels',
  'pi pi-sparkles': 'sparkles',
  'pi pi-star': 'medal',
  'pi pi-stopwatch': 'clock',
  'pi pi-sun': 'sun',
  'pi pi-tags': 'tags',
  'pi pi-th-large': 'board',
  'pi pi-upload': 'upload',
  'pi pi-user': 'person',
  'pi pi-user-minus': 'person-minus',
  'pi pi-user-plus': 'recruit',
  'pi pi-users': 'team',
  'pi pi-verified': 'verified',
  'pi pi-wallet': 'wallet',
};

/**
 * A menu icon as a two-tone picture, for app tiles (the launcher, the home screen's links). An icon
 * with no drawing yet stays a line icon, so a tile is never empty. Decorative: the tile's label is
 * what gets read out. Whoever holds it sets `--gggs-art-main` and `--gggs-art-soft` to change colour.
 */
@Component({
  selector: 'gggs-app-art',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (href(); as address) {
      <svg class="app-art" aria-hidden="true" focusable="false"><use [attr.href]="address" /></svg>
    } @else {
      <i [class]="icon()" aria-hidden="true"></i>
    }
  `,
  styles: `
    :host {
      display: contents;
    }

    .app-art {
      --art-main: var(--gggs-art-main);
      --art-soft: var(--gggs-art-soft);
      --art-detail: var(--gggs-art-detail);
      display: block;
      width: 2.625rem;
      height: 2.625rem;
    }
  `,
})
export class AppArt {
  /** A menu icon's name, such as `pi pi-wallet`. */
  readonly icon = input.required<string>();

  protected readonly href = computed(() => {
    const art = ICON_ART[this.icon()];
    return art ? `app-art.svg?v=${ART_VERSION}#${art}` : null;
  });
}
