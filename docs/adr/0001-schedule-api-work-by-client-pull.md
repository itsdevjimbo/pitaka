---
status: accepted
---

# Schedule API work by client pull, not by severity

The *Pitaka API Gap Register* catalogues twenty-eight gaps and sorts them by severity: two block the build, eight block launch, thirteen degrade, five are polish. That ordering was formed when the API was the only actor. It no longer is — Pitaka Web exists, has an ordered issue queue, and is the sole consumer of every endpoint here. So API work is scheduled by what the next client screen actually needs, and where two items are equally urgent to the client, the tiebreaker is which one opens .NET surface the author has never touched.

The register remains the inventory. It is no longer the queue.

## Considered options

**Launch-safety** — closing the eight "blocks launch" items so a stranger could hold money in Pitaka — was rejected. Nobody has committed to strangers signing up. Taken seriously it would justify building email verification, token revocation, and password rotation for an application with one user, which is how a learning project acquires a year of unused security surface.

**Severity order, worked top-down** was rejected for the same reason plus one more: the severity labels were assigned without a consumer, so they measure how bad a gap sounds rather than how much it currently costs.

**Learning surface as the primary driver** was rejected as too easy to rationalise. As a tiebreaker it selects between genuinely equivalent options; as a driver it would justify anything.

## The standing rule

An item is pulled when a screen touches its endpoint. That is the whole mechanism — no separate triage pass, no cleanup sprint that quietly never happens. E3, E5, and C3 surfaced exactly this way, and E1 will surface the day a transaction form appears.

When the client cannot wait and works around a gap instead, **the workaround names the gap ID in a comment and is deleted when the gap closes.** Without that, a "degrades" item gets silently absorbed into client code, stops hurting, and never resurfaces — which is how a twenty-eight-item register becomes permanent. `BODYLESS_MESSAGE` in the client's `normalize-error.ts` is the existing instance: it is E1's workaround and had not been marked as one.

## The exception: B3, password reset

B3 is scheduled deliberately, after the Accounts arc, in violation of the rule above.

The pull signal is circular here and cannot be trusted. The client's ADR 0001 deleted Fuse's forgot-password screens *because* this API has no endpoint, no token store, and no email sender behind them. So "no screen pulls B3" is evidence of the gap, not evidence it isn't wanted. That is materially different from currency (client ADR 0005), where the client made a real product decision to defer something it could have consumed.

The learning tiebreaker settles it: password reset opens three surfaces never touched here — an `IEmailSender`, a hashed single-use token store with expiry, and SMTP configuration — where widening a resource record teaches nothing. The `smtp4dev` container already in `docker-compose.yml` records the intent. The client re-adds the deleted screens as part of the same slice.

## Consequences

- **The register's severity labels no longer imply order.** B1, B2, B5, B6, C1, and D1 are all marked "blocks launch" and all stay open while E3 and C3 — merely "degrades" — ship first. This looks like negligence and is not; it is this decision.
- **Gap IDs are frozen.** An ID is allocated once, never reused and never renumbered. Closed gaps keep their ID and are struck through in place; new gaps append to their section's tail. Client code cites these IDs across a repo boundary, so a renumber would silently retarget every citation.
- **A slice is the planning unit; a PR is the shipping unit.** A slice such as *#5 Registration* names its API PR and its client issue, and the two merge independently — a cross-repo atomic PR is not possible, and single-concern PRs are what make review work here.
- **The plan spans both repos; the trackers do not merge.** Issues stay in each repo's own convention — local markdown under `.scratch/` here, GitHub issues in `pitaka-web`.
- Three client ADRs — 0002, 0004, 0005 — are accepted debt waiting on an API change to die. A plan that cannot see them will keep mis-ranking them.
