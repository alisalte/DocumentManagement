# Phase 10 — Production cutover & deferred seams

Phases 1–8 shipped the product; phase 9 hardens it. Phase 10 is the cutover track:
operational go-live, plus the deferred platform seams named in architecture decisions
**D11** (legacy import) and **D12** (retention / legal hold).

| Deliverable | Location |
|---|---|
| Go-live runbook | [go-live.md](go-live.md) |
| Release checklist | [release-checklist.md](release-checklist.md) |
| Retention & legal-hold seam (D12) | [retention.md](retention.md) · `DocumentTypeSettings.RetentionDaysAfterDelete` |
| Legacy import skeleton (D11) | [../legacy-import/](../legacy-import/) · `scripts/legacy-import.sh` |
| Build / role identity | `GET /version` on the API host |
| Email channel seam | `INotificationChannel` (null/no-op until SMTP is configured) |

## Status

**In progress.** Settings-level retention and purge wait, ops docs, `/version`, the email
channel port, and the import manifest/tooling land with this phase opening. Document-level
legal-hold columns, SMTP delivery, and a full importer against a real archive close the phase.
