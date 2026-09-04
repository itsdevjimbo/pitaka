---
status: accepted
---

# Authentication is ASP.NET Core Identity as a store, with JWT kept

Authentication in Pitaka is four hand-rolled pieces: an inline `new PasswordHasher<User>()` in `RegisterUser`, `LoginUser`, and `ResetPassword`; no lockout on `POST /login`; a bespoke single-use `PasswordResetToken` table with its own SHA-256 helper, unique index, and sibling-sweep rule; and a `POST /register` that hands back a JWT with nothing proving the email is real. Underneath all four, `User` is a plain entity with `Name`, `Email`, `PasswordHash`, and every capability a maintained identity system would provide — a normalized lookup column, a security stamp, a confirmed-email flag, failed-attempt counters — is the project's to add one column and one code path at a time.

This ADR records the decision to stop hand-rolling the credential store and adopt **ASP.NET Core Identity as the store, not as the API surface**. It is the umbrella decision; ADR 0012 records the email-confirmation half and ADR 0013 the reset-token half. The adoption is scheduled against ADR 0001's pull rule as that ADR's fourth recorded exception.

## The decision

`AddIdentityCore<User>()`, then `.AddSignInManager()`, `.AddEntityFrameworkStores<PitakaDbContext>()`, `.AddDefaultTokenProviders()`. This registers `UserManager`, the EF stores, the password hasher and its rehash-on-verify path, the password validators, `SignInManager` with its lockout counters, and `DataProtectorTokenProvider` for reset and confirmation tokens. Nothing about authentication moves:

- **`JwtBearer` stays the default authentication scheme.** `AddJwtAuthentication` is untouched. `GenerateJwtToken` still stamps `ClaimTypes.NameIdentifier` and `ClaimTypes.Email`, still signs HMAC-SHA256, still expires on the configured minutes.
- **No `MapIdentityApi`.** The endpoints stay hand-written on `AuthController`, so their shapes stay the project's to hold. `RegisterUser`, `LoginUser`, `RequestPasswordReset`, `ResetPassword`, `GenerateJwtToken`, and `GetCurrentUser` keep their place in `Actions/Auth/`; what changes is the dependency each takes and the body.
- **No cookie authentication, no refresh tokens, no `/logout`, no token denylist.** The wire contract for a successful sign-in — `{ token, user }` — does not move.

`User` becomes `IdentityUser<int>, ITimestamped`. `PitakaDbContext` becomes `IdentityUserContext<User, int>`.

## `AddIdentityCore` over `AddIdentity` is the load-bearing wiring choice

`AddIdentity()` is the call every tutorial reaches for, and it would silently break JWT authentication. It registers the application and external cookie schemes and calls `AddAuthentication()` with a cookie scheme as the default, so `[Authorize]` with no explicit scheme would stop resolving to `JwtBearer` and every authenticated endpoint would start looking for a cookie that is never set.

`AddIdentityCore` plus `AddSignInManager` gives the store, the hasher, the validators, and the sign-in checks, and leaves the authentication stack alone.

This is the single thing in this work most likely to be "fixed" by a later contributor into a regression — `AddIdentity` reads as the more complete call, the diff to switch is one line, and nothing fails at compile time or in a unit test. A `[Collection]`-scoped HTTP test that authenticates with a bearer token is what would catch it. The wiring choice is deliberate and this ADR is where that is written down.

## `User : IdentityUser<int>` and the `int` primary key

`IdentityUser<int>` rather than the default `IdentityUser` (string/GUID key). The `int` primary key is kept so that every existing foreign key and all 18 migrations are untouched — no key column is retyped, no relationship is rebuilt, no data is rewritten. `Email` and `PasswordHash` become inherited members; `Name` and the navigation collections stay as the project's own additions to the class.

`User` also implements a new `ITimestamped` interface directly, because `IdentityUser<int>` takes the base-class slot that `TimestampedEntity` used to hold and C# has no multiple inheritance. `TimestampedEntity` implements the same interface, so the twelve entities extending it are unaffected and the `SaveChanges` timestamp override retargets from `Entries<TimestampedEntity>()` to `Entries<ITimestamped>()`.

## `IdentityUserContext`, not `IdentityDbContext`

`IdentityUserContext<User, int>` adds `IdentityUserClaim<int>`, `IdentityUserLogin<int>`, and `IdentityUserToken<int>` and nothing else. `IdentityDbContext` would additionally add `roles`, `user_roles`, and `role_claims` for a role model the app has no use for — authorization here is "a valid bearer token for a resolvable Profile" and will stay that way.

The three satellite tables `IdentityUserContext` does map are an **accepted cost**. Nothing in Pitaka writes a row to `user_claims`, `user_logins`, or `user_tokens`; they exist because `EntityFrameworkStores` expects them. They are renamed to snake_case house style so `EFCore.NamingConventions` does not leave `asp_net_*` names in one corner of the schema.

Slice S1's migration therefore adds: the new Identity columns on `users`, the three satellite tables, and — for the reason ADR 0013 sets out — the persisted Data Protection key-ring table. It carries no data backfill: there is no deployed environment, and dev and CI both drop and re-migrate.

## The known gap: a live JWT outlives a credential change

A password reset or a lockout does **not** revoke a JWT that has already been issued. Tokens are validated by signature and expiry only — there is no denylist and no per-Profile version claim — so a token minted before a reset stays valid for the remainder of its configured lifetime, about an hour. If the reset was prompted by a compromise, the attacker keeps that session until it expires on its own.

This is **identical to Pitaka today**. It is not a regression introduced by adopting Identity; it is the existing behaviour, now written down because the work around it makes it worth naming. Closing it needs refresh tokens plus short-lived access tokens — a token denylist or a version claim checked on every request — which is its own future slice and its own ADR. It is named here so the ~1-hour window reads as a known, deliberate consequence rather than something this work broke.

## Considered options

**Keep hand-rolling, close the gaps one at a time.** Add a normalized lookup column, then a security stamp, then lockout counters, then a confirmed-email flag, each as its own small change pulled by its own screen. Rejected: it rebuilds Identity's `users` table column by column and its `UserManager` method by method, and the project would own the correctness of every piece and the interactions between them — exactly the position this ADR is trying to leave. A one-person app holding a person's whole financial history should not depend on a hand-rolled hasher lifecycle, a hand-rolled token store, and a hand-rolled duplicate-key catch each being correct and staying correct.

**`AddIdentity()` with cookie authentication, drop the JWT.** The conventional full-framework setup. Rejected: pitaka-web consumes `{ token, user }` and sends `Authorization: Bearer`. Switching to cookies is a contract break across the repo boundary with no product reason behind it, and it trades a working scheme for a rewrite.

**`MapIdentityApi`.** Let Identity own the endpoint shapes too. Rejected: the endpoints are hand-written on `AuthController` precisely so their request and response shapes stay the project's to hold and to bend deliberately (ADR 0012, ADR 0013). `MapIdentityApi` would hand that control to a framework surface that does not match Pitaka's `ProblemDetails` conventions or its vocabulary.

## Consequences

- **The credential store is a maintained framework's, not the project's.** Password hashing and its upgrade path, rehash-on-verify, lockout, and reset/confirmation token generation are configured rather than written. There is one store to keep sound instead of four.

- **`AddIdentityCore` vs `AddIdentity` is a documented trap.** A later contributor who "completes" the wiring by switching to `AddIdentity` breaks bearer authentication with a one-line diff that compiles. This ADR and the wiring comment are the guard; there is no test that fails at the unit level.

- **Three empty tables enter the schema**, plus the Data Protection key-ring table. `user_claims`, `user_logins`, `user_tokens` — mapped, renamed to house style, never written to. `IdentityUserContext` over `IdentityDbContext` already keeps three more (the role tables) out. The key-ring table is written to (ADR 0013).

- **`User` moves off `TimestampedEntity` onto `ITimestamped`.** `IdentityUser<int>` takes the base slot. The interface keeps `CreatedAt`/`UpdatedAt` working through the `SaveChanges` override; the twelve `TimestampedEntity` subclasses do not move.

- **A reset or a lockout still leaves a live JWT valid for ~1 hour.** Unchanged from today, named as a known gap, closed only by a future refresh-token slice. Recorded so it is not rediscovered as a surprise regression.

- **`GenerateJwtToken`, `GetCurrentUser`, `ResolveCurrentUserFilter`, and `CurrentUserAccessor` are untouched.** How a request's Profile is resolved does not ripple from this change.
