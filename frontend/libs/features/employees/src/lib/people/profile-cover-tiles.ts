import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export interface CoverTile {
  readonly label: string;
  readonly value: string;
  readonly hint?: string;
  /** 0–100: drawn as a bar under the value instead of a hint. */
  readonly progress?: number;
}

/**
 * The profile's at-a-glance numbers as glass tiles on the cover, so the band above the photo
 * carries information instead of only colour. On a wide page they sit to the right of the photo;
 * on a narrow one they wrap into two columns above it.
 */
@Component({
  selector: 'gggs-profile-cover-tiles',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="tiles" aria-label="Summary">
      @for (tile of tiles(); track tile.label) {
        <li class="tile">
          <span class="tile__label">{{ tile.label }}</span>
          <strong class="tile__value">{{ tile.value }}</strong>
          @if (tile.progress !== undefined) {
            <span class="tile__bar" role="progressbar" [attr.aria-label]="tile.label" aria-valuemin="0" aria-valuemax="100" [attr.aria-valuenow]="tile.progress">
              <span class="tile__fill" [style.width.%]="tile.progress"></span>
            </span>
          } @else if (tile.hint) {
            <span class="tile__hint">{{ tile.hint }}</span>
          }
        </li>
      }
    </ul>
  `,
  styles: `
    :host {
      position: relative;
      z-index: 1;
      display: block;
      container-type: inline-size;
    }

    .tiles {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--gggs-space-3);
      margin: 0;
      padding: var(--gggs-space-4) var(--gggs-space-4) 5.5rem;
      list-style: none;
    }

    /* Wide enough for the photo's column plus four readable tiles. */
    @container (min-width: 52rem) {
      .tiles {
        grid-template-columns: repeat(4, minmax(0, 1fr));
        padding: var(--gggs-space-5) var(--gggs-space-6) var(--gggs-space-5) 12.5rem;
      }
    }

    .tile {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-1);
      min-width: 0;
      padding: var(--gggs-space-3) var(--gggs-space-4);
      border: 1px solid color-mix(in srgb, #fff 28%, transparent);
      border-radius: var(--gggs-radius-lg);
      /* Dark glass, so white text keeps its contrast over the red end of the gradient. */
      background: color-mix(in srgb, var(--gggs-color-brand) 30%, transparent);
      backdrop-filter: blur(4px);
      color: #fff;
    }

    .tile__label {
      font-size: var(--gggs-font-size-xs);
      font-weight: var(--gggs-font-weight-semibold);
      letter-spacing: 0.04em;
      text-transform: uppercase;
    }

    /* Narrow tiles wrap the value rather than cut a manager's name short. */
    .tile__value {
      font-family: var(--gggs-font-family-display);
      font-size: var(--gggs-font-size-lg);
      overflow-wrap: anywhere;
    }

    @container (min-width: 64rem) {
      .tile__value {
        font-size: var(--gggs-font-size-xl);
      }
    }

    .tile__hint {
      overflow: hidden;
      font-size: var(--gggs-font-size-xs);
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .tile__bar {
      display: block;
      height: 0.375rem;
      margin-block-start: var(--gggs-space-1);
      overflow: hidden;
      border-radius: var(--gggs-radius-pill);
      background: color-mix(in srgb, #fff 25%, transparent);
    }

    .tile__fill {
      display: block;
      height: 100%;
      border-radius: inherit;
      background: #fff;
    }
  `,
})
export class ProfileCoverTiles {
  readonly tiles = input.required<readonly CoverTile[]>();
}
