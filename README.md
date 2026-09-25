# PitakaApp

A personal expense tracker API built with ASP.NET Core, EF Core, and MySQL.

See [Coding standards](docs/coding-standards.md) for implementation and review rules, examples,
and existing exceptions.

API contracts: [private-resource ownership and 404 responses](docs/api/ownership.md) and
[Linked Contributions](docs/api/linked-contributions.md), plus
[private Profile pictures](docs/api/profile-pictures.md).

## Docker development loop

Docker Compose is the supported contributor workflow. You need Docker with the Compose plugin;
you do not need a host .NET SDK or a host MySQL installation. Compose starts exactly four
long-running services: the API, MySQL, SeaweedFS, and smtp4dev. The API runs from the SDK stage
with the checkout mounted and watches source changes. Tests and migrations run only when you ask
for them with `pdotnet`.

### Setup

1. Copy the environment template:

   ```bash
   cp .env.example .env
   ```

   The template contains local-only example values. `JWT_KEY` must be at least 32 characters.
   Do not reuse production values.

2. Install the checked-in `pdotnet` wrapper once:

   ```bash
   ./scripts/install-pdotnet
   ```

   The installer creates `/usr/local/bin/pdotnet` as a symlink to this checkout, so `sudo` may
   ask for your administrator password. Keep the checkout in place after installing it.

3. Start the stack:

   ```bash
   docker compose up
   ```

   The API is at `http://pitaka.localhost`. Source edits are picked up while it runs. Password
   reset and other API mail is captured in smtp4dev's web UI at `http://localhost:5080`; it is not
   delivered onward. The private S3-compatible storage endpoint is available at
   `http://localhost:8333`, and its data persists in a Compose volume.

4. Create or update the local database schema explicitly:

   ```bash
   pdotnet ef database update
   ```

   This creates the schema on a fresh volume and applies new migrations after a pull. Compose
   startup does not run migrations.

### Running .NET commands

`pdotnet` sends a `dotnet` command to the running API container. Run it from any directory in
the checkout; it keeps that directory, forwards arguments and input, and does not start the stack
for you. For example:

```bash
# From the repository root
pdotnet build PitakaApp.Api/PitakaApp.Api.csproj
pdotnet test PitakaApp.Api.Tests/PitakaApp.Api.Tests.csproj

# From PitakaApp.Api/
pdotnet ef migrations add AddSomething

# From PitakaApp.Api.Tests/
pdotnet test
```

EF commands without `--project` or `--startup-project` use `PitakaApp.Api`; explicit values are
respected. Generated migrations and requested build outputs are in the host checkout. The API
watch build has a separate cache, so requested builds do not replace the files it is using. One
`pdotnet` command may run at a time; a second invocation reports that the first is still running.
If the stack is stopped, `pdotnet` reports that and leaves it stopped.

The test fixtures reset and migrate only `pitaka_test` and `pitaka_test_realauth`, separate from
the development `pitaka` database. Each fixture checks its configured database name before any
reset. Tests start only when requested with `pdotnet test`; the storage integration test uses
SeaweedFS from the same stack.

### Building the deployable API image

The Dockerfile's default final stage is the deployable image. It contains the published API on
the ASP.NET runtime image. The SDK is used by the build and development stages; `dotnet-ef` is
installed only in the development stage. Neither is in the final image.

```bash
docker build --target final -f PitakaApp.Api/Dockerfile -t pitaka-api:local .
```

## Stack

- ASP.NET Core (.NET 10)
- Entity Framework Core, MySQL (via Pomelo)
- JWT authentication
- SeaweedFS for private S3-compatible object storage
- smtp4dev for local mail capture
