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

Once declared, a Record never becomes mutable through document APIs — including after destruction
(`record.immutable` on edit, tags, metadata, new version, soft-delete, restore, purge).

Destruction itself is only allowed through an approved disposition (phase 10.4); see
[disposition.md](disposition.md).

## Audit

`RECORD_DECLARED`, `RECORD_STATUS_CHANGED`, `RECORD_CLASS_*`, `RECORD_SERIES_*`.

## Related

Retention (10.2), Legal Hold (10.3), Disposition / Certificate of Destruction (10.4).
