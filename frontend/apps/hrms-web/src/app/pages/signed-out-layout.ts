import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LEGAL } from './legal-details';
import { SignInShowcase } from './sign-in-showcase';

/**
 * The frame of every signed-out page: what the product covers on one side, the page's own form on the
 * other, and who makes it under that.
 */
@Component({
  selector: 'gggs-signed-out-layout',
  imports: [RouterLink, SignInShowcase],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="signed-out">
      <gggs-sign-in-showcase />
      <main class="signed-out__main">
        <div class="signed-out__content"><ng-content /></div>
        <footer class="signed-out__maker">
          <div class="signed-out__maker-row">
            <span class="signed-out__powered">
              Powered by
              <img class="signed-out__company" src="gggs-technologies.png" alt="GGGS Technologies" width="97" height="18" />
            </span>
            <nav class="signed-out__links" aria-label="Legal">
              <a routerLink="/privacy">Privacy Policy</a>
              <span aria-hidden="true">·</span>
              <a routerLink="/terms">Terms of Use</a>
            </nav>
          </div>
          <small class="signed-out__copyright">© {{ year }} {{ company }}. All rights reserved.</small>
        </footer>
      </main>
    </div>
  `,
  styles: `
    .signed-out {
      display: grid;
      grid-template-columns: minmax(0, 1.25fr) minmax(0, 1fr);
      min-height: 100dvh;
      background: var(--gggs-color-surface-raised);

      @media (max-width: 60rem) {
        grid-template-columns: minmax(0, 1fr);
        grid-template-rows: auto 1fr;
      }
    }

    .signed-out__main {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-6);
      min-width: 0;
      padding: var(--gggs-space-6) var(--gggs-space-8) 0;

      @media (max-width: 60rem) {
        padding-inline: var(--gggs-space-6);
      }
    }

    /* The form takes the free height and centres in it, so the footer sits at the bottom. */
    .signed-out__content {
      display: flex;
      flex: 1 0 auto;
      align-items: center;
      justify-content: center;
      min-width: 0;
    }

    .signed-out__maker {
      display: flex;
      flex-direction: column;
      gap: var(--gggs-space-2);
      padding-block: var(--gggs-space-4) var(--gggs-space-5);
      border-block-start: 1px solid var(--gggs-color-border);
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-xs);
    }

    .signed-out__maker-row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--gggs-space-2) var(--gggs-space-4);
    }

    .signed-out__powered {
      display: inline-flex;
      align-items: center;
      gap: var(--gggs-space-2);
    }

    /* The picture has a white background, so in dark mode it sits on a small white plate. */
    .signed-out__company {
      box-sizing: content-box;
      display: block;
      width: auto;
      height: 1.125rem;
      padding: 0.125rem 0.25rem;
      border-radius: var(--gggs-radius-sm);
      background: #fff;
    }

    /* Muted, not the brand red: red is kept for actions such as "Forgot password?" and Sign in. */
    .signed-out__links {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--gggs-space-2);

      a {
        padding-block: var(--gggs-space-1);
        color: inherit;
        text-decoration: none;

        &:hover,
        &:focus-visible {
          color: var(--gggs-color-text);
          text-decoration: underline;
        }
      }

      span {
        color: var(--gggs-color-text-subtle);
      }
    }

    .signed-out__copyright {
      font-size: inherit;
    }
  `,
})
export class SignedOutLayout {
  protected readonly company = LEGAL.company;
  protected readonly year = new Date().getFullYear();
}
