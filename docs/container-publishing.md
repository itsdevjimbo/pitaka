# Published API image

The `Code Quality and Tests` workflow runs formatting, build, and test checks. After a
successful push to `main`, the separate `Build and Deploy` workflow publishes an image from that
same tested commit. Pull requests and other branches only run validation. The API image supports
`linux/amd64` and `linux/arm64` and contains both the API and its EF Core migration bundle. The
workflow pulls that image into a disposable MySQL, SeaweedFS, and smtp4dev stack, runs the bundle
as an explicit one-time step by overriding the image entrypoint, and starts the API only after
the migration succeeds. It moves the `main` API tag only after the smoke check passes.

The image is public:

- `jimbodev0530/pitaka-api`

Each successful current-main publication exposes an immutable-by-policy `sha-<full-commit>`
tag and a moving `main` tag. The image index and platform-specific image configs carry
`org.opencontainers.image.revision`. The publication job reuses a SHA tag only when its source
revision and platform manifests match the commit; a conflict fails without overwriting it.
Every main push has a per-commit publish-and-smoke job. A separate serialized job resolves
the current `main` tip before changing the moving tag and rechecks it afterward. If the branch
advances during an update and the newer fixed image is ready, the promoter reconciles the alias
to that image; if it is not ready, it fails or leaves the alias unchanged for a later promoter.

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
```

The manifest list should include `linux/amd64` and `linux/arm64`. The OCI index and the
platform-specific images identify their source revision. The publication job checks the fixed
tag's source commit and both platforms.

## Run the published image against disposable data

With Docker Engine, Docker Compose, and `curl` installed, run the smoke script with the commit
SHA whose fixed tags you want to exercise:

```bash
./scripts/smoke-published-images.sh <full-40-character-commit-sha>
```

The script pulls the API image, verifies its source-revision label matches the requested SHA,
starts an isolated MySQL, SeaweedFS, and smtp4dev stack, runs `/app/efbundle` with an entrypoint
override, and then waits for the API's OpenAPI endpoint. The migration step and API service use
the same image tag. It removes that run's containers and volumes on exit, including after a
failure. Its credentials and database are disposable smoke-test values; they are not deployment
settings.
