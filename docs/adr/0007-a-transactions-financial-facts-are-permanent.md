---
status: accepted
---

# A Transaction's financial facts are permanent

`UpdateTransactionInput` accepts `TransactionDate`, `CategoryId` and `Description`, and `TransactionService.UpdateAsync` (`:141`) assigns those three and nothing else. `Amount`, `Type`, `AccountId` and `TransferToAccountId` are absent from it, and always have been. The reason recorded until now was that changing them "would require reversing the original balance effect first, which is a materially different, not-yet-built operation" — a statement about sequencing. It said the edit was pending.

It is not pending. **A Transaction records a movement of money that already happened, and the facts of that movement never change.** A wrong amount, a wrong type, a wrong account is corrected by deleting the Transaction and recording the right one. Transaction amendment is not deferred work; it is work this project has decided against.

The four fields become `{ get; init; }`, so an assignment is a compile error rather than a silent regression, in the same way and for the same reason `Category.Type` did in ADR 0003.

## The rule

A field is permanent — and takes `init` — when **something has already acted on its value and stored a result that still claims to describe the current value.**

Both clauses carry weight, and each exists to exclude something.

*Acted and stored* excludes `TransactionDate` and `CategoryId`. Both are read constantly: `GetBudgetAmountSpent` (`:30`) filters on category and date, the `from`/`to` filter reads the date on every request (ADR 0005). But every one of those readers computes from scratch when asked. Nothing was written down that a later edit could make false, so both stay revisable — a date that was mistyped, or a purchase filed under the wrong category, are exactly the kind of mistake a person should be able to fix.

*Still claims to describe the current value* excludes `RecurringTransaction` entirely. `GenerateTransaction` reads a recurring transaction's `Type`, `Amount` and `AccountId` and writes a Transaction row; `GetNextRunDate` derives `NextRunDate` from `StartDate` and `Frequency` and persists it. Those are stored results. But a Transaction generated last March records what the standing instruction said in March. Raising the instruction's amount today does not make that row false — it was never a claim about what the instruction says now. So `UpdateRecurringTransactionInput` keeps accepting `Amount`, correctly, and this ADR does not reach it.

## What actually banked a claim on the four fields

- **`Account.CurrentBalance`**, via `UpdateAccountBalance.ApplyTransaction` (`:20`), which switches on `Type` to move the balance by `Amount` on `AccountId`, and on `TransferToAccountId` as well for a Transfer. The balance is not a log of what happened; it asserts that it *is* the initial balance plus the current effect of every Transaction the Account holds. Change any of the four and the assertion is false, with no error and nothing to notice.

- **`ReverseTransaction` (`:43`)** reads the same four to undo that effect at delete time. A Transaction created as Income and reversed as Expense moves the balance the wrong way by twice its amount. This is the sharpest case, because the corruption happens on the *correction* path — the one a person reaches for precisely when something is already wrong.

- **`GoalContribution`**, via `GoalContributionService.CanEarmarkTransaction` (`:61`), which permits an earmark only if the Transaction is an Income into the account or a Transfer into it — reading `Type`, `AccountId` and `TransferToAccountId` together — and then writes the `TransactionId` to a row. The permission is granted once and stored. Flipping any of the three leaves a `GoalContribution` asserting a relationship that no longer holds.

Three readers, all shipped, all persisting their conclusion. This is not the ADR 0003 situation where the load-bearing reader was a single Budget rule; it is broader, and one of the three corrupts the delete path.

## Two further fields, same rule

**`Transaction.RecurringTransactionId`** takes `init` as well. `CONTEXT.md` tells a recorded transaction from a generated one by whether it is null, and the serialiser picks which time frame a `transactionDate` is in from exactly that (#71, shipped). A generated transaction's date is a wall-clock day; a recorded one's is a UTC instant. Flipping the field retroactively changes what a past timestamp *means* on the wire, which is a live claim if anything is.

**`Account.InitialBalance`** takes `init` for the reason ADR 0003 already cited it and could not enforce. It is the money that was already there before the ledger existed — a recorded fact about the past rather than a figure the person maintains — and `CurrentBalance` claims to be it plus every Transaction since. There is no way to revise it that does not either desync `CurrentBalance` or shift it by the same delta, which is the reconciliation-transaction pattern arriving through an undocumented second door.

## Considered options

**Enforce whatever the update contract omits.** The rule #80 was filed with, and the obvious one. Rejected because it catches fifteen fields across three entities and is wrong about several of them: `Transaction.Amount` would be `init` on the grounds that a `PUT` doesn't accept it, which says *permanent* about a value whose exclusion was, until this document, explicitly temporary. A rule that reads a contract's current shape as a domain statement will keep making that mistake — contracts change for scheduling reasons, and the compiler would then be recording the schedule.

**Permanence alone, without a reader.** Rejected because it cannot be checked. Every field on a Transaction can be argued permanent by someone who wants it to be — the row describes a past event, so on that reading nothing about it should ever move, including the description. The reader test is the part that can be settled by reading the code rather than by conviction, and it is ADR 0003's own criterion: what separated `Category.Type` from `Category.Name` was that other rules read it.

**"The facts of the movement are permanent" as the rule rather than the headline.** Rejected, though it survives as this document's first paragraph. *When* the money moved is a fact of the movement, and `TransactionDate` has always been editable — so the phrasing bans an edit the API has allowed since the endpoint existed. A summary that gives the wrong answer on the first field you check is a competing rule, not a summary.

**Build transaction amendment.** The alternative to all of this: accept a new `Amount` or `Type`, reverse the old effect, apply the new one. Rejected on what the operations mean rather than on cost. Correcting `100` to `105` is one transaction with a typo fixed, and is a fair thing to want. Flipping Income to Expense is not a correction — the money moved the other way, so it is a different event wearing an old row's id, and its `GoalContribution` and its balance effect were granted to the event it used to be. Amendment would have to accept the first and refuse the second, which is a rule about `Amount` and `Type` diverging that nothing in the domain supports. Delete-and-recreate answers both honestly and needs no new machinery.

## Consequences

- **The correction path for a wrong figure is destructive, and stays that way.** A person who typed `100` instead of `105` deletes the Transaction and enters it again. `TransactionService.DeleteAsync` reverses the balance effect and removes any `GoalContribution` attached (`:160`), so the delete is correct — but the tags, the description and the earmark are gone and must be re-entered. This is accepted rather than solved. The state being prevented is a silent, permanent disagreement between a balance and its history; the cost is visible retyping. Those are not equivalent risks.

- **`Account.Type` looks like it belongs here and does not.** It is written at create, carried on the wire by `AccountResource` (`:9`), and read by no branch, no query and no calculation in the API. The domain argument for its permanence is real — `CreditCard` is a liability and the rest are assets, so flipping it reclassifies history — but nothing has banked a conclusion on it, so nothing can silently break. It stays settable. If a reader ever appears (a liability-aware net-worth figure, a sign flip on credit cards), it takes `init` then, and this paragraph is the note explaining that the omission was deliberate.

- **`UserId` on both entities stays settable for the same reason.** Ownership never transfers, so it is permanent in the ordinary sense. But authorization is re-checked from scratch on every request — no stored decision assumes it held still — so the rule does not reach it, and `init` on an ownership FK would put EF's relationship fixup in play for no gain.

- **`Account.CurrentBalance` is the one field this rule cannot fix.** It must only ever move through `Increase`/`Decrease`, and nothing enforces that. `init` breaks those two methods, and `private set` breaks the two object initialisers that seed it. It needs a different construction shape and is filed as #93.

- **`Transaction.IsRecurring` is a settable duplicate of a field this ADR defends.** It states the same fact as `RecurringTransactionId` and is allowed to disagree with it; `TransactionFactory` (`:29`) derives one from the other, which is the tell. Removing it changes the read contract, so it is filed as #94 rather than folded in.

- **A stray `amount` or `type` in a `PUT` body is silently ignored**, as extra JSON properties are during model binding. Same position as ADR 0003 took for a stray `type` on a category, and acceptable for the same reason: nothing sends one. If a client ever needs to express "change this amount", it gets a `200` and no change, which is the point at which the update request should grow the field purely to refuse it.

- **Re-opening this means overturning the document, not resuming work.** That is the intended cost, and it is the difference between this ADR and the sentence it replaces. "Not yet built" invites someone to build it; a decision has to be argued down.
