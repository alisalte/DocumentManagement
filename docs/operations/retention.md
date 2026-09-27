# Retention and legal hold (D12 / phases 10.2–10.3)

## Soft-delete purge wait (opening seam)

On `DocumentTypeSettings` (JSONB):

| Field | Meaning |
|---|---|
| `retentionDaysAfterDelete` | After soft-delete, purge is refused until this many days have passed. |
| `supportsLegalHold` | Type may receive Legal Holds. |

## Record retention policies (10.2)

| Entity | Role |
|---|---|
| `documents.retention_policies` | Versioned policy (`code`, days, start event, `version_number`) |
| Record columns | Snapshot of policy version, start/expiry timestamps, optional exception reason |

Start events: `Declaration` (default) or `DocumentCreated`.

Lifecycle for Records with a policy:

`Active` → `UnderRetention` (on assign) → `Expired` → `PendingDisposal`

Worker job `documents.retention-advance` (hourly) advances clocks. It **never destroys** Records.
Retention exceptions pause the clock (`RETENTION_EXCEPTION_SET` / `CLEARED`).

API:

- `POST/PUT /api/v1/admin/records/retention-policies`
- `POST /api/v1/records/{id}/retention`
- `POST/DELETE /api/v1/records/{id}/retention-exception`

## Legal Hold (10.3)

| Entity | Role |
|---|---|
| `documents.legal_holds` | Stackable holds on a Document (reason, created, release history) |

**Critical rule:** a Document/Record under an active Legal Hold **cannot** be purged or destroyed.
Enforced on:

- `POST /documents/{id}/purge`
- Disposition request / approve / destroy (phase 10.4; re-checked immediately before destroy)
- Record transition to `Destroyed` is refused outright (`disposition.required`)
- (Record soft-delete/mutation already blocked by Record immutability)

API:

- `POST /api/v1/legal-holds`
- `POST /api/v1/legal-holds/{id}/release` (requires `ADMIN_MANAGE_LEGAL_HOLD`)
- `GET /api/v1/legal-holds/by-document/{documentId}`

Permissions: `ADMIN_MANAGE_LEGAL_HOLD` (system). Placement also allowed with `DOCUMENT_MANAGE_PERMISSION` on the document.

Audit: `LEGAL_HOLD_PLACED`, `LEGAL_HOLD_RELEASED`, `RETENTION_*`.

## Disposition (10.4)

See [disposition.md](disposition.md) for review → approve/reject → destroy → Certificate of
Destruction. The retention worker still never destroys Records.

## Still to build (10.5+)

- Classification levels
- Full import engine, SMTP, etc.
