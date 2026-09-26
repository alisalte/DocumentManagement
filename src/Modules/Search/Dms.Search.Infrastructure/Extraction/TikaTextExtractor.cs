using System.Net.Http.Headers;
using System.Text;
using Dms.Search.Application;
using Dms.Search.Domain;
using Microsoft.Extensions.Options;

namespace Dms.Search.Infrastructure.Extraction;

/// <summary>
/// Text through Apache Tika Server, with Tesseract behind it for scans (section 8.1, steps 2
/// and 3). A PDF is read for its text layer first; only when that is empty or too thin to be real
/// text is it sent again for OCR, which is slow. Images always go to OCR. When Tika is not
/// configured, <see cref="LocalTextExtractor"/> + <see cref="TesseractCliOcrEngine"/> take over.
/// </summary>
public sealed class TikaTextExtractor(HttpClient http, IOptions<SearchOptions> options) : ITextExtractor
{
    /// <summary>Fewer letters than this in a PDF's text layer means it is a scan (or a cover sheet on one).</summary>
    private const int ThinTextLayer = 64;

    /// <summary>Nothing to read in these; they are download-only.</summary>
    private static readonly string[] NoText = ["video/", "audio/", "application/octet-stream", "application/x-dwg", "image/vnd.dwg"];

    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.Value.TikaUrl);

    public async Task<ExtractedText> ExtractAsync(Stream content, string fileName, string mimeType, CancellationToken cancellationToken)
    {
        if (NoText.Any(prefix => mimeType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return new ExtractedText(string.Empty, ExtractionMethod.None, "none");
        }

        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return new ExtractedText(await CallAsync(content, mimeType, Mode.Ocr, cancellationToken), ExtractionMethod.Ocr, "tika+tesseract");
        }

        if (mimeType != "application/pdf")
        {
            return new ExtractedText(await CallAsync(content, mimeType, Mode.TextOnly, cancellationToken), ExtractionMethod.TextLayer, "tika");
        }

        // The PDF may be needed twice, and storage streams cannot rewind. Each request disposes
        // the stream it sends, so each one opens the temp file afresh.
        var path = Path.Combine(Path.GetTempPath(), $"dms-extract-{Guid.NewGuid():N}.pdf");
        try
        {
            await using (var file = File.Create(path))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            var text = await CallAsync(File.OpenRead(path), mimeType, Mode.TextOnly, cancellationToken);
            if (CountLetters(text) >= ThinTextLayer)
            {
                return new ExtractedText(text, ExtractionMethod.TextLayer, "tika");
            }

            var recognised = await CallAsync(File.OpenRead(path), mimeType, Mode.Ocr, cancellationToken);
            return CountLetters(recognised) > CountLetters(text)
                ? new ExtractedText(recognised, ExtractionMethod.Ocr, "tika+tesseract")
                : new ExtractedText(text, ExtractionMethod.TextLayer, "tika");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private enum Mode
    {
        TextOnly,
        Ocr,
    }

    private async Task<string> CallAsync(Stream content, string mimeType, Mode mode, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(new Uri(options.Value.TikaUrl!.TrimEnd('/') + "/"), "tika"));
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        request.Headers.AcceptCharset.Add(new StringWithQualityHeaderValue("utf-8"));

        if (mode == Mode.Ocr)
        {
            request.Headers.Add("X-Tika-OCRLanguage", options.Value.OcrLanguages);
            request.Headers.Add("X-Tika-PDFOcrStrategy", "ocr_only");
        }
        else
        {
            request.Headers.Add("X-Tika-OCRskipOcr", "true");
            request.Headers.Add("X-Tika-PDFOcrStrategy", "no_ocr");
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // 415/422: a type Tika cannot parse or a broken file. Not worth retrying.
            throw new InvalidOperationException($"Tika answered {(int)response.StatusCode} for a {mimeType} file.");
        }

        // Read at most the configured amount: an archive of text files cannot fill memory.
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(body, Encoding.UTF8);
        var buffer = new char[Math.Min(options.Value.MaxTextChars, 16 * 1024 * 1024)];
        var read = await reader.ReadBlockAsync(buffer, cancellationToken);
        return new string(buffer, 0, read);
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

/// <summary>No Tika configured: files are recorded as skipped and only metadata is searchable.</summary>
public sealed class NullTextExtractor : ITextExtractor
{
    public bool IsEnabled => false;

    public Task<ExtractedText> ExtractAsync(Stream content, string fileName, string mimeType, CancellationToken cancellationToken) =>
        Task.FromResult(new ExtractedText(string.Empty, ExtractionMethod.None, "none"));
}
