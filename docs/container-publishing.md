# Published API image

The `Code Quality and Tests` workflow runs application formatting, build, and test checks.
`Build and Deploy` runs only after that workflow succeeds on `main`, including a pull request
merge, and publishes an image from the same tested commit. Pull requests and other branches do
not trigger image publishing. The API image supports
`linux/amd64` and `linux/arm64` and contains both the API and its EF Core migration bundle. The
workflow resolves the fixed tag to its image-index digest, then pulls that exact digest into
disposable MySQL, SeaweedFS, and smtp4dev stacks for both `linux/amd64` and `linux/arm64`. On each
platform it runs the bundle as an explicit one-time step by overriding the image entrypoint and
starts the API only after the migration succeeds. It moves the `main` API tag only after both
smokes and the publisher's evidence upload pass.

The image is public:

- `jimbodev0530/pitaka-api`

Each successful current-main publication exposes an immutable-by-policy `sha-<full-commit>`
tag and a moving `main` tag. The image index and platform-specific image configs carry
`org.opencontainers.image.revision`. The publication job reuses a SHA tag only when its source
revision and its unique `linux/amd64` and `linux/arm64` descriptors match the commit. It checks
both the fixed tag and the digest-pinned index before and after smoke; a conflict or moved fixed
tag fails without overwriting it. Every main push has a per-commit publish-and-smoke job. A
separate serialized job resolves the current `main` tip before changing the moving tag and
rechecks it afterward. If the branch advances during an update and the newer fixed image is
ready, the promoter reconciles the alias to that image; if it is not ready, it fails or leaves
the alias unchanged for a later promoter.

## Docker Hub and GitHub setup

Create the `pitaka-api` Docker Hub repository as a public repository. Create a Docker Hub
access token with permission to push to it, then add these repository Actions secrets in GitHub:

- `DOCKERHUB_USERNAME`: the Docker Hub account that owns the repository.
- `DOCKERHUB_TOKEN`: the access token with write permission for the repository.

The workflow uses the token only during the `main` publication job. Do not put Docker Hub
credentials in this repository, image build arguments, or image environment variables.

## Pull and inspect a fixed image

The moving `main` tag is convenient for trying the latest build; fixed SHA tags are stable for
repeating a deployment.

```bash
commit=<full-40-character-commit-sha>
docker pull "jimbodev0530/pitaka-api:sha-$commit"

docker buildx imagetools inspect "jimbodev0530/pitaka-api:sha-$commit"
./scripts/inspect-published-image.sh "jimbodev0530/pitaka-api:sha-$commit" "$commit"
```

The manifest list should include `linux/amd64` and `linux/arm64`. The OCI index and the
platform-specific images identify their source revision. The repository inspector prints the
validated index digest and platform child digests as JSON. The publication job checks that same
identity through both the fixed tag and `repository@sha256:<index-digest>`.

## Run the published image against disposable data

With Docker Engine, Docker Compose, QEMU support for the non-native platform, and `curl`
installed, and Docker CLI 28.1.0 or newer, run the smoke script for each platform using the
index digest returned by the inspector. The publisher workflow pins Docker 29.8.1 because the
runtime-label check uses `docker image inspect --platform` to select each image variant:

```bash
./scripts/smoke-published-images.sh \
  <full-40-character-commit-sha> \
  <sha256-index-digest> \
  linux/amd64
./scripts/smoke-published-images.sh \
  <full-40-character-commit-sha> \
  <sha256-index-digest> \
  linux/arm64
```

Each run pulls the digest-pinned API image for the explicit platform, verifies its runtime
source-revision label, starts an isolated MySQL, SeaweedFS, and smtp4dev stack, runs
`/app/efbundle` with an entrypoint override, and then waits for the API's OpenAPI endpoint. The
migration step and API service use the same image digest. The script removes that platform run's
containers and volumes on exit, including after a failure. Its credentials and database are
disposable smoke-test values; they are not deployment settings.

## Digest-bound smoke evidence

After both platform smokes, the publisher rereads the fixed tag and digest-pinned index. It then
writes one schema-version-1 JSON record with the source repository and SHA, fixed tag, index
digest, both platform child digests, successful migration and API readiness results, and the
GitHub run ID and attempt. The record must pass the strict validator and remain under 16 KiB:

```bash
./scripts/validate-api-smoke-evidence.sh <api-smoke-evidence.json>
```

The publisher job uploads the record as
`api-smoke-evidence-<run-id>-<run-attempt>` for 14 days. A missing file, validation error, or
upload failure fails the publisher job, so the moving-tag job cannot start. Consumers must still
corroborate the record against the GitHub run and fresh registry reads; the artifact is the
publisher's bounded result record, not a substitute for those checks.

## Automatic local API selection

After a successful `Build and Deploy` run finishes its promoter, the separate `Select local API
image` workflow verifies the triggering run and jobs through GitHub, resolves the current `main`
SHA, and finds that SHA's successful publisher attempt and smoke artifact. It checks the artifact
against fresh reads of the fixed SHA tag, its digest-pinned index, and the moving `main` alias.
The alias index digest may differ; the source revision and both platform child digests must match.

The handoff uses the canonical `pitaka-deploy` version validator. It changes only the six-field
`api` selection in `versions/local.json`, preserves every other field, and updates `main` through
the Contents API with the version-file blob SHA as its compare-and-swap precondition. It orders
selections by source commit ancestry and makes at most three fresh read/evaluate/write attempts.
Each run writes a job summary. An unresolved handoff fails its own workflow and leaves the API
publication result intact. Updating this record changes desired local inputs; it does not apply
the running local stack.

Each successful handoff also uploads a 90-day receipt that names the exact API-changing deploy
commit and complete API selection. Later handoffs require that receipt to recognize the App
commit; a missing or expired receipt stops automatic selection for review. Keep the `pitaka`
repository's Actions artifact retention at 90 days so these history receipts remain available.

### App configuration and recovery

Install the shared GitHub App only on `itsdevjimbo/pitaka-deploy` with repository Contents
read/write permission. In the `pitaka` repository Actions settings, set the `DEPLOY_APP_CLIENT_ID`
variable and `DEPLOY_APP_PRIVATE_KEY` secret. The workflow creates a short-lived token restricted
to `pitaka-deploy`, after it verifies publication and registry evidence. The workflow's own
`GITHUB_TOKEN` has Actions and Contents read permission only.

To rotate the App key, generate a replacement private key in the GitHub App settings and update
`DEPLOY_APP_PRIVATE_KEY` in the `pitaka` repository Actions secrets. Confirm a selection workflow
can create its deploy-scoped token, then revoke the old key. Keep the private key and installation
tokens out of the repository and workflow logs.

When a selection handoff fails for a transient Contents API or network error, rerun the failed
`Select local API image` workflow run. The rerun verifies the same successful triggering
`Build and Deploy` run and attempt, then rechecks the live-main candidate and its independent
publisher evidence. To reconcile against a specific successful publisher attempt, open `Select
local API image` in Actions, choose **Run workflow** on the default branch, and enter only these
inputs:

- `trigger_run_id`: the numeric run ID of a successful `Build and Deploy` run.
- `trigger_run_attempt`: the positive attempt number whose publisher and promoter jobs both
  succeeded.

The handoff confirms that run belongs to this repository's `Build and Deploy` workflow, came from
a push to `main`, and completed both required jobs successfully. It then resolves the live `main`
candidate and that SHA's own publisher evidence. A workflow rerun or manual dispatch cannot waive
missing, expired, or contradictory smoke evidence. If evidence for the current `main` SHA has
expired or is missing, first produce a fresh successful `Build and Deploy` publish-and-smoke
attempt for that same SHA and unchanged fixed digest; use that run ID and attempt if manual
reconciliation is still needed.

Both automatic handoff and manual reconciliation use the same eligibility, ancestry, API-only
write, retry, read-back, and reporting path. The run summary identifies the triggering and
publisher attempts, previous and final API selections, write count, outcome, and recovery action.
Failures emit an error annotation and fail the handoff while leaving publication status unchanged.
Never provide pasted API JSON, image URLs, a bare SHA, or a digest as selection authority. A direct
API-changing edit without recognized handoff provenance stops selection for review. Controlled
manual pinning is not part of this workflow.

The record selects desired local inputs only. Applying those inputs and changing the running local
stack remain a separate, explicit deployment operation.
