# Document Archive (DMS)

Company-wide document archive: versioned documents, dynamic metadata, fine-grained permissions,
workflow, secure sharing, full-text search with OCR, and a complete audit trail.

- Architecture: [docs/architecture.md](docs/architecture.md)
- Versioning model: [docs/adr/0001-file-versioning-and-metadata-revisions.md](docs/adr/0001-file-versioning-and-metadata-revisions.md)

**Status: phases 1–8 are done:** identity,
authorization, audit, the job queue, documents and storage, dynamic document types with a rule
language shared by the server and the browser, versioned approval workflows with a task inbox,
malware scanning, watermarked previews and printing, Persian OCR and version-aware full-text
search, and sharing: internal shares and external links, each pinned to one version, re-checked
against the sharer's rights on every use and always overridden by an explicit DENY; a restricted
database role that can only append to the audit log, a tamper-evident seal chain over the log,
audit export and viewer, in-app notifications, and the admin UI (users, groups, roles, categories,
ACL editor with “why?”). **Phase 9 (hardening) is in progress:** baseline security headers,
[`SecuritySuiteTests`](tests/Dms.IntegrationTests/SecuritySuiteTests.cs), k6 load scripts under
[`load/`](load/), and runbooks in [`docs/hardening/`](docs/hardening/).

## Stack

.NET 10 / C# 14 · ASP.NET Core 10 minimal APIs · EF Core 10 · PostgreSQL · React 19 + TypeScript + Tailwind.
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

### First documents

Administrators manage the category tree but get **no document content by default** (decision D5).
To file documents, grant yourself (or someone else) rights on a category; the grants go through
the API and are audited. `scripts/grant-access.sh` does it and asks for the password:

```bash
scripts/grant-access.sh                                   # admin, full access to the root category
scripts/grant-access.sh --user sara.ahmadi --read-only    # someone else, view/download/print only
scripts/grant-access.sh --url http://localhost:5080       # against scripts/dev-api.sh instead of compose
```

Existing entries (ALLOW or DENY) are left untouched; `--help` lists the options.

### Previews, OCR and search (optional services)

Everything works without them: PDFs and images still get previews, and search falls back to
titles. To switch them on locally:

```bash
docker run -d --name dms-opensearch -p 9200:9200 -e discovery.type=single-node \
  -e DISABLE_SECURITY_PLUGIN=true -e DISABLE_INSTALL_DEMO_CONFIG=true opensearchproject/opensearch:2.19.3
docker build -t dms-tika deploy/tika && docker run -d --name dms-tika -p 9998:9998 dms-tika

Dms__Search__OpenSearchUrl=http://localhost:9200 Dms__Search__TikaUrl=http://localhost:9998 ./scripts/dev-api.sh
```

ClamAV (`Dms__Storage__Scanning__Enabled=true`) and Gotenberg for Office previews
(`Dms__Storage__Renditions__GotenbergUrl`) work the same way. Administrators see the index and
extraction status, rebuild the index and retry failed extractions under **جستجو و نمایه**.

### Sharing

A document's **اشتراک‌ها** panel shares a published version with a colleague (DOCUMENT_SHARE) or
creates an external link (DOCUMENT_SHARE_EXTERNAL, and the document type must allow it). Nobody
can share more than they may do themselves. Recipients find their shares under
**اشتراک‌شده با من**; link visitors open `/s/<token>` without an account. Links always expire
(`Dms__Sharing__MaxLinkLifetimeDays`, default 90), may be limited to a number of openings and may
need a password (locked for `LinkPasswordLockoutMinutes` after `LinkPasswordMaxAttempts` wrong
guesses). `Dms__Sharing__ExternalLinksEnabled=false` switches links off altogether. Opening links
is rate limited per address (`Dms__RateLimits__ShareLinkOpensPerMinute`, default 20).

### Audit and notifications

Users with AUDIT_VIEW find the log under **رویدادنگاری**: filters, the details of each entry, and
the state of the **seals**. Every hour of the log is sealed (a digest of its rows, chained to the
previous seal, HMAC-keyed with `Dms__Audit__SealKey`), so an edited, deleted or back-dated row
shows up when the chain is verified, daily by the worker or on demand. AUDIT_EXPORT adds CSV and
JSON-lines exports of up to a year (`Dms__Audit__MaxExportDays`); every export is audited too.

The API and the worker connect as the runtime role (`dms_app`), which the migrator creates and
grants DML only: INSERT and SELECT on `audit`, no UPDATE, DELETE, TRUNCATE or DDL anywhere
(section 4.10). `scripts/dev-api.sh` still runs the API as the owner for convenience.

The bell in the app bar shows in-app notifications: a task waiting for you (directly or through
your group or role), an overdue task, the outcome of a review you started or whose version you
wrote, and a version shared with you. Nobody is told about what they did themselves.

Files are stored on the local filesystem by default (`Dms:Storage:Provider = filesystem`, under
`.data/objects` in development). For S3 or MinIO set `Dms__Storage__Provider=s3` and the
`Dms__Storage__S3__*` values; the bucket must exist.

API reference while the API is running in development: <http://localhost:5080/scalar/v1>
(OpenAPI document at `/openapi/v1.json`).

## Tests

```bash
./scripts/dev-db.sh     # integration tests need PostgreSQL
./scripts/test.sh
cd frontend && npx vitest run   # rule language (shared vectors), Jalali dates, form logic, highlights
./scripts/e2e.sh        # phase 8 admin UI: Playwright on phone and desktop (needs Chrome + Postgres)
./scripts/load-test.sh          # phase 9: k6 against a live API (needs k6 installed)
./scripts/backup-restore-drill.sh   # phase 9: dump/restore marker on Postgres
```

Hardening notes and the pen-test checklist live under [`docs/hardening/`](docs/hardening/).

The search tests against real engines are skipped unless `DMS_TEST_OPENSEARCH` and
`DMS_TEST_TIKA` point at running services (see above); the rest of the suite fakes the engine.

The test projects are xunit v3 / Microsoft.Testing.Platform executables. `scripts/test.sh` runs
each one directly, because `dotnet test` on this SDK reports "Zero tests ran" for MTP projects.
Integration tests create and drop their own database per run; point `DMS_TEST_POSTGRES` at another
server to use one.

## Everything in Docker

```bash
cp deploy/.env.example deploy/.env    # then fill in every value marked below
cd deploy && docker compose up -d --build
```

`.env` must have **`POSTGRES_PASSWORD`, `DMS_DB_APP_PASSWORD`, `DMS_ADMIN_PASSWORD` and
`DMS_JWT_SIGNING_KEY`** set; compose refuses to start otherwise. Generate the key with
`openssl rand -base64 64`. `DMS_AUDIT_SEAL_KEY` (`openssl rand -base64 32`) is optional but
recommended; keep it out of the database backups.

This builds the images and starts five containers, plus the optional processing services:

| Container | What | Where |
|---|---|---|
| `dms-postgres-1` | PostgreSQL | `localhost:5433` |
| `dms-migrator-1` | applies migrations and seeds, then exits | — |
| `dms-api-1` | API | `localhost:5080` |
| `dms-worker-1` | background jobs: scan, previews, OCR, indexing | — |
| `dms-web-1` | nginx: the web app, proxying `/api` to the API | **http://localhost:8090** |
| `opensearch`, `tika` | profile `search`: full-text search and OCR | — |
| `clamav` | profile `scan`: malware scanning | — |
| `gotenberg` | profile `office`: previews of Office files | — |

Enable the profiles with `COMPOSE_PROFILES=search,scan,office` in `.env` and set the matching
`DMS_OPENSEARCH_URL`, `DMS_TIKA_URL`, `DMS_GOTENBERG_URL` and `DMS_SCAN_ENABLED` (see
`.env.example`).

Open **http://localhost:8090** and sign in as `DMS_ADMIN_USERNAME` / `DMS_ADMIN_PASSWORD`. The
browser talks to one origin, so no CORS setup is needed; nginx streams uploads to the API without
buffering them. Files live on the `object-data` volume.

Two things specific to this machine:

- `.env` sets `POSTGRES_IMAGE` to a locally cached image; `postgres:18` works too wherever it can
  be pulled.
- The compose stack and `scripts/dev-db.sh` both publish port 5433, so run one or the other.
  `docker stop dms-postgres` frees it for compose; `docker compose down` frees it for the script.

## Layout

```
src/BuildingBlocks/    SharedKernel, Application (dispatcher), Infrastructure (unit of work, jobs), Web
src/Modules/           Identity, Authorization, Audit, Storage, DocumentTypes, Documents, Workflow, Search,
                       Sharing
                       (Domain+Application / Infrastructure / Contracts)
src/Dms.Host/          API composition root, also hosts the background worker
src/Dms.Migrator/      applies migrations and seeds, runs as a one-shot container
tests/                 architecture, unit and integration tests
frontend/              React + TypeScript + MUI shell (Persian, RTL)
deploy/                Dockerfile, compose, .env.example, the Tika image with Persian OCR
docs/                  architecture and decision records
```

## Conventions

- Migrations only run from `Dms.Migrator`, never at API startup.
- No secrets in source: connection strings, the JWT signing key and the bootstrap password all come
  from the environment.
- Authorization is decided in one place (`IDmsAuthorizer`); endpoints and handlers call it and never
  reimplement the rules.
- The audit log is append-only; the database rejects UPDATE and DELETE on it.
- Document versions are immutable; a trigger rejects any change except the approval state, and
  deletes outside the purge routine.
- Uploads stream to storage before any database transaction opens; the transaction that files the
  document is short.
- Module boundaries are enforced by tests in `tests/Dms.ArchitectureTests`.
