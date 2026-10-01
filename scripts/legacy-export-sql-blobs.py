#!/usr/bin/env python3
"""Export legacy DMS rows (metadata + SQL binary content) into FilesRoot + manifest.json.

The DMS importer never reads SQL BLOBs directly. It expects files on disk under
Dms:Import:FilesRoot and a JSON manifest. This script turns exported rows into that layout.

Input formats
-------------
1) JSONL (one object per line) or a JSON array.

   Generic row:

   {
     "sourceId": "LEG-1",
     "title": "قرارداد",
     "categoryPath": "قراردادها/۱۴۰۴",
     "documentTypeCode": "GENERAL",
     "fileName": "contract.pdf",
     "contentBase64": "<base64 of the BLOB>"   // or contentHex
   }

   Customer file table (one row per attachment). Column names are matched
   case-insensitively. Files may be raw bytes or a 0x hex string:

   {
     "ID": 7,
     "IDTypeFile": 7,
     "IDSanad": 7,
     "FileSize": 303184,
     "Files": "0x89504E470D0A1A0A...",
     "Sharh": null
   }

   That row becomes sourceId "sanad-7-file-7", title "سند 7" when Sharh is
   empty, description "نوع فایل 7", type GENERAL, folder واردات. The file
   extension and content type are taken from the blob header (PNG, PDF, JPEG,
   …). FileSize must match the decoded byte length.

2) Optional SQL mode (--sql + --dsn) when pyodbc / psycopg are installed. The
   query may return the generic names above, or the customer columns
   ID, IDTypeFile, IDSanad, FileSize, Files, Sharh. The binary column may also
   be named content / FileData / file_bytes.

Examples
--------
  python3 scripts/legacy-export-sql-blobs.py \\
    --rows ./legacy-rows.jsonl \\
    --out-dir /var/lib/dms/import-staging/legacy \\
    --source old-sql-dms

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
HEX_BODY = re.compile(r"^[0-9A-Fa-f]+$")

# (prefix, extension, content type). RIFF/WEBP is checked separately.
_SIGNATURES: tuple[tuple[bytes, str, str], ...] = (
    (b"\x89PNG\r\n\x1a\n", ".png", "image/png"),
    (b"%PDF", ".pdf", "application/pdf"),
    (b"\xff\xd8\xff", ".jpg", "image/jpeg"),
    (b"GIF87a", ".gif", "image/gif"),
    (b"GIF89a", ".gif", "image/gif"),
    (b"PK\x03\x04", ".zip", "application/zip"),
    (b"\xd0\xcf\x11\xe0", ".doc", "application/msword"),
    (b"BM", ".bmp", "image/bmp"),
    (b"II*\x00", ".tif", "image/tiff"),
    (b"MM\x00*", ".tif", "image/tiff"),
)


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
        return [{columns[i]: raw[i] for i in range(len(columns))} for raw in cur.fetchall()]
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


def column_map(mapped: dict) -> dict[str, str]:
    return {str(key).lower(): key for key in mapped}


def cell(mapped: dict, lower: dict[str, str], *names: str):
    for name in names:
        key = lower.get(name.lower())
        if key is not None:
            return mapped.get(key)
    return None


def is_customer_file_row(lower: dict[str, str]) -> bool:
    """Customer attachment table: ID, IDTypeFile, IDSanad, FileSize, Files, Sharh."""
    if "idsanad" in lower:
        return True
    return "files" in lower and "sourceid" not in lower and "contentbase64" not in lower


def id_text(value) -> str:
    if isinstance(value, bool):
        return str(value)
    if isinstance(value, int):
        return str(value)
    if isinstance(value, float) and value.is_integer():
        return str(int(value))
    return str(value).strip()


def blank(value) -> bool:
    if value is None:
        return True
    text = str(value).strip()
    return text == "" or text.upper() == "NULL"


def prepare_row(mapped: dict) -> dict:
    if not isinstance(mapped, dict):
        raise SystemExit("Each row must be an object")
    lower = column_map(mapped)
    if is_customer_file_row(lower):
        return prepare_customer_row(mapped, lower)
    if any(name in lower for name in ("contentbase64", "contenthex", "contentbytes")):
        return mapped
    return normalise_sql_row(mapped, lower)


def prepare_customer_row(mapped: dict, lower: dict[str, str]) -> dict:
    sanad = cell(mapped, lower, "idsanad")
    file_id = cell(mapped, lower, "id")
    if blank(sanad):
        raise SystemExit(f"Customer file row {file_id!r} is missing IDSanad")
    if blank(file_id):
        raise SystemExit(f"Customer file row for سند {id_text(sanad)} is missing ID")

    source_id = f"sanad-{id_text(sanad)}-file-{id_text(file_id)}"
    sharh = cell(mapped, lower, "sharh")
    type_id = cell(mapped, lower, "idtypefile")
    title = id_text(sharh) if not blank(sharh) else f"سند {id_text(sanad)}"
    description = None if blank(type_id) else f"نوع فایل {id_text(type_id)}"

    content = cell(mapped, lower, "files")
    if content is None:
        raise SystemExit(f"Row {source_id}: column Files is empty")

    row = {
        "sourceId": source_id,
        "title": title,
        "description": description,
        "categoryPath": "واردات",
        "documentTypeCode": "GENERAL",
        "sniff": True,
        "expectedSize": parse_file_size(cell(mapped, lower, "filesize"), source_id),
    }
    attach_content(row, content, source_id)
    return row


def parse_file_size(value, source_id: str) -> int | None:
    if blank(value):
        return None
    try:
        size = int(value)
    except (TypeError, ValueError) as exc:
        raise SystemExit(f"Row {source_id}: FileSize is not an integer: {value!r}") from exc
    if size < 0:
        raise SystemExit(f"Row {source_id}: FileSize is negative: {size}")
    return size


def attach_content(row: dict, content, source_id: str) -> None:
    if isinstance(content, memoryview):
        content = content.tobytes()
    if isinstance(content, bytearray):
        content = bytes(content)
    if isinstance(content, str):
        if blank(content):
            raise SystemExit(f"Row {source_id}: column Files is empty")
        row["contentHex"] = content
        return
    if isinstance(content, bytes):
        row["contentBytes"] = content
        return
    raise SystemExit(f"Row {source_id}: Files must be bytes or a 0x hex string, got {type(content).__name__}")


def normalise_sql_row(mapped: dict, lower: dict[str, str]) -> dict:
    content = cell(
        mapped,
        lower,
        "content",
        "filedata",
        "file_data",
        "file_bytes",
        "blob",
        "document",
        "binary",
        "files",
    )
    source_id = str(cell(mapped, lower, "sourceId", "source_id", "id", "docid", "document_id") or "")
    file_name = cell(mapped, lower, "fileName", "file_name", "filename", "original_name")
    row = {
        "sourceId": source_id,
        "title": str(cell(mapped, lower, "title", "name", "subject") or ""),
        "categoryPath": str(cell(mapped, lower, "categoryPath", "category_path", "folder", "path") or "واردات"),
        "documentTypeCode": str(cell(mapped, lower, "documentTypeCode", "document_type", "type_code") or "GENERAL"),
        "fileName": None if blank(file_name) else str(file_name),
        "description": cell(mapped, lower, "description", "desc", "sharh"),
        "contentType": cell(mapped, lower, "contentType", "mime", "content_type"),
        "ownerUsername": cell(mapped, lower, "ownerUsername", "owner", "username"),
        "createdAt": cell(mapped, lower, "createdAt", "created_at", "create_date"),
        "sniff": blank(file_name),
        "expectedSize": parse_file_size(cell(mapped, lower, "filesize"), source_id or "?"),
    }
    if content is None:
        raise SystemExit(f"Row {row['sourceId']!r} has no binary column (content/FileData/Files/...)")
    attach_content(row, content, row["sourceId"] or "?")
    return row


def decode_content(row: dict) -> bytes:
    source_id = row.get("sourceId")
    if "contentBytes" in row and isinstance(row["contentBytes"], (bytes, bytearray)):
        return bytes(row["contentBytes"])
    if row.get("contentBase64"):
        try:
            return base64.b64decode(row["contentBase64"], validate=False)
        except (binascii.Error, ValueError) as exc:
            raise SystemExit(f"Row {source_id!r}: contentBase64 is not valid base64") from exc
    if row.get("contentHex"):
        hex_text = re.sub(r"\s+", "", str(row["contentHex"]))
        if hex_text[:2].lower() == "0x":
            hex_text = hex_text[2:]
        if not hex_text or not HEX_BODY.fullmatch(hex_text) or len(hex_text) % 2:
            raise SystemExit(f"Row {source_id!r}: content hex is empty or not an even number of hex digits")
        return binascii.unhexlify(hex_text)
    raise SystemExit(f"Row {source_id!r}: need contentBase64, contentHex, or contentBytes")


def sniff_type(content: bytes) -> tuple[str, str]:
    for prefix, ext, mime in _SIGNATURES:
        if content.startswith(prefix):
            return ext, mime
    if len(content) >= 12 and content.startswith(b"RIFF") and content[8:12] == b"WEBP":
        return ".webp", "image/webp"
    return ".bin", "application/octet-stream"


def safe_file_name(name: str, source_id: str) -> str:
    cleaned = SAFE_NAME.sub("_", name.strip()) or "file.bin"
    if "." not in cleaned:
        cleaned = f"{cleaned}.bin"
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
        content = decode_content(row)
        expected = row.get("expectedSize")
        if expected is not None and int(expected) != len(content):
            raise SystemExit(
                f"Row {source_id}: FileSize {expected} does not match blob length {len(content)}"
            )
        file_name = row.get("fileName")
        content_type = row.get("contentType")
        if row.get("sniff") or not file_name:
            ext, mime = sniff_type(content)
            file_name = f"{source_id}{ext}"
            if not content_type:
                content_type = mime
        file_name = str(file_name)
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
        if content_type:
            row = {**row, "contentType": content_type}
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

    raw_rows = load_rows_from_file(args.rows) if args.rows else load_rows_from_sql(args.sql, args.dsn, args.driver)
    rows = [prepare_row(row) for row in raw_rows]
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
