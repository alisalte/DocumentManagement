using Dms.Search.Application;
using Dms.Search.Domain;
using Dms.Search.Infrastructure.Extraction;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Local Tesseract OCR (no Tika, no Postgres). Skipped when <c>tesseract</c> is not on PATH.
/// </summary>
public sealed class LocalOcrTests
{
    private static bool TesseractAvailable => TesseractCliOcrEngine.CommandExists("tesseract");

    [Fact]
    public async Task A_Persian_scan_image_is_read_by_local_Tesseract()
    {
        Assert.SkipUnless(TesseractAvailable, "tesseract is not installed.");

        var options = Options.Create(new SearchOptions
        {
            OcrLanguages = "fas+eng",
            Tesseract = new TesseractOptions { Enabled = true, Command = "tesseract" },
        });
        var ocr = new TesseractCliOcrEngine(options, NullLogger<TesseractCliOcrEngine>.Instance);
        var extractor = new LocalTextExtractor(ocr, options, NullLogger<LocalTextExtractor>.Instance);

        await using var image = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Samples", "persian-scan.png"));
        var extracted = await extractor.ExtractAsync(image, "persian-scan.png", "image/png", CancellationToken.None);

        extracted.Method.ShouldBe(ExtractionMethod.Ocr);
        extracted.Engine.ShouldBe("tesseract");
        extracted.Text.ShouldContain("قرارداد");
        extracted.Text.ShouldContain("تهران");
    }

    [Fact]
    public void Local_engine_reports_disabled_when_command_missing()
    {
        var options = Options.Create(new SearchOptions
        {
            Tesseract = new TesseractOptions { Enabled = true, Command = "tesseract-does-not-exist-xyz" },
        });
        var ocr = new TesseractCliOcrEngine(options, NullLogger<TesseractCliOcrEngine>.Instance);

        ocr.IsEnabled.ShouldBeFalse();
    }
}
