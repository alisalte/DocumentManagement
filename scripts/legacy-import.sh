#!/usr/bin/env bash
# Phase 10 (D11): dry-run validation of a legacy-import manifest. See docs/legacy-import/.
set -euo pipefail

cd "$(dirname "$0")/.."

MANIFEST=""
FILES_ROOT=""

usage() {
    echo "Usage: $0 --manifest <path> --files-root <dir>" >&2
    exit 2
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --manifest) MANIFEST=${2:-}; shift 2 ;;
        --files-root) FILES_ROOT=${2:-}; shift 2 ;;
        -h|--help) usage ;;
        *) echo "Unknown option: $1" >&2; usage ;;
    esac
done

[[ -n "$MANIFEST" && -n "$FILES_ROOT" ]] || usage
[[ -f "$MANIFEST" ]] || { echo "Manifest not found: $MANIFEST" >&2; exit 1; }
[[ -d "$FILES_ROOT" ]] || { echo "Files root not found: $FILES_ROOT" >&2; exit 1; }

if ! command -v python3 >/dev/null 2>&1; then
    echo "python3 is required for manifest validation." >&2
    exit 1
fi

python3 - "$MANIFEST" "$FILES_ROOT" <<'PY'
import json, sys
from pathlib import Path

manifest_path, files_root = Path(sys.argv[1]), Path(sys.argv[2])
data = json.loads(manifest_path.read_text(encoding="utf-8"))
if data.get("schemaVersion") != 1:
    sys.exit(f"Unsupported schemaVersion: {data.get('schemaVersion')!r}")
entries = data.get("entries")
if not isinstance(entries, list) or not entries:
    sys.exit("manifest.entries must be a non-empty list")

errors = []
for i, entry in enumerate(entries):
    for key in ("title", "categoryPath", "documentTypeCode", "file"):
        if not entry.get(key):
            errors.append(f"entries[{i}].{key} is required")
    rel = entry.get("file") or ""
    path = files_root / rel
    if rel and not path.is_file():
        errors.append(f"entries[{i}].file missing on disk: {path}")

if errors:
    print("\n".join(errors), file=sys.stderr)
    sys.exit(1)

print(f"Dry-run OK: {len(entries)} entries, source={data.get('source')!r}")
print("No API calls were made. Wire upload in the next phase-10 increment.")
PY
