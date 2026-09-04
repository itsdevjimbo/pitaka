---
status: accepted
---

# A missing value-type field is a 400, enforced by the deserialiser

`[Required]` on a non-nullable value type validates nothing. `RequiredAttribute.IsValid`
returns false only for `null`, and a bound `int`, `bool`, `decimal`, `DateOnly` or enum is
never null — a JSON body with the property absent binds it to `default(T)` and it passes
every check the attribute runs. The attribute reads like a guarantee and, on those types,
is decoration.

What made this dangerous rather than untidy is that **every enum in this codebase has a
meaningful zero value.** There is no `Unknown = 0` sentinel anywhere: `default(AccountType)`
is `Cash`, `default(GoalStatus)` is `Active`, `default(CategoryType)` is `Income`,
`default(TransactionType)` is `Income`, and so on through `BudgetPeriod`, `Frequency`,
`RecurringTransactionType` and `RecurringTransactionStatus`. A missing enum never produced
an obviously-wrong value that a downstream check might catch. It produced a plausible,
valid-looking one, and the write succeeded silently.

Two `PATCH` endpoints were verified to change state on an empty `{}` body:

- **`PATCH /api/accounts/{id}/status`** retired the account — `default(bool)` is `false`,
  which `PatchAccountActiveStatusRequest.IsActive` took at face value.
- **`PATCH /api/goals/{id}/status`** revived an abandoned goal — `default(GoalStatus)` is
  `Active`, and nothing rejected it.

`PATCH /api/recurringtransactions/{id}/status` has the same shape — one
`[Required] RecurringTransactionStatus Status`, zero value `Active`, and an
`IValidatableObject` that rejects only `Completed`. On an empty body it would reactivate a
cancelled money-movement schedule. Now covered by a test
(`Patch_StatusWithEmptyBody_ReturnsBadRequestAndDoesNotReactivate`).

## The decision

Turn on `RespectRequiredConstructorParameters` in the JSON options
(`Program.cs`, in the `AddJsonOptions` block):

```csharp
options.JsonSerializerOptions.RespectRequiredConstructorParameters = true;
```

With this set, a constructor parameter **with no default value is mandatory** during
deserialization: a body that omits it is a `JsonException`, which `[ApiController]` turns
into a `400` before the action runs. Every request record is a positional record whose
parameters are bound through its primary constructor, so this reaches all of them at once,
and every request record added later is covered by default rather than by remembering to
annotate it.

Nullability does not make a parameter optional — only a default value does. So the switch
is paired with a pass over every request record: **each parameter a client may legitimately
omit is given an explicit default** (`= null` for the nullable ones, `= 0` where a numeric
zero is the intended absent value). The genuinely-required parameters — the enums, the
owning `AccountId`s, `Amount`, the dates a record cannot be built without — are left with no
default and are now mandatory in fact as well as in name.

Two records had an optional parameter sitting before a required one, which C# forbids once
the optional one carries a default. `CreateGoalContributionRequest` and
`CreateRecurringTransactionRequest` have their positional parameters reordered
required-before-optional. Neither is constructed positionally or deconstructed anywhere —
they are only ever deserialized by name and read through `.ToInput()` — so the reordering
has no call site.

The `[Required]` attributes stay. On a value type the attribute no longer does the load-
bearing work, but it still documents intent and still marks the property `required` in the
OpenAPI schema. On the string fields it does exactly what it always did.

## Considered options

**Make each value-type property nullable and keep `[Required]`.** `GoalStatus? Status`,
then `[Required]` means what it says. Explicit, local, reviewable one record at a time, and
it makes "absent" and "the zero value" different things in the type system. Rejected as the
primary fix because it is a dozen records changed by hand with a `.Value` at each use site,
it leaves every *future* request record exposed until someone remembers the pattern, and it
does nothing for the non-enum cases (`bool`, `int`, `DateOnly`) unless each of those is
chased separately. The global switch closes the whole class, including the ones not yet
written.

**Add an `Unknown = 0` sentinel to each enum and validate against it.** Storage is safe —
`PitakaDbContext` persists enums as strings via `EnumToStringConverter`, so a new zero
member shifts nothing on disk. Rejected because it makes an invalid state representable
throughout the domain: every `switch` over the enum grows a case that exists only to be
rejected, and the type stops being a closed set of real values. That is the wrong
direction — the fix belongs at the wire boundary, not in the domain model.

**Do nothing per-field; add tests.** Rejected on its own. No test anywhere in the suite
asserted a `400` for a missing enum, which is why this survived as long as it did. Whatever
the fix, it needed coverage — so the tests were written regardless (see Consequences), but
they guard the switch rather than substitute for it.

## Consequences

- **The blast radius was the whole API surface, and it was walked.** Turning on the switch
  without the defaulting pass would have started returning `400` from any endpoint whose
  request record quietly relied on a parameter defaulting — including paths with no test
  covering them. Every request record was read and classified field by field; the full
  suite (515 tests) passes unchanged, which is the evidence that the optional-field pass
  was complete.

- **`RespectRequiredConstructorParameters` is retroactive and hard to reverse once clients
  depend on it.** A client that today gets a silent default will, after this, get a `400` —
  and once callers rely on the stricter contract, loosening it back is itself a breaking
  change. This is the reason the decision is recorded here rather than made in passing.

- **Coverage was added for the missing-field case**, all asserting `400` *and* that nothing
  was written — the persisted status is unchanged, or no row was created and no balance
  moved. The empty-body `PATCH` endpoints get one test each
  (`Patch_ActiveStatusWithEmptyBody_ReturnsBadRequestAndLeavesStatusUnchanged` on accounts,
  and the parallel `Patch_..._WithEmptyBody_ReturnsBadRequestAndLeavesStatusUnchanged` on
  categories, goals and recurring transactions). Each `POST` endpoint gets a
  `Create_WithoutRequiredField` `[Theory]` that removes one required key at a time and
  covers every non-defaulted field — enums, the owning `AccountId`, `Amount`, the required
  dates. An explicit `null` for a value-type field already `400`'d before this change —
  System.Text.Json rejects `null` for a non-nullable value type — so the new tests omit the
  key entirely, which is the case the attribute missed.

- **An explicit `null` and an absent key are now the same `400` for a required field**, and
  a present `null` for an *optional* field still binds to the default. The distinction the
  nullable-per-field option would have drawn in the type system is not drawn here; the
  boundary just rejects both.

- **`CreateGoalContributionRequest` and `CreateRecurringTransactionRequest` carry a comment
  explaining their parameter order.** The order is now load-bearing — move a required
  parameter after an optional one and the record stops compiling — so the reason is written
  next to it.

- **Extra JSON properties are still ignored**, as before. The switch tightens what a missing
  property means, not what an unexpected one does.
