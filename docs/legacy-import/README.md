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
   The customer archive stores each attachment as `ID`, `IDTypeFile`, `IDSanad`,
   `FileSize`, `Files` (the `IMAGE` / `VARBINARY` blob), and nullable `Sharh`.
   A JSON or SQL row with those columns is recognised as-is: `sourceId` is
   `sanad-{IDSanad}-file-{ID}` (one manifest entry per file, so a سند with
   several files stays lossless and re-runs skip the same file), the title is
   `Sharh` or `سند {IDSanad}` when `Sharh` is null, and `نوع فایل {IDTypeFile}`
   is kept on the description. The blob header selects the extension
   (PNG, PDF, JPEG, GIF, ZIP, OLE `.doc`, BMP, TIFF, WEBP; otherwise `.bin`).
   `FileSize` must match the decoded length. The table name is not fixed —
   point the example `SELECT` at the real table. Rows are filed as document
   type `GENERAL` under the folder `واردات`.
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

## The whole old archive (ArchiveSaman)

The old program keeps every file inside SQL Server. There are two ways to convert **all** of
them at once; both give the same folders, titles and manifest.

### Straight from SQL Server (recommended for a large database)

Run on a machine that can reach the old server (often the server itself):

```bash
pip install pyodbc
python3 scripts/legacy-export-sql-blobs.py --server "HOST\SQLEXPRESS" --database ArchiveSaman --out-dir D:\dms-staging\archive-saman --source archive-saman
```

- Needs Microsoft **ODBC Driver 18 (or 17) for SQL Server**; the newest one installed is
  picked, or name one with `--odbc-driver`. Windows' built-in `SQL Server` driver also works.
- Windows login by default. For a SQL login use `--sql-user sa`: the password is asked for, or
  read from `LEGACY_SQL_PASSWORD` (never put it on the command line). `--dsn "<ODBC string>"`
  replaces all of these. `TrustServerCertificate=yes` is set because old in-house servers
  rarely have a trusted certificate.
- Only reads (`SELECT`); nothing in the old database is changed.
- Files are fetched **one at a time** by primary key, so memory stays at one file regardless of
  database size, and progress (files and MB) is printed every 100 files.
- **Resumable:** if the run stops (network, power, Ctrl+C), run the same command again. Files
  already on disk with the right length are not downloaded again. Re-running after a finished
  export only rebuilds `manifest.json`.
- Disk: the staging folder needs about as much free space as the files in the database.
- A table that is missing in the old database is reported as a warning and skipped.

### From one SQL script (small databases)

1. In SSMS: right-click the database → **Tasks → Generate Scripts** → *Script entire database
   and all database objects* → **Advanced** → **Types of data to script = Schema and data** →
   save to file. (A script with *Schema only* has no INSERTs and therefore no files; the
   exporter says so and stops.)
2. Convert it — no database driver needed, UTF-16 or UTF-8:

```bash
python3 scripts/legacy-export-sql-blobs.py \
  --sql-script ./ArchiveSaman.sql \
  --out-dir /var/lib/dms/import-staging/archive-saman \
  --source archive-saman
```

3. In `/admin/imports`: manifest `archive-saman/manifest.json`, relative root `archive-saman`,
   **create missing folders** on → validate → start.

| Old table | Becomes |
|-----------|---------|
| `tbl_Parvandeh` (پرونده) | folder `واردات/پرونده {id} (شماره …) - {موضوع}`; سندهای بدون پرونده → `واردات/بدون پرونده` |
| `tbl_FileSanad` → `tbl_ParvandehSanadPeyvaste3` (سند) | one document per file, `sanad-{IDSanad}-file-{ID}`, title = file Sharh or سند Mozoe |
| `tbl_FilePeyvast` → `tbl_PeyvasteSanad` (پیوست) | one document per file, `peyvast-{IDPeyvast}-file-{ID}`, in the سند's folder |
| `tbl_FilesSampleAsnad` → `tbl_SampleAsnad` | `واردات/نمونه اسناد`, `sample-{IDSampleAsnad}-file-{ID}` |
| `tbl_AnvaeFile` | file-type title on the description; its extension is used when the blob header is unknown |
| `tbl_ErsalKonandegan` | sender / receiver on the description |

Jalali number dates (`14020512`) are shown as `1402/05/12` on the description, and `DateSanad`
(or `TarikhSabt`, `DatePeyvast`) becomes `createdAt`. A سند with several files gets
`(فایل n از m)` on each title. Rows with an empty file column are skipped and counted; a
`FileSize` that does not match the real length is only a warning. The `sanad-…` ids are the
same as the single-table export, so files already imported are skipped on a re-run. Blobs are
streamed to disk while reading, so the script size is limited by disk, not memory — but SSMS
itself struggles to write multi-GB data scripts, which is why the direct mode above exists.

Tests: `python3 -m unittest discover -s scripts/tests`.

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
