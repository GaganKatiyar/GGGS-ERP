import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HelpArticle } from './help-article';
import { HelpCenter } from './help-center';

/**
 * Help for the screen on show, beside it. Opened from the "?" in the header; follows the person as
 * they move between screens until closed.
 */
@Component({
  selector: 'gggs-help-panel',
  imports: [RouterLink, HelpArticle],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'close()' },
  template: `
    @if (center.panelOpen()) {
      <aside class="help-panel" role="complementary" aria-label="Help for this screen">
        <div class="help-panel__head">
          <p class="help-panel__eyebrow">Help for this screen</p>
          <button type="button" class="btn btn--sm help-panel__close" aria-label="Close help" title="Close" (click)="close()">
            <i class="pi pi-times" aria-hidden="true"></i>
          </button>
        </div>
        <div class="help-panel__body">
          @if (center.current(); as topic) {
            <gggs-help-article [topic]="topic" [compact]="true" (navigated)="close()" />
          } @else {
            <p class="muted" role="status">Loading help...</p>
          }
        </div>
        <div class="help-panel__foot">
          <a class="btn btn--sm" routerLink="/help" [queryParams]="center.current() ? { topic: center.current()!.id } : null" (click)="close()">
            <i class="pi pi-book" aria-hidden="true"></i> Open Help & Wiki
          </a>
          <button type="button" class="btn btn--primary btn--sm" (click)="center.openAssistant()">
            <i class="pi pi-comments" aria-hidden="true"></i> Ask a question
          </button>
        </div>
      </aside>
    }
  `,
  styles: `
    .help-panel {
      position: fixed;
      inset-block: 0;
      inset-inline-end: 0;
      z-index: var(--gggs-z-drawer);
      width: min(26rem, 100vw);
      display: flex;
      flex-direction: column;
      background: var(--gggs-color-surface-raised);
      border-inline-start: 1px solid var(--gggs-color-border);
      box-shadow: var(--gggs-shadow-lg);
      padding-block: env(safe-area-inset-top, 0px) env(safe-area-inset-bottom, 0px);
    }
    .help-panel__head { display: flex; justify-content: space-between; align-items: center; padding: var(--gggs-space-3) var(--gggs-space-4); border-bottom: 1px solid var(--gggs-color-border); }
    .help-panel__eyebrow { margin: 0; color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-xs); text-transform: uppercase; letter-spacing: 0.06em; font-weight: var(--gggs-font-weight-semibold); }
    .help-panel__body { flex: 1; overflow-y: auto; padding: var(--gggs-space-4); }
    .help-panel__foot { display: flex; flex-wrap: wrap; gap: var(--gggs-space-2); justify-content: space-between; padding: var(--gggs-space-3) var(--gggs-space-4); border-top: 1px solid var(--gggs-color-border); }
  `,
})
export class HelpPanel {
  protected readonly center = inject(HelpCenter);

  protected close(): void {
    this.center.panelOpen.set(false);
  }
}
