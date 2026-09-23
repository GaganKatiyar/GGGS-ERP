import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  provideBrowserGlobalErrorListeners,
  type ApplicationConfig,
} from '@angular/core';
import {
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
import Aura from '@primeuix/themes/aura';
import { appRoutes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),

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
        preset: Aura,
        options: {
          // Dense ERP screens: no oversized default ripple, and dark mode driven by a class the
          // user toggles rather than only by the OS setting.
          darkModeSelector: '.gggs-dark',
          ripple: false,
        },
      },
    }),
  ],
};
