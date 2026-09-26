# Retention and legal hold (D12)

Architecture decision **D12** deferred full retention. Phase 10 attaches the first seam where
the plan said it would land: **document type settings** and the **purge path**.

## Settings (shipped)

On `DocumentTypeSettings` (JSONB, no schema migration):

| Field | Meaning |
|---|---|
| `retentionDaysAfterDelete` | After soft-delete, purge is refused until this many days have passed. `null` or `≤ 0` = no wait. |
| `supportsLegalHold` | Type may participate in legal hold. Document-level hold (column + API) is the next increment. |

Admins edit both on the document-type settings screen.

## Purge behaviour

`PurgeDocumentHandler` loads the type via `IDocumentTypeCatalog`. If
`retentionDaysAfterDelete` is set and `now < deletedAt + days`, the command fails with
`purge.retention` and the files stay.

## Still to build

- `documents.documents.legal_hold` (or equivalent) + grant/release commands + audit actions
- Refuse purge (and optionally soft-delete) while hold is active, regardless of retention days
- Scheduled job that proposes purge candidates past retention (never auto-purges without an actor)
- Legal-hold reports for counsel
