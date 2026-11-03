import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { PRODUCT_NAME } from '../product-title-strategy';

/** The parts of the app, one for each point of the logo, clockwise from the top. */
export const SHOWCASE_AREAS: readonly { readonly name: string; readonly covers: string; readonly slot: string; readonly x: number; readonly y: number }[] = [
  { name: 'People', covers: 'Records, org chart and documents', slot: 'a', x: 50, y: 16 },
  { name: 'Time', covers: 'Attendance, leave and timesheets', slot: 'b', x: 79.44, y: 33 },
  { name: 'Payroll', covers: 'Salaries, payslips and compliance', slot: 'c', x: 79.44, y: 67 },
  { name: 'Projects', covers: 'Clients, tasks and resources', slot: 'd', x: 50, y: 84 },
  { name: 'Hiring', covers: 'Openings, interviews and onboarding', slot: 'e', x: 20.56, y: 67 },
  { name: 'Talent', covers: 'Reviews, learning and rewards', slot: 'f', x: 20.56, y: 33 },
];

const STEP_MS = 2800;

/**
 * The panel beside the sign-in form: what the product covers, told through its logo, whose six points
 * become the six parts of the app around the company. One point lights at a time, or the one being
 * pointed at; nothing moves for someone who has asked for reduced motion. It holds nothing to tab to,
 * so the keyboard goes straight to the form.
 */
@Component({
  selector: 'gggs-sign-in-showcase',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <aside class="showcase" [attr.aria-label]="'What ' + productName + ' covers'">
      <p class="showcase__brand">
        <svg class="showcase__logo" viewBox="0 0 100 100" aria-hidden="true" focusable="false">
          <g stroke-width="7" fill="none">
            @for (area of areas; track area.name; let index = $index) {
              <line [attr.x1]="area.x" [attr.y1]="area.y" [attr.x2]="areas[(index + 1) % 6].x" [attr.y2]="areas[(index + 1) % 6].y" [class.showcase__line--accent]="index % 2 === 0" class="showcase__line" />
            }
          </g>
          @for (area of areas; track area.name; let index = $index) {
            <circle [attr.cx]="area.x" [attr.cy]="area.y" r="13" [class.showcase__dot--accent]="index % 2 === 0" class="showcase__dot" />
          }
          <circle cx="50" cy="50" r="11.5" class="showcase__dot" />
        </svg>
        {{ productName }}
      </p>
      <p class="showcase__headline">One Platform. Every Possibility.</p>
      <div class="showcase__diagram">
        @for (area of areas; track area.name; let index = $index) {
          <div class="showcase__area" [attr.data-slot]="area.slot" [class.showcase__area--lit]="lit() === index" (pointerenter)="point(index)" (pointerleave)="point(null)">
            <strong>{{ area.name }}</strong>
            <span>{{ area.covers }}</span>
          </div>
        }
        <svg class="showcase__ring" viewBox="0 0 100 100" aria-hidden="true" focusable="false">
          <g stroke-width="7" fill="none">
            @for (area of areas; track area.name; let index = $index) {
              <line [attr.x1]="area.x" [attr.y1]="area.y" [attr.x2]="areas[(index + 1) % 6].x" [attr.y2]="areas[(index + 1) % 6].y" [class.showcase__line--accent]="index % 2 === 0" class="showcase__line" />
            }
          </g>
          @for (area of areas; track area.name; let index = $index) {
            <circle [attr.cx]="area.x" [attr.cy]="area.y" r="13" [class.showcase__dot--accent]="index % 2 === 0" [class.showcase__point--lit]="lit() === index" class="showcase__dot showcase__point" />
          }
          <circle cx="50" cy="50" r="15" class="showcase__dot" />
          <text x="50" y="48.6" class="showcase__hub">Your</text>
          <text x="50" y="54.6" class="showcase__hub">company</text>
        </svg>
      </div>
    </aside>
  `,
  styles: `
    /* On the charcoal brand panel in every theme, so its colours do not follow the page's. */
    :host { --accent: #e8222f; --ink: #e8edf2; --lit: #ff7a82; --quiet: #9aa9b9; display: block; min-width: 0; }
    .showcase { position: relative; display: flex; flex-direction: column; gap: var(--gggs-space-6); height: 100%; padding: clamp(1.5rem, 3.5vw, 3.25rem); overflow: hidden; background: var(--gggs-color-brand); color: var(--ink); }
    .showcase::after { content: ''; position: absolute; inset: auto -20% -40% auto; width: 70%; aspect-ratio: 1; border-radius: 50%; background: radial-gradient(circle, rgb(232 34 47 / 22%), transparent 65%); pointer-events: none; }
    .showcase > * { position: relative; z-index: 1; }
    .showcase__brand { display: flex; align-items: center; gap: var(--gggs-space-2); margin: 0; font-family: var(--gggs-font-family-display); font-weight: var(--gggs-font-weight-bold); font-size: 1.05rem; }
    .showcase__logo { flex: none; width: 2.25rem; height: 2.25rem; }
    .showcase__headline { margin: 0; max-width: 19ch; font-family: var(--gggs-font-family-display); font-size: clamp(1.6rem, 2.7vw, 2.4rem); font-weight: 800; line-height: 1.14; letter-spacing: -0.025em; text-wrap: balance; }
    .showcase__diagram { display: grid; grid-template-columns: minmax(0, 1fr) minmax(10rem, 15.5rem) minmax(0, 1fr); grid-template-areas: 'a a a' 'f r b' 'e r c' 'd d d'; gap: 0.15rem var(--gggs-space-3); align-items: center; margin-block: auto; }
    .showcase__ring { grid-area: r; width: 100%; aspect-ratio: 1; }
    .showcase__line { stroke: var(--ink); }
    .showcase__line--accent { stroke: var(--accent); }
    .showcase__dot { fill: var(--ink); }
    .showcase__dot--accent { fill: var(--accent); }
    .showcase__point { transform-box: fill-box; transform-origin: center; transition: transform 0.35s ease; }
    .showcase__point--lit { transform: scale(1.18); }
    .showcase__hub { fill: var(--gggs-color-brand); font-family: var(--gggs-font-family-display); font-size: 5.2px; font-weight: 700; text-anchor: middle; }
    .showcase__area { padding: 0.35rem 0.5rem; border-radius: var(--gggs-radius-md); transition: background 0.25s ease; }
    .showcase__area strong { display: block; font-family: var(--gggs-font-family-display); font-size: 0.9375rem; }
    .showcase__area span { display: block; color: var(--quiet); font-size: 0.78rem; line-height: 1.35; }
    .showcase__area--lit { background: rgb(255 255 255 / 8%); }
    .showcase__area--lit strong { color: var(--lit); }
    .showcase__area[data-slot='a'] { grid-area: a; justify-self: center; text-align: center; }
    .showcase__area[data-slot='b'] { grid-area: b; }
    .showcase__area[data-slot='c'] { grid-area: c; }
    .showcase__area[data-slot='d'] { grid-area: d; justify-self: center; text-align: center; }
    .showcase__area[data-slot='e'] { grid-area: e; justify-self: end; text-align: right; }
    .showcase__area[data-slot='f'] { grid-area: f; justify-self: end; text-align: right; }
    @media (max-width: 60rem) {
      .showcase { gap: var(--gggs-space-3); height: auto; padding: 1.35rem 1.25rem; }
      .showcase__headline { max-width: none; font-size: 1.3rem; }
      .showcase__diagram { display: flex; flex-wrap: wrap; gap: 0.35rem; margin: 0; }
      .showcase__ring, .showcase__area span { display: none; }
      .showcase__area, .showcase__area[data-slot] { padding: 0.15rem 0.625rem; border: 1px solid rgb(255 255 255 / 16%); border-radius: var(--gggs-radius-pill); text-align: left; }
      .showcase__area strong { font-size: 0.75rem; font-weight: 600; }
    }
    @media (prefers-reduced-motion: reduce) {
      .showcase__point, .showcase__area { transition: none; }
    }
  `,
})
export class SignInShowcase {
  protected readonly productName = PRODUCT_NAME;
  protected readonly areas = SHOWCASE_AREAS;
  protected readonly lit = signal(0);
  private pointing = false;

  constructor() {
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) {
      return;
    }

    const timer = setInterval(() => {
      if (!this.pointing) {
        this.lit.update((index) => (index + 1) % this.areas.length);
      }
    }, STEP_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected point(index: number | null): void {
    this.pointing = index !== null;
    if (index !== null) {
      this.lit.set(index);
    }
  }
}
