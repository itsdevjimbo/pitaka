---
status: accepted
---

# An email change is pending until the new address is proven

ASP.NET Core Identity's `ChangeEmailAsync` writes the new address in place: redemption of the token
*is* the confirmation. Wire it up the way the primitive suggests — take the new address, write it,
drop the Profile to unconfirmed, wait for the click — and ADR 0012 finishes the job for you. An
unconfirmed Profile cannot sign in and is never issued a token. A person who mistypes their new
address is locked out of their entire financial history, permanently, with no surface anywhere in
the API able to put it back.

This ADR records the shape chosen instead, settled by grilling ticket `02-email-change-semantics`
under the profile-self-service effort. The spec is `.scratch/email-change/spec.md`.

## The decision

**The live address does not move until the new one is proven.**

A signed-in Profile submits a new address together with its current password. The address is stored
as a **pending email** on the Profile with its own expiry. Nothing about the live Email, the
UserName, or the confirmed state changes, and the person stays signed in throughout. When the
confirmation link is redeemed, one transaction sets `Email`, re-mirrors `UserName`, and clears the
pending columns. An unredeemed pending address expires and is thereafter treated as absent.

Three things follow that would not follow from the primitive alone:

- **The pending address is stored, not merely carried by the token.** `ChangeEmailAsync`'s token
  binds the new address inside its own protected payload and persists nothing, so this flow could
  have been built with no schema change. Storing it is what makes the pending change visible on the
  Profile, cancellable, and — because redemption validates the token's address against the stored
  value — singular. Without it a person accumulates live tokens for every address they ever typed,
  all of them redeemable.

- **The current password authorises the change.** The confirmation link proves control of the *new*
  address, which in the case worth guarding against is the attacker's own. The password is the only
  thing in the flow that proves identity.

- **Email uniqueness becomes a database constraint.** `EmailIndex` on `normalized_email` is not
  unique today; only `UserNameIndex` is, so email uniqueness holds sideways, as a side effect of the
  UserName mirror. A migration makes it unique, which also gives the request-then-redeem race a real
  backstop.

## Considered options

**Flip the address immediately and drop the Profile to unconfirmed** — what the primitive does
unaided. Rejected: composed with ADR 0012 it turns a typo into a permanent lockout from a person's
financial history. Cheapest to build and unshippable.

**Flip immediately, but exempt a mid-change Profile from the confirmation gate.** Rejected: it puts
a hole in ADR 0012's gate to work around a problem the pending model does not have, and the exempt
state would need explaining everywhere the gate is reasoned about.

**Keep the flow stateless — token only, no stored pending address.** Rejected for what it cannot
express: no pending state to show a person, nothing to cancel, and no way to make a superseded
request's link stop working. Cheapness in the schema bought incoherence in the flow.

**Answer an already-taken address with an indistinguishable `202`,** matching `forgot-password`.
Rejected: the endpoint is already behind a real session, ADR 0012 records that `POST /register`'s
`409` makes address existence answerable anyway, and hiding it here leaves the person waiting on
mail that will never be sent.

## Consequences

- **A typo is free.** The link never arrives, the pending address expires, the live address never
  moved. This is the property the whole decision exists to buy.

- **New schema.** A nullable pending email and expiry on the Profile, plus a migration making the
  email index unique.

- **A second path sets `EmailConfirmed`.** `ChangeEmailAsync` sets it on redemption. Only a
  signed-in — therefore already confirmed — Profile can reach it, so it is a no-op, accepted rather
  than asserted against. Written down so it does not later read as an unexplained code path.

- **Redemption rotates the security stamp,** killing outstanding password-reset and confirmation
  links. Consistent with ADR 0013, and correct.

- **Atomicity is ours to keep.** `ChangeEmailAsync` does not touch `UserName`, and Microsoft's
  reference UI syncs it in a second, non-atomic call. In this repo a mismatch between Email and
  UserName is a person who cannot sign in, so the two writes and the pending clear commit together.

- **Two emails per request, and no throttle in front of them.** The API has no rate limiting
  anywhere today, so there is no existing scheme for this flow to adopt. Throttling the
  email-sending endpoints as a group is named as its own follow-up rather than invented here, one
  endpoint at a time.

- **It does not close the JWT revocation gap.** Sessions issued before the change survive it. Gap
  Register B1/B2, already named by ADR 0011 as its own future slice.
