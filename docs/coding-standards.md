# Coding standards

These rules apply to new code and materially changed behavior. Existing code is evidence of
conventions, not automatic permission to repeat an exception. Keep unrelated cleanup out of a
feature change. A change to an existing HTTP contract needs an explicit migration decision;
adopting these standards does not authorize one.

## Adoption and sources of truth

Use this document for implementation and review. Use [CONTEXT.md](../CONTEXT.md) for domain
language and the relevant [ADRs](adr/) for architectural decisions. If a proposed change
conflicts with an ADR, identify the conflict explicitly before changing the decision.

Keep implementation rules here and domain definitions in the glossary. For example, an
`Account` holds money; `User` is the identity entity; API-authored copy addressing that person
says Profile. Human-facing account lifecycle copy says Retired. Preserve documented wire
compatibility exceptions such as the linked split reason `account_inactive`.

## Ownership

Obtain the authenticated User from the existing authentication flow. Never accept the owning
User ID from client input. Scope private-resource lookups and writes by that identity. Return
404 when a route resource is missing or inaccessible to that User.

For example, a new read method follows this shape (illustrative, not an existing signature):

```csharp
public Task<Account?> GetByIdForUserAsync(
    int userId,
    int accountId,
    CancellationToken cancellationToken
) =>
    _context.Accounts.AsNoTracking().SingleOrDefaultAsync(
        account => account.Id == accountId && account.UserId == userId,
        cancellationToken
    );
```

The controller maps a null result to `NotFound()`. A mutation operation likewise resolves
its target within the authenticated User's scope before changing it. Related IDs supplied
in a body still follow that endpoint's validation contract; the route-resource 404 rule does
not turn every invalid reference into a 404.

Visibility is domain-specific: system-default Categories are readable alongside the User's
own Categories. Reading a shared default does not grant permission to edit it. Internal
integrity queries may deliberately consider references across Users after the target has
been authorized; see `CategoryService`'s usage checks.

This ownership rule does not change two other 403 contracts: mutations of shared system-default
Categories remain forbidden while their reads remain visible, and sign-in for an unconfirmed
Profile remains forbidden as specified by [ADR 0012](adr/0012-email-confirmation-is-required.md).

Linked split creation has a separate idempotent-operation contract: an unavailable source
Transaction returns `409 transaction_missing` for a new operation, while a committed replay
can succeed after that Transaction is removed. Preserve this documented exception; see the
[linked Contribution API contract](api/linked-contributions.md) and ADR 0017.

## Application boundaries

Controllers bind requests, obtain authenticated identity, call the application operation,
and translate its result into HTTP. Services/actions own business eligibility checks and
persistence boundaries. Put a check and the write it protects in the same operation; when
concurrency matters, protect the invariant with database constraints, concurrency guards,
or a transaction rather than relying on an earlier check alone.

For example, transaction creation must own Account eligibility, Category compatibility,
transfer eligibility, tag resolution, balance changes, and persistence within its application
operation. The controller should not separately decide eligibility before calling a write
method that assumes those checks happened.

Request-only validation belongs on Request records: required fields, ranges, and relationships
between supplied fields. Rules requiring stored state belong in services/actions. Keep
entity-local invariants enforced by the entity as well.

Use EF Core directly in application services as the repo already does. Use `AsNoTracking()`
for entity reads that will not be mutated; load tracked entities when their changes will be
saved. Make tracked lookup names explicit. Keep balance changes and their Transaction in the
same atomic unit of work. Multiple saves that must succeed together need an explicit
transaction. Follow [ADR 0017](adr/0017-linked-contributions-and-idempotency-results-commit-together.md)
for linked splits, including concurrency guards, idempotency, and uncertain outcomes.

Existing exception: ordinary Transaction creation still coordinates business checks in the
controller. New or materially revised operations follow the boundary above; unrelated edits
do not require moving an entire controller.

### Services and actions

Keep straightforward operations on the existing service. Extract a named action when an
operation has substantial orchestration, its own transaction or recovery lifecycle, or
reusable business logic. A new endpoint alone does not require a new class. Give each
operation one clear owner for its business checks and persistence boundary; collaborating
helpers do not independently commit portions of that operation.

For example, renaming an Account can remain on `AccountService`; the focused `LoginUser`
action coordinates credential checks and sign-in outcomes. A category compatibility rule
shared by operations can live in `VerifyTransactionCategory`. Existing focused services,
such as `LinkedContributionSplitService`, already provide an operation boundary and need
no rename or extra action wrapper solely to satisfy this rule.

### Expected outcomes and exceptions

Return the smallest type that expresses what the caller needs to distinguish:

| Caller needs | Return shape | Example |
| --- | --- | --- |
| Success or failure, with no distinguishable failure reasons | `bool` | `ResetPassword.ExecuteAsync` |
| Distinct outcomes without associated data | Enum | `TransactionCategoryVerdict` |
| Outcomes carrying different data | Result records | `TransactionDeleteResult` |

Password reset deliberately collapses an unknown Profile, an invalid token, and an expired
token into the same failure. Preserve that indistinguishability. Category validation needs
separate `NotFound`, `TypeMismatch`, and `Retired` outcomes because callers handle them
differently. Transaction deletion must also identify the Contributions preventing deletion:

```csharp
public abstract record TransactionDeleteResult;

public sealed record TransactionDeleted : TransactionDeleteResult;

public sealed record TransactionHasLinkedContributions(
    int TransactionId,
    IReadOnlyList<TransactionLinkedContribution> LinkedContributions
) : TransactionDeleteResult;
```

Keep ordinary application outcomes independent of HTTP. The controller maps a
`TransactionHasLinkedContributions` result to 409 and constructs the response. Preserve the
linked split's durable-response exception described below.

Use exceptions for broken invariants and infrastructure failures, rather than expected
business refusals. The global handler already maps EF optimistic-concurrency exceptions
to 409; preserve its compatibility-sensitive detail string. Unexpected failures are logged
and become generic 500 responses. A failure must not be converted to success merely to fit
a result shape.

Introduce a shared result abstraction only when repeated usage demonstrates a concrete
benefit. Existing enum-plus-payload results such as `LoginResult` can remain; this rule does
not require replacing every result with separate subclasses.

## Requests, inputs, and resources

Use positional Request records for HTTP contracts, with explicit `ToInput()` mapping to
application inputs. Map domain entities to Resource records at the HTTP boundary using
`FromModel` and, when useful, `Collection`.

Required constructor parameters have no default; optional parameters have an explicit default.
Nullability alone does not make a field optional. `[Required]` alone does not reject an omitted
non-nullable value type; the global JSON constructor-parameter setting supplies that guarantee.
See [ADR 0009](adr/0009-a-missing-value-type-field-is-a-400.md).

Existing example from `CreateAccountRequest`:

```csharp
public record CreateAccountRequest(
    [Required, MaxLength(255)] string Name,
    [Required, EnumDataType(typeof(AccountType))] AccountType Type,
    decimal InitialBalance = 0
)
{
    public CreateAccountInput ToInput() =>
        new(Name: Name, Type: Type, InitialBalance: InitialBalance);
}
```

Linked split replay is a deliberate mapping exception: the service persists and returns the
original response status and serialized body. The controller forwards that saved body unchanged,
as required by ADR 0017; rebuilding a Resource from current state would change replay semantics.

## Entity invariants

Use `init` for financial facts whose original value supports a stored consequence, following
[ADR 0007](adr/0007-a-transactions-financial-facts-are-permanent.md). For example, a Transaction's
Amount is permanent because Account balances already incorporate it. Its editable description
does not carry that constraint.

Construct Accounts through `Account.Open(userId, name, type, initialBalance)`, which initializes
both balances together. Change current balances through the existing balance operations.
Other entities may use object initializers when those preserve their invariants; factories
are not mandatory for every entity. See
[ADR 0010](adr/0010-an-account-current-balance-is-only-ever-moved.md).

## Async

Name new asynchronous application methods with an `Async` suffix. Accept a `CancellationToken`
on new asynchronous I/O paths and pass it through every downstream API that supports it,
including EF queries, saves, and transaction operations. HTTP actions take the request token
and pass it to the application operation. Framework-mandated names retain their required shape.

```csharp
public async Task<IActionResult> Show(int id, CancellationToken cancellationToken)
{
    var user = _currentUserAccessor.User!;
    var account = await _accountService.GetByIdForUserAsync(user.Id, id, cancellationToken);
    return account is null ? NotFound() : Ok(AccountResource.FromModel(account));
}
```

This illustrates the read signature above. Existing methods such as
`AccountService.GetAllForUser` lack the suffix and token; update them when that path is
materially revised, without unrelated bulk renaming.

Cancellation does not prove a database commit failed. Linked split recovery deliberately uses
its own bounded token after an uncertain commit; preserve that behavior and recover with the
original idempotency key. Never retry a monetary write automatically. ADR 0017 is authoritative.

## Tests

Choose coverage by the failure it detects, rather than requiring the same CRUD test at every
layer:

| Behavior | Preferred coverage |
| --- | --- |
| HTTP shapes, binding, validation, ownership, and status mapping | HTTP tests using the application factory |
| JWT and real authentication behavior | The separate real-auth application factory |
| Pure calculations | Direct unit tests with explicit inputs |
| Persistence, constraints, atomicity, and concurrency | Integration tests against real MySQL, using independent scopes where needed |

Use xUnit and descriptive `Operation_Scenario_Outcome` test names. Keep tests under the
matching production area. Reuse the database collection fixtures and per-test Users; avoid
introducing another fixture that resets a database used by concurrently running tests.

Use factory `Make` methods for unpersisted entities and `CreateAsync` for persisted setup.
Factories should respect domain construction paths, such as `Account.Open`.

Assert rejected operations leave persisted state unchanged, not just that a status was returned.
For example, an unauthorized account update test should assert the rejection and reload the
Account to verify its name and balances did not change. Inspect persistence through a fresh
scope or a no-tracking query so a tracked instance cannot hide a failed write or rollback.

Overlapping existing controller/service tests need not be deleted. Add another layer's test
when it protects a distinct behavior, not merely to repeat the same CRUD assertions.

### Deterministic inputs and races

Supply fixed dates and explicit values for data that determines the expected outcome.
Use an injected `TimeProvider` and `FakeTimeProvider` in tests when behavior depends on now.
Random factory values are acceptable for incidental fields, such as a unique name, when
those values do not decide the assertion. Override factory defaults that use the wall clock
when dates affect the behavior being tested.

For example, a cadence test fixes both the clock and the starting date:

```csharp
var clock = new FakeTimeProvider();
clock.SetUtcNow(new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero));
var action = new GetNextRunDate(clock);

Assert.Equal(
    new DateOnly(2026, 8, 29),
    action.ExclusiveOfToday(new DateOnly(2026, 8, 21), Frequency.Daily)
);
```

Coordinate concurrency tests with observable pause/release signals and bounded waits.
Assert that each competing operation reached the relevant phase before relying on its
interleaving. Elapsed delay alone is not evidence that an operation reached the database.
Use `SplitFaultSchedule` for split scenarios; release every paused operation during cleanup,
including after assertion failures, before joining outstanding operations.

Existing exceptions include the current-date default in `GoalContributionFactory` and the
250 ms delay in `LinkedContributionReleaseRaceTest`. Follow these rules for new or materially
revised tests; their adoption does not require an unrelated test rewrite.

## Formatting and verification

Follow the existing file-scoped namespaces, constructor injection, and explicit record mapping.
Let CSharpier own formatting. Use braces for condition bodies as configured in
[.editorconfig](../.editorconfig). Consult [the hook configuration](../.husky/task-runner.json)
and [CI](../.github/workflows/tests.yml) for executable checks.

The pre-commit hook applies style formatting and CSharpier to staged C# files. CI checks
CSharpier, builds, and runs tests against MySQL. CI does not separately verify the style
formatter's rules, and nullable warnings are not explicitly elevated to errors. These standards
add review rules; they do not claim new automated enforcement.

Run checks appropriate to the changed behavior using the [README's development loops](../README.md).
Rebuild Docker test images after source changes so verification uses the changed source.
