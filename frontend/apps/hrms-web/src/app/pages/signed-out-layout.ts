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
          <span>Powered by</span>
          <img class="signed-out__company" src="gggs-technologies.png" alt="GGGS Technologies" width="173" height="32" />
          <span>© {{ year }} {{ company }}</span>
          <span class="signed-out__links">
            <a routerLink="/privacy">Privacy Policy</a>
            <a routerLink="/terms">Terms of Use</a>
          </span>
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
      justify-content: center;
      gap: var(--gggs-space-6);
      min-width: 0;
      padding: var(--gggs-space-6);

      @media (max-width: 60rem) {
        justify-content: flex-start;
      }
    }

    .signed-out__content {
      display: flex;
      justify-content: center;
      min-width: 0;
    }

    .signed-out__maker {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--gggs-space-1);
      color: var(--gggs-color-text-muted);
      font-size: var(--gggs-font-size-xs);
      text-align: center;
    }

    /* The picture has a white background, so in dark mode it sits on a small white plate. */
    .signed-out__company {
      box-sizing: content-box;
      display: block;
      max-width: 100%;
      height: auto;
      padding: 0.2rem 0.4rem;
      border-radius: var(--gggs-radius-sm);
      background: #fff;
    }

    .signed-out__links {
      display: flex;
      flex-wrap: wrap;
      justify-content: center;
      gap: var(--gggs-space-1) var(--gggs-space-4);
      margin-block-start: var(--gggs-space-1);
    }
  `,
})
export class SignedOutLayout {
  protected readonly company = LEGAL.company;
  protected readonly year = new Date().getFullYear();
}
