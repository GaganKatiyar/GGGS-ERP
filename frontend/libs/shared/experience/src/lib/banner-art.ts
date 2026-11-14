import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { BannerKind } from '@gggs/shared/models';

/**
 * The picture for each kind of banner slide, drawn so nothing has to be uploaded. The banner is dark
 * in every theme, so these colours are fixed rather than tokens, which change with the theme. The
 * only red comes from `currentColor`, which the banner sets to its palette's accent.
 */
@Component({
  selector: 'gggs-banner-art',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg viewBox="0 0 480 300" preserveAspectRatio="xMidYMid meet" aria-hidden="true" focusable="false">
      @switch (kind()) {
        @case ('Payroll') {
          <rect x="150" y="44" width="200" height="232" rx="9" fill="#fff" opacity="0.2" transform="rotate(6 250 160)" />
          <g transform="rotate(-4 240 150)">
            <rect x="140" y="34" width="204" height="236" rx="9" fill="#fff" />
            <text x="160" y="72" font-size="16" font-weight="700" fill="#1b2733">Payslip</text>
            <line x1="160" y1="90" x2="324" y2="90" stroke="#dde3e9" />
            <text x="160" y="118" font-size="12" fill="#55677a">Earnings</text>
            <text x="324" y="118" font-size="12" text-anchor="end" fill="#1b2733">₹ ••,•••</text>
            <text x="160" y="142" font-size="12" fill="#55677a">Deductions</text>
            <text x="324" y="142" font-size="12" text-anchor="end" fill="#1b2733">₹ •,•••</text>
            <line x1="160" y1="158" x2="324" y2="158" stroke="#dde3e9" />
            <text x="160" y="184" font-size="13" font-weight="700" fill="#1b2733">Net pay</text>
            <text x="324" y="184" font-size="15" font-weight="700" text-anchor="end" fill="currentColor">₹ ••,•••</text>
            <rect x="160" y="206" width="68" height="24" rx="12" fill="#e7f6ec" />
            <text x="194" y="222" font-size="11" font-weight="600" text-anchor="middle" fill="#0e6b30">Credited</text>
          </g>
        }
        @case ('Holiday') {
          <ellipse cx="240" cy="228" rx="205" ry="40" fill="none" stroke="#ffc56b" stroke-width="4" stroke-linecap="round" stroke-dasharray="0 15" opacity="0.65" />
          <ellipse cx="240" cy="228" rx="160" ry="26" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-dasharray="0 12" opacity="0.4" />
          @for (lamp of lamps; track lamp) {
            <g [attr.transform]="lamp">
              <circle cy="-30" r="66" fill="#ffc56b" opacity="0.14" />
              <circle cy="-30" r="38" fill="#ffc56b" opacity="0.18" />
              <path d="M0 -50C13 -30 10 -14 0 -8C-10 -14 -13 -30 0 -50Z" fill="#ffc56b" />
              <path d="M0 -34C6 -24 5 -16 0 -11C-5 -16 -6 -24 0 -34Z" fill="#fff1c9" />
              <path d="M-44 -6C-34 26 34 26 44 -6C24 2 -24 2 -44 -6Z" fill="#d98a4b" />
              <path d="M-44 -6C-24 -14 24 -14 44 -6C24 2 -24 2 -44 -6Z" fill="#a85f2c" />
            </g>
          }
        }
        @case ('Deadline') {
          <circle cx="240" cy="150" r="92" fill="none" stroke="#fff" stroke-opacity="0.2" stroke-width="20" />
          <circle cx="240" cy="150" r="92" fill="none" stroke="#fff" stroke-width="20" stroke-linecap="round" stroke-dasharray="404 578" transform="rotate(-90 240 150)" />
          <path d="M240 96V150L276 172" fill="none" stroke="#fff" stroke-width="10" stroke-linecap="round" stroke-linejoin="round" />
          <circle cx="240" cy="150" r="9" fill="#fff" />
        }
        @case ('WhatsNew') {
          <rect x="130" y="36" width="280" height="228" rx="10" fill="#fff" opacity="0.14" />
          <rect x="330" y="66" width="56" height="10" rx="5" fill="#fff" opacity="0.3" />
          <rect x="330" y="90" width="40" height="10" rx="5" fill="#fff" opacity="0.3" />
          <rect x="330" y="114" width="52" height="10" rx="5" fill="#fff" opacity="0.3" />
          <rect x="84" y="36" width="50" height="228" rx="10" fill="#16202b" />
          <circle cx="109" cy="66" r="9" fill="currentColor" />
          @for (y of railDots; track y) {
            <circle cx="109" [attr.cy]="y" r="8" fill="#c9d2dc" opacity="0.55" />
          }
          <rect x="122" y="52" width="186" height="200" rx="12" fill="#fff" />
          <rect x="138" y="68" width="154" height="24" rx="6" fill="#f1f4f7" />
          <rect x="138" y="106" width="154" height="26" rx="6" fill="#fef0f1" />
          <circle cx="152" cy="119" r="6" fill="currentColor" />
          <rect x="166" y="114" width="84" height="10" rx="5" fill="currentColor" />
          @for (row of menuRows; track row.y) {
            <circle cx="152" [attr.cy]="row.y + 5" r="6" fill="#c5cfd9" />
            <rect x="166" [attr.y]="row.y" [attr.width]="row.width" height="10" rx="5" fill="#dde3e9" />
          }
          <path d="M264 142l0 34 9-8 6 14 7-3-6-14 12-1z" fill="#16202b" stroke="#fff" stroke-width="2.5" stroke-linejoin="round" />
        }
        @case ('Recognition') {
          <path d="M240 42L334 96V204L240 258L146 204V96Z" fill="none" stroke="#fff" stroke-opacity="0.4" stroke-width="5" />
          @for (person of people; track person.x + ',' + person.y) {
            <g [attr.transform]="'translate(' + person.x + ' ' + person.y + ')'">
              <circle r="27" [attr.fill]="person.light ? '#fff' : '#16202b'" stroke="#fff" stroke-width="2" />
              <circle cy="-7" r="7" [attr.fill]="person.light ? '#1b2733' : '#fff'" />
              <path d="M-13 15C-13 4 13 4 13 15Z" [attr.fill]="person.light ? '#1b2733' : '#fff'" />
            </g>
          }
          <circle cx="240" cy="150" r="36" fill="currentColor" stroke="#fff" stroke-width="3" />
          <path d="M240 171s-20-12.500-20-27a11 11 0 0 1 20-6.500 11 11 0 0 1 20 6.500c0 14.500-20 27-20 27z" fill="#fff" />
        }
        @default {
          <path d="M136 124L300 66V234L136 176Z" fill="#fff" />
          <rect x="104" y="124" width="36" height="52" rx="8" fill="#fff" opacity="0.75" />
          <path d="M150 180L172 250H204L184 190Z" fill="#fff" opacity="0.75" />
          <path d="M328 112a50 50 0 0 1 0 76M352 88a84 84 0 0 1 0 124" fill="none" stroke="#fff" stroke-width="9" stroke-linecap="round" />
        }
      }
    </svg>
  `,
  styles: `
    :host {
      display: block;
      width: 100%;
      height: 100%;
    }

    svg {
      display: block;
      width: 100%;
      height: 100%;
      max-height: 13rem;
      color: var(--banner-art-accent);
      font-family: var(--gggs-font-family);
    }
  `,
})
export class BannerArt {
  readonly kind = input.required<BannerKind>();

  protected readonly lamps = ['translate(116 206) scale(0.82)', 'translate(364 206) scale(0.82)', 'translate(240 190) scale(1.2)'];
  protected readonly railDots = [100, 132, 164, 196];
  protected readonly menuRows = [
    { y: 145, width: 102 },
    { y: 175, width: 70 },
    { y: 205, width: 92 },
  ];

  /** The six points of the product's ring logo. */
  protected readonly people = [
    { x: 240, y: 42, light: true },
    { x: 334, y: 96, light: false },
    { x: 334, y: 204, light: true },
    { x: 240, y: 258, light: false },
    { x: 146, y: 204, light: true },
    { x: 146, y: 96, light: false },
  ];
}
