# ADR 0001: File versioning and metadata revisioning are separate concerns

| | |
|---|---|
| **Status** | Accepted (phase 1). No tables are created yet; the Documents tables land in phase 2. |
| **Date** | 2026-09-20 |
| **Context** | Requested review: "Do not assume that every metadata-only edit must necessarily create a new physical file version. Design the model so this distinction can be supported without a future database redesign." |

## The problem

Three requirements pull in different directions.

1. **A version is an immutable file.** Old versions must never be overwritten, and each one keeps its own filename, MIME type, size, SHA-256, storage object, creator and timestamp.
2. **Metadata is versioned too.** The search requirement says V1 has `amount = 8 billion` and V2 has `amount = 12 billion`, so a version has to carry the metadata that was true when it was created.
3. **Approval belongs to a specific version.** A workflow condition such as `amount > 10,000,000,000` routes a contract to senior management.

Requirement 3 is what makes this more than a modelling preference. If metadata were a single mutable row on the document:

- V3 is approved while `amount = 8,000,000,000`;
- someone edits `amount` to `12,000,000,000` without creating a new version;
- the document now claims an approval it never received at that value, and the senior-approval step was bypassed without anyone doing anything wrong.

The opposite extreme is just as bad in practice: forcing a full **file** version for every typo fix in a description would duplicate files (a 400 MB CAD drawing re-stored to correct a spelling), pollute version history, and train users to avoid fixing metadata.

So the two axes are genuinely independent:

| | File unchanged | File changed |
|---|---|---|
| **Metadata unchanged** | nothing to record | new content version |
| **Metadata changed** | **new metadata revision, same file** | new content version |

## Decision

**Two numbers, one immutable row.** `document_versions` is keyed by `(document_id, version_number, revision_number)`:

- `version_number` increments when the **file** changes. A new content version starts at revision 1.
- `revision_number` increments when only the **metadata** changes. The row reuses the existing `storage_object_id`, so **no file is copied or re-uploaded**, and the SHA-256 is identical by construction.
- Every row is immutable after creation, enforced by a database trigger on the file columns and the metadata snapshot.

Displayed as `V3`, `V3.2`. `UNIQUE (document_id, version_number, revision_number)` replaces the simpler unique constraint, and version-number allocation stays concurrency-safe in the same way.

```
Document "Contract with ABC"
├── V1.1  file A   amount  8,000,000,000   APPROVED     (superseded)
├── V2.1  file B   amount  8,000,000,000   APPROVED     (superseded)
├── V3.1  file C   amount  8,000,000,000   APPROVED  <- effective_version_id
├── V3.2  file C   amount 12,000,000,000   IN_WORKFLOW  (metadata only: same file, same SHA)
└── V4.1  file D   amount 12,000,000,000   DRAFT     <- current_version_id
```

### Not every metadata edit creates a revision

Whether an edit creates a revision is **configuration, not a hard-coded rule**:

- `DocumentType.settings.metadata_edit_policy`:
  - `NEW_REVISION` (default for governed types such as Contract): any metadata change creates a new revision.
  - `IN_PLACE` (for ungoverned types such as a scanned reference manual): the change updates the latest revision in place. It is still fully audited, with a before/after field diff in the audit metadata.
- `FieldDefinition.is_approval_relevant` (phase 3): under `IN_PLACE`, changing a field marked approval-relevant still forces a new revision. This is how an amount stays governed while a tag or a description does not.

### Consequences for the rest of the model

- **Approval** attaches to a row, so it covers exactly one (file, metadata) pair. A new revision of an approved version starts as `DRAFT`; the previously approved row stays `APPROVED` and stays effective until the new one is approved. This is exactly decision D7: no historical approval is ever rewritten or cancelled.
- **`documents.current_version_id`** points at the latest row and **`documents.effective_version_id`** at the latest approved row. Both are plain foreign keys to `document_versions`, unchanged by this decision.
- **Shares and share links** pin a row, so a recipient always sees the exact bytes *and* the exact metadata the sharer saw.
- **Search** indexes one document per row, which is what makes "find the version where amount was 12 billion" work.
- **Storage** never duplicates a file for a metadata edit. One storage object is referenced by every revision of that content version, and purging counts references before deleting bytes.
- **Audit** distinguishes `VERSION_CREATED` from `REVISION_CREATED` and records the changed fields.

## Alternatives considered

| Option | Why not |
|---|---|
| Metadata on the document only (as the original spec sketched) | Breaks the search requirement and allows the approval bypass described above. |
| Full file version for every metadata edit | Duplicates large binaries, makes version history unreadable, and discourages metadata hygiene. |
| Separate `document_content_versions` and `document_revisions` tables | Cleanest on paper, but every consumer (workflow, shares, search, audit, permissions) would need a two-part identifier. The composite numbering gives the same expressiveness with one identity. If a future requirement needs content versions to exist independently of metadata, the split is a data migration, not a redesign: the pair is already stored. |
| Event-sourced metadata | Far more machinery than the requirements justify, and hard to query. |

## Effect on phase 1

None of these tables exist yet, and nothing built in phase 1 constrains them:

- `ResourceRef` addresses a **document**, not a version, so permissions are unaffected.
- `ResourceDescriptor` already carries `ContentState` (published or draft) and `ContentScanState`; in phase 2 those are computed for the specific row being accessed.
- `audit.audit_logs` already has both `document_id` and `version_id`; `version_id` will hold the row id of the version-revision, and the revision numbers go into the JSONB metadata.
