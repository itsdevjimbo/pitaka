## Problem Statement

Pitaka's API and web have validation pipelines but do not publish deployable images.
The developer cannot pull a known pair of application versions from Docker Hub and
practice deploying the complete application locally. The existing contributor
environment uses mounted source and SDK tooling, so it does not demonstrate running
published application artifacts, applying release migrations, or recovering deployment data.

## Solution

Publish public, multi-architecture images after successful validation of each repository's
main updates and eligible version tags. Provide a separate, manually operated local
deployment using those images, available at http://localhost:8080, with persistent
MySQL and private object storage plus a local email inbox. Run a matching migration
container before starting the selected API version. Document upgrades and manual recovery.

## User Stories

1. As a maintainer, I want every push to main to publish validated images, so that merged work is available for deployment.
2. As a maintainer, I want squash and rebase merges supported, so that publishing does not depend on merge commit style.
3. As a maintainer, I want PR validation without image publication, so that proposed work is checked before distribution.
4. As a maintainer, I want failed checks to prevent publication, so that failing builds are not presented as deployable.
5. As a maintainer, I want stable version tags to publish releases, so that I can name deployment versions.
6. As a maintainer, I want to release an older commit already in main's history, so that releases need not use main's current tip.
7. As a maintainer, I want tags outside main's history rejected before publication, so that unmerged work is not released.
8. As a maintainer, I want release-tag builds validated too, so that older commits do not bypass checks.
9. As a maintainer, I want API and web publishing to remain independent, so that either repository can evolve without a coordinated release.
10. As a developer, I want public Docker Hub images under jimbodev0530, so that I can pull them without registry login.
11. As a developer, I want ARM64 and AMD64 images, so that deployment works on my current machine and typical Intel/AMD machines.
12. As a developer, I want a moving main image tag, so that I can try the latest successful main build.
13. As a developer, I want fixed commit and release image tags, so that I can select the same artifacts again.
14. As a maintainer, I want retried publications to preserve fixed tags, so that retries cannot silently replace published artifacts.
15. As a maintainer, I want overlapping builds to preserve main's publication order, so that a late older build does not replace a newer published build.
16. As a maintainer, I want API and migration images published as a matching pair, so that a partial upload is not reported as a usable release.
17. As a developer, I want to select API and web versions independently, so that I can deploy an explicit pair without requiring matching version numbers.
18. As a developer, I want deployment to pull application images without compiling locally, so that I practice running the artifacts produced by CI.
19. As a contributor, I want the existing development workflow preserved, so that deployment simulation does not change the source-watching loop.
20. As a developer, I want deployment data isolated from development data, so that exercising deployments does not alter my development database or uploads.
21. As a developer, I want persistent database and object storage volumes, so that replacing containers preserves my data.
22. As a developer, I want the web and API accessible through one local HTTP origin, so that the browser does not require a machine-specific API host.
23. As a developer, I want browser route refreshes to work, so that deployed web navigation behaves normally.
24. As a person registering a Profile, I want confirmation emails captured in a local inbox, so that I can complete registration without external email delivery.
25. As a person using a Profile, I want authentication email links to return to the deployed web origin, so that confirmation and password reset work locally.
26. As a person using Pitaka, I want to sign in and record an expense, so that I can demonstrate the API and web working together.
27. As a person using a Profile, I want to upload and retrieve my private Profile picture, so that deployment exercises persistent authenticated file access.
28. As a developer, I want runtime secrets supplied locally, so that published images and source control contain no deployment secrets.
29. As a developer, I want migrations packaged with the API release, so that schema updates correspond to the selected API version.
30. As a developer, I want the database ready before migrations run, so that deployment does not race database startup.
31. As a developer, I want the new API to start only after migration success, so that it does not serve requests against an outdated schema.
32. As a developer, I want all selected images pulled before stopping the application, so that download failures leave the existing application running.
33. As a developer, I accept upgrade downtime, so that the old API does not access the database while its schema changes.
34. As a developer, I want migration failure to halt deployment and leave the application stopped, so that I can investigate before serving requests.
35. As a developer, I want an explicit manual backup and restore procedure, so that I can recover the database and uploaded files together.
36. As a developer, I want recovery guidance to explain schema compatibility, so that I do not assume reverting an API image reverses a migration.
37. As a maintainer, I want registry setup and publishing credential requirements documented, so that both repositories can publish successfully.
38. As a developer, I want a repeatable fresh-deployment and upgrade checklist, so that I can demonstrate the deployment works beyond containers merely starting.

## Implementation Decisions

- Scope spans the API repository, itsdevjimbo/pitaka, and the web repository,
  itsdevjimbo/pitaka-web. The API repository owns the deployment configuration and
  operating instructions. Each repository publishes only its own artifacts; the API
  repository additionally publishes migrations. This is a cross-repository feature.
- Extend existing GitHub Actions validation and publishing behavior for pushes to
  main and eligible version-tag pushes. PRs retain validation without publishing.
  Main updates publish regardless of merge style; branch policy should require PRs.
- Accept release tags in vMAJOR.MINOR.PATCH form only. Prerelease and build-metadata
  variants are excluded initially. Resolve the tag to its commit and require that
  commit to be reachable from main before publication; historical commits qualify.
  Other branches and unrelated tag formats do not publish.
- Run the repository's existing checks against the exact selected revision before
  publishing. Tag runs must use valid comparison inputs for existing web standards
  checks rather than assuming a branch-push or PR event payload.
- Publish public Docker Hub repositories jimbodev0530/pitaka-api,
  jimbodev0530/pitaka-web, and jimbodev0530/pitaka-migrations. Each supports
  linux/arm64 and linux/amd64. Configure publishing credentials in CI secret storage;
  document registry prerequisites without committing credentials.
- Main publications expose sha-<commit> and a moving main tag. Release publications
  expose the matching Git version tag. Commit and release tags are never reassigned
  to different artifacts. A retry must reuse an existing verified artifact or fail
  clearly on a conflict; it must not overwrite a fixed tag with a rebuilt digest.
- Older main runs must not replace a newer published main build. Historical release
  tags do not change the moving main tag. Preserve enough source-revision information
  to identify the commit behind each image and verify matching API/migration artifacts.
- API and migration images use the same source commit and version selection. Both
  must be available before API publication is declared successful or moving main
  tags advance. Registry uploads and separate tag updates are not atomic: detect
  incomplete/mismatched pairs, report failure, and require a verified pair for deployment.
- Build the API's deployable runtime stage. Build and serve the web's production
  static assets in a deployable image with browser-route fallback and an internal
  reverse proxy for /api. The browser uses relative API requests, preserving the
  existing API routes. The image must not depend on the current placeholder API URL.
- Provide a separate deployment Compose configuration with explicit API/web image
  selections, MySQL, SeaweedFS, smtp4dev, and a one-shot migration service. API and web
  versions need not match each other; API and migration versions must match.
  Local deployment does not build application source or require a host .NET/Node SDK.
- Preserve ADR 0019's contributor workflow: its source mounts, SDK watcher, four
  ordinary services, and explicitly requested development migrations remain a separate
  concern. Deployment services must not share the development volumes or project identity.
- Persist deployment MySQL and object data across ordinary restarts and replacements.
  Initialize the private storage bucket and wait for dependency readiness. Preserve
  ADR 0018's private S3-compatible access: Profile pictures remain authenticated API
  resources, not public object URLs.
- Serve the deployment at http://localhost:8080. Configure authentication email links
  to that same origin. HTTP must work in the deployment configuration without
  redirecting the browser to an unavailable HTTPS endpoint. Capture outbound emails
  with smtp4dev and document how to open its inbox.
- Supply database, authentication, storage, and email configuration at runtime through
  a local environment file with documented placeholders. Exclude real secrets from
  source control, image layers, and published build output. No new domain schema or
  public business API contract is required by this feature.
- Build an EF Core migration bundle in CI and package it as the migration image
  for each supported architecture. Execute it against the ready deployment database
  as an explicit, manually initiated deployment step. Successful completion exits
  the container; it is not a continuously running service or API-startup migration.
- Initial deployment prepares configuration and dependencies, applies migrations,
  then starts the API and web. Upgrades pull and validate all selected artifacts,
  stop the existing API/web, run the selected migration, and start the selected
  versions only on success. Accept downtime. Do not leave an older API serving
  while migrations run. A migration failure leaves application services stopped
  and exposes actionable logs and a failed result.
- Document a consistent manual backup and restore procedure covering the database
  and uploaded files, with the associated application version selections. Do not
  automatically reverse migrations or promise that a failed migration left the
  database unchanged. Restoring an old image alone is not schema recovery.

## Testing Decisions

- Keep the existing API and web checks as publication prerequisites. Test observable
  behavior, not YAML layout, Dockerfile instruction order, or private helpers.
- Add a small automated container smoke check at the actual Compose boundary:
  dependencies become ready, the matching migration completes, the API responds,
  and the web serves successfully with /api reaching the API through the proxy.
  Use isolated disposable data. Do not build a new general-purpose test framework.
- Verify publishing through actual workflow runs and Docker Hub artifacts: main and
  eligible version-tag publication, failed-check gating, tag ancestry restrictions,
  fixed image tags, matching API/migration revisions, and ARM64/AMD64 manifests.
  Review retry and concurrency behavior; a framework simulating every GitHub event
  or registry failure is not required. Native or emulated architecture smoke runs
  can supplement manifest inspection when available.
- Provide a manual acceptance checklist for a fresh deployment: register a Profile,
  read the confirmation email in smtp4dev, confirm, sign in, create an Account,
  record an expense Transaction, and upload/read a private Profile picture. Check
  browser-route refresh and authentication email links use the deployment origin.
- Manually verify persistence across container replacement, an upgrade with a pending
  migration, and rerunning the migration bundle without duplicate schema changes.
  Use disposable test artifacts/data for upgrade and failure exercises; do not add
  an unrelated permanent domain migration just to demonstrate deployment.
- Include manual checks for pull failure leaving the running app untouched, migration
  failure leaving API/web stopped, and the documented database/upload backup and
  restore procedure. Do not require automated disaster-recovery or registry-failure
  simulation suites.
- Prior art: API controller tests use WebApplicationFactory and MySQL; real-auth tests
  exercise registration/confirmation/sign-in; storage integration tests use SeaweedFS.
  The web already uses Angular/Vitest and standards checks. Retain these tests and
  reuse relevant existing fixtures for regressions rather than duplicating them.
  Deployment smoke checks use real dependencies because in-process substitutions
  cannot verify published packaging and proxy configuration.

## Out of Scope

- Automatically deploying to the developer's machine from CI, cloud hosting,
  Kubernetes, production infrastructure, or a hosted deployment service.
- HTTPS, certificates, custom hostnames, external email delivery, and public internet exposure.
- Coordinated version numbers or automatic cross-repository releases.
- Prerelease tags, extra moving release aliases, or a latest image tag.
- Zero-downtime upgrades, automatic database downgrades, and automatic rollback.
- Replacing the contributor development workflow, rewriting business features,
  or changing domain contracts merely to demonstrate deployment.
- Implementing this feature during specification authoring. This issue defines later work.

## Further Notes

- The user confirmed the design after a structured interview; the final address is
  http://localhost:8080. Earlier discussion of pitaka.localhost is superseded.
- The user confirmed the spec with this reduced testing scope. Publish it in the API
  repository's GitHub tracker with ready-for-agent. A later task breakdown may link
  implementation work in both repositories; this specification does not authorize
  treating API-only implementation as completion.
- Existing validation is already present in both repositories. The API has a runtime
  image but no migration runner; the web needs container packaging and currently embeds
  a production API placeholder. These are the principal gaps this work addresses.
