# Document Archive (DMS)

Company-wide document archive: versioned documents, dynamic metadata, fine-grained permissions,
workflow, secure sharing, full-text search with OCR, and a complete audit trail.

- Architecture: [docs/architecture.md](docs/architecture.md)
- Versioning model: [docs/adr/0001-file-versioning-and-metadata-revisions.md](docs/adr/0001-file-versioning-and-metadata-revisions.md)

**Status: phase 1 (foundation) is complete.** Identity, the authorization engine, the audit log and
the transactional job queue exist and are tested. Documents, storage, document types, workflow,
search and sharing are the later phases listed in the architecture document.

## Stack

.NET 10 / C# 14 · ASP.NET Core 10 minimal APIs · EF Core 10 · PostgreSQL · React 19 + TypeScript + MUI.
Modular monolith: DDD, clean architecture, CQRS, one schema and one DbContext per module.

## Prerequisites

- .NET 10 SDK. If it is not on the machine:
  `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$HOME/.dotnet"`
- Docker, for PostgreSQL.
- Node 20+, for the frontend.

With a user-local SDK, both variables are needed (the second one lets the test executables find
the runtime):

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
```

## Run it

```bash
# 1. PostgreSQL on port 5433 (does not touch anything already using 5432)
./scripts/dev-db.sh

# 2. Migrations, seed and the API on http://localhost:5080
#    The bootstrap password is only used the first time, and must be changed at first sign-in.
Dms__Bootstrap__AdminPassword='ChangeMe!Dev12345' ./scripts/dev-api.sh

# 3. Frontend on http://localhost:5173
cd frontend && npm install && npm run dev
```

Check it:

```bash
curl http://localhost:5080/health/ready

curl -X POST http://localhost:5080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"ChangeMe!Dev12345"}'
```

API reference while the API is running in development: <http://localhost:5080/scalar/v1>
(OpenAPI document at `/openapi/v1.json`).

## Tests

```bash
./scripts/dev-db.sh     # integration tests need PostgreSQL
./scripts/test.sh
```

The test projects are xunit v3 / Microsoft.Testing.Platform executables. `scripts/test.sh` runs
each one directly, because `dotnet test` on this SDK reports "Zero tests ran" for MTP projects.
Integration tests create and drop their own database per run; point `DMS_TEST_POSTGRES` at another
server to use one.

## Everything in Docker

```bash
cp deploy/.env.example deploy/.env    # then fill in every value marked below
cd deploy && docker compose up -d --build
```

`.env` must have **`POSTGRES_PASSWORD`, `DMS_ADMIN_PASSWORD` and `DMS_JWT_SIGNING_KEY`** set;
compose refuses to start otherwise. Generate the key with `openssl rand -base64 64`.

This builds one image with two entrypoints: the migrator runs to completion, then the API starts
on http://localhost:5080. The API is `dms-api-1`, the database `dms-postgres-1`.

Two things specific to this machine:

- The Docker daemon reaches `mcr.microsoft.com` but **not Docker Hub**, so `postgres:18` cannot be
  pulled. `.env` sets `POSTGRES_IMAGE` to a locally cached image instead.
- The compose stack and `scripts/dev-db.sh` both publish port 5433, so run one or the other.
  `docker stop dms-postgres` frees it for compose; `docker compose down` frees it for the script.

## Layout

```
src/BuildingBlocks/    SharedKernel, Application (dispatcher), Infrastructure (unit of work, jobs), Web
src/Modules/           Identity, Authorization, Audit   (Domain+Application / Infrastructure / Contracts)
src/Dms.Host/          API composition root, also hosts the background worker
src/Dms.Migrator/      applies migrations and seeds, runs as a one-shot container
tests/                 architecture, unit and integration tests
frontend/              React + TypeScript + MUI shell (Persian, RTL)
deploy/                Dockerfile, compose, .env.example
docs/                  architecture and decision records
```

## Conventions

- Migrations only run from `Dms.Migrator`, never at API startup.
- No secrets in source: connection strings, the JWT signing key and the bootstrap password all come
  from the environment.
- Authorization is decided in one place (`IDmsAuthorizer`); endpoints and handlers call it and never
  reimplement the rules.
- The audit log is append-only; the database rejects UPDATE and DELETE on it.
- Module boundaries are enforced by tests in `tests/Dms.ArchitectureTests`.
