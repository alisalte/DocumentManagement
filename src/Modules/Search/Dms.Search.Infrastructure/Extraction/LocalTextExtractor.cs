using System.Text;
using Dms.Search.Application;
using Dms.Search.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Search.Infrastructure.Extraction;

/// <summary>
/// Text extraction without Tika: digital PDFs via <c>pdftotext</c>, scans and images via
/// <see cref="IOcrEngine"/> (local Tesseract). Used when <c>Dms:Search:TikaUrl</c> is empty.
/// </summary>
public sealed class LocalTextExtractor(
    IOcrEngine ocr,
    IOptions<SearchOptions> options,
    ILogger<LocalTextExtractor> logger) : ITextExtractor
{
    private const int ThinTextLayer = 64;

    private static readonly string[] NoText =
    [
        "video/", "audio/", "application/octet-stream", "application/x-dwg", "image/vnd.dwg",
    ];

    private readonly SearchOptions _options = options.Value;

    public bool IsEnabled => ocr.IsEnabled;

    public async Task<ExtractedText> ExtractAsync(
        Stream content,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || NoText.Any(prefix => mimeType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return new ExtractedText(string.Empty, ExtractionMethod.None, "none");
        }

        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            var text = await ocr.RecogniseAsync(content, mimeType, cancellationToken);
            return new ExtractedText(Trim(text), ExtractionMethod.Ocr, "tesseract");
        }

        if (mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractPdfAsync(content, cancellationToken);
        }

        // Office and other types need Tika/Gotenberg; without them there is nothing useful to OCR.
        logger.LogDebug("Local extractor skips {Mime} ({File}); configure Tika for this type.", mimeType, fileName);
        return new ExtractedText(string.Empty, ExtractionMethod.None, "none");
    }

    private async Task<ExtractedText> ExtractPdfAsync(Stream content, CancellationToken cancellationToken)
    {
        var work = Directory.CreateTempSubdirectory("dms-pdf-");
        try
        {
            var pdfPath = Path.Combine(work.FullName, "doc.pdf");
            await using (var file = File.Create(pdfPath))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            var layer = await PdfToTextAsync(pdfPath, cancellationToken);
            if (CountLetters(layer) >= ThinTextLayer)
            {
                return new ExtractedText(Trim(layer), ExtractionMethod.TextLayer, "pdftotext");
            }

            var pages = await RasterisePdfAsync(pdfPath, work.FullName, cancellationToken);
            if (pages.Count == 0)
            {
                return new ExtractedText(Trim(layer), ExtractionMethod.TextLayer, "pdftotext");
            }

            var builder = new StringBuilder();
            foreach (var page in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var image = File.OpenRead(page);
                var recognised = await ocr.RecogniseAsync(image, "image/png", cancellationToken);
                if (recognised.Length == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine().AppendLine();
                }

                builder.Append(recognised);
            }

            var ocrText = builder.ToString();
            return CountLetters(ocrText) > CountLetters(layer)
                ? new ExtractedText(Trim(ocrText), ExtractionMethod.Ocr, "tesseract")
                : new ExtractedText(Trim(layer), ExtractionMethod.TextLayer, "pdftotext");
        }
        finally
        {
            try
            {
                work.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task<string> PdfToTextAsync(string pdfPath, CancellationToken cancellationToken)
    {
        var command = _options.Tesseract.PdfToTextCommand;
        if (string.IsNullOrWhiteSpace(command) || !TesseractCliOcrEngine.CommandExists(command))
        {
            return string.Empty;
        }

        var (exit, _, stdout) = await RunCaptureAsync(
            command,
            $"-layout \"{pdfPath}\" -",
            _options.Tesseract.TimeoutSeconds,
            cancellationToken);

        return exit == 0 ? stdout : string.Empty;
    }

    private async Task<IReadOnlyList<string>> RasterisePdfAsync(
        string pdfPath,
        string workDir,
        CancellationToken cancellationToken)
    {
        var command = _options.Tesseract.PdfToPpmCommand;
        if (string.IsNullOrWhiteSpace(command) || !TesseractCliOcrEngine.CommandExists(command))
        {
            logger.LogWarning("pdftoppm is not available; scanned PDFs cannot be OCR'd locally.");
            return [];
        }

        var prefix = Path.Combine(workDir, "page");
        var dpi = Math.Clamp(_options.Tesseract.PdfDpi, 72, 400);
        var (exit, stderr, _) = await RunCaptureAsync(
            command,
            $"-png -r {dpi} -f 1 -l {_options.Tesseract.MaxPdfPages} \"{pdfPath}\" \"{prefix}\"",
            _options.Tesseract.TimeoutSeconds,
            cancellationToken);

        if (exit != 0)
        {
            logger.LogWarning("pdftoppm failed (exit {Exit}): {Error}", exit, stderr.Trim());
            return [];
        }

        return Directory.GetFiles(workDir, "page-*.png").OrderBy(path => path, StringComparer.Ordinal).ToList();
    }

    private static async Task<(int ExitCode, string StdErr, string StdOut)> RunCaptureAsync(
        string fileName,
        string arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            },
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException($"{fileName} exceeded {timeoutSeconds}s.");
        }

        await Task.WhenAll(stdoutTask, stderrTask);
        return (process.ExitCode, stderrTask.Result, stdoutTask.Result);
    }

    private string Trim(string text)
    {
        if (text.Length <= _options.MaxTextChars)
        {
            return text;
        }

        return text[.._options.MaxTextChars];
    }

    private static int CountLetters(string text)
    {
        var count = 0;
        foreach (var character in text)
        {
            if (char.IsLetter(character))
            {
                count++;
            }
        }

        return count;
    }
}
