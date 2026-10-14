import {
  HttpErrorResponse,
  type HttpEvent,
  type HttpHandlerFn,
  type HttpInterceptorFn,
  type HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import type { ProblemDetails } from '@gggs/shared/models';
import { throwError, type Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';

/** Requests to our own API, as opposed to assets or third-party URLs. */
function isApiRequest(request: HttpRequest<unknown>): boolean {
  return request.url.startsWith('/api/') || request.url.startsWith('/bff/');
}

/**
 * Sends the session cookie with API calls.
 *
 * Under the BFF pattern there is no Authorization header to attach: the cookie is the credential
 * and the BFF exchanges it for a token server-side.
 */
export const credentialsInterceptor: HttpInterceptorFn = (request, next) =>
  isApiRequest(request)
    ? next(request.clone({ withCredentials: true }))
    : next(request);

/**
 * Marks state-changing calls with a custom header.
 *
 * A cross-site form post cannot set custom headers, so requiring one on unsafe verbs blocks CSRF
 * even before the SameSite=Strict cookie is considered. Two cheap defences, because a cookie-based
 * session has to survive one of them being wrong.
 */
export const csrfInterceptor: HttpInterceptorFn = (request, next) => {
  const isUnsafe = !['GET', 'HEAD', 'OPTIONS'].includes(request.method);

  return isUnsafe && isApiRequest(request)
    ? next(request.clone({ setHeaders: { 'X-CSRF': '1' } }))
    : next(request);
};

/**
 * Gives each request a correlation id and keeps it in the response for support.
 *
 * The same id reaches the API, its events, its background jobs and its audit rows, so one value
 * answers "what happened when the user clicked this".
 */
export const correlationIdInterceptor: HttpInterceptorFn = (request, next) =>
  isApiRequest(request)
    ? next(
        request.clone({
          setHeaders: { 'X-Correlation-Id': crypto.randomUUID() },
        }),
      )
    : next(request);

/** Raised for callers that want the parsed problem document rather than an HttpErrorResponse. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails,
  ) {
    super(problem.message ?? problem.detail ?? problem.title ?? 'The request failed.');
    this.name = 'ApiError';
  }

  /** Stable code to switch on, e.g. `LEAVE_INSUFFICIENT_BALANCE`. */
  get errorCode(): string {
    return this.problem.code ?? 'UNKNOWN';
  }

  /** Field-level messages grouped by field, ready to bind onto a reactive form. */
  get fieldErrors(): Readonly<Record<string, readonly string[]>> {
    const grouped: Record<string, string[]> = {};
    for (const { field, message } of this.problem.errors ?? []) {
      (grouped[field] ??= []).push(message);
    }
    return grouped;
  }

  /** Quote this in a bug report. */
  get traceId(): string | undefined {
    return this.problem.traceId;
  }
}

/**
 * Turns API failures into something the UI can act on.
 *
 * Only the cases that need a global response are handled here - session expiry and step-up
 * authentication. Everything else is re-thrown as an {@link ApiError} for the feature to handle,
 * because a toast saying "validation failed" is useless next to the field that is actually wrong.
 */
export const errorInterceptor: HttpInterceptorFn = (
  request: HttpRequest<unknown>,
  next: HttpHandlerFn,
): Observable<HttpEvent<unknown>> => {
  const router = inject(Router);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const problem: ProblemDetails =
        typeof error.error === 'object' && error.error !== null
          ? (error.error as ProblemDetails)
          : { status: error.status, title: error.statusText };

      if (error.status === 401) {
        // The session expired or was revoked. A full navigation rather than a router one, so every
        // store and cache holding the previous session's data is discarded with the page.
        window.location.href = `/sign-in?returnUrl=${encodeURIComponent(router.url)}`;
        return throwError(() => new ApiError(error.status, problem));
      }

      if (error.status === 403 && problem.code === 'FEATURE_NOT_ENABLED') {
        void router.navigate(['/feature-unavailable']);
      }

      // The server asks for a fresh MFA challenge before a sensitive action.
      if (
        error.status === 401 ||
        error.headers
          ?.get('WWW-Authenticate')
          ?.includes('insufficient_user_authentication')
      ) {
        void router.navigate(['/step-up'], {
          queryParams: { returnUrl: router.url },
        });
      }

      return throwError(() => new ApiError(error.status, problem));
    }),
  );
};
