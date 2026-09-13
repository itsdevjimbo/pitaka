---
status: accepted
---

# Credit cards are not yet an Account type

Pitaka removes `CreditCard` from its fixed `AccountType` vocabulary. A bare enum member exposes a
credit-card promise without the surrounding behaviour people expect, including an amount-owed
presentation and decisions about limits, payments, statements, reconciliation, and interest.
Credit cards return only through a fresh, end-to-end feature decision; until then the API neither
accepts nor returns them as an account type. This supersedes ADR 0015.

## Consequences

- The surviving enum members keep their existing numeric values; the former value `2` is not
  reused, so stored `Wallet` and `Investment` values cannot silently change meaning.
- Negative balances remain valid for supported Accounts and mean that the place is overdrawn.
- The API has only development data, so no production balance migration is required. A local
  database containing the removed value must be recreated.
