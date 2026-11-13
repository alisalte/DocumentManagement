"""Run: python3 -m unittest discover -s scripts/tests"""

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))

from legacy_sqlscript import jalali_to_gregorian, legacy_date  # noqa: E402

PNG = bytes.fromhex("89504E470D0A1A0A0000000D49484452") + b"x" * 8
PDF = b"%PDF-1.4\n" + b"y" * 8


def n(value):
    return f"CAST({value} AS Numeric(18, 0))"


SCRIPT = f"""USE [ArchiveSaman]
GO
CREATE TABLE [dbo].[tbl_FilePeyvast](
\t[ID] [numeric](18, 0) NOT NULL,
\t[IDTypeFile] [numeric](18, 0) NULL,
\t[IDPeyvast] [numeric](18, 0) NULL,
\t[FileSize] [numeric](18, 0) NULL,
\t[Files] [varbinary](max) NULL,
\t[Sharh] [nvarchar](80) NULL,
 CONSTRAINT [PK_tbl_FilePeyvast] PRIMARY KEY CLUSTERED
(
\t[ID] ASC
) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
INSERT [dbo].[tbl_Parvandeh] ([id], [ShomareParvandeh], [MozoeParvandeh], [Sharh]) VALUES ({n(10)}, {n(345)}, N'قرارداد / خرید', N'it''s
two lines')
GO
INSERT [dbo].[tbl_ParvandehSanadPeyvaste3] ([id], [idSanad], [Mozoe], [DateSanad]) VALUES ({n(100)}, {n(10)}, N'نامه', {n(14021201)})
GO
INSERT [dbo].[tbl_FileSanad] ([ID], [IDTypeFile], [IDSanad], [FileSize], [Files], [Sharh]) VALUES ({n(1)}, NULL, {n(100)}, {n(len(PNG))}, 0x{PNG.hex()}, NULL)
GO
INSERT [dbo].[tbl_FileSanad] ([ID], [IDTypeFile], [IDSanad], [FileSize], [Files], [Sharh]) VALUES ({n(2)}, NULL, {n(100)}, NULL, NULL, NULL)
GO
INSERT [dbo].[tbl_PeyvasteSanad] ([ID], [IDSanad], [Title]) VALUES ({n(7)}, {n(100)}, N'ضمیمه')
GO
INSERT INTO [dbo].[tbl_FilePeyvast] VALUES ({n(1)}, NULL, {n(7)}, {n(len(PDF))}, 0x{PDF.hex()}, NULL),
 ({n(2)}, NULL, {n(7)}, NULL, 0x{PNG.hex()}, N'دوم');
GO
"""


class LegacySqlScriptTests(unittest.TestCase):
    def run_export(self, text: str, encoding: str = "utf-16"):
        tmp = Path(tempfile.mkdtemp())
        script = tmp / "archive.sql"
        script.write_text(text, encoding=encoding)
        out = tmp / "out"
        result = subprocess.run(
            [sys.executable, str(SCRIPTS / "legacy-export-sql-blobs.py"), "--sql-script", str(script),
             "--out-dir", str(out), "--source", "archive-saman"],
            capture_output=True, text=True,
        )
        return result, out

    def test_exports_every_file_with_folder_title_and_date(self):
        result, out = self.run_export(SCRIPT)
        self.assertEqual(result.returncode, 0, result.stderr)
        entries = json.loads((out / "manifest.json").read_text(encoding="utf-8"))["entries"]
        by_id = {entry["sourceId"]: entry for entry in entries}
        self.assertEqual(set(by_id), {"sanad-100-file-1", "peyvast-7-file-1", "peyvast-7-file-2"})

        sanad = by_id["sanad-100-file-1"]
        self.assertEqual(sanad["title"], "نامه")
        self.assertEqual(sanad["categoryPath"], "واردات/پرونده 10 (شماره 345) - قرارداد - خرید")
        self.assertEqual(sanad["contentType"], "image/png")
        self.assertEqual(sanad["createdAt"], "2024-02-20T00:00:00Z")
        self.assertEqual((out / sanad["file"]).read_bytes(), PNG)

        self.assertEqual(by_id["peyvast-7-file-1"]["title"], "ضمیمه (فایل 1 از 2)")
        self.assertEqual((out / by_id["peyvast-7-file-1"]["file"]).read_bytes(), PDF)
        self.assertEqual(by_id["peyvast-7-file-2"]["title"], "دوم (فایل 2 از 2)")
        self.assertFalse((out / ".raw").exists())

    def test_utf8_script_works_too(self):
        result, _ = self.run_export(SCRIPT, encoding="utf-8")
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_schema_only_script_explains_what_to_do(self):
        schema_only = SCRIPT[: SCRIPT.index("INSERT")]
        result, _ = self.run_export(schema_only)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Schema and data", result.stderr)

    def test_jalali_dates(self):
        self.assertEqual(jalali_to_gregorian(1403, 1, 1).isoformat(), "2024-03-20")
        self.assertEqual(jalali_to_gregorian(1399, 12, 30).isoformat(), "2021-03-20")
        self.assertEqual(legacy_date(14020512)[0], "1402/05/12")
        self.assertIsNone(legacy_date(0))
        self.assertIsNone(legacy_date(14021340))


if __name__ == "__main__":
    unittest.main()
