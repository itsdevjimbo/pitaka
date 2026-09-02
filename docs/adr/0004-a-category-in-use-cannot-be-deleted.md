---
status: accepted
---

# A Category in use cannot be deleted

`DELETE /api/categories/{id}` succeeds unconditionally on a person's own category. Nothing is consulted about what points at it, and four relationships are configured `OnDelete(DeleteBehavior.SetNull)` to absorb the consequences: a Budget's narrowing (`PitakaDbContext.cs:89`), a Transaction's filing (`:101`), a recurring transaction's category (`:95`), and a child Category's parent (`:77`). One `204` silently rewrites all four.

A Category is **in use** once anything points at it, and a Category in use cannot be deleted. One rule across all four referents, one `409`, one sentence.

Nobody chose `SetNull`. It reads as EF's sensible default for an optional foreign key, not as an answer to "what should deleting a Category do to the Budgets narrowed to it". This ADR answers that question, and the answer is the same for all four things asking it.

## Why the Transactions are the strong case, not the weak one

#75 filed this as a Budget problem and treated Transactions as the softer half, on the grounds that a Budget is a live measurement and a Transaction is a historical record. That ordering is backwards.

A Category on a settled Transaction is a **recorded fact** — part of what the person wrote down about money that has already moved — not a live pointer that may evaporate. Someone quits the gym and deletes "Gym Membership" to tidy their picker; three years of gym spending becomes uncategorised, without a single Transaction being edited. Nothing announces it, and no screen would look wrong afterwards.

The Budget failure is real and is the one #75 leads with, but it is *loud*: "groceries spending" becoming "all spending" is visible the moment someone looks at the tile. Lost filing on old Transactions is visible never. Where the two disagree about how much protection is warranted, the quieter loss sets the rule.

## Why one rule and not a rule per referent

The tempting middle — block on Budgets, let Transactions go null — was rejected for the reason ADR 0003 already gave when it rejected "block the flip while Budgets reference the category". It is a rule that grows a limb per dependent. Every future reader of `CategoryId` joins the guard, joins the error message, and joins the list of relationships a person has to reconstruct before a refusal makes sense. It also means two pointers at the same row behave differently, which arrives owing an explanation nobody can give in a `409` body.

The uniform rule has no such list and never grows one. Adding a fifth referent later changes nothing about what this endpoint does or says.

That uniformity is also what brings the fourth relationship in. #75 named three referents and missed `Category.Parent`, where deleting a parent silently promotes its children to top-level. It is the same defect — a structural mutation riding on a `204` — and #77 is about to make the parent link load-bearing rather than decorative. Deciding three now and the fourth later would mean writing this document twice.

## Why refusal, and what has to come with it

An Account already answers the same question, and answers it with a matched pair rather than a single mechanism: hard deletion is refused once there is history (`AccountsController.cs:117`), and `Retired` exists for the need underneath — *I have stopped using this, get it out of my way, keep what it recorded*. A Category the person no longer uses is that situation exactly.

The pair is not decorative. Refusal on its own tells a person that a category they used once, three years ago, is theirs forever; the pressure that produces is what makes people delete things in the first place. So this ADR commits to `Retired` for Categories — the same word, because the concept is identical and `CONTEXT.md` already spends *archived*, *inactive* and *disabled* on its `_Avoid_` line. That a retired Account stays visible in the account list while a retired Category leaves the picker is not a divergence in the state; it is what each list is for.

Only the refusal ships now. ADR 0001 schedules API work by client pull, and `pitaka-web`'s `categories.service.ts` exposes `names()` and `list()` and nothing else — there is no category management screen, so nothing pulls this. The B3 exception does not apply: the client did not skip category management *because* this API lacks something, the way it deleted the forgot-password screens. It simply has not built it.

What separates the two halves is that the refusal is a **defect fix** and `Retired` is a **feature**. A shipped endpoint that destroys records was never an entry in the Gap Register, and the fix is a query, a branch and a migration. `Retired` is a column, an endpoint, a wire field and a client change — precisely the shape ADR 0001's rule exists to defer. It is filed as #86 and waits for a screen.

## Enforcement in two layers

Layer one is `CategoryService.IsInUseAsync(int categoryId)` and a `409` in the controller. The check needs no user scoping: every write path already confines a category reference to its owner — `VerifyCategoryExistence.VerifyAsync(user, categoryId)` on Transactions, `VerifyBudgetCategory` on Budgets — and system defaults are unreachable behind the existing `Forbid`. A plain `AnyAsync(x => x.CategoryId == id)` is therefore already correctly scoped, and the reason belongs in a comment rather than in a redundant `WHERE`.

Layer two is the schema: all four relationships move from `SetNull` to `Restrict`. This follows ADR 0003, which did not settle for a guard either — `Category.Type` became `init` so re-adding the assignment is a compile error rather than a silent regression. The analogue holds. Leaving `SetNull` in place after deciding against it means the model keeps asserting the opposite of this document, and the next code path that removes a Category — a bulk cleanup, a seeder change, a profile-deletion feature — resurrects the whole defect without touching the controller.

`Restrict` is not novel here. `GoalContribution → Transaction` is already `Restrict` (`:113`), and it is already paired with a `409` in `AccountsController` for the same reason: a delete that would destroy meaning is refused rather than absorbed.

## Considered options

The five below are from #75, which filed the question with no recommendation.

**Block the delete while anything references the category.** Chosen. The cost #75 identified is real — a category used once three years ago is pinned by that one Transaction — and it is the cost `Retired` exists to pay.

**Block only on Budgets; let Transactions go null.** Rejected as the growing rule above, and on the premise it rests on: it treats a Transaction's category as disposable because the Transaction is historical, which is the inversion this ADR spends its second section on.

**Keep the cascade, make it visible.** Rejected. Reporting the damage is not preventing it, and a `204` growing a response body to describe what it destroyed is a worse endpoint than one that refuses. It also assumes someone reads the response, which is exactly the assumption that fails for the case that matters.

**Soft-delete the category.** Not rejected — deferred, and renamed. This is `Retired`, and it is half of the decision rather than an alternative to it. What #75 costed as "a new column, a filter on every read path" is smaller than it looks, because a retired Category must still resolve names on old Transactions and so must still be returned by `GET /api/categories`; the filtering happens in pickers, not on read.

**Accept and document.** Rejected. It would make ADR 0003's correction path — delete and recreate a mis-typed category — permanently destructive, which that ADR already flagged as the more important of the two questions to answer.

## Consequences

- **The `409` is a dead end until `Retired` ships.** Nothing in the API tells a person *what* is using the category, and one sentence for four referents is a deliberate choice not to try. That gap is the honest price of splitting the pair, and it is the thing that should make #86 easy to prioritise the day a category screen appears.

- **A race between the check and the delete surfaces as a 500.** A Transaction created between `IsInUseAsync` returning false and `SaveChangesAsync` hits `Restrict` at the database and throws `DbUpdateException`. Accepted rather than caught: translating a provider exception to reconstruct information the guard already computed is only exercisable by racing two `DbContext`s, and is worth writing the day a second concurrent session exists.

- **`Restrict` collides with `User → Category` being `Cascade` (`:83`) the day profile deletion arrives.** Cascading a user delete would reach a Category still referenced by that same person's Transactions and fail at the database. There is no user-delete path in the API today, and the feature would need explicitly ordered deletion regardless — a "delete everything I have" flow that leans on FK cascade ordering is not one to trust.

- **A parent Category cannot be deleted while it has children**, and the escape hatch already exists: `PUT /api/categories/{id}` accepts `parentId`, so a child is detached or re-parented first. Nothing has to be built for the hierarchy, which is deliberate — there is no plan for parent categories as a product feature, and this rule does not create one.

- **A *paused* recurring transaction still pins its category.** It falls out of the uniform rule: a plan that has moved no money still carries a category, and pausing is not un-using. Worth stating because it is the one referent where "in use" and "in effect" come apart.

- **Categories that are already in use are unaffected.** No backfill, no cleanup pass, nothing to migrate beyond the four foreign keys. The rule governs deletion from here, and nothing that exists today is in a state it forbids.
