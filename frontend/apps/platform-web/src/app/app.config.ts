import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { Injectable, inject, provideBrowserGlobalErrorListeners, type ApplicationConfig } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { TitleStrategy, provideRouter, withComponentInputBinding, type RouterStateSnapshot } from '@angular/router';
import { correlationIdInterceptor, credentialsInterceptor, csrfInterceptor, errorInterceptor } from '@gggs/core/http';
import { CONSOLE_ROUTES } from '@gggs/platform/console';
import { GggsPreset } from '@gggs/shared/theme';
import { ConfirmationService, MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';

/** Browser tab titles as "Page · Platform console". */
@Injectable({ providedIn: 'root' })
class ConsoleTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const page = this.buildTitle(snapshot);
    this.title.setTitle(page ? `${page} · Platform console` : 'Platform console · Nuts and Bolts');
  }
}

/**
 * The platform console application: GGGS staff only, served from its own address and talking only
 * to its own server (Gggs.PlatformApi). Shares the HR app's design system, session handling and HTTP
 * interceptors, and none of its screens.
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: TitleStrategy, useClass: ConsoleTitleStrategy },
    provideRouter(CONSOLE_ROUTES, withComponentInputBinding()),
    provideHttpClient(withInterceptors([credentialsInterceptor, csrfInterceptor, correlationIdInterceptor, errorInterceptor])),
    providePrimeNG({
      theme: {
        preset: GggsPreset,
        options: { darkModeSelector: '.gggs-dark', ripple: false },
      },
    }),
    MessageService,
    ConfirmationService,
  ],
};
