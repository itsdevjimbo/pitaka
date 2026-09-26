# Pitaka artifact publishing and deployment plan

Status: confirmed local design, 2026-09-26. Planning only; implementation is deferred.
This revision supersedes the earlier plan's web-image handoff and its placement of
local deployment Compose in the API repository. Web storage and retention are resolved by web ADR 0018; the production platform
and production deployment setup remain open decisions.

## Decisions confirmed during issue #190 review

- The separate deployment repository will be `itsdevjimbo/pitaka-deploy`.
  Its initial local workflow applies a selected revision with an explicit command;
  a Git commit alone does not update the running environment.
- API SemVer publication is deferred. Local deployment selects the existing API
  image by digest, with its source SHA recorded.
- Local web selection may use either a named 14-day Actions candidate or a
  promoted GitHub Release asset. The version record identifies the exact source,
  artifact, and checksum. Expiry of a candidate fails clearly.
- Local deployment data persists across routine container replacement. Recovery
  exercises use disposable data: back up database and uploads before a migration,
  retain the current and previous backup, and run a restore drill. Production
  backup frequency and retention remain undecided.
- Protect API digests used by active environments and the last three successful
  production versions. Deletion of an unreferenced digest requires manual review;
  an age rule awaits measured registry usage.
- The proposer of a deployment revision records web/API compatibility evidence
  and migration impact for review. Local smoke tests verify the selected pair
  before recording a successful rollout.
- The current work designs a local simulation of production. Production hosting,
  operations, and costs will be decided separately.
- A failed migration leaves the API offline for explicit repair or restore. The
  procedure never restarts an older API or reverses schema changes automatically.
- Keep the previously successful, verified web bytes locally while that version
  is a rollback target, even if its Actions candidate expires. Record their
  checksum and remove them only after the version is no longer protected.

## 1. Evidence and boundaries

Inspected clean local checkouts: API `itsdevjimbo/pitaka` at
`34c3da4be7e30e565b6974da95551452e5b654f8`, and web
`itsdevjimbo/pitaka-web` at `6c29de3a4fa4f4458eaffbdab37a15444471a22d`.
Repository files establish intended behavior; registry contents, successful remote
runs, account settings, and production infrastructure were not independently audited.

Read the web repository's `AGENTS.md`, `docs/standards.md`,
`docs/agents/domain.md`, `CONTEXT.md`, and relevant ADRs. This API checkout has
`CLAUDE.md` and `docs/coding-standards.md` instead of `AGENTS.md` and
`docs/standards.md`; those instructions, its domain guide, glossary, and ADRs 0018
and 0019 were also read.

### Confirmed from repository files

- Web `Dockerfile` builds Angular using Node 24 and copies
  `dist/pitaka/browser/` into Nginx. The old plan's claim that no web Dockerfile
  exists is obsolete. Web ADR 0001 establishes a static client-rendered SPA.
- Web `.github/workflows/ci.yml` checks formatting, standards, lint, tests, and
  the production build. `build-and-deploy.yml` subsequently rebuilds the tested
  main SHA in Docker and publishes `jimbodev0530/pitaka-web`, with fixed
  `sha-<full-SHA>` and moving `main` tags for AMD64 and ARM64. Image verification
  currently follows promotion of `main`.
- Web `README.md`, `nginx/default.conf.template`, and `nginx/api-proxy.conf`
  establish runtime `API_UPSTREAM` (default `http://api:8080`), port 8080,
  exact `/api` and `/api/` prefix proxying without stripping the path, preservation
  of upstream errors, and SPA fallback. Production browser requests use the same
  origin; they do not need a deployment hostname compiled into Angular.
- API [README](../README.md), [publishing docs](container-publishing.md),
  [Dockerfile](../PitakaApp.Api/Dockerfile), and workflows
  [build](../.github/workflows/build.yml) and [tests](../.github/workflows/tests.yml)
  establish `jimbodev0530/pitaka-api`. Successful main CI gates publication of
  AMD64/ARM64 images with `sha-<full-SHA>` tags and a moving `main` alias.
  The ASP.NET runtime image contains the API and `/app/efbundle` from the same
  revision. Fixed API tags are immutable by publication policy; select digests
  for deployment rather than assuming a tag is technically unchangeable.
- [Development Compose](../docker-compose.yml),
  [image smoke Compose](../docker-compose.images-smoke.yml), and API settings
  establish MySQL, private S3-compatible storage, SMTP, JWT configuration, and
  explicitly configured HTTP port 8080. Local storage uses SeaweedFS and local
  mail capture uses smtp4dev. Production requires a private bucket under
  [ADR 0018](adr/0018-private-s3-compatible-object-storage.md).
- Password reset, email confirmation, and email-change confirmation URLs are
  configurable; current API defaults use localhost:4200 and need deployment
  overrides. The default JWT issuer/audience and other runtime settings remain
  part of the API's configuration contract.
- Existing workflows publish images. Neither is a GitOps controller or evidence
  that an environment has deployed an image. No production host or Kubernetes
  target is established by the inspected plan and files.

### Retained decisions and confirmed scope

Retain independent web/API versions, a local browser origin of
`http://localhost:8080`, ARM64/AMD64 support for runtime images, explicit migrations,
local downtime during upgrades, persistent deployment data separate from development,
and manual database/upload backup and restore procedures. Local HTTPS remains deferred.

Replace the previous requirement that both applications publish deployable images:
web publishes static bytes; API continues publishing its container image. Move
operational routing and environment composition into `itsdevjimbo/pitaka-deploy`.
The existing API contributor workflow and image smoke stack remain API concerns.
This preserves [ADR 0019](adr/0019-docker-only-contributor-loop.md); replacing its
four-service contributor loop with the deployment stack would contradict that ADR
and is outside this proposal.

The previous plan proposed `vMAJOR.MINOR.PATCH` publication for commits in main
history, with checks rerun for historical commits and no prerelease tags. The
inspected workflows only establish main publication. API release-tag support is deferred; web version-tag
promotion is confirmed in ADR 0018 and web #293. For release publication, validate ancestry and checks before publication, never move a fixed
version, and never let a historical release move the `main` alias backward.

### Resolved web publication decision

Web [ADR 0018](https://github.com/itsdevjimbo/pitaka-web/blob/main/docs/adr/0018-retain-promoted-web-builds-for-rollback.md)
records public immutable GitHub Releases for
production-selected builds, 14-day Actions candidates, and at least 90 days of
retention from the most recent promotion. Always protect active versions and the
last three successful production versions, including when older than 90 days.
Version tags select existing checked bytes; missing temporary and durable copies
cause promotion to fail without rebuilding. Failed deployment attempts retain the
selected build but do not count as successful production versions. Cleanup is
manual and must retain builds when deployment records cannot be checked.

Web work is split into [#291](https://github.com/itsdevjimbo/pitaka-web/issues/291)
(main artifacts), [#293](https://github.com/itsdevjimbo/pitaka-web/issues/293)
(durable version-tag promotion), and [#294](https://github.com/itsdevjimbo/pitaka-web/issues/294)
(serving-image retirement after deploy consumer acceptance). These decisions do
not claim the publishing workflows are implemented.

## 2. Repository responsibilities and handoffs

| Owner | Responsibilities | Published or consumed contract |
| --- | --- | --- |
| `pitaka-web` | Angular source, app tests/checks, production build, artifact provenance and publication | Static archive associated with the full web source SHA; checksum and build metadata |
| `pitaka-api` (currently the `pitaka` repository) | API source, CI, runtime Dockerfile, migration bundle, image publication and image smoke checks | `jimbodev0530/pitaka-api:sha-<API-SHA>` plus immutable image index digest; API and migration bundle are one version |
| `itsdevjimbo/pitaka-deploy` | Local Compose, environment version selections, Nginx routing, runtime configuration, deployment procedures, promotion and rollback | Consumes exact web artifact and API image; commits desired state and configuration only |

The deploy repo must not contain generated Angular bundles, API binaries, downloaded
archives, image tarballs, runtime data, or plaintext secrets. Downloads belong in an
ignored cache or an external deployment directory. App repositories own publication
credentials; environment credentials and secret references belong to deployment.
Do not add Compose, ingress manifests, production host settings, or GitOps controller
configuration to `pitaka-web`.

Proposed web handoff: `pitaka-web-<full-web-SHA>.tar.gz`, containing the contents of
`dist/pitaka/browser/` with `index.html` at the extracted root. Publish a manifest
with source repository/SHA, build run, toolchain, archive SHA-256, and production
build configuration. Static bytes are architecture independent. Publish only after
all CI gates succeed, reusing the checked production output instead of rebuilding
it in another job. A SHA identifies source; the checksum identifies exact bytes.
If a retry finds different bytes for an existing version, fail rather than overwrite.

Each environment version record should contain:

- Web source SHA, artifact source and locator (Actions run/artifact ID or GitHub
  Release asset URL), asset identity, and SHA-256.
- API source SHA, human-readable tag, and image index digest, consumed as
  `jimbodev0530/pitaka-api@sha256:<recorded-digest>`.
- Nginx and supporting service image digests, configuration revision, external
  origin, secret references, and migration/compatibility notes.

The deploy Git commit identifies the complete selection. Web and API SHAs are
independent; neither moving `main` nor `latest` is a production selection.

## 3. Local workflow using published outputs

These are steps for a future deploy-repo implementation, not commands available today.
Prerequisites: Docker with Compose, registry access, an artifact download client,
and credentials when an artifact is private. No Angular or .NET compilation is needed.

1. Check out a chosen deploy-repo revision and choose the full web SHA and API
   digest recorded for that environment. Resolve the web SHA to its published
   manifest; reject a manifest for another commit.
2. Download that exact web archive from its recorded source: a promoted GitHub
   Release asset or, for short-lived testing, an Actions candidate. For Actions,
   identify the successful main run by
   `head_sha`, then select its artifact ID/name explicitly, never “latest run.”
   Private/cross-repository downloads need appropriate read credentials. If it
   has expired, fail clearly or retrieve the recorded durable copy; do not silently
   substitute a different SHA or rebuild a supposed identical release. Retain
   locally verified bytes for the previously successful version while it is a
   rollback target, even after the remote candidate expires.
3. Verify SHA-256 before extraction; reject unsafe archive paths and extract into
   a versioned staging directory outside Git. Require `index.html`. Retain the
   previous directory for rollback and avoid overwriting files being served.
4. Pull the recorded API digest, Nginx digest, and dependency images before
   interrupting an existing application. Verify the API source revision and
   architecture. Use an official Nginx runtime with deploy-owned template/snippets
   and the extracted web directory mounted read-only at `/usr/share/nginx/html`.
5. Supply local secrets through an ignored environment file. Start MySQL,
   SeaweedFS, and smtp4dev with persistent volumes in a separate deployment Compose
   project. Wait for database/storage readiness and ensure the private bucket exists.
6. For an upgrade, back up database and uploads, then stop application traffic and
   the API before schema changes. Run a one-time migration container using the
   exact selected API digest and entrypoint `/app/efbundle`. A failed migration
   halts the procedure and leaves the application stopped for explicit repair or
   restore. Never restart an older API or reverse schema changes automatically.
7. Start API and Nginx only after migration succeeds. Set the API to listen on
   container port 8080 and Nginx `API_UPSTREAM=http://api:8080`. Limit template
   substitution to `API_UPSTREAM` to preserve Nginx variables. Publish Nginx on
   `127.0.0.1:8080`; keep API/database/storage internal to the Compose network.
   An optional smtp4dev management port is for inspection, not browser API traffic.
8. Set all three authentication email URLs to their existing paths under
   `http://localhost:8080`. Supply database connection, JWT key, S3 endpoint/region/
   bucket/credentials, and SMTP settings using the established API contract.
9. Check SPA refreshes, `/api` and `/api/...` routing, API error preservation,
   registration/confirmation/sign-in, an expense, and a Profile picture upload.
   Record the deployed versions only after readiness and smoke checks succeed.
   A deploy revision is applied by an explicit local command; committing it does
   not update the running environment.

Routing contract: `/` and client routes serve Angular; exact `/api` and prefix
`/api/` proxy to the API, preserving URI and query string. API 404/500 responses
must never fall through to `index.html`. Reuse the existing Nginx behavior in the
deploy repo, including Docker DNS resolution for replaced API containers. A future
cache policy should revalidate `index.html` and retain old hashed assets long enough
for open browser sessions. Recreate Nginx when switching versioned bind mounts.

## 4. Production promotion, deployment, and rollback

The production platform is unresolved. The deployment contract is portable; an
implementation must choose one of these mappings before production work begins:

| Possible target | Mapping and deployment owner |
| --- | --- |
| Single Docker host | Deploy-owned Compose and Nginx serve the verified web directory and proxy to the API; an operator or deploy-repo job applies a reviewed revision. This is the simplest extension of local deployment. |
| Managed container platform plus static hosting | Deploy-owned automation uploads the exact archive contents and deploys the API digest. An edge router must serve one public HTTPS origin with `/api` routed to API and other paths to static hosting. Independent hostnames alone do not meet the contract. Confirm platform support and routing costs first. |
| Kubernetes, only if explicitly chosen | Deploy repo owns workload/Service/ingress configuration. Fetch and verify web bytes into a volume before Nginx starts, or package those exact bytes into a serving image in deploy CI with recorded provenance. No Angular rebuild. Migration ordering must be explicit. A selected controller reconciles the reviewed state. |

GitOps principles include automatic pull and continuous reconciliation; they are
not inherently Kubernetes-only. Kubernetes controllers are an option if that
platform is chosen. Local manual Compose needs no controller; a push deployment
job alone should be described as deployment automation. See the official
[OpenGitOps principles](https://opengitops.dev/).

Proposed production sequence:

1. App CI validates and publishes immutable outputs. Publication makes a candidate
   available; it does not deploy or automatically promote it to production.
2. A deploy-repo change selects a compatible web checksum/API digest pair, records
   migration impact, and proves both outputs can be fetched. First exercise the
   pair locally or in a chosen validation environment.
3. Promote the same bytes/digests through a reviewed production desired-state
   change. Do not rebuild for each environment. Serialize deployments per
   environment and prevent an older queued change from overwriting a newer one.
4. The chosen executor prefetches outputs, obtains secrets, backs up state, applies
   the explicit migration, and starts/switches traffic after readiness succeeds.
   Use HTTPS in production, correct forwarded-header trust, and authentication
   email links pointing to the public origin. Production SMTP delivery and private
   storage replace local mail capture and local-only defaults.
5. Record actual rollout status against the deploy commit; a merged change is
   desired state, not proof of successful deployment. Preserve the previous
   working pair and its configuration.

Rollback creates a reviewed revert/change to the previous web checksum and API
image digest, then reapplies that state. A web-only rollback may be sufficient if
compatible with the running API. API rollback requires schema compatibility:
reverting Git or a container does not undo a migration. Prefer compatible schema
changes; otherwise stop and follow an explicit backup-restore or forward-fix
procedure. Never automatically reverse migrations. Keep backups of database and
private uploaded files with a coordinated restore procedure; images are not backups.

## 5. Retention, durable storage, and costs

Official documentation checked on 2026-09-26; USD prices and plan limits can change.
Repository visibility, account entitlements, artifact sizes, and actual usage are
not established by local files. Free-tier suitability is conditional on those facts.

| Option | Verified limits/costs | Proposed role |
| --- | --- | --- |
| GitHub Actions artifacts | Default retention 90 days; configurable 1–90 days for public repos and 1–400 for private repos, subject to organization policy. GitHub Free includes 500 MB artifact storage and 2,000 private-repo standard-runner minutes/month. Public standard runners are free. Shared artifact/Packages storage overage is $0.25/GB-month; standard Linux 2-core x64 overage is $0.006/minute. [Retention](https://docs.github.com/en/organizations/managing-organization-settings/configuring-the-retention-period-for-github-actions-artifacts-and-logs-in-your-organization), [billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions). | Retain test candidates for 14 days under the confirmed web policy. Never the sole production/rollback store. |
| GitHub Release assets | Up to 1,000 assets per release, each under 2 GiB; no total release-size or bandwidth limit documented. [Release limits](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases). | Selected durable handoff for production-selected web builds; no Actions artifact expiry. Attach built archive and manifest, not GitHub's autogenerated source archive. Public releases are a practical free option; private access follows repo permissions. |
| Cloudflare R2 Standard | Monthly free allowance: 10 GB-month storage, 1 million Class A and 10 million Class B operations. Beyond it: $0.015/GB-month, $4.50/million Class A, $0.36/million Class B; egress is free. Infrequent Access does not receive this free tier; billing rounds units upward. [R2 pricing](https://developers.cloudflare.com/r2/pricing/). | Evaluated alternative, not selected for web artifacts. Use immutable SHA/checksum keys, restricted writes, and retention protections. Charges apply above the allowance. Keep build artifacts separate from private Profile pictures. |
| Docker Hub | Personal supports unlimited public repositories, one private repo, 200 authenticated pulls per six hours; anonymous pulls are 100 per IPv4 or IPv6 /64 per six hours. Fair-use constraints also apply. [Docker limits](https://docs.docker.com/docker-hub/usage/). | Retain the established public API registry; authenticate pulls and plan retries. Do not infer unlimited storage or guaranteed retention from unlimited repository count. |
| GitHub Packages / GHCR | Public packages are free; Container registry storage and bandwidth are currently free, with advance notice promised for policy changes. [Packages billing](https://docs.github.com/en/billing/concepts/product-billing/github-packages). | Registry alternative if needed; an OCI web artifact adds packaging/download tooling and is not the initial recommendation. |

For GitHub Releases, enable release immutability before publishing selected builds;
stage all assets in a draft and publish only when complete. GitHub protects the
associated tag/assets and produces attestations. Record checksums and retain a
recovery copy rather than treating any provider as a backup guarantee.
[Immutable releases](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases).

Confirmed web policy: Actions retains disposable candidates for 14 days; a candidate
must be copied unchanged into durable storage before promotion. Keep all active
environment versions, the last three successful production versions, and promoted builds for at least 90 days from their latest promotion, including
repeat promotion and failed deployment attempts. Deletion is manually reviewed;
retain builds if deployment records are unavailable. Protect API digests used by
active environments and the last three successful production versions. Deletion
of any unreferenced digest requires manual review; choose an age rule only after
measuring registry usage.
Never apply age-only deletion to referenced artifacts or API digests. Expired
unpromoted builds cannot be promised downloadable by SHA; every-main durable storage is not required. Production tag promotion fails when
no copy exists; a rebuild cannot recover or replace the selected artifact identity.
Estimate storage as archive size × retained build count, plus backups, and monitor
shared quotas before enabling paid overages.

Free publishing/storage does not supply production compute. No free host is selected
or verified here. Budget for API/Nginx compute, MySQL persistent storage and backups,
private object storage, outbound mail, bandwidth and any routing service. These may
require paid services unless a chosen host's current free tier satisfies the measured
workload. Existing local hardware avoids new cloud compute fees. Production cost
cannot be quoted responsibly until the platform, region, capacity, and availability
requirements are known; verify its official pricing at that decision point.

## 6. Migration sequence and acceptance

All steps below are future work; this change updates only this plan.

1. Apply the confirmed public GitHub Releases and web retention decision; create
   `itsdevjimbo/pitaka-deploy`. Preserve current publications during transition.
2. Extend web CI to package its checked production output with SHA/checksum metadata
   and publish it. Enforce trusted main publication and least-privilege credentials.
   Gate candidate promotion on complete artifact verification. If temporary web
   image publishing remains, have it consume those bytes and gate its moving alias
   on verification; the current workflow verifies after promoting the alias.
3. Create `pitaka-deploy` with version records, local Compose, Nginx configuration,
   download/checksum procedures, secret templates, migrations, and backup/restore
   instructions. Transfer runtime routing ownership out of web. Keep app CI and
   app artifact publishing in their respective source repositories.
4. Retain API image publication and architecture/migration checks. Capture the
   published digest for consumption. Implement web release promotion in web #293.
   API SemVer publication is deferred; never reinterpret the moving alias as promotion.
5. Demonstrate fresh local deployment without app builds, independent SHA selection,
   SPA refresh/API errors, authentication email links, expense entry and Profile
   picture upload, persistence across replacement, successful upgrade, failed
   migration behavior, checksum failure, expired download handling, and rollback.
6. Once artifact consumers work, complete web #294 to retire web image publishing and its serving
   Dockerfile/Nginx ownership from `pitaka-web`; update the source/deploy READMEs
   in that later implementation. Preserve old images while rollback depends on them.
7. Select a production platform and price it, implement its mapping in deploy, then
   validate promotion and restore procedures before a first production rollout.
   Only introduce a GitOps controller if the selected platform and operations
   requirements justify continuous reconciliation.

## 7. Risks and unresolved decisions

- **Production:** Which host, region, domain, availability/downtime target, budget,
  and operator? Which executor applies deploy commits? Kubernetes is not assumed.
- **Runtime sizing:** API image name, port convention, migration bundle and service
  dependencies are established above. CPU/RAM sizing, production MySQL version and
  service, SMTP provider, storage provider, secret store, readiness endpoint and
  multi-instance/background-job behavior need validation; do not invent defaults.
- **Storage/access:** Web uses public immutable Releases for production-selected
  builds and the confirmed 14/90-day policy above. Confirm actual account quotas
  and enable immutability during implementation. Production database/upload backup
  frequency and retention remain undecided. Local recovery exercises use disposable
  data, retain the current and previous coordinated backup, and test a restore.
- **Release process:** Web version-tag promotion is confirmed. API SemVer is
  deferred. The deploy revision proposer records web/API compatibility evidence
  and migration impact for review; local smoke tests verify the pair before success.
- **Operational risks:** Deleted artifacts prevent rollback; source SHA alone does
  not prove identical builds; registry throttling can block pulls; a failed schema
  change can require restore. Prefetch and verify before downtime, retain referenced
  bytes and digests, and validate restore procedures.
- **Routing risks:** SPA fallback can mask API errors, stale HTML can reference removed
  chunks, and TLS termination can produce wrong external URLs. Preserve explicit
  API routes, define cache/asset overlap, and validate production proxy behavior.
