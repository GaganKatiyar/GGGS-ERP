import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BrandMark } from '@gggs/shared/ui';
import { PRODUCT_NAME } from '../product-title-strategy';
import { LEGAL, legalParts, type LegalSection } from './legal-details';

/** A policy as a plain reading page: open to anyone, because people must be able to read it before they sign in. */
@Component({
  selector: 'gggs-legal-page-layout',
  imports: [RouterLink, BrandMark],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="legal">
      <header class="legal__top">
        <a class="legal__brand" routerLink="/sign-in"><gggs-brand-mark class="legal__logo" />{{ productName }}</a>
        <a routerLink="/sign-in">Back to sign in</a>
      </header>
      <main class="legal__main">
        <h1 class="legal__title">{{ heading() }}</h1>
        <p class="legal__date">
          Effective:
          @for (part of parts(legal.effectiveDate); track $index) {
            @if (part.blank) {<mark>{{ part.text }}</mark>} @else {{{ part.text }}}
          }
        </p>
        @for (section of sections(); track section.title; let number = $index) {
          <section class="legal__section">
            <h2>{{ number + 1 }}. {{ section.title }}</h2>
            @for (paragraph of section.paragraphs ?? []; track $index) {
              <p>
                @for (part of parts(paragraph); track $index) {
                  @if (part.blank) {<mark>{{ part.text }}</mark>} @else {{{ part.text }}}
                }
              </p>
            }
            @if (section.items?.length) {
              <ul>
                @for (item of section.items; track $index) {
                  <li>
                    @for (part of parts(item); track $index) {
                      @if (part.blank) {<mark>{{ part.text }}</mark>} @else {{{ part.text }}}
                    }
                  </li>
                }
              </ul>
            }
            @for (paragraph of section.after ?? []; track $index) {
              <p>
                @for (part of parts(paragraph); track $index) {
                  @if (part.blank) {<mark>{{ part.text }}</mark>} @else {{{ part.text }}}
                }
              </p>
            }
          </section>
        }
      </main>
      <footer class="legal__foot">
        <span>© {{ year }} {{ legal.company }}</span>
        <a routerLink="/privacy">Privacy Policy</a>
        <a routerLink="/terms">Terms of Use</a>
      </footer>
    </div>
  `,
  styles: `
    .legal { display: flex; flex-direction: column; min-height: 100dvh; background: var(--gggs-color-surface-raised); color: var(--gggs-color-text); }
    .legal__top { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: var(--gggs-space-3); padding: var(--gggs-space-4) clamp(1rem, 4vw, 3rem); border-bottom: 1px solid var(--gggs-color-border); }
    .legal__brand { display: inline-flex; align-items: center; gap: var(--gggs-space-2); color: inherit; font-family: var(--gggs-font-family-display); font-weight: var(--gggs-font-weight-bold); text-decoration: none; }
    .legal__logo { width: 2rem; height: 2rem; }
    .legal__main { flex: 1; width: 100%; max-width: 46rem; margin-inline: auto; padding: clamp(1.5rem, 4vw, 3rem) clamp(1rem, 4vw, 1.5rem); line-height: 1.65; }
    .legal__title { margin: 0; font-family: var(--gggs-font-family-display); font-size: clamp(1.75rem, 4vw, 2.5rem); font-weight: 800; line-height: 1.15; letter-spacing: -0.025em; }
    .legal__date { margin: var(--gggs-space-2) 0 0; color: var(--gggs-color-text-muted); }
    .legal__section { margin-block-start: var(--gggs-space-8); }
    .legal__section h2 { margin: 0 0 var(--gggs-space-2); font-family: var(--gggs-font-family-display); font-size: 1.2rem; font-weight: var(--gggs-font-weight-bold); letter-spacing: -0.01em; }
    .legal__section p { margin: 0 0 var(--gggs-space-3); }
    .legal__section ul { margin: 0 0 var(--gggs-space-3); padding-inline-start: 1.25rem; }
    .legal__section li { margin-block-end: var(--gggs-space-1); }
    mark { padding: 0 0.25rem; border-radius: var(--gggs-radius-sm); background: var(--gggs-color-warning-subtle); color: var(--gggs-color-text); font-weight: var(--gggs-font-weight-semibold); }
    .legal__foot { display: flex; flex-wrap: wrap; justify-content: center; gap: var(--gggs-space-2) var(--gggs-space-5); padding: var(--gggs-space-5) var(--gggs-space-4); border-top: 1px solid var(--gggs-color-border); color: var(--gggs-color-text-muted); font-size: var(--gggs-font-size-sm); }
  `,
})
export class LegalPageLayout {
  readonly heading = input.required<string>();
  readonly sections = input.required<readonly LegalSection[]>();

  protected readonly productName = PRODUCT_NAME;
  protected readonly legal = LEGAL;
  protected readonly parts = legalParts;
  protected readonly year = new Date().getFullYear();
}
