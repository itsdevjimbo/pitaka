---
status: accepted
---

# The transaction filter bounds carry their own offset

`GET /api/transactions` gained `from`/`to` in the `transaction-filtering` slice (#65), and those bounds were compared literally against `TransactionDate`. That column holds two frames and the slice knew it — its spec says the comparison is "over the stored values as they are". A recorded transaction is a real UTC instant (`CreateAsync` applies `ToUniversalTime()`); a generated transaction is a wall-clock day (`NextRunDate.ToDateTime(TimeOnly.MinValue)`, no offset). One literal comparison cannot be right for both, so no `from`/`to` a client sends is correct for its whole history. #71 fixed the *read* serialisation of the same two frames; left alone, the filter would now disagree with the display it sits next to — a row rendered "1 September" that a September filter drops.

The client sends calendar days, because that is what a person picks in a date-range control and how pitaka-web already models Budget dates. At UTC+8 an expense recorded at 02:00 on 1 September is stored `2026-08-31T18:00:00`, and an unshifted September filter excludes it. The error is bounded by the offset on each boundary day, silent, and always drops rows.

**`from` and `to` become `DateTimeOffset?`, and each frame is filtered in its own terms.** The zone stops being a thing said once about the request and becomes a property of each bound. A `DateTimeOffset` carries both an instant and the wall-clock reading it was taken from — exactly the two frames the column holds — so the filter can ask each kind of row the question that fits it:

- A **recorded** transaction (`RecurringTransactionId == null`) is a moment. It is compared against the bound's instant — `from.UtcDateTime`, `to.UtcDateTime`.
- A **generated** transaction (`RecurringTransactionId != null`) is a bare calendar day; there is no zone in which its midnight is a fact. It is compared against the bound's wall-clock reading — `from.DateTime`, `to.DateTime`.

That is the whole decision, and it is two lines. It was verified in a throwaway prototype at a positive and a negative offset before being written down (branch `prototype/transaction-filter-frame-split`).

Two things the caller must now get right, both 400s:

- **A bound carries a zone designator** — a trailing `Z` or `±HH:MM`. A bare timestamp names a time on somebody's wall and does not say whose; left permissive, the server would answer with *its own* clock, a different answer in the container than on a developer's machine from the same request. This cannot be a validation rule: by the time `IValidatableObject` runs, the value is a `DateTimeOffset` and a missing designator has already been silently filled in with the server's offset. It is enforced in a model binder, `ZoneBearingDateTimeOffsetModelBinder`, where the raw text still exists — and a value that is not a timestamp at all is refused as *that*, not as a missing designator.
- **Both bounds carry the same offset.** One range, one zone. This also keeps the two readings of the range ordered together, so the existing "from must be strictly earlier than to" guard stays correct as a single instant comparison, with no second one beside it.

Absent, `from`/`to` are `null` and the endpoint behaves exactly as it did after #65.

## Why the frame split rather than one shifted comparison

A single shift applied to both frames re-breaks the one it does not fit. Shift the bounds to UTC and generated rows skew by the offset; leave the bounds unshifted and recorded rows do. There is also no instant a generated row could be *stamped* with at generation time that survives every offset: a generated row dated 1 September stamped `00:00Z` sits inside a local-September window at UTC+8 and outside it at UTC−5. The windows over the full offset range have an empty intersection. Since generation has no zone context to stamp a better instant with, the generated frame has to stay a wall-clock day and be *compared* as one. The split falls out of that: it is not extra machinery, it is the two frames each getting the only comparison that fits.

The `RecurringTransactionId is null` disambiguator is already load-bearing for the same distinction on read (#71's `TransactionDateForWire`). This decision reuses it rather than inventing a second signal.

## Considered options

The issue (#72) put three on the table; a fourth arrived when the first attempt was redesigned.

**Per-user timezone** — store an IANA zone on the User, interpret `from`/`to` in it, generate recurring transactions at a real instant in it — was rejected as too much for what it buys here. It is the only option that also fixes *generation* (a generated row would get a defensible instant), and it handles DST, which an offset does not. But it needs the zone set somewhere: a column with a backfill, a registration field or a profile-edit endpoint to change it, and every filter query reading it. For a one-person tracker whose person has a fixed, DST-free offset, that is a data-model change and two new endpoints to avoid carrying one piece of information the client already has. If a second user in a different zone ever appears, or DST ever matters, this is the option to revisit — offset-bearing bounds do not block it, and a bound that already carries its offset is a smaller step towards it than a bare `DateTime` would be.

**Uniform UTC** — document `from`/`to` as UTC, stamp generated transactions with a real instant — was rejected because it does not meet the acceptance criteria. A recorded transaction in the local evening still needs the client to pre-shift its bounds for a local-calendar filter to catch it, and the generated-row instant has the no-good-value problem above. It also re-raises the wrong-day question for generated rows.

**An explicit `utcOffsetMinutes` parameter** — the first implementation, reset before it reached `main`. `from`/`to` stayed bare `DateTime`s and a separate integer said what zone to read them in. It works, and its frame-split `Where` is the one kept here. It was replaced because the offset and the bounds it governs travelled separately: three values to keep consistent instead of two, a parameter that means nothing without `from`/`to`, and a second thing for the contract to describe. Folding the offset onto the bound removes the parameter, the `[Range]` on it, and the "what if they disagree" question — the bound is a `DateTimeOffset` and it is either well-formed or a 400.

**Offset-bearing bounds** — chosen. Least surface: no migration, no backfill, no change to generation, no new endpoint, no new parameter. The cost is real and worth naming, and it is the same cost the offset parameter carried: an offset is not a timezone. It holds no DST, so a caller in a DST zone must send the offset in force for the range they are asking about.

## Consequences

- **A bound the client pre-converted to `Z` breaks this design, and the API cannot tell.** A caller who sends `2026-09-01T05:00:00Z` where they meant `2026-09-01T00:00:00-05:00` has named the right moment and the wrong day — the wall-clock reading died in the browser, most often at a `toISOString()`. The server sees a well-formed, designator-bearing bound and filters the generated frame against `05:00`, not `00:00`. This is not guarded here: it is indistinguishable from a caller genuinely at UTC. It is enforced, if anywhere, on the client, and it is why there is a client-side issue in the pitaka-web tracker at all.

- **The write path is unchanged.** `CreateTransactionRequest.TransactionDate` stays a `DateTime` with `[RequiresUtcOffset]`. A write needs only the instant the money moved; only a read needs both frames, because only a read has to place a generated row on a calendar. Changing the write contract to `DateTimeOffset` would be churn with no reader.

- **A range that mixes offsets is not expressible**, by construction — the same-offset guard rejects it. One request, one zone.

- **A range that straddles a DST change now *is* expressible**, which a single per-request offset could never do: send `from` at the pre-transition offset and `to` at the post-transition one and the guard... rejects it, today, because they differ. The capability is latent, not delivered — it needs the same-offset guard relaxed to "the bounds are still ordered" and a reason to do so. It is recorded here because it is the one thing the per-user-timezone option does not strictly dominate: two offsets on one range carry information a single zone-plus-DST-table also carries, but by a different route.

- **The two-frame `Where` is the shape any future `TransactionDate` filter inherits.** A `CreatedAt` range, a "this month" shortcut, note-search combined with a date bound — each has to decide the same recorded-vs-generated question. The comment in `TransactionService.GetPageForUser` and this ADR are the pointer; the split is not visible from the column type.

- **Generation is untouched.** Generated rows stay wall-clock midnights. This is a read-path decision only; the "generated rows have no real instant" problem is documented here rather than fixed, exactly as #71 documented it rather than fixing it.

- **The designator guard lives in a model binder, and its registration is one line in `Program.cs`.** Remove that line and `from`/`to` fall back to the default binder, which reads a bare timestamp as the server's local time with no error. A test sends a bare bound and asserts a 400 precisely so that regression fails loudly.
