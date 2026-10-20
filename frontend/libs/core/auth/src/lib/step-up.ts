import type { HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { ApiError } from '@gggs/core/http';
import { type Observable, catchError, from, switchMap, throwError } from 'rxjs';

/**
 * Asks the signed-in person to confirm it is them - when the API refuses a sensitive action because
 * the last authentication is too old - and remembers the answer for requests waiting on it.
 */
@Injectable({ providedIn: 'root' })
export class StepUpPrompt {
  readonly open = signal(false);
  private pending: Promise<boolean> | null = null;
  private resolve: ((confirmed: boolean) => void) | null = null;

  /** Resolves true once the person has confirmed, false if they cancel. Concurrent callers share one prompt. */
  request(): Promise<boolean> {
    this.pending ??= new Promise<boolean>((resolve) => {
      this.resolve = resolve;
      this.open.set(true);
    });
    return this.pending;
  }

  finish(confirmed: boolean): void {
    this.open.set(false);
    this.resolve?.(confirmed);
    this.pending = null;
    this.resolve = null;
  }
}

/**
 * Answers `STEP_UP_REQUIRED` by asking the person to confirm and then sending the request again,
 * so a sensitive action completes instead of failing with an access-denied message.
 */
export const stepUpInterceptor: HttpInterceptorFn = (request: HttpRequest<unknown>, next: HttpHandlerFn): Observable<HttpEvent<unknown>> => {
  const prompt = inject(StepUpPrompt);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof ApiError) || error.errorCode !== 'STEP_UP_REQUIRED' || request.url.startsWith('/bff/')) {
        return throwError(() => error);
      }

      return from(prompt.request()).pipe(switchMap((confirmed) => (confirmed ? next(request) : throwError(() => error))));
    }),
  );
};
