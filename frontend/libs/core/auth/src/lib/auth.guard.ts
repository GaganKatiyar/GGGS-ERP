import { inject } from '@angular/core';
import type { CanActivateFn } from '@angular/router';
import { SessionStore } from './session.store';

/**
 * Requires a signed-in user.
 *
 * The session is loaded lazily on first navigation rather than eagerly at bootstrap, so a
 * deep-linked user is sent to sign in and returned to the page they asked for, instead of landing
 * on the dashboard having lost their destination.
 */
export const authGuard: CanActivateFn = async (_route, state) => {
  const session = inject(SessionStore);

  if (session.status() === 'unknown') {
    await session.load();
  }

  if (session.isAuthenticated()) {
    return true;
  }

  session.signIn(state.url);
  return false;
};
