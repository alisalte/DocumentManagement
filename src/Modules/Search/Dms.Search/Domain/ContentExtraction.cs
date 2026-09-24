using Dms.SharedKernel;

namespace Dms.Search.Domain;

public enum ExtractionStatus
{
    Pending,
    Completed,
    Failed,

    /// <summary>No text engine is configured, or the type holds no text (CAD, archives beyond listing).</summary>
    Skipped,
}

public enum ExtractionMethod
{
    None,

    /// <summary>Text taken from the file itself (PDF text layer, Office XML, email body…).</summary>
    TextLayer,

    /// <summary>Text recognised from images of pages (Tesseract, fas + eng).</summary>
    Ocr,
}

/// <summary>
/// The text of one stored file (section 4.9). Keyed by the file, not the version: a metadata-only
/// revision reuses the file and so reuses its text, and a rebuild of the index never re-runs OCR.
/// The text itself is a gzip object in storage; only its id lives here.
/// </summary>
public sealed class ContentExtraction : Entity<Guid>
{
    private ContentExtraction()
    {
    }

    private ContentExtraction(Guid id)
        : base(id)
    {
    }

    public Guid StorageObjectId { get; private set; }

    public ExtractionStatus Status { get; private set; }

    public ExtractionMethod Method { get; private set; }

    public Guid? TextObjectId { get; private set; }

    public int CharCount { get; private set; }

    public string? Engine { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static ContentExtraction Start(Guid storageObjectId, DateTimeOffset now) => new(Guid.CreateVersion7())
    {
        StorageObjectId = storageObjectId,
        Status = ExtractionStatus.Pending,
        CreatedAt = now,
    };

    public void BeginAttempt() => Attempts++;

    public void Complete(ExtractionMethod method, Guid? textObjectId, int charCount, string engine, DateTimeOffset now)
    {
        Status = ExtractionStatus.Completed;
        Method = method;
        TextObjectId = textObjectId;
        CharCount = charCount;
        Engine = engine;
        LastError = null;
        CompletedAt = now;
    }

    public void Skip(string reason, DateTimeOffset now)
    {
        Status = ExtractionStatus.Skipped;
        LastError = reason;
        CompletedAt = now;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        Status = ExtractionStatus.Failed;
        LastError = error.Length > 2000 ? error[..2000] : error;
        CompletedAt = now;
    }
}
