using System.Text.Json.Nodes;
using Dms.Search.Domain;

namespace Dms.Search.Application;

public sealed record ExtractedText(string Text, ExtractionMethod Method, string Engine);

/// <summary>Text from a file (Tika and/or local Tesseract OCR for scans). Infrastructure only.</summary>
public interface ITextExtractor
{
    bool IsEnabled { get; }

    Task<ExtractedText> ExtractAsync(Stream content, string fileName, string mimeType, CancellationToken cancellationToken);
}

/// <summary>
/// OCR of a single raster page or image (section 8.1 / R2). Tika can own OCR when configured;
/// otherwise <c>TesseractCliOcrEngine</c> shells out to the local <c>tesseract</c> binary.
/// </summary>
public interface IOcrEngine
{
    bool IsEnabled { get; }

    /// <summary>Recognise text in an image stream (PNG/JPEG/WebP/TIFF). Languages come from options.</summary>
    Task<string> RecogniseAsync(Stream image, string mimeType, CancellationToken cancellationToken);
}

/// <summary>A search request already reduced to engine terms: the query JSON and where to send it.</summary>
public sealed record EngineHit(string Id, JsonObject Source, JsonObject? Highlight);

public sealed record EngineResult(long Total, IReadOnlyList<EngineHit> Hits, JsonObject? Aggregations);

/// <summary>
/// The search engine (OpenSearch). A read model only (section 2.4): everything in it can be
/// rebuilt from Postgres and the stored text, and it never decides access on its own.
/// </summary>
public interface ISearchEngine
{
    bool IsEnabled { get; }

    /// <summary>Creates the index behind the alias when there is none yet.</summary>
    Task EnsureReadyAsync(CancellationToken cancellationToken);

    /// <summary>Replaces every indexed version of the document with these (none = removed).</summary>
    Task ReplaceDocumentAsync(Guid documentId, IReadOnlyList<JsonObject> versions, string? index, CancellationToken cancellationToken);

    Task<EngineResult> SearchAsync(JsonObject query, CancellationToken cancellationToken);

    /// <summary>A fresh index for a rebuild; the alias keeps serving the old one meanwhile.</summary>
    Task<string> CreateGenerationAsync(CancellationToken cancellationToken);

    /// <summary>Points the alias at the new index and drops the old ones.</summary>
    Task SwapAliasAsync(string index, CancellationToken cancellationToken);

    Task<(string? Index, long Count)> StatusAsync(CancellationToken cancellationToken);
}

public interface IContentExtractionRepository
{
    Task<ContentExtraction?> FindAsync(Guid storageObjectId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, ContentExtraction>> FindManyAsync(IReadOnlyCollection<Guid> storageObjectIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<ExtractionStatus, int>> CountByStatusAsync(CancellationToken cancellationToken);

    /// <summary>Failed extractions, oldest first; with <paramref name="belowAttempts"/>, only those tried fewer times.</summary>
    Task<IReadOnlyList<ContentExtraction>> ListFailedAsync(int? belowAttempts, int limit, CancellationToken cancellationToken);

    void Add(ContentExtraction extraction);
}

public sealed class SearchOptions
{
    public const string SectionName = "Dms:Search";

    /// <summary>OpenSearch base URL; empty switches the engine off (title search in Postgres only).</summary>
    public string? OpenSearchUrl { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>Alias name; generations are "{alias}-v{n}". Tests use a unique one per run.</summary>
    public string IndexAlias { get; set; } = "dms-versions";

    /// <summary>"wait_for" makes writes visible before the call returns; tests need it, production does not.</summary>
    public string Refresh { get; set; } = "false";

    /// <summary>Tika server URL; empty falls back to local Tesseract when available.</summary>
    public string? TikaUrl { get; set; }

    /// <summary>Tesseract languages for scans. Persian first: most scans are Persian with some Latin.</summary>
    public string OcrLanguages { get; set; } = "fas+eng";

    /// <summary>Extracted text beyond this is cut: enough for any real document, not enough for a text bomb.</summary>
    public int MaxTextChars { get; set; } = 5_000_000;

    /// <summary>How much of the text goes into the index; the rest stays in storage.</summary>
    public int MaxIndexedChars { get; set; } = 1_000_000;

    /// <summary>Local CLI OCR used when <see cref="TikaUrl"/> is empty.</summary>
    public TesseractOptions Tesseract { get; set; } = new();
}

public sealed class TesseractOptions
{
    /// <summary>
    /// When true (default), the worker uses <c>tesseract</c> on PATH if Tika is not configured.
    /// Set false to force metadata-only search without OCR.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Executable name or absolute path. Default resolves via PATH.</summary>
    public string Command { get; set; } = "tesseract";

    /// <summary><c>pdftotext</c> for digital PDFs; empty disables the text-layer shortcut.</summary>
    public string PdfToTextCommand { get; set; } = "pdftotext";

    /// <summary><c>pdftoppm</c> to rasterise scanned PDFs before OCR.</summary>
    public string PdfToPpmCommand { get; set; } = "pdftoppm";

    /// <summary>DPI when rasterising PDF pages for OCR.</summary>
    public int PdfDpi { get; set; } = 200;

    /// <summary>Hard cap on pages OCR'd from one PDF (defence against huge scans).</summary>
    public int MaxPdfPages { get; set; } = 50;

    /// <summary>Per-process timeout for tesseract / poppler.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
