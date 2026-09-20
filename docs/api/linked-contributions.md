# Linked Contributions

## Create an atomic Transaction split

`POST /api/transactions/{transactionId}/linked-contributions` is the only Linked Contribution
creation route. One row is the single-Goal case. Send one UUID in the `Idempotency-Key` header and
this body:

```json
{
  "contributionDate": "2026-09-19",
  "contributions": [
    {
      "goalId": 12,
      "amount": 500.00,
      "note": null,
      "acknowledgeTargetOverrun": false
    }
  ]
}
```

The date is required and is applied unchanged to every row. Do not send an Account, datetime, or
timezone: every row uses the source Transaction's Account. There must be at least one row, each Goal
may appear once, and amounts must be positive, at least `0.01`, cent-precise, and no more than
`999999999999.99`. Rows are identified in errors by zero-based position and Goal ID. Notes are
optional and preserve supplied text, including empty text and whitespace. Target-overrun
acknowledgement defaults to `false`.

Success is `201`. It returns the Transaction amount and post-operation linked capacity, a coherent
Account headroom snapshot, and only the newly created complete Contribution resources in submitted
order. The snapshot describes the successful operation; refresh authoritative reads for current
state after success or replay.

Malformed structure, unavailable Goals, duplicate Goals, and invalid amounts or dates return `400
ValidationProblemDetails` with indexed field paths. Definitive state refusal returns `409
ProblemDetails` with `created: false` and a `failures` array. The top-level `reason` is the one failure
reason or `split_rejected` when several apply. Stable reasons are `goal_inactive`, `account_inactive`,
`transaction_ineligible`, `transaction_missing`, `transaction_capacity_exceeded`,
`account_headroom_exceeded`, `concurrent_state_changed`, and
`target_overrun_acknowledgement_required`. Missing and foreign-owned resources are intentionally
indistinguishable.

The key is scoped to the authenticated User. A semantic replay returns the original `201` status and
body, even if its Contributions or source resources were later deleted. JSON property order, UUID
casing, decimal scale, omitted versus null notes, and omitted versus false acknowledgement do not
change request identity; row order and supplied note text do. Reusing a key with a changed semantic
payload returns `409 idempotency_mismatch` and does not say the original operation failed.

Duplicate arbitration is bounded to five seconds. When a competing or commit outcome cannot be
established, the API returns `503 operation_outcome_unknown` without a `created` property. A timeout
is not proof of rollback, and the API never automatically retries this monetary write.

## Read and correct Linked Contributions

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

## Consolidated client handoff

Generate a fresh key when the person confirms a split. Retain that key, the submitted date, and the
unchanged ordered payload until the outcome is known, including across midnight. After `201`, whether
new or replayed, refresh the Transaction's links/capacity, the Account's headroom, and affected Goal
progress/history. A refresh failure leaves creation confirmed and the views stale; it must never
trigger another creation request. After `503` or a generic transport failure, show an uncertain state
and offer only an explicit retry with the same key and unchanged payload. Do not switch keys to escape
uncertainty. Resolve the first outcome before submitting changed input under a new key.

All Contributions support note-only correction through `PUT /api/goal-contributions/{id}`. Do not
offer date, amount, Goal, Account, or source-Transaction edits. Recreate explicitly when one of those
fixed facts must change.

Keep a Contribution visible until deletion is confirmed. An initial `404` means the displayed data
was stale. After a timeout or generic server failure, do not assume deletion; offer an explicit retry.
A retry that returns `404` establishes absence and completes recovery, without claiming which request
removed the row. After `204` or recovery `404`, refresh the Transaction links/capacity, Account
headroom and affected Goal progress/history. If that refresh fails, deletion remains confirmed while
the surrounding views remain stale or unavailable; the failure must never trigger replacement
creation. Replacement creation is a separate, explicit operation that rechecks current eligibility
and capacity.

Source Transaction removal returns `409 transaction_has_linked_contributions` until every linked row
has been removed. Present the returned Contribution and Goal identities without assuming the source
was deleted. Once all rows are gone, a new explicit Transaction deletion follows the ordinary flow.
