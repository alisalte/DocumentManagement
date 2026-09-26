# Legacy archive import (D11)

Architecture decision **D11** deferred bulk import from older file shares. Phase 10 ships the
**manifest contract** and a dry-run script so a real importer can land without redesigning storage.

## Manifest

See [manifest.example.json](manifest.example.json). Each entry becomes one document:

- `title`, optional `description`
- `categoryPath` — slash-separated names under the archive root (created if missing when the
  importer runs with `--create-categories`, otherwise must already exist)
- `documentTypeCode` — existing type code
- `file` — path relative to `--files-root`
- optional `tags`, `ownerUsername`, `createdAt` (ISO-8601; defaults to import time)

## Dry-run

```bash
./scripts/legacy-import.sh --manifest docs/legacy-import/manifest.example.json --files-root /path/to/files
```

Exit 0 means the manifest parses and every `file` exists. No API calls yet.

## Next increment

A small console tool (or extension of this script) that:

1. Authenticates as an import service account with `DOCUMENT_CREATE` on target categories.
2. Stages each file through the normal upload API (so ClamAV / hashing / audit stay on the path).
3. Writes an import batch id into audit metadata for replay and support.
4. Never bypasses ACL or malware quarantine.
