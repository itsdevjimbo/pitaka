---
status: accepted
---

# A Category's type is permanent

`CategoryService.UpdateAsync` assigned `category.Type = input.Type` with nothing consulted about what already depended on that category, because `CategoryRequest` was shared between `POST` and `PUT` and carried a required `Type` on both. That was harmless for as long as no write path read `CategoryType` at all — and none did. It stops being harmless with #67, which makes a Budget narrowable only to an expense category: create an expense category, narrow a Budget to it, then `PUT` the category as `Income`, and the Budget is back in exactly the state #67 exists to prevent, reporting ₱0 spent forever.

A Category's `Type` is therefore set at creation and never changes. Not "cannot change while a Budget points at it" — permanently, independent of what references it. `CONTEXT.md` already said as much ("Each Category is permanently either Income or Expense"); the code contradicted its own glossary.

The narrower rule was the one the issue proposed and it is the one to be clearest about rejecting. It works today because Budgets are the only thing that carries a type expectation. It gets worse every time something new depends on `CategoryType`: each dependent joins the guard, joins the error message, and joins the list of relationships a person has to remember creating before they can understand why an edit was refused. The permanent rule has no such list and never grows one.

What separates `Type` from every other field on a Category is that other rules *read* it. A Budget's narrowing is only valid because the category it names is an expense category, and two more readers are already filed: a Transaction's type must agree with its Category's (#76), and a child Category's type must match its parent's (#77). Flipping the type retroactively invalidates decisions that were made about the row while it held the other value. `Name` carries no such weight — no invariant reads it — which is why renaming a category stays legal under this ADR and reflipping it does not. It is tempting to put this as identity, that an Income "Salary" flipped to Expense is a different category wearing an old name and an old history, and that reads well; but the metaphor would ban renames too, and we are not banning renames. The rule rests on the readers, not on the metaphor.

Enforcement is structural rather than guarded, in two layers. `CategoryRequest` and `CategoryInput` split into `Create`/`Update` pairs — the same shape `Transaction` and `Account` already use — and the update half carries no `Type`, so the assignment in `UpdateAsync` is deleted rather than protected by a check. Then `Category.Type` becomes `{ get; init; }`, so re-adding that assignment later is a compile error rather than a silent regression. `CategorySeeder` and `CreateUserOwnedAsync` are the only remaining writers, and the compiler holds them to it.

Only the first of those two layers has precedent here. `UpdateTransactionRequest` has no `Type` and `UpdateAccountRequest` carries `Name` alone — an immutable field is not rejected, it is not accepted. But `Transaction.Type` and `Account.InitialBalance` are still plain settable properties, so `Category.Type` is the first field in this codebase that the compiler actually defends. Widening `init` to those two is mechanical and filed as #80; until it lands, the precedent cited above is contract-level only.

## Considered options

The four below are from #74, which filed the question with no recommendation. All four share a premise — that `Type` is mutable and the job is to constrain it — and it was the premise that turned out to be wrong.

**Block the flip while Budgets reference the category.** Rejected as the growing rule described above. It also buys less than it appears to: the person blocked from flipping deletes the category and recreates it, which unnarrows the Budget *and* strips the category from every past Transaction (both `SetNull`, `PitakaDbContext.cs:85` and `:97`). Blocking the tidy path funnels people onto the destructive one.

**Cascade — null out the category on affected Budgets.** Rejected. Silently changing what a money figure measures is the worst class of surprise, and "groceries spending" becoming "all spending" under the same name on the same tile is exactly that.

**Accept and document.** Rejected. It would leave #67's guarantee as "unreachable except through a category type flip", so every reader of a Budget still needs the branch #67 was written to delete — which was the point of closing it server-side.

**Warn and confirm.** Not currently expressible; the API has no two-step confirm and inventing one for this is out of proportion.

## Consequences

- **None of the three limbs is enforced at the time of writing.** This ADR records the position; #74 implements immutability, #76 and #77 are filed and unscheduled. A reader comparing this to the code should expect to find the gap, not assume the document is stale.

- **The only correction path for a mis-typed category is delete and recreate**, and that path today unnarrows every Budget pointing at the category and un-files every Transaction, silently, in one `204`. This is accepted rather than solved: the state being prevented is silent and permanent, the correction path is loud and requires the person to actively destroy something. Those are not equivalent risks. #75 asks what that delete should actually do, and is the more important of the two to answer.

- **A stray `type` in a `PUT` body is silently ignored**, as extra JSON properties are during model binding. This is only acceptable because nothing sends one — `pitaka-web` has no category edit form. If a client ever needs to express "change this type", it would get a `200` and no change, which is the point at which the update request should grow a `Type` purely to refuse it. That is a new decision, not a defect in this one.

- **The controller tests are blind to this rule, and that is now the compiler's problem rather than a gap.** Every `Update_*` test in `CategoriesControllerTest` builds an anonymous object rather than a `CategoryRequest`, so re-adding `Type` to the update contract fails no assertion there. With `init` on the model, re-adding the assignment fails the build instead. What the one deliberate test still pins is narrower and worth having on its own terms: that a `PUT` carrying a stray `"type"` answers `200` and ignores it, rather than `400`. That is an HTTP contract statement, not a restatement of the model rule.

- **Budgets and Transactions that already carry a mismatched category stay as they are.** No backfill, in line with #67. A migration that nulled them out would silently change what a Budget measures, which is the option rejected above arriving through a different door.
