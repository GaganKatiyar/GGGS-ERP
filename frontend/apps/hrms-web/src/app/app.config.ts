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
import { stepUpInterceptor } from '@gggs/core/auth';
import { providePrimeNG } from 'primeng/config';
import { ConfirmationService, MessageService } from 'primeng/api';
import { GggsPreset } from '@gggs/shared/theme';
import { appRoutes } from './app.routes';
import { ProductTitleStrategy } from './product-title-strategy';

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
        stepUpInterceptor,
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
