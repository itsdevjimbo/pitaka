# PitakaApp

A personal expense tracker API built with ASP.NET Core, EF Core, and MySQL.

## Requirements

Docker (with Compose) is the only thing you need installed. No local .NET SDK, no local MySQL — everything, including running tests and generating migrations, happens through containers.

## Setup

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

## Running tests

```bash
docker compose run --rm test
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
