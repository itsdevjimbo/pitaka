# Pitaka

A personal expense and savings tracker. One person records where their money sits, what moves in and out, and what they're saving toward.

## Language

### Identity

**User**:
A person who signs in. Owns everything else in the system — nothing exists without one. This is the entity name, used in code, routes, and API payloads.
_Avoid_: Account, member

**Profile**:
The same person, named the way they are addressed. Every user-facing string the API authors — reset emails, error messages — says Profile, never User and never Account.
_Avoid_: Account, user account, my account, user settings

**Account**:
A place money sits — a bank account, cash on hand, a credit card. Carries a balance.
_Avoid_: Wallet, login, user account

> `User` and `Account` are the collision to watch. Plain English uses "account" for a login, this codebase never does.

### Money that is planned

**Recurring transaction**:
A standing instruction that creates a Transaction on a repeating cadence. It is a plan rather than money that has moved, and it can be paused and resumed.
_Avoid_: Subscription, repeat, recurring

**Generated transaction**:
A Transaction created by a recurring transaction rather than entered by the person. An ordinary Transaction in every other respect.
_Avoid_: Auto transaction, scheduled transaction

### Money that is spent

**Budget**:
A ceiling on spending across a repeating cycle. Only expenses count against it — income and transfers never do.
_Avoid_: Limit, allowance, envelope

**Narrowed**:
A Budget carrying a Category is _narrowed_ to it: only expenses in that category count against it. A Budget without one watches every expense, which is a deliberate and normal state rather than a missing value. Only expense categories can narrow a Budget.
_Avoid_: Filtered, scoped, assigned

**Category**:
What a Transaction is filed under. Each Category is permanently either Income or Expense, and a Transaction has at most one.
_Avoid_: Tag, label

**Tag**:
A free label on a Transaction, for finding it again. A Transaction may carry any number.
_Avoid_: Category, group

> `Category` and `Tag` are the second collision to watch. A Transaction has **one** Category and **many** Tags; a Category is typed and may be a system default, a Tag is untyped and always the person's own. Budgets count Categories and never Tags.

## Where Pitaka Web diverges

Pitaka Web mirrors this repo's names except in three places, translated at its HTTP adapter and nowhere above it (its ADR 0003). Those three are the only points where the same concept has two correct names, so they are the only points an agent working across both repos can get wrong:

| Here | In Pitaka Web |
|---|---|
| `User` | **Profile** |
| `RecurringTransaction` | **Schedule** |
| a Transaction with a `RecurringTransactionId` | **generated transaction** |

Every other name passes through unchanged, so this glossary deliberately does not restate the client's other eight terms — two files kept in sync forever, carrying no information.

The consequence a reader will hit: `SchedulesService` in the client calls `/api/recurring-transactions`. That mismatch is deliberate and lives in one file.

User-facing copy this API authors — reset emails, error messages — uses **Profile**, never User and never Account. The client owns nearly all such copy, so a word existing only in this repo's strings would be a second vocabulary for one person to remember.
