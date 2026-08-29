# PitakaApp

A personal expense tracker API built with ASP.NET Core, EF Core, and MySQL.

## Two dev loops

There are two supported ways to run the API, and they carry different guarantees:

- **The Docker loop** (below) is the one the setup instructions describe. Its guarantee is that Docker with Compose is the *only* thing you need installed — no local .NET SDK, no local MySQL. Everything, including running tests and generating migrations, happens through containers. It serves the API at `http://pitaka.localhost`.
- **[The SDK loop](#the-sdk-loop)** runs the API with `dotnet run` against your own machine's SDK. It exists for the step debugger and hot reload, which matter on a project whose purpose is learning .NET. It serves the API at `http://localhost:5044` (and `https://localhost:7272` under the `https` launch profile). This is the address Pitaka Web's `environment.ts` points at.

Pick one. The Docker loop needs nothing but Docker; the SDK loop needs a local .NET SDK and a reachable MySQL.

## Setup — the Docker loop

1. Copy the environment template and fill in your own values:

   ```bash
   cp .env.example .env
   ```

   `MYSQL_ROOT_PASSWORD` and `JWT_KEY` can be any values for local development — just don't reuse anything real.

2. Start the API and database:

   ```bash
   docker compose up
   ```

   The database schema isn't created automatically — run the migrator once the first time (and again after pulling any new migrations):

   ```bash
   docker compose run --rm migrator
   ```

3. The API is now available at `http://pitaka.localhost`. No DNS setup needed — `.localhost` is a reserved TLD that every OS and browser resolves to `127.0.0.1` automatically.

   Mail the API sends (currently just password-reset links) goes to the `smtp4dev` container, which delivers nothing onward and instead shows every message in a web UI at `http://localhost:5080`.

## The SDK loop

This loop trades the zero-SDK guarantee for a step debugger and hot reload. It does **not** replace the Docker loop — the setup above is still the supported baseline. Use this one when you're debugging.

You need:

- A local .NET 10 SDK.
- A MySQL the API can reach. The simplest option is to start just the database from Compose — `docker compose up mysql` publishes it on `localhost:3306` — and leave the rest of the stack off.
- An SMTP server to catch outgoing mail. Same approach as MySQL: `docker compose up smtp4dev` publishes its SMTP port on `localhost:2525` and its web UI on `http://localhost:5080`. The mail defaults in `appsettings.json` already point at `localhost:2525`, so this works with no extra configuration; override `Email__Host` / `Email__Port` if you run SMTP elsewhere.
- A `DefaultConnection` connection string and a JWT key. Neither ships in `appsettings.json`; supply them with user secrets or environment variables, e.g.:

  ```bash
  export ConnectionStrings__DefaultConnection="Server=localhost;Port=3306;Database=pitaka;User=root;Password=<MYSQL_ROOT_PASSWORD>"
  export Jwt__Key="<any value for local dev>"
  export Email__Host="localhost"   # only if not using the shipped localhost:2525 default
  export Email__Port="2525"
  ```

Apply migrations the same way as the Docker loop (`docker compose run --rm migrator`), or run `dotnet ef database update` from `PitakaApp.Api/` against the same connection string.

Run it:

```bash
cd PitakaApp.Api
dotnet run                          # http://localhost:5044
dotnet run --launch-profile https   # also binds https://localhost:7272
dotnet watch                        # same, with hot reload
```

`ASPNETCORE_ENVIRONMENT` is `Development` under both launch profiles. In Development the HTTPS-redirect middleware is guarded off (see `Program.cs`), so a plain-HTTP caller on `http://localhost:5044` is served directly and never bounced to the `https` port's self-signed certificate — which is why `environment.ts` can keep pointing at `http://localhost:5044`.

## Running tests

```bash
docker compose run --rm test
```

`test` (like `api` and `migrator`) copies your source into the image at *build* time — it doesn't see changes automatically. If you've changed code since the image was last built, rebuild first or the suite will silently run against stale source:

```bash
docker compose build test && docker compose run --rm test
```

## Working with migrations

Applying migrations (what you already ran in setup) and generating new ones are two different services, because generating a migration needs to write a real file back onto your machine — see [`docker-compose.yml`](docker-compose.yml) for why `dev` uses a bind mount instead of a build-time copy like the other services.

**Apply migrations:**

```bash
docker compose run --rm migrator
```

**Generate a new migration**, after changing a model:

```bash
docker compose run --rm dev dotnet ef migrations add SomeMigrationName
```

The generated files land directly in `PitakaApp.Api/Migrations/`, ready to commit.

**Remove the most recent (not-yet-applied) migration:**

```bash
docker compose run --rm dev dotnet ef migrations remove
```

## Stack

- ASP.NET Core (.NET 10)
- Entity Framework Core, MySQL (via Pomelo)
- JWT authentication
