---
status: accepted
---

# Password-reset tokens are stateless, not a single-use store

B3 shipped password reset as a bespoke single-use token store two weeks ago: a `PasswordResetToken` table, a SHA-256 hashing helper so the raw token is never at rest, a unique index, an `ExpiresAt`, a `UsedAt`, and a "spend every sibling on success" rule that guarantees only one live reset link at a time. It works. It is also a credential-security primitive the project now owns and must keep sound, and ASP.NET Core Identity ships the same primitive as a stateless, key-ring-backed token provider that needs no table.

This ADR records the move, taken as part of the Identity adoption (ADR 0011). It lands in slice S3. It supersedes the token-store decisions in `.scratch/password-reset/spec.md` — the entity, the SHA-256 helper, the unique index, and the `UsedAt` sibling sweep.

## The decision

`RequestPasswordReset` and `ResetPassword` move onto `UserManager.GeneratePasswordResetTokenAsync` and `ResetPasswordAsync`. The `PasswordResetToken` entity, its `Hash` helper, its `DbSet`, its `HasIndex`/`OnDelete` configuration, and the DbContext relationship are deleted, and a migration drops the `password_reset_tokens` table.

### What is preserved

- **`POST /forgot-password` still returns `202` for every address.** `RequestPasswordReset` resolves the Profile with `FindByEmailAsync` and, on a miss, short-circuits as a silent no-op. It still returns nothing and still cannot start distinguishing a known address from an unknown one.
- **One non-specific `400` for every bad-token case.** `ResetPassword` collapses every `ResetPasswordAsync` failure — unknown id, bad token, expired token, even a password-strength error that should never reach it — into a single `400` `ProblemDetails` with unchanged wording. Identity's own error messages do not leak.
- **"Use one reset link and the others die."** `ResetPasswordAsync` rotates the Profile's `SecurityStamp` on success, and `DataProtectorTokenProvider` binds every token it issues to the stamp as it stood at issue time. Every other outstanding reset link stops validating — and so does any outstanding email-confirmation link, which is correct. This is B3's sibling-sweep guarantee, preserved with no code owning it.

### What changes

- **A `userId` returns to the reset request and the reset link.** `POST /api/auth/reset-password` takes `{ userId, token, password }`, and the reset email's link carries `?userId=&token=`. `ResetPasswordAsync` needs the resolved Profile, and the token is not self-identifying. This is a contract bend with a paired pitaka-web issue: the reset screen reads `userId` from the link's query string alongside the token.
- **A Data Protection key ring persisted to the database becomes a dependency.** `AddDataProtection().PersistKeysToDbContext<PitakaDbContext>()`. The default key ring lives at `~/.aspnet/DataProtection-Keys`, which is per-machine and, in a container, wiped on every deploy — taking every outstanding confirmation and reset token with it. Persisting the ring to the database is what makes tokens survive a redeploy. It adds one table on slice S1's migration. This is the `APP_KEY` role from the Laravel world, played by a rotating key set rather than a single static secret.

## What the stateless provider gives up

**An injectable clock for the expiry boundary.** B3's `ResetPasswordTest` used a `FakeTimeProvider` to assert the behaviour one second before and one second after `ExpiresAt`. `DataProtectorTokenProvider` owns token lifespan through `DataProtectionTokenProviderOptions.TokenLifespan` and does not take an injectable clock, so that boundary is no longer testable at the action seam. It is the framework's tested behaviour now; the HTTP arc still covers "an expired link fails as one `400`" by other means.

**A row to inspect.** B3 had a deliberate below-seam test asserting the stored token is not equal to the emailed token — proof the hash-at-rest worked. There is no store to assert on. The stateless token is a signed, encrypted blob by construction; there is nothing at rest to hash.

Both are accepted. They are the cost of not owning the primitive.

## Considered options

**Keep the table, keep the sweep.** The status quo. Rejected on what the project then owns: a credential-security primitive whose correctness — the hashing, the expiry check, the atomic sibling sweep, the unique index doing its job — is the project's to keep sound as the surface grows. Identity ships the same guarantees, tested and maintained, for the cost of a key-ring table.

**Identity's provider, but keep the `PasswordResetToken` table as an audit log.** Rejected: it is a table that is written and never read, and "using one link kills the others" would then live in two places — the security stamp and a sweep — that must agree. The whole point is to have one mechanism.

**A static reset secret instead of a persisted rotating key ring.** Simpler config, one value in `appsettings`. Rejected: it forgoes key rotation, and a leaked secret then compromises every reset and confirmation token ever issued with no way to roll forward. The persisted rotating ring is Identity's designed shape and the marginal complexity is one table.

## Consequences

- **`password_reset_tokens` is dropped** — the entity, the `Hash` helper, the `DbSet`, the index and cascade configuration, and a migration to drop the table. The deletion lands in the same slice (S3) that stops using it, so no dead table lingers behind this `accepted` ADR.

- **`POST /reset-password` gains a `userId` field and the reset link gains a `userId` query parameter.** Contract bend, paired pitaka-web issue.

- **A persisted Data Protection key ring is now infrastructure.** One table, added on S1's migration, required in every environment. A redeploy that loses it invalidates every outstanding confirmation and reset link.

- **The expiry boundary and the hash-at-rest are no longer unit-tested here.** They are the framework's, covered by the HTTP arc at the level a client observes. `ResetPasswordTest`'s `FakeTimeProvider` pair and B3's below-seam token-inequality case retire.

- **One shared token lifespan.** `DataProtectorTokenProvider` is the default for reset, email-confirmation, and change-email tokens, and `TokenLifespan = TimeSpan.FromHours(1)` covers all of them — matching B3's `PasswordResetOption.TokenLifetime`. Splitting confirmation onto a longer-lived named provider is deferred until there is a signal that hour-stale confirmation links are a support burden.
