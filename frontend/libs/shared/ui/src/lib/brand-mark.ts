import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * The product's logo, the six-point ring. Its two colours are tokens, so it reads on any theme. Whoever
 * uses it sets its width and height; it says nothing to a screen reader because whatever holds it names it.
 */
@Component({
  selector: 'gggs-brand-mark',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg viewBox="0 0 100 100" width="100%" height="100%" aria-hidden="true" focusable="false">
      <path class="accent" d="M50 16 L79.44 33" />
      <path class="ink" d="M79.44 33 L79.44 67" />
      <path class="accent" d="M79.44 67 L50 84" />
      <path class="ink" d="M50 84 L20.56 67" />
      <path class="accent" d="M20.56 67 L20.56 33" />
      <path class="ink" d="M20.56 33 L50 16" />
      <circle class="accent" cx="50" cy="16" r="13" />
      <circle class="ink" cx="79.44" cy="33" r="13" />
      <circle class="accent" cx="79.44" cy="67" r="13" />
      <circle class="ink" cx="50" cy="84" r="13" />
      <circle class="accent" cx="20.56" cy="67" r="13" />
      <circle class="ink" cx="20.56" cy="33" r="13" />
      <circle class="ink" cx="50" cy="50" r="11.5" />
    </svg>
  `,
  styles: `
    :host { display: block; }
    svg { display: block; }
    path { stroke-width: 7; fill: none; }
    path.accent { stroke: var(--gggs-logo-accent); }
    path.ink { stroke: var(--gggs-logo-ink); }
    circle.accent { fill: var(--gggs-logo-accent); }
    circle.ink { fill: var(--gggs-logo-ink); }
  `,
})
export class BrandMark {}
