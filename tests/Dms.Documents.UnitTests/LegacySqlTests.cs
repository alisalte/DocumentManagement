using Dms.Documents.Application;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;
using Shouldly;

namespace Dms.Documents.UnitTests;

public sealed class LegacyArchiveMapperTests
{
    private static LegacyRow Row(params (string Column, object? Value)[] values) =>
        new(values.ToDictionary(value => value.Column.ToLowerInvariant(), value => value.Value));

    private static LegacyArchiveCatalog Catalog()
    {
        var catalog = new LegacyArchiveCatalog();
        // SQL Server numeric(18,0) arrives as decimal.
        catalog.Cases["10"] = Row(("id", 10m), ("ShomareParvandeh", 345m), ("MozoeParvandeh", "قرارداد / خرید  تجهیزات"));
        catalog.Sanads["100"] = Row(
            ("id", 100m),
            ("idSanad", 10m),
            ("Mozoe", "نامه درخواست"),
            ("ShomareName", "۱۲/۳۴۵"),
            ("DateSanad", 14021201m),
            ("IDErsalKonnadeh", 5m),
            ("SharhPeyvast", "شرح سند"));
        catalog.Sanads["101"] = Row(("id", 101m), ("idSanad", null), ("Mozoe", "سند بی‌پرونده"));
        catalog.Peyvasts["7"] = Row(("ID", 7m), ("IDSanad", 100m), ("Title", "ضمیمه"), ("DatePeyvast", 14030101m));
        catalog.Samples["3"] = Row(("ID", 3m), ("Title", "فرم مرخصی"));
        catalog.FileTypes["2"] = Row(("ID", 2m), ("Title", "ورد"), ("PasvandFile", ".DOCX"));
        catalog.Parties["5"] = Row(("ID", 5m), ("Title", "وزارت نیرو"));
        return catalog;
    }

    private static LegacyManifestEntry Map(LegacyArchiveCatalog catalog, LegacyFileRow file, int part = 1, int parts = 1) =>
        LegacyArchiveMapper.Map(catalog, file, part, parts, "files/x.pdf", "x.pdf", "application/pdf", 10, new string('a', 64));

    [Fact]
    public void A_sanad_file_lands_in_its_case_folder_with_title_date_and_details()
    {
        var file = new LegacyFileRow(LegacyArchiveMapper.FileSanad, "1", "100", "2", 10, null, 10);

        var entry = Map(Catalog(), file, part: 1, parts: 2);

        entry.SourceId.ShouldBe("sanad-100-file-1");
        entry.Title.ShouldBe("نامه درخواست (فایل 1 از 2)");
        entry.CategoryPath.ShouldBe("واردات/پرونده 10 (شماره 345) - قرارداد - خرید تجهیزات");
        entry.DocumentTypeCode.ShouldBe("GENERAL");
        entry.CreatedAt.ShouldBe(new DateTimeOffset(2024, 2, 20, 0, 0, 0, TimeSpan.Zero));
        var description = entry.Description.ShouldNotBeNull();
        description.ShouldContain("شماره نامه: ۱۲/۳۴۵");
        description.ShouldContain("تاریخ سند: 1402/12/01");
        description.ShouldContain("فرستنده: وزارت نیرو");
        description.ShouldContain("نوع فایل: ورد");
        description.ShouldContain("منبع: tbl_FileSanad شناسه 1");
    }

    [Fact]
    public void A_file_description_wins_and_a_sanad_without_case_goes_to_the_no_case_folder()
    {
        var file = new LegacyFileRow(LegacyArchiveMapper.FileSanad, "3", "101", null, null, "صفحه دوم", 10);

        var entry = Map(Catalog(), file);

        entry.Title.ShouldBe("صفحه دوم");
        entry.CategoryPath.ShouldBe("واردات/بدون پرونده");
        entry.CreatedAt.ShouldBeNull();
    }

    [Fact]
    public void Attachments_follow_their_sanad_and_samples_get_their_own_folder()
    {
        var catalog = Catalog();

        var attachment = Map(catalog, new LegacyFileRow(LegacyArchiveMapper.FilePeyvast, "1", "7", null, null, null, 10));
        attachment.SourceId.ShouldBe("peyvast-7-file-1");
        attachment.Title.ShouldBe("ضمیمه");
        attachment.CategoryPath.ShouldBe("واردات/پرونده 10 (شماره 345) - قرارداد - خرید تجهیزات");
        attachment.Description.ShouldNotBeNull().ShouldContain("پیوستِ سند 100 (نامه درخواست)");
        attachment.CreatedAt.ShouldBe(new DateTimeOffset(2024, 3, 20, 0, 0, 0, TimeSpan.Zero));

        var sample = Map(catalog, new LegacyFileRow(LegacyArchiveMapper.FilesSampleAsnad, "1", "3", null, null, null, 10));
        sample.SourceId.ShouldBe("sample-3-file-1");
        sample.Title.ShouldBe("فرم مرخصی");
        sample.CategoryPath.ShouldBe("واردات/نمونه اسناد");
    }

    [Fact]
    public void Rows_missing_from_the_lookup_tables_still_get_a_title_and_folder()
    {
        var entry = Map(new LegacyArchiveCatalog(), new LegacyFileRow(LegacyArchiveMapper.FileSanad, "9", "999", "1", null, null, 10));

        entry.Title.ShouldBe("سند 999");
        entry.CategoryPath.ShouldBe("واردات/بدون پرونده");
    }

    [Theory]
    [InlineData(14020512, "1402/05/12", "2023-08-03")]
    [InlineData(13991230, "1399/12/30", "2021-03-20")]
    [InlineData(14030101, "1403/01/01", "2024-03-20")]
    [InlineData(20240115, "2024-01-15", "2024-01-15")]
    public void Old_number_dates_are_read_as_jalali_or_gregorian(long value, string display, string gregorian)
    {
        var parsed = LegacyArchiveMapper.ParseDate((decimal)value);

        parsed.ShouldNotBeNull();
        parsed.Value.Display.ShouldBe(display);
        parsed.Value.Gregorian.ToString("yyyy-MM-dd").ShouldBe(gregorian);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14021340)]
    [InlineData(14021231)]
    [InlineData(140205)]
    public void Impossible_dates_are_ignored(long value) =>
        LegacyArchiveMapper.ParseDate((decimal)value).ShouldBeNull();

    [Fact]
    public void The_type_table_extension_is_cleaned_and_unsafe_ones_are_dropped()
    {
        var catalog = Catalog();
        LegacyArchiveMapper.TypeExtension(catalog, new LegacyFileRow("tbl_FileSanad", "1", "1", "2", null, null, 1))
            .ShouldBe("docx");

        catalog.FileTypes["3"] = Row(("ID", 3m), ("PasvandFile", "../exe"));
        LegacyArchiveMapper.TypeExtension(catalog, new LegacyFileRow("tbl_FileSanad", "1", "1", "3", null, null, 1))
            .ShouldBeNull();
    }

    [Fact]
    public void The_manifest_parses_back_with_the_import_engine()
    {
        var entry = Map(Catalog(), new LegacyFileRow(LegacyArchiveMapper.FileSanad, "1", "100", null, null, null, 10));

        var parsed = ManifestSerializer.Parse(LegacyManifestWriter.Write([entry]));

        parsed.IsSuccess.ShouldBeTrue();
        parsed.Value.Source.ShouldBe("archive-saman");
        parsed.Value.Entries.Single().Title.ShouldBe("نامه درخواست");
    }
}

public sealed class FileSignatureTests
{
    [Fact]
    public void Headers_decide_the_type() =>
        FileSignature.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0], null).ShouldBe(("png", "image/png"));

    [Fact]
    public void A_zip_marked_docx_in_the_type_table_is_a_docx() =>
        FileSignature.Detect("PK\u0003\u0004"u8, "docx").Extension.ShouldBe("docx");

    [Fact]
    public void An_ole_file_marked_xls_is_an_xls_but_a_pdf_header_beats_the_table() =>
        (FileSignature.Detect([0xD0, 0xCF, 0x11, 0xE0], "xls").Extension, FileSignature.Detect("%PDF-1.4"u8, "docx").Extension)
            .ShouldBe(("xls", "pdf"));

    [Fact]
    public void Unknown_headers_fall_back_to_the_type_table_then_bin()
    {
        FileSignature.Detect([1, 2, 3], "txt").ShouldBe(("txt", "text/plain"));
        FileSignature.Detect([1, 2, 3], null).ShouldBe(("bin", "application/octet-stream"));
    }
}

public sealed class LegacyExtractionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    private static LegacyExtraction Create() =>
        LegacyExtraction.Create("", " 192.168.1.5\\SQLEXPRESS ", "ArchiveSaman", "sa", new UserId(Guid.NewGuid()), Now).Value;

    [Fact]
    public void A_new_copy_is_queued_with_its_own_staging_folder_and_a_default_name()
    {
        var extraction = Create();

        extraction.Status.ShouldBe(LegacyExtractionStatus.Queued);
        extraction.Server.ShouldBe("192.168.1.5\\SQLEXPRESS");
        extraction.Name.ShouldBe("ArchiveSaman @ 192.168.1.5\\SQLEXPRESS");
        extraction.StagingFolder.ShouldBe($"legacy-sql/{extraction.Id.Value:N}");
    }

    [Fact]
    public void A_running_copy_that_stops_reporting_can_be_resumed()
    {
        var extraction = Create();
        extraction.Start(Now).IsSuccess.ShouldBeTrue();
        extraction.Progress(5, 0, 500, Now);

        extraction.CanResume(Now.AddSeconds(30)).ShouldBeFalse();
        extraction.Requeue("sa", Now.AddSeconds(30)).IsFailure.ShouldBeTrue();

        var later = Now + LegacyExtraction.HeartbeatTimeout + TimeSpan.FromSeconds(1);
        extraction.IsStale(later).ShouldBeTrue();
        extraction.Requeue("sa", later).IsSuccess.ShouldBeTrue();
        extraction.Status.ShouldBe(LegacyExtractionStatus.Queued);
        extraction.Start(later).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Stopping_a_live_copy_only_raises_the_flag_but_a_dead_one_is_cancelled_at_once()
    {
        var live = Create();
        live.Start(Now);
        live.RequestCancel(Now.AddSeconds(1)).IsSuccess.ShouldBeTrue();
        live.CancelRequested.ShouldBeTrue();
        live.Status.ShouldBe(LegacyExtractionStatus.Running);

        var dead = Create();
        dead.Start(Now);
        dead.RequestCancel(Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        dead.Status.ShouldBe(LegacyExtractionStatus.Cancelled);
    }

    [Fact]
    public void A_finished_copy_cannot_be_resumed_but_a_failed_one_can()
    {
        var done = Create();
        done.Start(Now);
        done.Complete(ImportJobId.New(), null, Now);
        done.CanResume(Now.AddDays(1)).ShouldBeFalse();

        var failed = Create();
        failed.Start(Now);
        failed.Fail("legacy_sql.login_failed: Login failed", Now);
        failed.CanResume(Now).ShouldBeTrue();
        failed.Requeue(null, Now).IsSuccess.ShouldBeTrue();
        failed.LastError.ShouldBeNull();
        failed.SqlUser.ShouldBeNull();
    }
}
