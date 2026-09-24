---
status: accepted
---

# Private S3-compatible object storage

The API accesses object storage through the S3 protocol using an explicitly configured private
bucket. Objects have no public-read policy or public URL.

The API uses the S3 protocol through the AWS SDK for .NET. The endpoint, region, bucket name,
and credentials come from deployment configuration. Docker Compose runs SeaweedFS for local
development, creates the private bucket at startup, and persists its data in a named volume.
The SDK loop connects to the same service through its host endpoint. Production points the API
at an existing private S3-compatible bucket.

## Why

S3-compatible storage gives the API one protocol for local development and production. Keeping
the bucket private makes the API's configured credentials the access boundary instead of a
public object URL.

## Consequences

- The API accesses the configured bucket with credentials supplied by deployment configuration.
- Bucket objects remain private and are reached through the API or approved server-side clients.
- Local storage is persistent across Compose service restarts. Removing the named volume deletes
  that local object data.
- Production deployment supplies an existing private bucket and credentials through environment
  or secret configuration; no production secret is checked into this repository.
