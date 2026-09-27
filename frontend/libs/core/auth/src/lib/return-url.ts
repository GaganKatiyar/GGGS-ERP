/**
 * Where to go after signing in.
 *
 * Only same-site paths are followed. An unchecked `returnUrl` is an open redirect, and the sign-in
 * page is the worst place to have one: a link to our own domain could land the user on an attacker's
 * site immediately after they typed a password, with our address bar still fresh in mind.
 *
 * Rejected, and why each case matters:
 * - anything not starting with `/` - an absolute URL to another origin
 * - `//evil.example` - protocol-relative, which browsers treat as another origin
 * - `/\evil.example` - some browsers normalise the backslash to a slash, making it protocol-relative
 * - `sign-in` itself - it would bounce the user straight back to where they just were
 */
export function safeReturnUrl(
  returnUrl: string | null | undefined,
  fallback = '/dashboard',
): string {
  if (returnUrl === null || returnUrl === undefined || returnUrl === '') {
    return fallback;
  }

  const trimmed = returnUrl.trim();

  if (!trimmed.startsWith('/')) {
    return fallback;
  }

  if (trimmed.startsWith('//') || trimmed.startsWith('/\\')) {
    return fallback;
  }

  // Control characters can be used to smuggle a second header or split a URL past a naive check.
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f]/.test(trimmed)) {
    return fallback;
  }

  if (trimmed === '/sign-in' || trimmed.startsWith('/sign-in?')) {
    return fallback;
  }

  return trimmed;
}
