---
status: superseded by ADR-0016
---

# A credit card is an Account with a negative balance

A credit card remains an `Account`; Pitaka does not introduce a separate liability entity. An
Account's `CurrentBalance` always means money the person has, so money owed on a card is negative:
a card with ₱5,000 owed has a balance of −5,000, while an overpaid card may legitimately have a
positive balance. Whether an Account is a liability is derived from `AccountType`, not stored as
another field.

This is the only sign convention under which ADR 0010's rule — current balance is the initial
balance plus every Transaction recorded against the Account — remains literal for every Account.
An Expense decreases the balance and Income increases it regardless of type; neither
`UpdateAccountBalance` nor any caller needs a credit-card exception. Summing all current balances,
including those of retired Accounts, consequently means net worth: money held less money owed.

## Considered options

**Store the magnitude of card debt as a positive balance.** Rejected because the same Expense
would then have to increase a credit-card balance while decreasing every other Account. Balance
movement, reversal, opening, reconciliation, and totals would all need branches on `AccountType`,
turning ADR 0010's single invariant into one rule per type. A positive card balance would also be
ambiguous between debt and an overpayment unless the type were consulted every time it was read.

**Introduce a separate `Liability` entity.** Rejected because a credit card has the same ledger
relationship and lifecycle as every other Account in Pitaka. A second entity would duplicate
those concepts and require a migration without serving a behaviour the signed balance cannot
express. Credit limits, statements, minimum payments, or interest may eventually justify a richer
model, but they are not part of this decision.

**Store whether an Account is a liability independently of `AccountType`.** Rejected because it
creates two fields that can disagree. In the current model `CreditCard` is the only liability
account type, so whether an Account is a liability is derived rather than separately recorded.

## Consequences

- `InitialBalance` remains an unconstrained signed amount for every account type. A card can open
  overpaid and a bank account can open overdrawn; the sign is the financial fact, not a validation
  consequence of the type.
- `AccountResource` remains signed and gains no separate liability field. The API has no
  server-side consumer for that derivation. Pitaka Web owns the presentation rule that turns a
  negative card balance into a positive "amount owed"; its single account-type mapping derives
  from `AccountType.CreditCard`.
- Adding another liability account type is a cross-repo change: Pitaka Web's presentation
  mapping must change with this API's enum.
- `Account.Type` is permanent and will take an `init` accessor. The type informed how the person
  signed the stored balance, so changing it would silently change what that number means. This is
  the credit-card sign-flip trigger anticipated by ADR 0007; `Account.Open` remains its only
  production writer.
- The API needs no total endpoint. A sum of every signed current balance already means net worth.
