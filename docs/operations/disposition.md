# Disposition and Certificate of Destruction (phase 10.4)

Completes the Record lifecycle after retention expiry and Legal Hold checks.

## Lifecycle

```text
PendingDisposal
  → Disposition PendingReview   (request)
  → Approved | Rejected
  → Destroyed + Certificate      (only from Approved)
```

A Record **never** moves `PendingDisposal → Destroyed` without an approved disposition.
Direct `POST /records/{id}/transition` with `Destroyed` fails with `disposition.required`.

On rejection, the Record returns to `Expired` (retention worker may promote it again later).

## Eligibility to request disposition

1. Record status is `PendingDisposal`
2. No active Legal Hold on the Document (`purge.legal_hold`)
3. No active disposition (`PendingReview` or `Approved`) already exists (re-request is idempotent)

## Legal Hold

Checked at **request**, **approve**, and again immediately before **destroy**.
A hold placed after approval blocks destruction; content is preserved and the failure is audited
(`DISPOSITION_BLOCKED_BY_LEGAL_HOLD`, `DESTRUCTION_BLOCKED`).

## Destruction safety

- Requires disposition status `Approved`
- Re-checks Legal Hold
- Marks stored objects for deletion via `IStorageService.MarkForDeletionAsync` (same path as purge)
- Soft-deletes the Document, sets Record `Destroyed`, writes immutable Certificate
- Idempotent: retries return the existing certificate id
- Concurrent active dispositions blocked by partial unique index `ux_dispositions_active_record`
- Disposition uses PostgreSQL `xmin` row version

Destroyed Records remain immutable (`record.immutable`): restore/purge/edit stay refused.
Certificates are never removed by document purge.

## Permissions (system)

| Code | Meaning |
|---|---|
| `DISPOSITION_REQUEST` | Request disposition review |
| `DISPOSITION_APPROVE` | Approve or reject |
| `DISPOSITION_DESTROY` | Execute destruction |
| `DISPOSITION_VIEW_CERTIFICATE` | View Certificate of Destruction |

`ADMINISTRATOR` receives all system permissions. `AUDITOR` receives `DISPOSITION_VIEW_CERTIFICATE`.
Document VIEW/EDIT/DOWNLOAD do **not** grant destructive archive rights.

## API

| Method | Path | Notes |
|---|---|---|
| GET | `/api/v1/disposition/pending` | PendingDisposal (+ Destroyed for certificate lookup) |
| POST | `/api/v1/records/{id}/disposition` | Request review |
| GET | `/api/v1/disposition/{id}` | Disposition state |
| GET | `/api/v1/disposition/by-record/{recordId}` | Latest disposition |
| POST | `/api/v1/disposition/{id}/approve` | Approve |
| POST | `/api/v1/disposition/{id}/reject` | Reject (reason required) |
| POST | `/api/v1/disposition/{id}/destroy` | Destroy → `{ certificateId }` |
| GET | `/api/v1/destruction-certificates/{id}` | Certificate JSON |
| GET | `/api/v1/destruction-certificates/by-record/{recordId}` | By Record |

## Certificate of Destruction

Immutable evidence in `documents.destruction_certificates`:

- Certificate number and SHA-256 over certificate fields
- Record/document/final version identity and content SHA-256
- Retention policy snapshot and expiry
- Legal Hold check timestamp
- Approver / destroyer / timestamps / reason
- One certificate per disposition and per Record

## Audit events

`DISPOSITION_REQUESTED`, `DISPOSITION_APPROVED`, `DISPOSITION_REJECTED`,
`DISPOSITION_BLOCKED_BY_LEGAL_HOLD`, `DESTRUCTION_ATTEMPTED`, `DESTRUCTION_BLOCKED`,
`RECORD_DESTROYED`, `CERTIFICATE_CREATED`.

## Workers

`documents.retention-advance` still only advances `UnderRetention → Expired → PendingDisposal`.
It never destroys Records. Destruction is a human-authorized API operation.

## UI

Admin screen `/admin/disposition` for authorized users: list candidates, request/approve/reject,
confirm destruction (typed confirmation), view/export certificate JSON, disposition audit trail.
