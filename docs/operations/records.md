# Record management (phase 10.1)

Documents and Records are **distinct**. A Document is the living editable object. Declaring a
**Record** pins a final version, freezes metadata, and refuses further mutation through normal
document APIs.

## Model

| Entity | Schema | Role |
|---|---|---|
| `record_classes` | `documents` | Filing-plan class (code, name) |
| `record_series` | `documents` | Series under a class |
| `records` | `documents` | Declared Record: `document_id` (unique), `final_version_id`, status, freeze timestamps |

Statuses: `Active` → `UnderRetention` → `Expired` → `PendingDisposal` → `Destroyed`
(also `Active`/`UnderRetention` → `Expired`). Invalid jumps are refused.

## Permissions

| Code | Scope | Meaning |
|---|---|---|
| `RECORD_DECLARE` | Resource | Declare a document as a Record (requires VIEW) |
| `ADMIN_MANAGE_RECORDS` | System | Manage classes/series; may transition lifecycle |

## API

- `POST /api/v1/admin/records/classes` / `PUT …/classes/{id}`
- `POST /api/v1/admin/records/series` / `PUT …/series/{id}`
- `GET /api/v1/records/classes` · `GET /api/v1/records/series`
- `POST /api/v1/records/declare/{documentId}` — pins effective (or current) version unless `finalVersionId` set
- `GET /api/v1/records/{id}` · `GET /api/v1/records/by-document/{documentId}`
- `POST /api/v1/records/{id}/transition` — audited status change
- Document details include a `record` summary when declared

## Immutability

While a Record exists and is not `Destroyed`, these document commands fail with `record.immutable`:

edit title/category, tags, metadata, new version, soft-delete, restore, purge.

## Audit

`RECORD_DECLARED`, `RECORD_STATUS_CHANGED`, `RECORD_CLASS_*`, `RECORD_SERIES_*`.

## Next

Retention policies and workers (10.2), legal hold (10.3), disposition (10.4) build on this model.
