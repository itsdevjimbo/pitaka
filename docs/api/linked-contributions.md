# Linked Contribution reads and corrections

`GET /api/transactions/{transactionId}/linked-contributions` is the authoritative current-state
read for a Transaction's Linked Contributions. One response contains the Transaction amount,
linked total and signed remaining capacity; the Account's current balance, total earmarks and signed
available headroom; and every linked Contribution with its Goal name. Totals are not clamped. The
response is one coherent database snapshot. Historical rows remain visible when their Account is
retired or their Goal is no longer Active. A missing Transaction and one owned by somebody else both
return `404`.

Existing global, Goal-owned and individual Contribution reads continue to return the complete
Contribution resource. A nullable `transactionId` distinguishes an ordinary Contribution from a
Linked Contribution.

`PUT /api/goal-contributions/{id}` is note-only. Its request is `{ "note": string | null }`.
Contribution date, amount, Account, Goal and source Transaction are fixed facts; correct them by
deleting and recreating the Contribution. Dates remain required during creation and remain present in
reads.

`DELETE /api/goal-contributions/{id}` deletes only that Contribution. It returns `204` when it removes
the row and `404` when the row is absent or belongs to somebody else. Deletion is allowed for retired
Accounts and Completed or Abandoned Goals. It releases Account headroom and, for a Linked
Contribution, Transaction capacity. It does not change Account balance, the source Transaction,
sibling Contributions or Goal lifecycle.

`DELETE /api/transactions/{id}` refuses to remove a Transaction while any Linked Contributions
refer to it. The response is `409 ProblemDetails` with this additional shape:

```json
{
  "reason": "transaction_has_linked_contributions",
  "transactionId": 42,
  "linkedContributions": [
    { "contributionId": 7, "goalId": 3, "goalName": "Emergency fund" }
  ]
}
```

The complete list is returned for an owned Transaction, including historical links to retired
Accounts or Completed/Abandoned Goals. The refusal changes no Contribution, Transaction, Account
balance or Goal state. Ownership is checked before these facts are disclosed, so existing
not-found/authorization behavior is unchanged. After every Linked Contribution has been removed,
Transaction deletion follows its ordinary behavior and reverses the Transaction's balance effect.

## Client recovery expectations

Keep a Contribution visible until deletion is confirmed. An initial `404` means the displayed data
was stale. After a timeout or generic server failure, do not assume deletion; offer an explicit retry.
A retry that returns `404` establishes absence and completes recovery, without claiming which request
removed the row. After `204` or recovery `404`, refresh the Transaction links/capacity, Account
headroom and affected Goal progress/history. If that refresh fails, deletion remains confirmed while
the surrounding views remain stale or unavailable; the failure must never trigger replacement
creation. Replacement creation is a separate, explicit operation that rechecks current eligibility
and capacity.
