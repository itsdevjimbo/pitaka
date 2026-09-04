---
status: accepted
---

# An Account's current balance is only ever moved, never set

`Account.CurrentBalance` was `public decimal CurrentBalance { get; set; }`. `CONTEXT.md` says it is "not a figure anyone states: it is the initial balance plus every Transaction the Account has recorded, and it stays fully explained by that history", corrected against the real world "by recording the difference as a Transaction, never by editing the number". The code let any caller assign it directly and contradicted its own glossary — the same shape ADR 0003 found on `Category.Type` and ADR 0007 found on the four permanent Transaction fields.

ADR 0007 named this field explicitly as the one its rule could not reach: `init` breaks `Increase`/`Decrease`, which are *meant* to mutate it after construction, and `private set` breaks the two object initialisers that seed it from outside the class — `AccountService.CreateAsync` and the test `AccountFactory`. It filed the construction-shape question as #93. This ADR answers it.

## The decision

`CurrentBalance` becomes `{ get; private set; }`. `Increase` and `Decrease` — the two methods on `Account` that `UpdateAccountBalance` calls (`ApplyTransaction :20`, `ReverseTransaction :43`) — are then its only writers, and any other assignment is a compile error rather than a silent balance/history disagreement.

The seed path stops being an object initialiser. `Account` gains a static factory:

```csharp
public static Account Open(int userId, string name, AccountType type, decimal initialBalance) =>
    new()
    {
        UserId = userId,
        Name = name,
        Type = type,
        InitialBalance = initialBalance,
        CurrentBalance = initialBalance
    };
```

`initialBalance` seeds both fields in one place — an Account with no Transactions yet *is* its initial balance — and the initialiser runs inside the declaring type, so it reaches the private setter. `AccountService.CreateAsync` and `AccountFactory.Make` both route through `Account.Open`; the `Increase`/`Decrease` call sites are untouched.

This is the first domain factory method in the codebase. `Transaction` and `Category` are still built with object initialisers, because nothing on them needs a writer the initialiser cannot be. `Account` does.

## Why `Open` and not `Create`

`AccountService.CreateAsync` and `AccountFactory.CreateAsync` already exist and mean "construct **and persist**". The model-level method that only constructs needs a different verb, and `Open` is the one the domain uses for bringing an account into being — `CONTEXT.md` gains an **Opened** entry to record it. Renaming the two service/factory methods to free up `Create` was rejected as churn well outside #93's scope.

## Considered options

**A constructor taking `initialBalance`.** `public Account(decimal initialBalance)` setting both balance fields, plus a private parameterless constructor for EF. Rejected on how the call sites read: every other field is still `required` and still set by initialiser, so construction becomes `new Account(x) { UserId = ..., Name = ..., Type = ... }` — one value in the parentheses, the rest in the braces, with nothing signalling why `initialBalance` is the odd one out. A static `Open` puts every field in one list and names the operation.

**A seeding method called after construction.** `new Account { ... }` then `account.SeedBalance(initialBalance)`. Rejected: it is `Increase` from zero wearing a different name, it can be forgotten, and an `Account` between the two calls is a half-built object with a zero balance that typechecks. The factory has no such window.

**Let `InitialBalance`'s `init` accessor also set `CurrentBalance`.** Smallest diff — both call sites keep their object initialiser and just drop the `CurrentBalance` line. Rejected as too subtle to trust: it depends on EF materialisation writing `InitialBalance` before `CurrentBalance` on every rehydration of an account that already has history, or on EF using backing fields and bypassing the side effect entirely. Either way the correctness of every existing balance rests on property-write ordering that nothing in the code makes visible. `Open` seeds at construction only and leaves rehydration alone.

## Consequences

- **The `CONTEXT.md` rule is now enforced rather than described.** Nothing outside `Account` can correct a drifted balance; the only path is to record the difference as a Transaction, which runs through `UpdateAccountBalance` and therefore through `Increase`/`Decrease`. That was always the intended design and is now the only one that compiles.

- **`InitialBalance` and `CurrentBalance` are seeded together, once.** ADR 0007 gave `InitialBalance` `init`; `Open` sets it in the same initialiser as `CurrentBalance`. The two balance fields can no longer be seeded to different values by mistake, because there is one expression that seeds them and it uses the same operand twice.

- **No migration and no schema change.** `private set` is a C# visibility concern; the column is untouched. EF writes the private setter during materialisation as it always did, so accounts with history round-trip unchanged — the concurrency and balance-update suites, which reload accounts after `Increase`, stay green.

- **The test `AccountFactory` makes an inactive account by calling `Deactivate()` after `Open`.** `Open` does not take `isActive` — it is not a fact about how an account comes into existence, it is a state an account is later put into, and `Deactivate()` is how everything else puts it there. The factory's public signature is unchanged.

- **`Open` omits `Version`, `IsActive`, and the timestamps.** `Version` and the timestamps are EF-managed; `IsActive` defaults to `true` at the field. A new account is active with a fresh optimistic-concurrency token, and the factory does not restate that.

- **A stray assignment to `CurrentBalance` from a new service is a build failure**, the same position ADR 0003 took for `Category.Type` and ADR 0007 for the Transaction fields. There is no runtime guard and none is needed.

- **Re-opening this means changing the construction shape back**, not resuming deferred work. #93 is closed; the question of how an `Account` is built has an answer.
