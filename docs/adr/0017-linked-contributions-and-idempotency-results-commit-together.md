---
status: accepted
---

# Linked Contributions and idempotency results commit together

A Linked Contribution can commit even when its HTTP response never reaches the client. Retrying
must recover that success without creating another earmark. The Transaction split operation therefore
commits its Contributions and durable idempotency result in one explicit database transaction,
including when implementation needs multiple `SaveChangesAsync()` calls.

This is the implementation decision for the route-independent core in
[API #141](https://github.com/itsdevjimbo/pitaka/issues/141). Its settled Transaction-first contract
supersedes the historical Goal-first specification. One-row creation also uses this operation;
[API #140](https://github.com/itsdevjimbo/pitaka/issues/140) owns the authenticated HTTP adapter.
Goal-first creation remains deferred.

## Key identity and lifetime

Keys are scoped to the authenticated User, with a database unique constraint on `(UserId, Key)`.
The request fingerprint includes the operation, route identities, and normalized validated payload:
Transaction identity, required client-supplied Contribution date, ordered Goal rows, exact decimal
amounts, notes, and target-overrun acknowledgements. No timezone or server-derived date participates. JSON formatting and property order do not change identity. Split row order is
preserved in the fingerprint. UUID casing and decimal scale are normalized. Null/omitted notes
are equivalent; supplied text (including empty text and whitespace) is preserved. Omitted
acknowledgement equals false. Switching operations with the same key is a mismatch.

Successful records retain the fingerprint, original response status and body, and creation timestamp.
They have no automatic expiry while the User exists. Deleting a Contribution must not delete its
idempotency record: a late retry returns the original result and must not recreate a deleted earmark.
A future retention policy must define safe handling of old keys before introducing cleanup.

## Transaction boundary

1. Authenticate and authorize access to the current User's operations, validate request structure,
   and compute its fingerprint. The internal service takes the authenticated User ID; the HTTP
   adapter must obtain it from the existing authentication flow, never from client input.
2. If a committed key exists for this User, return its saved success for the same fingerprint or
   `409 idempotency_mismatch` for a different one, before resolving historical resources. Neither
   outcome requires the original Goal, Transaction, or Contribution to exist. Replays do not rerun
   creation or derive a new date.
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
the client must keep the original key and payload for explicit recovery. Duplicate arbitration is
bounded to five seconds, including the initial key lookup, reservation wait, and winner lookup.
An unresolved outcome returns `503 operation_outcome_unknown` without a `created` assertion.
Commit failures receive a fresh, bounded, read-only recovery attempt after disposing the write
context. Never automatically retry a monetary write, including on a deadlock or uncertain commit. Do not translate unrelated database
errors into successful replays.

One key covers every row of an atomic split and its complete response. Different keys remain distinct
operations: idempotency does not replace concurrency protection for Transaction capacity and pooled
Account headroom. All writers affecting those invariants must participate in that protection.

## Coherent success snapshots

The split uses an explicit MySQL repeatable-read transaction. All ordinary reads share one snapshot;
Account and Goal version tokens are captured before their protected observations. Guard updates
compare those versions against current rows and hold the write locks through the final commit.
Guards are checked even for state refusals and rolled back with the reservation. A stale observation
returns resource identities to refresh, without pretending to know current monetary amounts.

Capacity releases do not increment guards. Repeatable read keeps Account earmarked total, Transaction
linked total, and Goal progress coherent even if releases interleave between their aggregate queries.
The response combines that snapshot with only this operation's new rows. It can conservatively predate
a concurrent release; it never mixes totals from different observations or promises arrival-time state.
Acknowledgement permits the amount to exceed target, not a particular overrun figure, and never waives
guards or capacity checks. Ordinary Contribution behavior remains unchanged.

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

The split-versus-source-removal integration test remains a cross-slice release check: #143 is not
present in the #141 baseline. Complete both race directions after #143 and before #140 merges;
this core does not change source-removal behavior to anticipate that slice.
