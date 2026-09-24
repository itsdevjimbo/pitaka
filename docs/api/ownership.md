# Private-resource ownership

Private route resources are resolved using the authenticated User and the route ID. A missing
target and one owned by another User return equivalent `404 Not Found` ProblemDetails responses;
only the per-request `traceId` differs. The inaccessible path returns before route-resource business
facts are disclosed or changes are made. Request-body reference validation keeps its existing
endpoint-specific response.

## Mutation route inventory

The foreign-owner column records the response before this migration. Every missing target already
returned 404. The successful response is for an owned target when the operation's other business
rules allow it.

| Route | Owned success | Missing before | Foreign-owned before → after |
| --- | --- | --- | --- |
| `PUT /api/tags/{id}` | 200 | 404 | 403 → 404 |
| `DELETE /api/tags/{id}` | 204 | 404 | 403 → 404 |
| `PUT /api/transactions/{id}` | 200 | 404 | 403 → 404 |
| `DELETE /api/transactions/{id}` | 204 | 404 | 403 → 404 |
| `PUT /api/goals/{id}` | 200 | 404 | 403 → 404 |
| `PATCH /api/goals/{id}/status` | 200 | 404 | 403 → 404 |
| `DELETE /api/goals/{id}` | 204 | 404 | 403 → 404 |
| `PUT /api/budgets/{id}` | 200 | 404 | 403 → 404 |
| `DELETE /api/budgets/{id}` | 204 | 404 | 403 → 404 |
| `PUT /api/categories/{id}` | 200 | 404 | 403 → 404 |
| `PATCH /api/categories/{id}/status` | 200 | 404 | 403 → 404 |
| `DELETE /api/categories/{id}` | 204 | 404 | 403 → 404 |
| `PUT /api/recurring-transactions/{id}` | 200 | 404 | 403 → 404 |
| `PATCH /api/recurring-transactions/{id}/status` | 200 | 404 | 403 → 404 |
| `POST /api/recurring-transactions/{id}/extend` | 200 | 404 | 403 → 404 |
| `DELETE /api/recurring-transactions/{id}` | 204 | 404 | 403 → 404 |
| `PUT /api/goal-contributions/{id}` | 200 | 404 | 403 → 404 |

The success status does not override the operation's other rules. An owned Transaction with Linked
Contributions still cannot be deleted and returns its existing 409 response. Deleting an in-use
Category or a Recurring Transaction that generated Transactions also keeps its existing 409.

System-default Categories are readable by every User. Attempts to update, change status, or delete
one continue to return 403 because the shared Category is not editable. A Category owned by another
User returns 404. Sign-in for an unconfirmed Profile also keeps its existing 403 response; it is an
authentication result, not an ownership response.

## Client compatibility and rollout

The `pitaka-web` review found that its shared error normalizer already gives 403 and 404 the same
not-found message, the authentication interceptor expires a session only on 401, and the Tags
adapter already maps 403 and 404 to the same unavailable outcome. Goal list and detail screens had a
separate 403 path that displayed “You can no longer change this Goal,” while a 404 refreshed into the
not-found state. The compatibility branch adds Goal list and detail specs that exercise both 403 and
404 write failures, assert that each refreshes into the unavailable state, and assert that the
ownership-specific message stays hidden. The client compatibility change makes those outcomes
follow the same refresh path and removes the ownership-specific message. The branch is on the separate
[`issue-167-goal-ownership-compat` client branch](https://github.com/itsdevjimbo/pitaka-web/tree/issue-167-goal-ownership-compat).
Its specs were inspected for this API change; their test run is not recorded here.

Merge and deploy that client change before deploying the API status migration. The sign-in handling
of 403 remains unchanged. If the client release cannot go first, hold the API deployment until the
client no longer distinguishes Goal ownership failures by status.
