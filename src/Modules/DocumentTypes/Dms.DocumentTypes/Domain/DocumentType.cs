using Dms.DocumentTypes.Contracts;
using Dms.SharedKernel;

namespace Dms.DocumentTypes.Domain;

public enum DocumentTypeVersionStatus
{
    Draft,
    Published,
    Retired,
}

/// <summary>
/// A kind of document (Contract, Drawing, Invoice…). The schema itself is versioned, so a document
/// created last year stays interpretable under the schema it was created with. Phase 2 has the
/// versioning skeleton; phase 3 fills versions with field definitions and rules.
/// </summary>
public sealed class DocumentType : AggregateRoot<DocumentTypeId>
{
    private readonly List<DocumentTypeVersion> _versions = [];

    private DocumentType()
    {
    }

    private DocumentType(DocumentTypeId id, string code, string name, string? description, DateTimeOffset now)
        : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Immutable once created: documents and search mappings key off it.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid? DefaultCategoryId { get; private set; }

    public DocumentTypeSettings Settings { get; private set; } = new();

    public bool IsActive { get; private set; }

    public DocumentTypeVersionId? LatestPublishedVersionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<DocumentTypeVersion> Versions => _versions;

    public static DocumentType Create(string code, string name, string? description, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var documentType = new DocumentType(
            DocumentTypeId.New(),
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            description,
            now);

        // A type is useless without a schema version, so it starts with a draft.
        documentType._versions.Add(DocumentTypeVersion.CreateDraft(documentType.Id, 1, now));
        return documentType;
    }

    public void Update(string name, string? description, DocumentTypeSettings settings, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description;
        Settings = settings;
        UpdatedAt = now;
    }

    public void SetDefaultCategory(Guid? categoryId, DateTimeOffset now)
    {
        DefaultCategoryId = categoryId;
        UpdatedAt = now;
    }

    public void SetActive(bool isActive, DateTimeOffset now)
    {
        IsActive = isActive;
        UpdatedAt = now;
    }

    public DocumentTypeVersion? Draft =>
        _versions.FirstOrDefault(version => version.Status == DocumentTypeVersionStatus.Draft);

    public Result<DocumentTypeVersionId> PublishDraft(UserId publishedBy, DateTimeOffset now)
    {
        var draft = Draft;
        if (draft is null)
        {
            return Result.Failure<DocumentTypeVersionId>(Error.Conflict(
                "document_type.no_draft",
                "There is no draft version to publish."));
        }

        draft.Publish(publishedBy, now);
        LatestPublishedVersionId = draft.Id;
        UpdatedAt = now;

        // The next round of changes starts from a fresh draft; published schemas never change.
        _versions.Add(DocumentTypeVersion.CreateDraft(Id, draft.VersionNumber + 1, now));
        return Result.Success(draft.Id);
    }

    public DocumentTypeSummary ToSummary() =>
        new(Id, Code, Name, IsActive, LatestPublishedVersionId, Settings);
}

/// <summary>One immutable schema version. Published versions never change.</summary>
public sealed class DocumentTypeVersion : Entity<DocumentTypeVersionId>
{
    private DocumentTypeVersion()
    {
    }

    private DocumentTypeVersion(
        DocumentTypeVersionId id,
        DocumentTypeId documentTypeId,
        int versionNumber,
        DateTimeOffset now)
        : base(id)
    {
        DocumentTypeId = documentTypeId;
        VersionNumber = versionNumber;
        Status = DocumentTypeVersionStatus.Draft;
        CreatedAt = now;
    }

    public DocumentTypeId DocumentTypeId { get; private set; }

    public int VersionNumber { get; private set; }

    public DocumentTypeVersionStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public UserId? PublishedBy { get; private set; }

    internal static DocumentTypeVersion CreateDraft(
        DocumentTypeId documentTypeId,
        int versionNumber,
        DateTimeOffset now) =>
        new(DocumentTypeVersionId.New(), documentTypeId, versionNumber, now);

    internal void Publish(UserId publishedBy, DateTimeOffset now)
    {
        Status = DocumentTypeVersionStatus.Published;
        PublishedBy = publishedBy;
        PublishedAt = now;
    }
}
