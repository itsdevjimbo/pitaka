# Published API and migration images

The `main` push workflow runs the existing formatting, build, and test checks on the pushed
commit before publishing. Pull requests and other branches only run validation. Publication
builds both images from that same commit for `linux/amd64` and `linux/arm64`, then pulls the
published pair into a disposable MySQL, SeaweedFS, and smtp4dev stack. The migration bundle
must complete and the API must answer at `/openapi/v1.json` before the workflow moves either
`main` image tag.

The images are public:

- `jimbodev0530/pitaka-api`
- `jimbodev0530/pitaka-migrations`

Each successful current-main publication exposes an immutable-by-policy `sha-<full-commit>`
tag and a moving `main` tag. Both image indexes and image configs carry
`org.opencontainers.image.revision`. The publication job reuses a SHA tag only when its source
revision and platform manifests match the commit; a conflict fails without overwriting it.
Every main push has a per-commit publish-and-smoke job. A separate serialized job resolves
the current `main` tip before changing either moving tag and rechecks it afterward. If the
branch advances during an update and the newer fixed pair is ready, the promoter reconciles
both aliases to that pair; if that pair is not ready, it fails or leaves aliases unchanged
for a later promoter. Docker Hub cannot update two repository tags atomically, so clients
should compare the source revision annotations on the API and migration tags before using
them as a pair.

## Docker Hub and GitHub setup

Create both Docker Hub repositories as public repositories. Create a Docker Hub access token
with permission to push to those repositories, then add these repository Actions secrets in
GitHub:

- `DOCKERHUB_USERNAME`: the Docker Hub account that owns the repositories.
- `DOCKERHUB_TOKEN`: the access token with write permission for both repositories.

The workflow uses the token only during the `main` publication job. Do not put Docker Hub
credentials in this repository, image build arguments, or image environment variables.

## Pull and inspect a matching pair

Use the same full commit SHA for both images. The moving `main` tags are convenient for trying
the latest build; fixed SHA tags are stable for repeating a deployment.

```bash
commit=<full-40-character-commit-sha>
docker pull "jimbodev0530/pitaka-api:sha-$commit"
docker pull "jimbodev0530/pitaka-migrations:sha-$commit"

docker buildx imagetools inspect "jimbodev0530/pitaka-api:sha-$commit"
docker buildx imagetools inspect "jimbodev0530/pitaka-migrations:sha-$commit"
```

Each manifest list should include `linux/amd64` and `linux/arm64`. The OCI index and the
platform-specific images identify their source revision. The publication job checks that both
SHA tags report the same commit and contain both platforms.

## Run the published pair against disposable data

With Docker Engine, Docker Compose, and `curl` installed, run the smoke script with the commit
SHA whose fixed tags you want to exercise:

```bash
./scripts/smoke-published-images.sh <full-40-character-commit-sha>
```

The script pulls both images, verifies their source-revision labels match the requested SHA,
starts an isolated MySQL, SeaweedFS, and smtp4dev stack, applies the migration bundle, and waits
for the API's OpenAPI endpoint. It removes that run's containers and volumes on exit, including
after a failure. Its credentials and database are disposable smoke-test values; they are not
deployment settings.
