using Dms.DocumentTypes.Contracts;
using Dms.Documents.Contracts;
using Dms.SharedKernel;
using Dms.Storage.Contracts;

namespace Dms.Documents.Domain;

/// <summary>
/// One immutable (file, metadata) pair, addressed as V{VersionNumber}.{RevisionNumber} (ADR 0001).
///
/// Everything here is set once at creation. The only field that ever changes afterwards is the
/// approval state, which the workflow phase drives; a database trigger rejects an UPDATE of any
/// other column, so immutability does not depend on application discipline alone.
/// </summary>
public sealed class DocumentVersion : Entity<DocumentVersionId>
{
    private DocumentVersion()
    {
    }

    private DocumentVersion(DocumentVersionId id)
        : base(id)
    {
    }

    public DocumentId DocumentId { get; private set; }

    /// <summary>Increments when the file changes.</summary>
    public int VersionNumber { get; private set; }

    /// <summary>Increments when only the metadata changes; the file is reused as is.</summary>
    public int RevisionNumber { get; private set; }

    public StorageObjectId StorageObjectId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string MimeType { get; private set; } = string.Empty;

    public long FileSize { get; private set; }

    public byte[] Sha256 { get; private set; } = [];

    /// <summary>The schema this row's metadata was written against.</summary>
    public DocumentTypeVersionId DocumentTypeVersionId { get; private set; }

    /// <summary>Dynamic metadata snapshot as JSON; empty object until phase 3 adds fields.</summary>
    public string DynamicData { get; private set; } = "{}";

    public VersionChangeKind ChangeKind { get; private set; }

    public string? ChangeDescription { get; private set; }

    public ApprovalStatus ApprovalStatus { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string Label => $"V{VersionNumber}.{RevisionNumber}";

    /// <summary>A published row is visible to anyone with plain VIEW (decision D6).</summary>
    public bool IsPublished => ApprovalStatus is ApprovalStatus.NotRequired or ApprovalStatus.Approved;

    internal static DocumentVersion Create(
        DocumentId documentId,
        int versionNumber,
        int revisionNumber,
        StorageObjectId storageObjectId,
        string fileName,
        string mimeType,
        long fileSize,
        byte[] sha256,
        DocumentTypeVersionId documentTypeVersionId,
        string dynamicData,
        VersionChangeKind changeKind,
        string? changeDescription,
        ApprovalStatus approvalStatus,
        UserId createdBy,
        DateTimeOffset now)
    {
        if (sha256.Length != 32)
        {
            throw new ArgumentException("A SHA-256 digest is 32 bytes.", nameof(sha256));
        }

        return new DocumentVersion(DocumentVersionId.New())
        {
            DocumentId = documentId,
            VersionNumber = versionNumber,
            RevisionNumber = revisionNumber,
            StorageObjectId = storageObjectId,
            FileName = fileName,
            MimeType = mimeType,
            FileSize = fileSize,
            Sha256 = sha256,
            DocumentTypeVersionId = documentTypeVersionId,
            DynamicData = string.IsNullOrWhiteSpace(dynamicData) ? "{}" : dynamicData,
            ChangeKind = changeKind,
            ChangeDescription = changeDescription,
            ApprovalStatus = approvalStatus,
            ApprovedAt = approvalStatus == ApprovalStatus.Approved ? now : null,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }

    /// <summary>Only the workflow phase changes approval state; the file never changes.</summary>
    public void RecordApproval(ApprovalStatus status, DateTimeOffset now)
    {
        ApprovalStatus = status;
        ApprovedAt = status == ApprovalStatus.Approved ? now : null;
    }
}

public sealed class DocumentTag
{
    private DocumentTag()
    {
    }

    public DocumentTag(DocumentId documentId, TagId tagId, UserId addedBy, DateTimeOffset now)
    {
        DocumentId = documentId;
        TagId = tagId;
        AddedBy = addedBy;
        AddedAt = now;
    }

    public DocumentId DocumentId { get; private set; }

    public TagId TagId { get; private set; }

    public UserId AddedBy { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }
}

/// <summary>
/// A free-form label. Tags are shared vocabulary, so they are matched on a normalised form:
/// Persian text arrives with Arabic look-alike letters and Persian digits, and "قرارداد" typed
/// two different ways must be one tag, not two.
/// </summary>
public sealed class Tag : AggregateRoot<TagId>
{
    private Tag()
    {
    }

    private Tag(TagId id, string name, string normalizedName, UserId createdBy, DateTimeOffset now)
        : base(id)
    {
        Name = name;
        NormalizedName = normalizedName;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Tag Create(string name, UserId createdBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Tag(TagId.New(), name.Trim(), TextNormalizer.Normalize(name), createdBy, now);
    }
}

public static class TextNormalizer
{
    /// <summary>
    /// Folds Arabic/Persian look-alikes, strips zero-width joiners and converts Persian and Arabic
    /// digits to ASCII, then lower-cases. Used for tag identity now and for search later.
    /// </summary>
    public static string Normalize(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            switch (character)
            {
                case 'ي' or 'ی':
                    builder.Append('ی');
                    break;
                case 'ك' or 'ک':
                    builder.Append('ک');
                    break;
                case 'ۀ' or 'ة':
                    builder.Append('ه');
                    break;
                case 'أ' or 'إ' or 'آ' or 'ا':
                    builder.Append('ا');
                    break;
                case '‌' or '‍' or '‎' or '‏' or '﻿':
                    break;
                default:
                    if (character is >= '۰' and <= '۹')
                    {
                        builder.Append((char)('0' + (character - '۰')));
                    }
                    else if (character is >= '٠' and <= '٩')
                    {
                        builder.Append((char)('0' + (character - '٠')));
                    }
                    else
                    {
                        builder.Append(char.ToLowerInvariant(character));
                    }

                    break;
            }
        }

        return System.Text.RegularExpressions.Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }
}
