---
status: accepted
---

# Linked Contributions and idempotency results commit together

A Linked Contribution can commit even when its HTTP response never reaches the client. Retrying
must recover that success without creating another earmark. Both creation operations therefore
commit their Contributions and durable idempotency result in one explicit database transaction,
including when implementation needs multiple `SaveChangesAsync()` calls.

This is the implementation decision for [API #138](https://github.com/itsdevjimbo/pitaka/issues/138)
and the shared recovery work in [API #141](https://github.com/itsdevjimbo/pitaka/issues/141).
The product contract lives in [the Linked Contribution specification](https://github.com/itsdevjimbo/pitaka-web/issues/196).
This ADR specifies intended implementation; it does not claim the current API implements it.

## Key identity and lifetime

Keys are scoped to the authenticated User, with a database unique constraint on `(UserId, Key)`.
The request fingerprint includes the operation, route identities, and normalized validated payload:
Transaction, Goal or split rows, exact decimal amounts, notes, IANA timezone, and target-overrun
acknowledgements. JSON formatting and property order do not change identity. Split row order is
preserved in the fingerprint. Switching operations with the same key is a mismatch.

Successful records retain the fingerprint, original response status and body, and creation timestamp.
They have no automatic expiry while the User exists. Deleting a Contribution must not delete its
idempotency record: a late retry returns the original result and must not recreate a deleted earmark.
A future retention policy must define safe handling of old keys before introducing cleanup.

## Transaction boundary

1. Authenticate, authorize, validate the request, and compute its fingerprint.
2. If a committed key exists for this User, return its saved success for the same fingerprint or
   `409 idempotency_mismatch` for a different one. Replays do not rerun creation or derive a new date.
3. For a new key, begin a database transaction and insert/save its reservation before financial writes.
4. Recheck eligibility and both capacities under the operation's concurrency protection. Create all
   Contribution rows, saving as needed to obtain their identifiers.
5. Store the successful response on the reservation and save. Commit once, then return the response.

Any failure before commit rolls back both the reservation and every Contribution, including changes
flushed by earlier saves. Validation and state refusals are not retained as successful results. An
uncertain commit outcome must be resolved using the original key, never by assuming rollback and
starting a new keyed operation.

The unique constraint arbitrates simultaneous requests for the same key; an existence check alone
does not. After the specific reservation uniqueness violation, roll back/dispose the failed unit of
work and read the winner's committed result in a fresh context. If the competing transaction rolls
back, a contender can acquire the reservation. A bounded wait may fail without confirming an outcome;
the client must keep the original key for explicit recovery. Do not translate unrelated database
errors into successful replays.

One key covers every row of an atomic split and its complete response. Different keys remain distinct
operations: idempotency does not replace concurrency protection for Transaction capacity and pooled
Account headroom. All writers affecting those invariants must participate in that protection.

## Replay and current state

The saved response describes the original success, including its Contribution IDs, date, and capacity
snapshot. A replay may occur after other Contributions, spending, or deletion changed current state.
After replay, the client refreshes authoritative Transaction capacity, Account headroom, and affected
Goal progress/history. Failure of that refresh must not trigger another creation request.

The client generates one key per confirmed operation and retains the key and unchanged payload while
the outcome is uncertain. Explicit retry reuses both. Changed input requires a new key after the prior
uncertain outcome is resolved; it is not a recovery shortcut.

## Alternatives and verification

Saving the Contribution first and its idempotency record in a separate transaction leaves a crash
window that duplicates earmarks. An independently committed pending reservation requires a separate
abandoned-reservation recovery protocol. A cache-only key can disappear before a late retry. The shared
transaction avoids those gaps at the cost of durable response storage and contention on duplicate keys.

Integration coverage must include simultaneous same-key requests, different-payload mismatch,
independent Users using the same key, failure between saves, lost-response recovery, retry across
midnight, replay after Contribution deletion, and different-key capacity contention. Split coverage
must prove all rows and their result commit together or all roll back. Replay coverage must verify
stable original results even when current capacity has changed.
