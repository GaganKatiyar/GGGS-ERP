import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  provideBrowserGlobalErrorListeners,
  type ApplicationConfig,
} from '@angular/core';
import {
  TitleStrategy,
  provideRouter,
  withComponentInputBinding,
  withInMemoryScrolling,
} from '@angular/router';
import {
  correlationIdInterceptor,
  credentialsInterceptor,
  csrfInterceptor,
  errorInterceptor,
} from '@gggs/core/http';
import { providePrimeNG } from 'primeng/config';
import { ConfirmationService, MessageService } from 'primeng/api';
import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';
import { appRoutes } from './app.routes';
import { ProductTitleStrategy } from './product-title-strategy';

/**
 * Bridges the `--gggs-color-*` custom properties (styles.scss) into PrimeNG's own token system, so
 * a single source of truth recolors hand-rolled markup and PrimeNG components alike - without this,
 * changing `--gggs-color-primary` (tenant branding) would silently do nothing to PrimeNG components.
 * `primary` reuses Aura's stock `blue` scale, whose 700 step is already our exact primary hex.
 */
const GggsPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '{blue.50}',
      100: '{blue.100}',
      200: '{blue.200}',
      300: '{blue.300}',
      400: '{blue.400}',
      500: '{blue.500}',
      600: '{blue.600}',
      700: '{blue.700}',
      800: '{blue.800}',
      900: '{blue.900}',
      950: '{blue.950}',
    },
    colorScheme: {
      light: {
        primary: {
          color: 'var(--gggs-color-primary)',
          contrastColor: '#ffffff',
          hoverColor: '{blue.800}',
          activeColor: '{blue.900}',
        },
        highlight: {
          background: 'var(--gggs-color-primary-subtle)',
          focusBackground: 'var(--gggs-color-primary-subtle)',
          color: 'var(--gggs-color-primary)',
          focusColor: 'var(--gggs-color-primary)',
        },
        text: {
          color: 'var(--gggs-color-text)',
          hoverColor: 'var(--gggs-color-text)',
          mutedColor: 'var(--gggs-color-text-muted)',
          hoverMutedColor: 'var(--gggs-color-text-muted)',
        },
        content: {
          background: 'var(--gggs-color-surface-raised)',
          hoverBackground: 'var(--gggs-color-surface-hover)',
          borderColor: 'var(--gggs-color-border)',
        },
        formField: {
          background: 'var(--gggs-color-surface-raised)',
          borderColor: 'var(--gggs-color-border)',
          hoverBorderColor: 'var(--gggs-color-text-muted)',
          focusBorderColor: 'var(--gggs-color-primary)',
          color: 'var(--gggs-color-text)',
          placeholderColor: 'var(--gggs-color-text-muted)',
        },
      },
      dark: {
        primary: {
          color: 'var(--gggs-color-primary)',
          contrastColor: '#0b1220',
          hoverColor: '{blue.300}',
          activeColor: '{blue.200}',
        },
        highlight: {
          background: 'var(--gggs-color-primary-subtle)',
          focusBackground: 'var(--gggs-color-primary-subtle)',
          color: 'var(--gggs-color-primary)',
          focusColor: 'var(--gggs-color-primary)',
        },
        text: {
          color: 'var(--gggs-color-text)',
          hoverColor: 'var(--gggs-color-text)',
          mutedColor: 'var(--gggs-color-text-muted)',
          hoverMutedColor: 'var(--gggs-color-text-muted)',
        },
        content: {
          background: 'var(--gggs-color-surface-raised)',
          hoverBackground: 'var(--gggs-color-surface-hover)',
          borderColor: 'var(--gggs-color-border)',
        },
        formField: {
          background: 'var(--gggs-color-surface-raised)',
          borderColor: 'var(--gggs-color-border)',
          hoverBorderColor: 'var(--gggs-color-text-muted)',
          focusBorderColor: 'var(--gggs-color-primary)',
          color: 'var(--gggs-color-text)',
          placeholderColor: 'var(--gggs-color-text-muted)',
        },
      },
    },
  },
});

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

    { provide: TitleStrategy, useClass: ProductTitleStrategy },
    provideRouter(
      appRoutes,
      withComponentInputBinding(),
      // Returning to a list should return to where the user was in it.
      withInMemoryScrolling({
        scrollPositionRestoration: 'enabled',
        anchorScrolling: 'enabled',
      }),
    ),

    // Order matters: credentials and CSRF headers go on before the error interceptor sees the
    // response, so a retried request still carries them.
    provideHttpClient(
      withInterceptors([
        credentialsInterceptor,
        csrfInterceptor,
        correlationIdInterceptor,
        errorInterceptor,
      ]),
    ),

    providePrimeNG({
      theme: {
        preset: GggsPreset,
        options: {
          // Dense ERP screens: no oversized default ripple, and dark mode driven by a class the
          // user toggles rather than only by the OS setting.
          darkModeSelector: '.gggs-dark',
          ripple: false,
        },
      },
    }),

    // Backs `gggs-toast-host`/`gggs-confirm-dialog-host` (app.ts) - any feature can inject these
    // directly from 'primeng/api' rather than shared/ui wrapping the services too.
    MessageService,
    ConfirmationService,
  ],
};
