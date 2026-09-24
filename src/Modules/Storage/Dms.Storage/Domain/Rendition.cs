using Dms.SharedKernel;
using Dms.Storage.Contracts;

namespace Dms.Storage.Domain;

public enum RenditionKind
{
    /// <summary>Every page as an image, for the viewer and for printing.</summary>
    Pages,

    /// <summary>A small image of the first page, for lists.</summary>
    Thumbnail,
}

/// <summary>
/// A derived view of one stored file (section 4.6). Made once by the processing job; the source
/// never changes, so neither does a finished rendition.
/// </summary>
public sealed class Rendition : Entity<Guid>
{
    private readonly List<RenditionPage> _pages = [];

    private Rendition()
    {
    }

    private Rendition(Guid id)
        : base(id)
    {
    }

    public StorageObjectId SourceObjectId { get; private set; }

    public RenditionKind Kind { get; private set; }

    public RenditionStatus Status { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<RenditionPage> Pages => _pages;

    public static Rendition Start(StorageObjectId source, RenditionKind kind, DateTimeOffset now) => new(Guid.CreateVersion7())
    {
        SourceObjectId = source,
        Kind = kind,
        Status = RenditionStatus.Pending,
        CreatedAt = now,
    };

    public void AddPage(int number, StorageObjectId objectId) => _pages.Add(new RenditionPage(Id, number, objectId));

    public void Complete(DateTimeOffset now)
    {
        Status = RenditionStatus.Ready;
        CompletedAt = now;
        Error = null;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        Status = RenditionStatus.Failed;
        Error = error.Length > 1000 ? error[..1000] : error;
        CompletedAt = now;
    }

    public void NotSupported(DateTimeOffset now)
    {
        Status = RenditionStatus.NotSupported;
        CompletedAt = now;
    }

    /// <summary>A failed run is retried from scratch: its pages (if any) are dropped.</summary>
    public void Reset()
    {
        _pages.Clear();
        Status = RenditionStatus.Pending;
        Error = null;
        CompletedAt = null;
    }
}

public sealed class RenditionPage
{
    private RenditionPage()
    {
    }

    public RenditionPage(Guid renditionId, int pageNumber, StorageObjectId objectId)
    {
        RenditionId = renditionId;
        PageNumber = pageNumber;
        ObjectId = objectId;
    }

    public Guid RenditionId { get; private set; }

    public int PageNumber { get; private set; }

    public StorageObjectId ObjectId { get; private set; }
}
