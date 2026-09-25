---
status: accepted
---

# Use one Docker contributor loop

Issues #183 and #184 replace the two documented development loops from [ADR 0002](0002-support-both-dev-loops.md)
with one Docker-only workflow. Contributors start the API and its dependencies with Compose and
run requested .NET commands through the checked-in `pdotnet` wrapper. A host .NET SDK is not part
of setup.

## Why

The previous Docker loop copied source into command images and ran seven unprofiled services,
while its API used a runtime-only image. It therefore could not provide live source updates or
reliable commands against the current checkout. The supported workflow now bind-mounts the full
checkout into an SDK-capable API container. Tests and migrations run only on request, and a
  separate final image stage contains only the ASP.NET runtime and published API. `dotnet-ef`
  is installed in the development stage and is absent from the final image.

## Consequences

- Plain `docker compose up` starts the API, MySQL, SeaweedFS, and smtp4dev. It runs no tests or
  migrations.
- The API watches the mounted source. Its build cache is separate from the output of requested
  `pdotnet` commands.
- `pdotnet` preserves the caller's directory and input, forwards arguments, and writes generated
  source and requested build output into the checkout. The default `dotnet ef` project is the
  API project.
- The test fixtures use dedicated databases, and the storage integration test reaches the
  Compose SeaweedFS service.
- Applying the initial schema and later migrations is an explicit `pdotnet ef database update`
  command.
- The SDK loop and host-SDK instructions are no longer a supported contributor workflow.
