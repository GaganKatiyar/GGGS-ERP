# ADR-13: Remove two-step sign-in

- **Status:** Accepted (2026-10-29)
- **Context:** [Architecture §8.3, authentication](../architecture/architecture.md), the M12 milestone in
  the [roadmap](../roadmap.md)
- **Supersedes:** the MFA part of the M12 security work

## Decision

PeopleOS no longer offers two-step sign-in with an authenticator app (TOTP). Signing in takes a
password, or the organisation's own OpenID Connect provider through single sign-on.

The following were removed:

- **Sign-in flow:** enrolling an authenticator, one-time recovery codes, the second sign-in step
  (`/bff/login/mfa`), and the `/api/v1/me/security/mfa` endpoints.
- **Policy:** the password policy's MFA requirement (Off / Administrators only / Everyone).
- **Data:** the user's MFA columns, dropped by migration `RemoveTwoStepSignIn`.
- **Screens:** the "Sign-in security" page (`/me/security`) and its links in My Space and the user
  menu.

**Step-up stays.** Sensitive permissions still demand a recent authentication (15 minutes in
production, `Gggs:Security:EnforceStepUp`). When it has lapsed, the "Confirm it's you" dialog now
asks for the password only (`/bff/step-up` takes `{ password }`), and `StepUpService` replaces the
step-up half of the old `MfaService`. Single sign-on is unchanged. A provider's own second factor
still applies at the provider; PeopleOS no longer records it in the `amr` claim.

## Why

This was the product owner's decision. The default policy required an authenticator for every
administrator, so the administrator account could not use the application until one was set up.
The owner chose to remove the feature rather than switch the policy off.

## Consequences

- **Accounts are protected by a password alone.** This includes administrators and anyone who can
  see bank details, salaries or payroll. The remaining protections are:
  - lockout after repeated failures;
  - rate limits on credential endpoints;
  - breached-password checks;
  - short idle and absolute session lifetimes;
  - step-up re-entry of the password before sensitive actions.
- **Single sign-on is the way to get a second factor.** An organisation that needs one should sign
  in through its identity provider, which enforces its own MFA.
- **Restoring the feature means reverting this change.** The domain, endpoints, tests (`MfaTests`,
  `TotpTests`) and the frontend page are in version control from before 2026-10-29. Restoring them
  needs a new migration to add the columns back.
