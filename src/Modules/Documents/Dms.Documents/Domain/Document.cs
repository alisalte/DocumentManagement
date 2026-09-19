using Dms.DocumentTypes.Contracts;
using Dms.Documents.Contracts;
using Dms.SharedKernel;
using Dms.Storage.Contracts;

namespace Dms.Documents.Domain;

public enum DocumentStatus
{
    Active,
    Archived,
}

/// <summary>
/// The logical document. It owns its versions: a version can only be created through the root,
/// which is what keeps version numbering correct under concurrency (together with the unique
/// index and the optimistic concurrency token on this row).
/// </summary>
public sealed class Document : AggregateRoot<DocumentId>
{
    private readonly List<DocumentVersion> _versions = [];
    private readonly List<DocumentTag> _tags = [];

    private Document()
    {
    }

    private Document(
        DocumentId id,
        string title,
        string? description,
        DocumentTypeId documentTypeId,
        CategoryId categoryId,
        UserId ownerId,
        UserId createdBy,
        DateTimeOffset now)
        : base(id)
    {
        Title = title;
        Description = description;
        DocumentTypeId = documentTypeId;
        CategoryId = categoryId;
        OwnerId = ownerId;
        Status = DocumentStatus.Active;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedBy = createdBy;
        UpdatedAt = now;
    }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DocumentTypeId DocumentTypeId { get; private set; }

    public CategoryId CategoryId { get; private set; }

    public UserId OwnerId { get; private set; }

    /// <summary>The newest row, draft or not.</summary>
    public DocumentVersionId? CurrentVersionId { get; private set; }

    /// <summary>The newest published row: what a plain reader sees (decision D7).</summary>
    public DocumentVersionId? EffectiveVersionId { get; private set; }

    /// <summary>Highest content version number allocated so far.</summary>
    public int LatestVersionNumber { get; private set; }

    public DocumentStatus Status { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public UserId? DeletedBy { get; private set; }

    public string? DeleteReason { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public UserId UpdatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public IReadOnlyList<DocumentVersion> Versions => _versions;

    public IReadOnlyList<DocumentTag> Tags => _tags;

    public static Document Create(
        string title,
        string? description,
        DocumentTypeId documentTypeId,
        CategoryId categoryId,
        UserId ownerId,
        UserId createdBy,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new Document(
            DocumentId.New(),
            title.Trim(),
            description,
            documentTypeId,
            categoryId,
            ownerId,
            createdBy,
            now);
    }

    /// <summary>
    /// Adds a new content version: a new file, numbered V(n+1).1. The previous versions keep their
    /// approval state; nothing is rewritten (decision D7).
    /// </summary>
    public DocumentVersion AddContentVersion(
        StorageObjectId storageObjectId,
        string fileName,
        string mimeType,
        long fileSize,
        byte[] sha256,
        DocumentTypeVersionId documentTypeVersionId,
        string dynamicData,
        string? changeDescription,
        ApprovalStatus approvalStatus,
        UserId createdBy,
        DateTimeOffset now)
    {
        LatestVersionNumber++;
        var version = DocumentVersion.Create(
            Id,
            LatestVersionNumber,
            revisionNumber: 1,
            storageObjectId,
            fileName,
            mimeType,
            fileSize,
            sha256,
            documentTypeVersionId,
            dynamicData,
            LatestVersionNumber == 1 ? VersionChangeKind.Initial : VersionChangeKind.Content,
            changeDescription,
            approvalStatus,
            createdBy,
            now);

        Attach(version, now, createdBy);
        return version;
    }

    /// <summary>
    /// Adds a metadata-only revision: V(n).(r+1) reusing the same stored file, so no bytes are
    /// copied and the SHA-256 is identical by construction (ADR 0001).
    /// </summary>
    public Result<DocumentVersion> AddMetadataRevision(
        DocumentVersion basedOn,
        string dynamicData,
        string? changeDescription,
        ApprovalStatus approvalStatus,
        UserId createdBy,
        DateTimeOffset now)
    {
        if (basedOn.DocumentId != Id)
        {
            return Result.Failure<DocumentVersion>(Error.Validation(
                "version.foreign",
                "The version belongs to another document."));
        }

        var nextRevision = _versions
            .Where(version => version.VersionNumber == basedOn.VersionNumber)
            .Max(version => version.RevisionNumber) + 1;

        var revision = DocumentVersion.Create(
            Id,
            basedOn.VersionNumber,
            nextRevision,
            basedOn.StorageObjectId,
            basedOn.FileName,
            basedOn.MimeType,
            basedOn.FileSize,
            basedOn.Sha256,
            basedOn.DocumentTypeVersionId,
            dynamicData,
            VersionChangeKind.Metadata,
            changeDescription,
            approvalStatus,
            createdBy,
            now);

        Attach(revision, now, createdBy);
        return Result.Success(revision);
    }

    private void Attach(DocumentVersion version, DateTimeOffset now, UserId actor)
    {
        _versions.Add(version);
        CurrentVersionId = version.Id;

        // With no workflow attached, a new row is published immediately. Once a workflow exists
        // (phase 4) the effective pointer only moves when the row is approved.
        if (version.ApprovalStatus is ApprovalStatus.NotRequired or ApprovalStatus.Approved)
        {
            EffectiveVersionId = version.Id;
        }

        UpdatedAt = now;
        UpdatedBy = actor;
    }

    /// <summary>Used by the workflow phase when a version is approved.</summary>
    public void MarkVersionEffective(DocumentVersionId versionId, DateTimeOffset now)
    {
        EffectiveVersionId = versionId;
        UpdatedAt = now;
    }

    public Result UpdateDetails(
        string title,
        string? description,
        CategoryId categoryId,
        UserId actor,
        DateTimeOffset now)
    {
        if (IsDeleted)
        {
            return Result.Failure(Error.Conflict("document.deleted", "A deleted document cannot be edited."));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title.Trim();
        Description = description;
        CategoryId = categoryId;
        UpdatedAt = now;
        UpdatedBy = actor;
        return Result.Success();
    }

    public Result SoftDelete(UserId actor, string? reason, DateTimeOffset now)
    {
        if (IsDeleted)
        {
            return Result.Success();
        }

        DeletedAt = now;
        DeletedBy = actor;
        DeleteReason = reason;
        UpdatedAt = now;
        UpdatedBy = actor;
        return Result.Success();
    }

    public Result Restore(UserId actor, DateTimeOffset now)
    {
        if (!IsDeleted)
        {
            return Result.Success();
        }

        DeletedAt = null;
        DeletedBy = null;
        DeleteReason = null;
        UpdatedAt = now;
        UpdatedBy = actor;
        return Result.Success();
    }

    public void SetTags(IEnumerable<TagId> tagIds, DateTimeOffset now, UserId actor)
    {
        _tags.Clear();
        foreach (var tagId in tagIds.Distinct())
        {
            _tags.Add(new DocumentTag(Id, tagId, actor, now));
        }

        UpdatedAt = now;
        UpdatedBy = actor;
    }

    public DocumentRef ToRef() => new(Id, CategoryId, OwnerId, IsDeleted);
}
