#!/usr/bin/env python3
"""Export legacy DMS rows (metadata + SQL binary content) into FilesRoot + manifest.json.

The DMS importer never reads SQL BLOBs directly. It expects files on disk under
Dms:Import:FilesRoot and a JSON manifest. This script turns exported rows into that layout.

Input formats
-------------
1) JSONL (one object per line) or a JSON array. Each row needs at least:

   {
     "sourceId": "LEG-1",
     "title": "قرارداد",
     "categoryPath": "قراردادها/۱۴۰۴",
     "documentTypeCode": "GENERAL",
     "fileName": "contract.pdf",
     "contentBase64": "<base64 of the BLOB>"   // or contentHex
   }

2) Optional SQL mode (--sql + --dsn) when pyodbc / psycopg are installed. The query must
   return columns named like the JSON fields above; the binary column may be named
   content / FileData / file_bytes.

Examples
--------
  # From a JSONL dump produced by your DBA / ETL:
  python3 scripts/legacy-export-sql-blobs.py \\
    --rows ./legacy-rows.jsonl \\
    --out-dir /var/lib/dms/import-staging/legacy \\
    --source old-sql-dms

  # Then dry-run:
  ./scripts/legacy-import.sh \\
    --manifest /var/lib/dms/import-staging/legacy/manifest.json \\
    --files-root /var/lib/dms/import-staging/legacy
"""

from __future__ import annotations

import argparse
import base64
import binascii
import hashlib
import json
import re
import sys
from pathlib import Path


SAFE_NAME = re.compile(r"[^A-Za-z0-9._\u0600-\u06FF\-]+")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--rows", type=Path, help="JSONL or JSON array of legacy rows")
    parser.add_argument("--sql", help="SQL query returning legacy columns (optional)")
    parser.add_argument("--dsn", help="ODBC/Postgres DSN or connection URI for --sql")
    parser.add_argument("--driver", choices=("auto", "pyodbc", "psycopg"), default="auto")
    parser.add_argument("--out-dir", type=Path, required=True, help="Staging folder (= FilesRoot subfolder)")
    parser.add_argument("--source", default="legacy-sql", help="Manifest source system name")
    parser.add_argument("--relative-prefix", default="files", help="Subfolder under out-dir for binaries")
    return parser.parse_args()


def load_rows_from_file(path: Path) -> list[dict]:
    text = path.read_text(encoding="utf-8").strip()
    if not text:
        return []
    if text[0] == "[":
        data = json.loads(text)
        if not isinstance(data, list):
            raise SystemExit("--rows JSON must be an array of objects")
        return data
    rows = []
    for line_no, line in enumerate(text.splitlines(), start=1):
        line = line.strip()
        if not line:
            continue
        try:
            rows.append(json.loads(line))
        except json.JSONDecodeError as exc:
            raise SystemExit(f"{path}:{line_no}: {exc}") from exc
    return rows


def load_rows_from_sql(sql: str, dsn: str, driver: str) -> list[dict]:
    conn = connect(dsn, driver)
    try:
        cur = conn.cursor()
        cur.execute(sql)
        columns = [col[0] for col in cur.description]
        # Normalise names for lookup.
        lower = {name.lower(): name for name in columns}
        rows = []
        for raw in cur.fetchall():
            mapped = {columns[i]: raw[i] for i in range(len(columns))}
            rows.append(normalise_sql_row(mapped, lower))
        return rows
    finally:
        conn.close()


def connect(dsn: str, driver: str):
    if driver in ("auto", "psycopg") and (dsn.startswith("postgres") or dsn.startswith("postgresql")):
        try:
            import psycopg  # type: ignore

            return psycopg.connect(dsn)
        except ImportError as exc:
            if driver == "psycopg":
                raise SystemExit("Install psycopg: pip install psycopg[binary]") from exc
    if driver in ("auto", "pyodbc"):
        try:
            import pyodbc  # type: ignore

            return pyodbc.connect(dsn)
        except ImportError as exc:
            raise SystemExit("Install a DB driver: pip install pyodbc  or  pip install 'psycopg[binary]'") from exc
    raise SystemExit(f"Unsupported driver/dsn combination: driver={driver} dsn={dsn!r}")


def first(mapped: dict, lower: dict[str, str], *names: str):
    for name in names:
        key = lower.get(name.lower())
        if key is not None and mapped.get(key) is not None:
            return mapped[key]
    return None


def normalise_sql_row(mapped: dict, lower: dict[str, str]) -> dict:
    content = first(mapped, lower, "content", "filedata", "file_data", "file_bytes", "blob", "document", "binary")
    row = {
        "sourceId": str(first(mapped, lower, "sourceId", "source_id", "id", "docid", "document_id") or ""),
        "title": str(first(mapped, lower, "title", "name", "subject") or ""),
        "categoryPath": str(first(mapped, lower, "categoryPath", "category_path", "folder", "path") or "واردات"),
        "documentTypeCode": str(first(mapped, lower, "documentTypeCode", "document_type", "type_code") or "GENERAL"),
        "fileName": str(first(mapped, lower, "fileName", "file_name", "filename", "original_name") or "file.bin"),
        "description": first(mapped, lower, "description", "desc"),
        "contentType": first(mapped, lower, "contentType", "mime", "content_type"),
        "ownerUsername": first(mapped, lower, "ownerUsername", "owner", "username"),
        "createdAt": first(mapped, lower, "createdAt", "created_at", "create_date"),
    }
    if content is None:
        raise SystemExit(f"Row {row['sourceId']!r} has no binary column (content/FileData/...)")
    if isinstance(content, memoryview):
        content = content.tobytes()
    if isinstance(content, bytearray):
        content = bytes(content)
    if isinstance(content, str):
        # Some drivers return hex strings for binary.
        row["contentHex"] = content
    else:
        row["contentBytes"] = content
    return row


def decode_content(row: dict) -> bytes:
    if "contentBytes" in row and isinstance(row["contentBytes"], (bytes, bytearray)):
        return bytes(row["contentBytes"])
    if row.get("contentBase64"):
        return base64.b64decode(row["contentBase64"], validate=False)
    if row.get("contentHex"):
        hex_text = str(row["contentHex"]).removeprefix("0x")
        return binascii.unhexlify(hex_text)
    raise SystemExit(f"Row {row.get('sourceId')!r}: need contentBase64, contentHex, or contentBytes")


def safe_file_name(name: str, source_id: str) -> str:
    cleaned = SAFE_NAME.sub("_", name.strip()) or "file.bin"
    if "." not in cleaned:
        cleaned = f"{cleaned}.bin"
    # Keep collisions rare when two docs share a file name.
    stem, dot, ext = cleaned.rpartition(".")
    suffix = hashlib.sha1(source_id.encode("utf-8")).hexdigest()[:8]
    return f"{stem or 'file'}_{suffix}.{ext}" if dot else f"{cleaned}_{suffix}"


def export(rows: list[dict], out_dir: Path, source: str, relative_prefix: str) -> Path:
    files_dir = out_dir / relative_prefix
    files_dir.mkdir(parents=True, exist_ok=True)
    entries = []
    for index, row in enumerate(rows):
        source_id = str(row.get("sourceId") or f"row-{index + 1}")
        title = str(row.get("title") or source_id)
        category = str(row.get("categoryPath") or "واردات")
        type_code = str(row.get("documentTypeCode") or "GENERAL")
        file_name = str(row.get("fileName") or f"{source_id}.bin")
        content = decode_content(row)
        relative = f"{relative_prefix}/{safe_file_name(file_name, source_id)}".replace("\\", "/")
        target = out_dir / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(content)
        entry = {
            "sourceId": source_id,
            "title": title,
            "categoryPath": category,
            "documentTypeCode": type_code,
            "file": relative,
            "fileName": file_name,
            "size": len(content),
            "sha256": hashlib.sha256(content).hexdigest(),
        }
        for optional in ("description", "contentType", "ownerUsername", "createdAt", "classification"):
            if row.get(optional):
                entry[optional] = row[optional]
        if row.get("tags"):
            entry["tags"] = row["tags"]
        entries.append(entry)

    manifest = {"schemaVersion": 2, "source": source, "entries": entries}
    manifest_path = out_dir / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return manifest_path


def main() -> int:
    args = parse_args()
    if not args.rows and not args.sql:
        raise SystemExit("Provide --rows FILE or --sql QUERY (with --dsn)")
    if args.sql and not args.dsn:
        raise SystemExit("--sql requires --dsn")

    rows = load_rows_from_file(args.rows) if args.rows else load_rows_from_sql(args.sql, args.dsn, args.driver)
    if not rows:
        raise SystemExit("No rows to export")

    args.out_dir.mkdir(parents=True, exist_ok=True)
    manifest_path = export(rows, args.out_dir, args.source, args.relative_prefix)
    print(f"Wrote {len(rows)} files under {args.out_dir / args.relative_prefix}")
    print(f"Manifest: {manifest_path}")
    print("Next:")
    print(f"  ./scripts/legacy-import.sh --manifest {manifest_path} --files-root {args.out_dir}")
    print("  then create an import job in /admin/imports with that manifest and relative root.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
