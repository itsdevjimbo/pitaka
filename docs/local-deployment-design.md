# Container publishing and local deployment design

Status: confirmed by the user; ready for a later specification.
This document is design input for a later specification, not an implementation.

## Confirmed decisions

- CI publishes API and web images to Docker Hub. The user manually pulls and
  starts the images locally using Docker Compose; CI does not update the machine.
- Every push to `main` publishes images, including updates produced by squash or
  rebase merges. Repository rules should require pull requests.
- Version tags, such as `v1.2.0`, may publish any commit in `main`'s history, not
  only its current tip. A tag targeting a commit outside that history must fail
  before publishing.
- This work is design only. Implementation will follow a later specification.
- Each repository publishes its own image independently. Local deployment selects
  explicit API and web versions; their version numbers may differ.
- Publication must pass the existing checks, including when a version tag targets
  an older commit.
- Docker Hub images will be public under `jimbodev0530`: `pitaka-api` and
  `pitaka-web`. The API image contains both the API and its EF Core migration bundle.
- Images must support ARM64 and AMD64.
- Local deployment initially uses one HTTP origin, forwarding `/api` requests to
  the API container. HTTPS is deferred.
- Main builds publish a commit tag (`sha-<commit>`) and update the moving `main`
  image tag. Version-tag builds publish the matching release tag (for example,
  `v1.2.0`). Commit and release tags must never be reassigned.
- A separate deployment Compose file in the API repository pulls published API
  and web images and runs MySQL, SeaweedFS, and smtp4dev. Deployment data uses
  persistent volumes separate from development data.
- Migrations run as an explicit one-time deployment step in a dedicated container
  using the selected API image tag and overriding its entrypoint with
  `/app/efbundle`. The API service uses that same selected API tag. The database
  must be ready first; the API starts only after migration succeeds. A failed
  migration stops the deployment procedure.
- Local upgrades permit downtime. Pull all selected images before stopping the
  existing API and web, apply migrations, then start the selected application
  versions. Migration failure leaves the application stopped for investigation.
- Include a documented manual backup and restore procedure for the database and
  uploaded files. Do not automatically reverse migrations; reverting an API image
  alone does not guarantee compatibility with the changed schema.
- Use `http://localhost:8080` as the browser-facing origin, with authentication
  email links pointing to the same origin. This replaces the earlier request for
  `pitaka.localhost`.
  Configuration comes from a local environment file; secrets are excluded from
  Git and published images.
- An API publication succeeds only when the single API image passes its revision,
  architecture, and migration/API smoke checks. The migration bundle and API are
  selected by one image tag, so there is no separate migration image to match.
  Deployment must pull the selected API and web images before stopping the running
  application.
- Release Git tags use `vMAJOR.MINOR.PATCH` (for example, `v1.2.0`). Prerelease
  tags such as `v1.2.0-beta.1` are outside the initial scope; main builds provide
  images for trying unreleased changes.

## Agreed acceptance scenarios

- A main merge and a valid version tag publish images. Failed checks and tags
  outside `main` history do not publish images.
- Published API and web images support ARM64 and AMD64. The API image's migration
  bundle runs from the same architecture-specific image as the API.
- A fresh local deployment runs pulled images without locally building application
  code.
- Registration, email confirmation, sign-in, recording an expense, and uploading
  a profile picture work.
- Data survives container replacement. An upgrade applies a new migration.

## Existing constraints

- API and web live in separate repositories: `itsdevjimbo/pitaka` and
  `itsdevjimbo/pitaka-web`.
- Both repositories validate changes in CI. The API repository publishes one
  `pitaka-api` image containing the API and migration bundle; web image publication
  remains part of the later local-deployment work.
- The API has a runtime Dockerfile stage. The web has no Dockerfile and embeds
  an API URL during its build.
- The API depends on MySQL, private object storage (SeaweedFS), and SMTP.
- The current development Compose setup is governed by ADR 0019. Its API uses
  SDK tooling and explicit migrations; the runtime image has no EF CLI.
- The future deployment selects one API image tag for both its migration job and
  API service. This does not change the separate contributor workflow, which keeps
  its SDK watcher and explicitly requested development migrations.
- The local machine uses ARM64.

## Design confirmation

The user confirmed the consolidated design. No interview decisions remain open.
Implementation is deferred to the later specification.

## Details for the later specification

- Preserve `/api` routing, support browser refreshes on web routes, and configure
  authentication email URLs consistently with the selected external origin.
- Define registry retry behavior without reassigning immutable image tags. Prevent
  older main builds finishing late from replacing a newer published `main` tag.
- Keep historical version-tag publications from moving `main` backwards.
- Define database readiness, storage initialization, application readiness, and
  migration failure reporting as part of the deployment procedure.
- Document configuring CI publishing credentials and local runtime secrets.

Recommendations made during the interview are not decisions until confirmed.
