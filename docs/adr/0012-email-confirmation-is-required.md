---
status: accepted
---

# Email confirmation is required before a Profile can sign in

`POST /register` returns a JWT the instant a Profile is created, with nothing proving the email address belongs to the person who typed it. B3's spec named this as exactly why B5 — email verification — would matter "if public signup were ever a commitment". The project has since made real-system security a goal, so an unverified address holding a live session is a hole to close, not a deferred maybe.

This ADR records the decision, taken as part of the Identity adoption (ADR 0011). It lands in slice S2; the endpoint shapes, status codes, and email contents are that slice's to settle. `status: accepted` is correct now even though the behaviour ships later, because the decision is settled.

## The decision

A Profile must confirm control of its email address before it can sign in. An unconfirmed Profile cannot sign in and is never issued a token.

Two things follow that a client can see, and each is a contract bend with a paired pitaka-web issue:

- **`POST /register` stops returning a session.** On success it returns the Profile and no token, and a confirmation link is sent to the address. The registration screen routes to a "check your inbox" screen instead of into the app.
- **`POST /login` gains a refusal state for a correct password against an unconfirmed Profile** — distinct from a wrong password, so the person is told confirmation is what is missing rather than assuming they forgot the password.

A confirm-email endpoint and a resend-confirmation endpoint are added to complete the flow. Resend answers every address identically, the same indistinguishability `forgot-password` already has.

## This supersedes `registration-contract` user story 9

`registration-contract` user story 9 asked that a wrong password and a correct-but-unusable password be indistinguishable on `POST /login`, so the endpoint could not be used to check which addresses have a Profile. That held while every registered Profile could sign in.

With confirmation required, a correct password against an unconfirmed Profile now returns `403` where a wrong password returns `401`. The two **are** distinguishable, deliberately: user story 7 in the `auth-identity` spec wants a person told that confirmation is what is missing. This ADR supersedes user story 9 on that point.

The enumeration resistance user story 9 protected was already partial: `POST /register` returns `409` for a duplicate email, so "does this address have a Profile" was already answerable. What the `403` adds is only that a known address's confirmation state is visible to someone who also knows its password — not the threat the indistinguishability rule was for.

## Considered options

**Confirm on a timer, not a gate — let an unconfirmed Profile sign in for a while, then bar it.** Rejected: it is a second lockout-like state to build and explain, and it still issues a token to an unverified address in the meantime, which is the exact hole.

**Keep issuing the token at register, require confirmation only for the next sign-in.** Rejected: the first session is the one most worth protecting — it is the one an attacker who typed someone else's address into signup would get. Requiring confirmation only later protects every session but that one.

**Fold the `403` back into the `401` to preserve user story 9.** Rejected: user story 7 explicitly wants the person told that confirmation is missing, and the enumeration cost is marginal (a duplicate email is already a `409`).

## Consequences

- **Closes Gap Register B5.** Email verification exists; an unverified address cannot hold a Profile or be issued a token.

- **Two contract bends**, each with a paired pitaka-web issue: `POST /register` no longer returns a token, and `POST /login` gains a distinct "confirm your email" refusal.

- **A mistyped signup address simply never confirms.** No email arrives, the Profile cannot sign in, and nobody else's address is confirmed into it. That is the intended behaviour, not an error path.
