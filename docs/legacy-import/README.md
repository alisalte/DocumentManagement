# Legacy archive import (Phase 10.6 / D11)

Bulk import from a legacy DMS uses a **manifest**, server-side **files root**, dry-run
**validation**, then a resumable **import job** that writes through `IStorageService` and
existing document / record / ACL / classification / retention / legal-hold domain services.

## Manifest

See [manifest.example.json](manifest.example.json).

- `schemaVersion`: `1` or `2`
- `source`: logical source system name (idempotency namespace)
- `entries[]`:
  - `sourceId` (required for v2; derived from path+title when omitted)
  - `title`, optional `description`
  - `categoryPath` — slash-separated names under the archive root
  - `documentTypeCode` — existing type code (or mapped)
  - `file` / `path` — path **relative** to `Dms:Import:FilesRoot` (never absolute / `..`)
  - optional `sha256`, `size`, `contentType`, `tags`, `ownerUsername`, `createdAt`
  - optional `classification`, `record`, `recordClassCode`, `recordSeriesCode`,
    `retentionPolicyCode`, `legalHoldReason`, `metadata`, `acl`, `versions`

## When the old DMS keeps files in SQL (BLOB / `varbinary` / `bytea`)

The importer **does not** open the legacy database. Binary columns must be exported to
real files first, then referenced from the manifest.

```text
Legacy SQL (BLOB)  →  scripts/legacy-export-sql-blobs.py  →  FilesRoot + manifest.json
                                                              ↓
                                                    /admin/imports  (validate → start)
```

1. Export rows (see [sql-blobs.example.sql](sql-blobs.example.sql) for SQL Server / Postgres).
2. Build the staging tree:

```bash
python3 scripts/legacy-export-sql-blobs.py \
  --rows ./legacy-rows.jsonl \
  --out-dir /var/lib/dms/import-staging/legacy \
  --source old-sql-dms
```

Or pass `--sql` + `--dsn` when `pyodbc` / `psycopg` can reach the old server.

3. Dry-run, then import in the UI with that `manifest.json` and relative root `legacy`
   (if `FilesRoot` is `/var/lib/dms/import-staging`).

## Configuration

```json
"Dms": {
  "Import": {
    "FilesRoot": "/var/lib/dms/import-staging"
  }
}
```

Clients may only request a **relative subfolder** under that root. Arbitrary filesystem
targets are rejected.

## API

| Method | Path | Permission |
|--------|------|------------|
| `POST` | `/api/v1/imports` | `IMPORT_MANAGE` |
| `POST` | `/api/v1/imports/{id}/validate` | `IMPORT_MANAGE` or `IMPORT_RUN` |
| `POST` | `/api/v1/imports/{id}/start` | `IMPORT_RUN` |
| `POST` | `/api/v1/imports/{id}/pause` | `IMPORT_RUN` |
| `POST` | `/api/v1/imports/{id}/resume` | `IMPORT_RUN` |
| `POST` | `/api/v1/imports/{id}/retry-failed` | `IMPORT_RUN` |
| `GET` | `/api/v1/imports` | `IMPORT_VIEW` |
| `GET` | `/api/v1/imports/{id}` | `IMPORT_VIEW` |
| `GET` | `/api/v1/imports/{id}/items` | `IMPORT_VIEW` |
| `GET` | `/api/v1/imports/{id}/report` | `IMPORT_VIEW` (audited) |

## Lifecycle

```text
Created → Validating → Ready → Running → Completed
                      ↘ ValidationFailed
                 Ready → Running → Paused → Running
                 Running → CompletedWithErrors | Failed
                 Failed | CompletedWithErrors → retry / resume
```

Default failure policy is `ContinueOnError` (per-item). `StopOnError` finishes the job on
the first failed item.

## Idempotency

Successful imports are keyed by `(sourceSystem, sourceId)` in `documents.import_source_index`.
Re-running the same source skips with `IMPORT_IDEMPOTENT_SKIP` and does not create a second
document.

## Local dry-run script

```bash
./scripts/legacy-import.sh --manifest docs/legacy-import/manifest.example.json --files-root /path/to/files
```

Exit 0 means the manifest parses and every `file` exists. Full validation/import uses the API
and admin UI (`/admin/imports`).

## Mappings

Pass `mappings` on create (`kind`, `sourceKey`, `targetKey`) for User, Group, Folder,
DocumentType, Classification, RecordClass, RecordSeries, RetentionPolicy, Permission,
MetadataField. Unknown classifications / principals / document types fail validation — they
are never silently downgraded.
