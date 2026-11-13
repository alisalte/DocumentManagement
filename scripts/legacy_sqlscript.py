"""Read a SQL Server "Generate Scripts" file (schema + data) from the old archive.

The old program (database ArchiveSaman) keeps every file inside SQL Server as
VARBINARY(MAX). SSMS → Tasks → Generate Scripts, with "Types of data to script"
set to "Schema and data", writes a .sql file (usually UTF-16) whose INSERT
statements carry each blob as a 0x… hex literal. This module reads that file
without a database driver and turns it into import entries.

Tables used (everything else in the script is ignored):

  tbl_Parvandeh                 پرونده (case file) → one folder each
  tbl_ParvandehSanadPeyvaste3   سند (document inside a پرونده)
  tbl_FileSanad                 files of a سند            → sanad-{IDSanad}-file-{ID}
  tbl_PeyvasteSanad             پیوست (attachment) of a سند
  tbl_FilePeyvast               files of a پیوست          → peyvast-{IDPeyvast}-file-{ID}
  tbl_SampleAsnad               نمونه اسناد (templates)
  tbl_FilesSampleAsnad          files of a template       → sample-{IDSampleAsnad}-file-{ID}
  tbl_AnvaeFile                 file types (title + extension)
  tbl_ErsalKonandegan           senders / receivers
  tbl_User                      users (only FullName / UserName for descriptions)

Blobs are streamed to disk as soon as their INSERT is read, so the whole
database never has to fit in memory.
"""

from __future__ import annotations

import codecs
import hashlib
import os
from decimal import Decimal
import re
from dataclasses import dataclass, field
from datetime import date, datetime, timezone
from pathlib import Path
from typing import Callable, Iterator

ROOT_FOLDER = "واردات"
NO_CASE_FOLDER = "بدون پرونده"
SAMPLES_FOLDER = "نمونه اسناد"

FILE_TABLES = {
    "tbl_filesanad": ("files", "idsanad"),
    "tbl_filepeyvast": ("files", "idpeyvast"),
    "tbl_filessampleasnad": ("filesample", "idsampleasnad"),
}
META_TABLES = {
    "tbl_parvandeh",
    "tbl_parvandehsanadpeyvaste3",
    "tbl_peyvastesanad",
    "tbl_sampleasnad",
    "tbl_anvaefile",
    "tbl_ersalkonandegan",
}

# Direct SQL Server reads: (real table name, columns without the blob, blob column).
DB_FILE_TABLES = {
    "tbl_filesanad": ("tbl_FileSanad", ["ID", "IDTypeFile", "IDSanad", "FileSize", "Sharh"], "Files"),
    "tbl_filepeyvast": ("tbl_FilePeyvast", ["ID", "IDTypeFile", "IDPeyvast", "FileSize", "Sharh"], "Files"),
    "tbl_filessampleasnad": (
        "tbl_FilesSampleAsnad",
        ["ID", "IDSampleAsnad", "IDTypeFile", "SizeFile", "Sharh"],
        "FileSample",
    ),
}
DB_META_TABLES = {
    "tbl_parvandeh": "tbl_Parvandeh",
    "tbl_parvandehsanadpeyvaste3": "tbl_ParvandehSanadPeyvaste3",
    "tbl_peyvastesanad": "tbl_PeyvasteSanad",
    "tbl_sampleasnad": "tbl_SampleAsnad",
    "tbl_anvaefile": "tbl_AnvaeFile",
    "tbl_ersalkonandegan": "tbl_ErsalKonandegan",
}


class SqlScriptError(Exception):
    pass


class _Incomplete(Exception):
    """The statement continues on the next line."""


# ---------------------------------------------------------------------------
# Reading the file
# ---------------------------------------------------------------------------

def open_script(path: Path):
    with path.open("rb") as probe:
        head = probe.read(4)
    if head.startswith(codecs.BOM_UTF16_LE):
        encoding = "utf-16"
    elif head.startswith(codecs.BOM_UTF16_BE):
        encoding = "utf-16"
    elif head.startswith(codecs.BOM_UTF8):
        encoding = "utf-8-sig"
    elif len(head) >= 2 and head[1] == 0:
        encoding = "utf-16-le"
    else:
        encoding = "utf-8"
    return path.open("r", encoding=encoding, newline=None)


def statements(lines: Iterator[str]) -> Iterator[str]:
    """Yield CREATE TABLE / INSERT statements; string literals and multi-row VALUES may span lines."""
    buffer: list[str] = []
    quotes = 0
    is_create = False
    for line in lines:
        stripped = line.strip()
        if not buffer:
            head = stripped[:12].upper()
            if head.startswith("INSERT"):
                is_create = False
            elif head.startswith("CREATE TABLE"):
                is_create = True
            else:
                continue
        buffer.append(line)
        quotes += line.count("'")
        if quotes % 2:
            continue
        if is_create:
            if not (stripped.startswith(")") or stripped.upper() == "GO"):
                continue
        elif not (stripped.endswith(")") or stripped.endswith(";")):
            continue
        yield "".join(buffer)
        buffer = []
        quotes = 0
    if buffer:
        if is_create:
            yield "".join(buffer)
        else:
            raise SqlScriptError("The script ends in the middle of an INSERT statement (file cut off?)")


_TOKEN = re.compile(
    r"""
      (?P<ws>\s+)
    | (?P<str>N?'(?:[^']|'')*')
    | (?P<hex>0[xX][0-9A-Fa-f]*)
    | (?P<num>[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)
    | (?P<ident>\[[^\]]*\]|[A-Za-z_@#][A-Za-z0-9_@#$]*)
    | (?P<punct>[(),.;*])
    """,
    re.VERBOSE,
)


def tokenize(text: str) -> list[tuple[str, str]]:
    tokens: list[tuple[str, str]] = []
    pos = 0
    end = len(text)
    while pos < end:
        match = _TOKEN.match(text, pos)
        if match is None:
            if text[pos] == "'" or text[pos : pos + 2].upper() == "N'":
                raise _Incomplete()
            raise SqlScriptError(f"Unexpected character {text[pos]!r} near: {text[max(0, pos - 40):pos + 40]!r}")
        kind = match.lastgroup or ""
        if kind != "ws":
            tokens.append((kind, match.group()))
        pos = match.end()
    return tokens


def _name(token: str) -> str:
    return token[1:-1] if token.startswith("[") else token


class _Parser:
    def __init__(self, tokens: list[tuple[str, str]]):
        self.tokens = tokens
        self.i = 0

    def peek(self) -> tuple[str, str] | None:
        return self.tokens[self.i] if self.i < len(self.tokens) else None

    def take(self) -> tuple[str, str]:
        token = self.peek()
        if token is None:
            raise _Incomplete()
        self.i += 1
        return token

    def expect(self, value: str) -> None:
        kind, text = self.take()
        if text.upper() != value.upper():
            raise SqlScriptError(f"Expected {value!r}, found {text!r}")

    def accept(self, value: str) -> bool:
        token = self.peek()
        if token is not None and token[1].upper() == value.upper():
            self.i += 1
            return True
        return False

    def qualified_name(self) -> str:
        parts = [_name(self.take()[1])]
        while self.accept("."):
            parts.append(_name(self.take()[1]))
        return parts[-1]

    def skip_parens(self) -> None:
        depth = 0
        while True:
            _, text = self.take()
            if text == "(":
                depth += 1
            elif text == ")":
                depth -= 1
                if depth <= 0:
                    return

    def value(self):
        kind, text = self.take()
        if kind == "str":
            body = text[2:-1] if text[0] in "Nn" else text[1:-1]
            return body.replace("''", "'")
        if kind == "hex":
            return _HexBlob(text)
        if kind == "num":
            return int(text) if re.fullmatch(r"[-+]?\d+", text) else float(text)
        if kind == "ident":
            upper = text.upper()
            if upper == "NULL":
                return None
            if upper in ("CAST", "CONVERT", "TRY_CAST"):
                self.expect("(")
                if upper == "CONVERT":
                    self.skip_type()
                    self.expect(",")
                    inner = self.value()
                    while not self.accept(")"):
                        self.take()
                    return inner
                inner = self.value()
                self.expect("AS")
                self.skip_type()
                self.expect(")")
                return inner
            return text
        if text == "(":
            inner = self.value()
            self.expect(")")
            return inner
        raise SqlScriptError(f"Unexpected value token {text!r}")

    def skip_type(self) -> None:
        self.take()
        if self.peek() is not None and self.peek()[1] == "(":
            self.skip_parens()


@dataclass
class _HexBlob:
    text: str

    def to_bytes(self) -> bytes:
        body = self.text[2:]
        if len(body) % 2:
            raise SqlScriptError("A 0x binary literal has an odd number of hex digits")
        return bytes.fromhex(body)


def parse_insert(text: str, columns_by_table: dict[str, list[str]]) -> tuple[str, list[dict]]:
    parser = _Parser(tokenize(text))
    parser.expect("INSERT")
    parser.accept("INTO")
    table = parser.qualified_name()
    columns: list[str] | None = None
    if parser.peek() is not None and parser.peek()[1] == "(":
        parser.take()
        columns = []
        while True:
            columns.append(_name(parser.take()[1]))
            if parser.accept(")"):
                break
            parser.expect(",")
    if columns is None:
        columns = columns_by_table.get(table.lower())
        if not columns:
            raise SqlScriptError(f"INSERT into {table} has no column list and no CREATE TABLE was seen for it")
    parser.expect("VALUES")
    rows = []
    while True:
        parser.expect("(")
        values = []
        while True:
            values.append(parser.value())
            if parser.accept(")"):
                break
            parser.expect(",")
        if len(values) != len(columns):
            raise SqlScriptError(f"INSERT into {table}: {len(columns)} columns but {len(values)} values")
        rows.append({columns[i].lower(): values[i] for i in range(len(columns))})
        if not parser.accept(","):
            break
    return table, rows


def parse_create_columns(text: str) -> tuple[str, list[str]]:
    match = re.search(r"CREATE\s+TABLE\s+((?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+))*)\s*\(", text, re.IGNORECASE)
    if not match:
        raise SqlScriptError("Could not read a CREATE TABLE statement")
    table = _name(re.split(r"\.(?![^\[]*\])", match.group(1))[-1])
    columns = []
    for line in text[match.end():].splitlines():
        col = re.match(r"\s*(\[[^\]]+\]|[A-Za-z_]\w*)\s+\[?\w", line)
        if not col:
            continue
        name = _name(col.group(1))
        if name.upper() in ("CONSTRAINT", "PRIMARY", "UNIQUE", "INDEX", "CHECK", "FOREIGN"):
            break
        columns.append(name)
    return table, columns


# ---------------------------------------------------------------------------
# Mapping the old archive onto import entries
# ---------------------------------------------------------------------------

@dataclass
class StagedFile:
    table: str
    row: dict
    raw_path: Path
    size: int
    sha256: str
    head: bytes


@dataclass
class ScriptContents:
    tables_seen: set[str] = field(default_factory=set)
    rows_seen: dict[str, int] = field(default_factory=dict)
    meta: dict[str, dict[str, dict]] = field(default_factory=dict)
    files: list[StagedFile] = field(default_factory=list)
    skipped_empty: int = 0
    reused: int = 0
    warnings: list[str] = field(default_factory=list)


def read_script(path: Path, raw_dir: Path, progress: Callable[[str], None] | None = None) -> ScriptContents:
    raw_dir.mkdir(parents=True, exist_ok=True)
    contents = ScriptContents()
    columns_by_table: dict[str, list[str]] = {}
    with open_script(path) as handle:
        for text in statements(handle):
            if text.lstrip()[:6].upper() == "CREATE":
                table, columns = parse_create_columns(text)
                columns_by_table[table.lower()] = columns
                contents.tables_seen.add(table.lower())
                continue
            try:
                table, rows = parse_insert(text, columns_by_table)
            except _Incomplete as exc:
                raise SqlScriptError(f"Incomplete INSERT statement: {text[:200]!r}") from exc
            key = table.lower()
            contents.tables_seen.add(key)
            contents.rows_seen[key] = contents.rows_seen.get(key, 0) + len(rows)
            for row in rows:
                if key in FILE_TABLES:
                    _stage_file(contents, key, row, raw_dir)
                elif key in META_TABLES:
                    row_id = _id(row.get("id"))
                    if row_id is not None:
                        contents.meta.setdefault(key, {})[row_id] = {
                            k: v for k, v in row.items() if not isinstance(v, _HexBlob)
                        }
            if progress and key in FILE_TABLES and len(contents.files) % 100 == 0:
                progress(f"{len(contents.files)} files read")
    return contents


def _stage_file(contents: ScriptContents, table: str, row: dict, raw_dir: Path) -> None:
    blob_column, _ = FILE_TABLES[table]
    blob = row.pop(blob_column, None)
    if blob is None:
        contents.skipped_empty += 1
        return
    if isinstance(blob, str):
        blob = _HexBlob(blob if blob.lower().startswith("0x") else "0x" + blob)
    if not isinstance(blob, _HexBlob):
        raise SqlScriptError(f"{table} row {row.get('id')!r}: file column is not a 0x binary literal")
    data = blob.to_bytes()
    if not data:
        contents.skipped_empty += 1
        return
    raw_path = raw_dir / f"{table}-{_id(row.get('id')) or len(contents.files)}.bin"
    raw_path.write_bytes(data)
    contents.files.append(
        StagedFile(table, row, raw_path, len(data), hashlib.sha256(data).hexdigest(), data[:16])
    )


def _id(value) -> str | None:
    if value is None:
        return None
    if isinstance(value, Decimal) and value == value.to_integral_value():
        value = int(value)
    if isinstance(value, float) and value.is_integer():
        value = int(value)
    text = str(value).strip()
    return text or None


def _text(value) -> str | None:
    if value is None:
        return None
    text = re.sub(r"\s+", " ", str(value)).strip()
    return text or None


def _folder_name(text: str) -> str:
    cleaned = re.sub(r"[/\\]+", "-", text)
    cleaned = re.sub(r"\s+", " ", cleaned).strip(" -.")
    return cleaned[:80].strip() or "بدون نام"


def jalali_to_gregorian(jy: int, jm: int, jd: int) -> date:
    jy += 1595
    days = -355668 + 365 * jy + (jy // 33) * 8 + ((jy % 33) + 3) // 4 + jd
    days += (jm - 1) * 31 if jm < 7 else (jm - 7) * 30 + 186
    gy = 400 * (days // 146097)
    days %= 146097
    if days > 36524:
        days -= 1
        gy += 100 * (days // 36524)
        days %= 36524
        if days >= 365:
            days += 1
    gy += 4 * (days // 1461)
    days %= 1461
    if days > 365:
        gy += (days - 1) // 365
        days = (days - 1) % 365
    gd = days + 1
    leap = (gy % 4 == 0 and gy % 100 != 0) or gy % 400 == 0
    months = [0, 31, 29 if leap else 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
    gm = 1
    while gm <= 12 and gd > months[gm]:
        gd -= months[gm]
        gm += 1
    return date(gy, gm, gd)


def legacy_date(value) -> tuple[str, date] | None:
    """Old dates are numbers like 14020512 (Jalali yyyymmdd). Returns (display, gregorian)."""
    raw = _id(value)
    if raw is None:
        return None
    digits = re.sub(r"\D", "", raw)
    if len(digits) != 8:
        return None
    year, month, day = int(digits[:4]), int(digits[4:6]), int(digits[6:])
    try:
        if 1300 <= year <= 1500 and 1 <= month <= 12 and 1 <= day <= (31 if month <= 6 else 30):
            return f"{year:04d}/{month:02d}/{day:02d}", jalali_to_gregorian(year, month, day)
        if 1900 <= year <= 2100:
            gregorian = date(year, month, day)
            return gregorian.isoformat(), gregorian
    except ValueError:
        return None
    return None


def _iso(day: date) -> str:
    return datetime(day.year, day.month, day.day, tzinfo=timezone.utc).isoformat().replace("+00:00", "Z")


_SOURCE_PREFIX = {"tbl_filesanad": "sanad", "tbl_filepeyvast": "peyvast", "tbl_filessampleasnad": "sample"}


def source_id_for(table: str, row: dict) -> str:
    parent = _id(row.get(FILE_TABLES[table][1])) or "?"
    return f"{_SOURCE_PREFIX[table]}-{parent}-file-{_id(row.get('id')) or '?'}"


@dataclass
class ArchiveEntry:
    source_id: str
    title: str
    category_path: str
    description: str | None
    file_name_hint: str | None
    created_at: str | None
    staged: StagedFile


def build_entries(contents: ScriptContents) -> list[ArchiveEntry]:
    meta = contents.meta
    cases = meta.get("tbl_parvandeh", {})
    sanads = meta.get("tbl_parvandehsanadpeyvaste3", {})
    peyvasts = meta.get("tbl_peyvastesanad", {})
    samples = meta.get("tbl_sampleasnad", {})
    file_types = meta.get("tbl_anvaefile", {})
    parties = meta.get("tbl_ersalkonandegan", {})

    per_parent: dict[tuple[str, str], int] = {}
    for staged in contents.files:
        parent = _id(staged.row.get(FILE_TABLES[staged.table][1])) or "?"
        per_parent[(staged.table, parent)] = per_parent.get((staged.table, parent), 0) + 1
    seen_in_parent: dict[tuple[str, str], int] = {}

    def case_folder(case_id: str | None) -> str:
        if case_id is None or case_id not in cases:
            return f"{ROOT_FOLDER}/{NO_CASE_FOLDER}"
        case = cases[case_id]
        label = _text(case.get("mozoeparvandeh")) or _text(case.get("kholaseparvandeh"))
        number = _id(case.get("shomareparvandeh"))
        # The folder code is the first 12 letters/digits, so the id goes first to keep codes unique.
        name = f"پرونده {case_id}"
        if number and number != case_id:
            name += f" (شماره {number})"
        if label:
            name += f" - {label}"
        return f"{ROOT_FOLDER}/{_folder_name(name)}"

    def type_info(staged: StagedFile) -> tuple[str | None, str | None]:
        ftype = file_types.get(_id(staged.row.get("idtypefile")) or "")
        if not ftype:
            return None, None
        return _text(ftype.get("title")), _text(ftype.get("pasvandfile"))

    def sanad_lines(sanad: dict) -> tuple[list[str], str | None]:
        lines = []
        created = None
        if _text(sanad.get("shomarename")):
            lines.append(f"شماره نامه: {_text(sanad.get('shomarename'))}")
        if _id(sanad.get("sanadnumber")):
            lines.append(f"شماره سند: {_id(sanad.get('sanadnumber'))}")
        for column, label in (("datesanad", "تاریخ سند"), ("tarikhsabt", "تاریخ ثبت")):
            parsed = legacy_date(sanad.get(column))
            if parsed:
                lines.append(f"{label}: {parsed[0]}")
                created = created or _iso(parsed[1])
        for column, label in (("idersalkonnadeh", "فرستنده"), ("iddaryaftkonnadeh", "گیرنده")):
            party = parties.get(_id(sanad.get(column)) or "")
            if party and _text(party.get("title")):
                lines.append(f"{label}: {_text(party.get('title'))}")
        if _text(sanad.get("sharhpeyvast")):
            lines.append(f"شرح: {_text(sanad.get('sharhpeyvast'))}")
        return lines, created

    entries: list[ArchiveEntry] = []
    for staged in contents.files:
        row = staged.row
        file_id = _id(row.get("id")) or "?"
        parent_id = _id(row.get(FILE_TABLES[staged.table][1])) or "?"
        key = (staged.table, parent_id)
        seen_in_parent[key] = seen_in_parent.get(key, 0) + 1
        part = f" (فایل {seen_in_parent[key]} از {per_parent[key]})" if per_parent[key] > 1 else ""
        type_title, extension = type_info(staged)
        lines: list[str] = []
        created = None

        if staged.table == "tbl_filesanad":
            source_id = source_id_for(staged.table, row)
            sanad = sanads.get(parent_id, {})
            base = _text(row.get("sharh")) or _text(sanad.get("mozoe")) or f"سند {parent_id}"
            folder = case_folder(_id(sanad.get("idsanad")))
            lines, created = sanad_lines(sanad)
        elif staged.table == "tbl_filepeyvast":
            source_id = source_id_for(staged.table, row)
            peyvast = peyvasts.get(parent_id, {})
            sanad_id = _id(peyvast.get("idsanad"))
            sanad = sanads.get(sanad_id or "", {})
            base = (
                _text(row.get("sharh"))
                or _text(peyvast.get("title"))
                or f"پیوست {parent_id}" + (f" سند {sanad_id}" if sanad_id else "")
            )
            folder = case_folder(_id(sanad.get("idsanad")))
            if sanad_id:
                lines.append(f"پیوستِ سند {sanad_id}" + (f" ({_text(sanad.get('mozoe'))})" if _text(sanad.get("mozoe")) else ""))
            if _text(peyvast.get("sharh")):
                lines.append(f"شرح پیوست: {_text(peyvast.get('sharh'))}")
            for column, label in (("datepeyvast", "تاریخ پیوست"), ("datesabt", "تاریخ ثبت")):
                parsed = legacy_date(peyvast.get(column))
                if parsed:
                    lines.append(f"{label}: {parsed[0]}")
                    created = created or _iso(parsed[1])
        else:
            source_id = source_id_for(staged.table, row)
            sample = samples.get(parent_id, {})
            base = _text(row.get("sharh")) or _text(sample.get("title")) or f"نمونه سند {parent_id}"
            folder = f"{ROOT_FOLDER}/{SAMPLES_FOLDER}"
            if _text(sample.get("description")):
                lines.append(_text(sample.get("description")))

        if type_title:
            lines.append(f"نوع فایل: {type_title}")
        lines.append(f"منبع: {staged.table} شناسه {file_id}")
        entries.append(
            ArchiveEntry(
                source_id=source_id,
                title=(base + part)[:300],
                category_path=folder,
                description="\n".join(lines)[:4000] or None,
                file_name_hint=extension,
                created_at=created,
                staged=staged,
            )
        )
    return entries


# ---------------------------------------------------------------------------
# Reading straight from SQL Server (any DB-API connection, normally pyodbc)
# ---------------------------------------------------------------------------

def _file_digest(path: Path) -> tuple[str, bytes]:
    sha = hashlib.sha256()
    with path.open("rb") as handle:
        head = handle.read(16)
        sha.update(head)
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            sha.update(chunk)
    return sha.hexdigest(), head


def _rows(cursor) -> list[dict]:
    columns = [column[0].lower() for column in cursor.description]
    return [dict(zip(columns, raw)) for raw in cursor.fetchall()]


def read_database(
    conn,
    raw_dir: Path,
    schema: str = "dbo",
    progress: Callable[[str], None] | None = None,
    find_existing: Callable[[str], Path | None] | None = None,
) -> ScriptContents:
    """Read the old archive table by table. Files are fetched one row at a time
    (a single blob in memory), and a file already on disk with the right length
    is not downloaded again, so an interrupted run can simply be started again."""
    raw_dir.mkdir(parents=True, exist_ok=True)
    contents = ScriptContents()
    cursor = conn.cursor()

    def qualified(table: str) -> str:
        return f"[{schema}].[{table}]" if schema else f"[{table}]"

    for key, table in DB_META_TABLES.items():
        try:
            cursor.execute(f"SELECT * FROM {qualified(table)}")
            rows = _rows(cursor)
        except Exception as exc:  # noqa: BLE001 - driver-specific error types
            contents.warnings.append(f"{table} not read ({exc.__class__.__name__}: {exc}); its details are left out")
            continue
        contents.tables_seen.add(key)
        contents.rows_seen[key] = len(rows)
        for row in rows:
            row_id = _id(row.get("id"))
            if row_id is not None:
                contents.meta.setdefault(key, {})[row_id] = row

    plan: list[tuple[str, str, str, dict, int]] = []
    for key, (table, columns, blob) in DB_FILE_TABLES.items():
        select = ", ".join(f"[{column}]" for column in columns)
        try:
            cursor.execute(
                f"SELECT {select}, DATALENGTH([{blob}]) AS blob_length FROM {qualified(table)} ORDER BY [ID]"
            )
            rows = _rows(cursor)
        except Exception as exc:  # noqa: BLE001
            contents.warnings.append(f"{table} not read ({exc.__class__.__name__}: {exc})")
            continue
        contents.tables_seen.add(key)
        contents.rows_seen[key] = len(rows)
        for row in rows:
            length = row.pop("blob_length", None)
            if not length:
                contents.skipped_empty += 1
                continue
            plan.append((key, table, blob, row, int(length)))

    total = len(plan)
    total_bytes = sum(item[4] for item in plan)
    done_bytes = 0
    if progress:
        progress(f"{total} files to export ({total_bytes / 1048576:,.1f} MB)")
    for index, (key, table, blob, row, length) in enumerate(plan, start=1):
        file_id = _id(row.get("id")) or str(index)
        raw_path = raw_dir / f"{key}-{file_id}.bin"
        existing = raw_path if raw_path.is_file() else None
        if existing is None and find_existing is not None:
            existing = find_existing(source_id_for(key, row))
        if existing is not None and existing.stat().st_size == length:
            sha, head = _file_digest(existing)
            contents.reused += 1
            contents.files.append(StagedFile(key, row, existing, length, sha, head))
        else:
            cursor.execute(f"SELECT [{blob}] FROM {qualified(table)} WHERE [ID] = ?", row.get("id"))
            fetched = cursor.fetchone()
            data = bytes(fetched[0]) if fetched and fetched[0] is not None else b""
            if not data:
                contents.skipped_empty += 1
                continue
            partial = raw_path.with_suffix(".part")
            partial.write_bytes(data)
            os.replace(partial, raw_path)
            contents.files.append(
                StagedFile(key, row, raw_path, len(data), hashlib.sha256(data).hexdigest(), data[:16])
            )
        done_bytes += length
        if progress and (index % 100 == 0 or index == total):
            progress(f"{index}/{total} files, {done_bytes / 1048576:,.1f} of {total_bytes / 1048576:,.1f} MB")
    return contents
