---
status: accepted
---

# Categories are flat

`Category.ParentId` is a nullable self-reference. `POST` and `PUT /api/categories` both accept it, `CategoryService.IsValidParentAsync` (`:51`) guards it, `CategoryResource` returns it, and `IsInUseAsync` (`:110`) counts a child as a use. It has been part of the model since `InitialCreate`.

Nothing reads it. `GetBudgetAmountSpent` matches a Budget's category exactly (`:33`) with no rollup to descendants, and there is no other consumer — the hierarchy is written, validated, stored and serialised, and then contributes to no figure and no screen. `pitaka-web` receives it and throws it away at the HTTP adapter, with a comment saying so: *"drop what nothing above the adapter reads (`isDefault`, `parentId`)"*. The database holds nineteen categories and not one of them has a parent. The seeded defaults are all top-level.

**A Category has no parent.** The column, the foreign key, the navigations, the validator and the wire field are removed.

## Why this is a rejection rather than a deferral

ADR 0004 said both things, two paragraphs apart. Line 27 justified bringing `Category.Parent` under the in-use rule on the grounds that "#77 is about to make the parent link load-bearing rather than decorative"; line 69 said "there is no plan for parent categories as a product feature, and this rule does not create one." Only one of those can be true, and the evidence is entirely on line 69's side. This ADR settles it there.

The distinction matters because it decides what the two open defects in this surface mean. `IsValidParentAsync` does not check that a child's type matches its parent's (#77), and it catches `A -> A` but not `A -> B -> A`, which its own comment admits. Read as an unbuilt feature, those are two tickets. Read as a rejected one, they are evidence: a relationship that has been writable for the project's whole life, has never once been used, and has accumulated two known ways to enter an invalid state that nobody has been in a hurry to close.

ADR 0001 is not being excepted here. Its rule schedules *building* by client pull, and the parent link predates the rule — it was never pulled, it was simply there from the first migration. Removing surface that no screen has ever wanted is what that rule implies rather than a departure from it.

## Considered options

**Build #77, and the ancestor walk that closes the cycle gap with it.** Rejected. It is real work — a type check on two endpoints and a traversal — spent making correct a feature with no consumer, no screen, no rows and no plan. The issue itself concedes the ground: it calls a mixed-type tree "currently a display problem rather than an arithmetic one" and says it would matter "the moment anything rolls up — which is the natural next feature for a hierarchy that otherwise has no purpose." That sentence is the argument for this ADR, written before it.

**Remove it from the wire, keep the column.** Rejected. It leaves a column that nothing writes and nothing reads, which is strictly worse than the state being fixed: today the field is at least reachable and therefore visible. A dormant column is the kind of thing that gets rediscovered years later by someone who assumes it means something.

**Leave it alone.** Rejected. Doing nothing is not free: the field stays writable through two endpoints, stays guarded by a validator with two known bugs, stays a referent of the in-use rule, and stays a limb of ADR 0003 — so every future reader of `CategoryType` and every future decision about deletion has to account for a relationship that has never held a row.

## Consequences

- **Nothing is removed at the time of writing.** This ADR records the position, as ADR 0003 did before #74 implemented it; the removal follows in its own change. A reader comparing this to the code should expect to find `ParentId` still there, not assume the document is stale.

- **#77 is withdrawn, not unimplemented.** ADR 0003 line 33 lists three limbs and says a reader "should expect to find the gap, not assume the document is stale". Two limbs remain — #74, which shipped, and #76. The third was not deferred; it was decided against, and the distinction is worth keeping visible because "filed and unscheduled" invites someone to pick it up.

- **ADR 0003's argument narrows but survives.** Its case for immutability rests on other rules *reading* `Type`, and it named three readers: a Budget's narrowing, #76, and #77. Losing one of three does not weaken it — a Budget's narrowing alone was always the load-bearing example — but the sentence has to stop naming a reader that cannot exist.

- **ADR 0004's rule is unchanged; only its arity moves.** "A Category is in use once anything points at it" now covers three referents rather than four. That document argued explicitly that the uniform rule "has no such list and never grows one" and that "adding a fifth referent later changes nothing" — the same property holds subtracting the fourth. What does change is that a category which would once have been pinned by a child category is now deletable, and that its stated escape hatch — re-parent the child first — describes an operation that no longer exists.

- **A `parentId` on a write is silently ignored**, as extra JSON properties are during model binding. This is the same position ADR 0003 line 37 took for a stray `type`, and it is acceptable for the same reason: `pitaka-web` exposes `names()` and `list()` and has no category management screen, so nothing sends one.

- **There is nothing to migrate.** No row has a parent, so the column drop loses no information and needs no backfill. This is what makes the decision cheap to make now and expensive to postpone: the first nested category anyone creates turns a clean drop into a data question.

- **Re-adding nesting later means overturning this document, not resuming work.** That is the intended cost. A hierarchy is a real feature with real questions attached — rollup into descendants, type agreement, cycle prevention, how deep, what a picker shows — and none of them were ever answered. Re-deciding them from a blank column is more honest than inheriting a half-built answer nobody chose.
