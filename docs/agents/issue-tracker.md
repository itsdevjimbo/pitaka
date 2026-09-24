# Issue tracker: GitHub

Issues and specs live in this repo's GitHub Issues. Use `gh` from this clone.

## Operations

- Create: `gh issue create` with a title and body file for multiline text.
- Read: `gh issue view <number> --comments`.
- List: `gh issue list` with state and label filters.
- Comment, label, and close: `gh issue comment`, `gh issue edit`, and `gh issue close`.
- Publishing a spec means creating a GitHub issue; fetching a ticket means reading its issue and comments.

## Pull requests as a triage surface

PRs as a request surface: no.

## Wayfinding

Use one map issue and linked child issues. Mark maps `wayfinder:map` and children
`wayfinder:<type>`. Use GitHub sub-issues and native dependencies when available;
otherwise use a map task list and `Blocked by: #<number>`. Claim by assigning the
issue; resolve with an answer comment, close it, and link the result from the map.
