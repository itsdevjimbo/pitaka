---
status: accepted
---

# Email confirmation is required before a Profile can sign in

`POST /register` returns a JWT the instant a Profile is created, with nothing proving the email address belongs to the person who typed it. B3's spec named this as exactly why B5 — email verification — would matter "if public signup were ever a commitment". The project has since made real-system security a goal, so an unverified address holding a live session is a hole to close, not a deferred maybe.

This ADR records the decision, taken as part of the Identity adoption (ADR 0011). It lands in slice S2. `status: accepted` is correct now even though the behaviour ships later, because the decision is settled.

## The decision

A Profile must confirm control of its email address before it can sign in. `SignIn.RequireConfirmedAccount = true`, so `SignInManager.CheckPasswordSignInAsync` returns `IsNotAllowed` for a Profile whose `EmailConfirmed` is still `false` even when the password is correct.

`POST /register` therefore **stops returning a session**. On success it returns `201` with the Profile and no `token` field — `UserResponse`, not `LoginResponse` — and sends a confirmation email carrying a `GenerateEmailConfirmationTokenAsync` token and the Profile's id as query parameters on a configured client URL. `GenerateJwtToken` is no longer called on the register path.

Two new anonymous endpoints support the flow:

- **`POST /api/auth/confirm-email`** — body `{ userId, token }`, resolves the Profile by id and calls `ConfirmEmailAsync`. `204` on success; one `400` `ProblemDetails` for an unknown id, a bad token, or an expired token, indistinguishable.
- **`POST /api/auth/resend-confirmation`** — body `{ email }`, always `202`, known address or not. Sends a fresh confirmation email only when the address has an unconfirmed Profile.

`POST /login` gains one new failure branch from the result it already gets: `IsNotAllowed` (correct password, unconfirmed email) returns `403` `ProblemDetails` with a `detail` the client can show, so a person looks in their inbox instead of assuming they forgot the password.

## This supersedes `registration-contract` user story 9

`registration-contract` user story 9 asked that a wrong password and a correct-but-unusable password be indistinguishable on `POST /login`, so the endpoint could not be used to check which addresses have a Profile. That held while every registered Profile could sign in.

With confirmation required, a correct password against an unconfirmed Profile now returns `403` where a wrong password returns `401`. The two **are** distinguishable, deliberately: user story 7 in the `auth-identity` spec wants a person told that confirmation is what is missing. This ADR supersedes user story 9 on that point.

The enumeration surface this opens is small and was already partial. `POST /register` returns `409` for a duplicate email, so "does this address have a Profile" was already answerable there. `resend-confirmation` and `forgot-password` both return `202` for every address and do not widen it. What the `403` adds is only that a known address's confirmation state is visible to someone who also knows its password — which, for someone who knows the password, is not the threat the indistinguishability rule was protecting against.

## Considered options

**Confirm on a timer, not a gate — let an unconfirmed Profile sign in for 48 hours, then bar it.** Rejected: it is a second lockout-like state to build and explain, and it still issues a JWT to an unverified address for two days, which is the exact hole. A gate is simpler and closes it fully.

**Keep issuing the JWT at register, require confirmation only for the next sign-in.** Rejected: the first session is the one most worth protecting — it is the one an attacker who mistyped someone else's address into signup would get. Issuing it and then requiring confirmation later protects every session but that one.

**Fold the `403` back into the `401` to preserve user story 9.** Rejected: user story 7 explicitly wants the person told that confirmation is missing, and the enumeration cost is marginal (a duplicate email is already a `409`). The spec chooses the clearer message.

## Consequences

- **Closes Gap Register B5.** Email verification exists; an unverified address cannot hold a Profile or be issued a token.

- **`POST /register` no longer returns a token.** This is a contract bend with a paired pitaka-web issue: the registration screen routes to a "check your inbox" screen instead of into the app.

- **`POST /login` gains a `403` state.** Contract bend, paired pitaka-web issue: the sign-in screen shows "confirm your email" and can offer a resend affordance.

- **Two new endpoints** — `confirm-email` and `resend-confirmation`, each with a paired pitaka-web issue.

- **A mistyped signup address simply never confirms.** No email arrives, the Profile cannot sign in, and nobody else's address is confirmed into it. That is the intended behaviour, not an error path.

- **`GetCurrentUser` does not recheck confirmation.** An unconfirmed Profile is never issued a JWT, so a bearer token in hand already means the Profile was confirmed when the token was minted. No per-request revalidation is added.
