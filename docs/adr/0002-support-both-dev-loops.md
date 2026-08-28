---
status: accepted
---

# Support both the Docker and the SDK dev loop

PR #43 containerised local development and the README sells it plainly: install Docker, nothing else. The API is served at `http://pitaka.localhost` by five compose services. Meanwhile Pitaka Web's `environment.ts` points at `http://localhost:5044`, the `dotnet run` Kestrel profile — a second dev loop the README does not document.

Both are supported, deliberately and in writing. The Docker loop keeps the zero-SDK guarantee a forker depends on; the SDK loop keeps the debugger and hot reload, which matter more on a project whose stated purpose is learning .NET than a tidy single story does.

## Considered options

**Docker only, with the client repointed at `http://pitaka.localhost`,** was the initial recommendation and was reversed on evidence. Commit `ac05a50` in `pitaka-web` had `environment.ts` open, added a second environment file, and left `localhost:5044` in place. That is a preference expressed in code, and a decision that contradicts it would be ignored rather than followed.

**Leaving it undecided** is what was actually happening and is the only option genuinely rejected. An undocumented second way to run the API is how the README's guarantee quietly rots — `docker compose run test` silently serving stale source was one instance of that already.

## Consequences

- **The README documents both loops**, and says which guarantees each one carries. The zero-SDK claim is scoped to the Docker loop rather than dropped.
- **CORS allows the page's origin, `http://localhost:4200`** — the `ng serve` origin, and the same one under either loop. The choice of `apiBaseUrl` is not a CORS origin question; both API hosts are request targets, not callers. A second origin gets added only if the client is itself served from a container.
- **No `AllowCredentials`.** The client holds its token in `localStorage` and attaches it as an `Authorization: Bearer` header (`auth.interceptor.ts:34`); no cookie crosses the boundary, so the policy needs origins, headers, and methods only.
- **A2 is part of this decision, not a separate cleanup.** `UseHttpsRedirection()` at `Program.cs:50` runs unguarded, including inside the container, which listens on plain HTTP. Under the Docker loop it turns preflight into a redirect and surfaces as a confusing CORS failure. It has to be environment-guarded for the loop this ADR commits to.
