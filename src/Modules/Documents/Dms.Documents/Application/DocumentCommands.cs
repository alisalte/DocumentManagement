using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.DocumentTypes.Contracts;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;
using Dms.Storage.Contracts;

namespace Dms.Documents.Application;

public sealed record RegisterUploadCommand(WrittenFile File) : ICommand<Result<UploadResultDto>>;

public sealed record UploadResultDto(
    Guid UploadId,
    string FileName,
    string MimeType,
    long Size,
    string Sha256,
    IReadOnlyList<DuplicateDto> Duplicates);

/// <summary>"This exact file already exists as …", limited to documents the caller can see (section 7.5).</summary>
public sealed record DuplicateDto(Guid DocumentId, string Title);

public sealed record CreateDocumentCommand(
    string Title,
    string? Description,
    Guid CategoryId,
    Guid DocumentTypeId,
    Guid UploadId,
    IReadOnlyList<string>? Tags,
    string? ChangeDescription,
    string? IdempotencyKey = null) : ICommand<Result<CreatedVersionDto>>;

public sealed record AddVersionCommand(
    Guid DocumentId,
    Guid UploadId,
    string? ChangeDescription,
    Guid? BaseVersionId,
    string? IdempotencyKey = null) : ICommand<Result<CreatedVersionDto>>;

public sealed record CreatedVersionDto(Guid DocumentId, Guid VersionId, string Label);

public sealed record UpdateDocumentCommand(Guid Id, string Title, string? Description, Guid CategoryId)
    : ICommand<Result>;

public sealed record SetDocumentTagsCommand(Guid Id, IReadOnlyList<string> Tags) : ICommand<Result>;

public sealed record DeleteDocumentCommand(Guid Id, string? Reason) : ICommand<Result>;

public sealed record RestoreDocumentCommand(Guid Id) : ICommand<Result>;

public sealed record PurgeDocumentCommand(Guid Id, string Reason) : ICommand<Result>;

/// <summary>
/// Opens the bytes of one version for download. A command rather than a query on purpose: the
/// DOCUMENT_DOWNLOADED audit row must be committed before the first byte leaves (fail closed,
/// section 4.9), and only commands run in a transaction.
/// </summary>
public sealed record OpenContentCommand(Guid DocumentId, Guid? VersionId) : ICommand<Result<StoredContent>>;

internal static class DocumentRules
{
    public const int MaxTitleLength = 500;
    public const int MaxDescriptionLength = 4000;
    public const int MaxTags = 20;
    public const int MaxTagLength = 64;

    /// <summary>
    /// What an owner receives when a document is created (section 5.5): explicit, visible ACL rows,
    /// never hidden implicit rights. Phase 3 moves this onto the document type's permission policy.
    /// </summary>
    public static readonly IReadOnlyList<string> OwnerDefaults =
    [
        PermissionCodes.DocumentView,
        PermissionCodes.DocumentDownload,
        PermissionCodes.DocumentEdit,
        PermissionCodes.DocumentCreateVersion,
    ];

    public static Error? ValidateDetails(string? title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation("document.title_required", "A title is required.");
        }

        if (title.Trim().Length > MaxTitleLength)
        {
            return Error.Validation("document.title_too_long", $"The title is limited to {MaxTitleLength} characters.");
        }

        return description is { Length: > MaxDescriptionLength }
            ? Error.Validation(
                "document.description_too_long",
                $"The description is limited to {MaxDescriptionLength} characters.")
            : null;
    }

    public static string RequestHash(object request)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(request);
        return Convert.ToHexStringLower(SHA256.HashData(json));
    }

    public static readonly Error IdempotencyMismatch = Error.Conflict(
        "idempotency.key_reused",
        "This Idempotency-Key was already used for a different request.");
}

/// <summary>
/// Checks an uploaded file against the target document type before it is attached. Shared by
/// "create document" and "add version" so both apply exactly the same limits.
/// </summary>
public sealed class UploadAttachment(IStorageService storage, IDocumentTypeCatalog documentTypes, ICurrentUser currentUser)
{
    public async Task<Result<StorageObjectInfo>> ResolveAsync(
        Guid uploadId,
        DocumentTypeSummary documentType,
        CancellationToken cancellationToken)
    {
        var upload = await storage.FindAsync(new StorageObjectId(uploadId), cancellationToken);

        // Only the uploader may attach their own staged upload; anything else looks like it does
        // not exist, so upload ids cannot be probed or borrowed.
        if (upload is null || upload.Status != StorageObjectStatus.Staged || upload.CreatedBy != currentUser.UserId)
        {
            return Result.Failure<StorageObjectInfo>(Error.NotFound(
                "upload.not_found",
                "The uploaded file does not exist or was already used."));
        }

        var settings = documentType.Settings;
        if (settings.MaxUploadBytes is { } maxBytes && upload.Size > maxBytes)
        {
            return Result.Failure<StorageObjectInfo>(Error.Validation(
                "upload.too_large_for_type",
                $"Files of this document type are limited to {maxBytes} bytes."));
        }

        if (settings.AllowedExtensions.Count > 0)
        {
            var extension = Path.GetExtension(upload.FileName).TrimStart('.');
            var allowed = settings.AllowedExtensions.Any(candidate =>
                string.Equals(candidate.TrimStart('.'), extension, StringComparison.OrdinalIgnoreCase));

            if (!allowed)
            {
                return Result.Failure<StorageObjectInfo>(Error.Validation(
                    "upload.extension_not_allowed",
                    $"This document type accepts only: {string.Join(", ", settings.AllowedExtensions)}."));
            }
        }

        return Result.Success(upload);
    }

    public async Task<Result<DocumentTypeSummary>> FindTypeAsync(Guid documentTypeId, CancellationToken cancellationToken)
    {
        var documentType = await documentTypes.FindAsync(new DocumentTypeId(documentTypeId), cancellationToken);
        return documentType is null
            ? Result.Failure<DocumentTypeSummary>(Error.Validation(
                "document_type.not_found",
                "The document type does not exist."))
            : Result.Success(documentType);
    }
}

/// <summary>Turns tag names into tag ids, creating tags that do not exist yet.</summary>
public sealed class TagResolver(ITagRepository tags, TimeProvider timeProvider)
{
    public async Task<Result<IReadOnlyList<TagId>>> ResolveAsync(
        IReadOnlyList<string>? names,
        UserId actor,
        CancellationToken cancellationToken)
    {
        var wanted = (names ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .DistinctBy(TextNormalizer.Normalize)
            .ToList();

        if (wanted.Count > DocumentRules.MaxTags)
        {
            return Result.Failure<IReadOnlyList<TagId>>(Error.Validation(
                "tags.too_many",
                $"A document can have at most {DocumentRules.MaxTags} tags."));
        }

        if (wanted.Any(name => name.Length > DocumentRules.MaxTagLength))
        {
            return Result.Failure<IReadOnlyList<TagId>>(Error.Validation(
                "tags.too_long",
                $"A tag is limited to {DocumentRules.MaxTagLength} characters."));
        }

        var normalized = wanted.Select(TextNormalizer.Normalize).ToList();
        var existing = (await tags.FindByNormalizedAsync(normalized, cancellationToken))
            .ToDictionary(tag => tag.NormalizedName);

        var ids = new List<TagId>(wanted.Count);
        foreach (var name in wanted)
        {
            if (!existing.TryGetValue(TextNormalizer.Normalize(name), out var tag))
            {
                tag = Tag.Create(name, actor, timeProvider.GetUtcNow());
                tags.Add(tag);
                existing[tag.NormalizedName] = tag;
            }

            ids.Add(tag.Id);
        }

        return Result.Success<IReadOnlyList<TagId>>(ids);
    }
}

public sealed class RegisterUploadHandler(
    IStorageService storage,
    IDocumentReadModel readModel,
    DocumentAccess access,
    ICurrentUser currentUser) : ICommandHandler<RegisterUploadCommand, Result<UploadResultDto>>
{
    public async Task<Result<UploadResultDto>> HandleAsync(
        RegisterUploadCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<UploadResultDto>(DocumentErrors.Unauthenticated);
        }

        var staged = await storage.RegisterAsync(command.File, cancellationToken);

        // The duplicate notice must not become an oracle for documents the caller cannot see.
        var duplicates = new List<DuplicateDto>();
        foreach (var candidate in await readModel.FindByFileHashAsync(command.File.Sha256, cancellationToken))
        {
            if (await access.IsAllowedAsync(candidate.DocumentId, PermissionCodes.DocumentView, cancellationToken))
            {
                duplicates.Add(new DuplicateDto(candidate.DocumentId, candidate.Title));
            }
        }

        return Result.Success(new UploadResultDto(
            staged.Id.Value,
            staged.FileName,
            staged.MimeType,
            staged.Size,
            staged.Sha256Hex,
            duplicates));
    }
}

public sealed class CreateDocumentHandler(
    DocumentAccess access,
    ICategoryRepository categories,
    IDocumentRepository documents,
    IDocumentTypeCatalog documentTypes,
    UploadAttachment uploads,
    TagResolver tagResolver,
    IStorageService storage,
    IResourceAclWriter acl,
    IIdempotencyStore idempotency,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<CreateDocumentCommand, Result<CreatedVersionDto>>
{
    public async Task<Result<CreatedVersionDto>> HandleAsync(
        CreateDocumentCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<CreatedVersionDto>(DocumentErrors.Unauthenticated);
        }

        var requestHash = DocumentRules.RequestHash(command with { IdempotencyKey = null });
        if (command.IdempotencyKey is { } key)
        {
            switch (await idempotency.FindAsync(actor, key, requestHash, cancellationToken))
            {
                case IdempotencyLookup.Replay replay:
                    return Result.Success(JsonSerializer.Deserialize<CreatedVersionDto>(replay.ResultJson)!);
                case IdempotencyLookup.Mismatch:
                    return Result.Failure<CreatedVersionDto>(DocumentRules.IdempotencyMismatch);
            }
        }

        if (DocumentRules.ValidateDetails(command.Title, command.Description) is { } invalid)
        {
            return Result.Failure<CreatedVersionDto>(invalid);
        }

        var category = await categories.FindAsync(new CategoryId(command.CategoryId), cancellationToken);
        if (category is null)
        {
            return Result.Failure<CreatedVersionDto>(DocumentErrors.CategoryNotFound);
        }

        var allowed = await access.RequireOnCategoryAsync(
            category.Id.Value,
            PermissionCodes.DocumentCreate,
            cancellationToken);

        if (allowed.IsFailure)
        {
            return Result.Failure<CreatedVersionDto>(allowed.Error);
        }

        if (!category.IsActive)
        {
            return Result.Failure<CreatedVersionDto>(Error.Validation(
                "category.inactive",
                "Documents cannot be filed in an inactive category."));
        }

        var documentType = await uploads.FindTypeAsync(command.DocumentTypeId, cancellationToken);
        if (documentType.IsFailure)
        {
            return Result.Failure<CreatedVersionDto>(documentType.Error);
        }

        var schemaVersion = await documentTypes.ResolveVersionForNewDocumentAsync(
            documentType.Value.Id,
            cancellationToken);

        if (schemaVersion.IsFailure)
        {
            return Result.Failure<CreatedVersionDto>(schemaVersion.Error);
        }

        var metadata = await documentTypes.ValidateMetadataAsync(schemaVersion.Value, "{}", cancellationToken);
        if (metadata.IsFailure)
        {
            return Result.Failure<CreatedVersionDto>(metadata.Error);
        }

        var upload = await uploads.ResolveAsync(command.UploadId, documentType.Value, cancellationToken);
        if (upload.IsFailure)
        {
            return Result.Failure<CreatedVersionDto>(upload.Error);
        }

        var tagIds = await tagResolver.ResolveAsync(command.Tags, actor, cancellationToken);
        if (tagIds.IsFailure)
        {
            return Result.Failure<CreatedVersionDto>(tagIds.Error);
        }

        var now = timeProvider.GetUtcNow();
        var document = Document.Create(
            command.Title,
            string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
            documentType.Value.Id,
            category.Id,
            ownerId: actor,
            createdBy: actor,
            now);

        var file = upload.Value;
        var version = document.AddContentVersion(
            file.Id,
            file.FileName,
            file.MimeType,
            file.Size,
            file.Sha256,
            schemaVersion.Value,
            "{}",
            command.ChangeDescription,
            ApprovalStatus.NotRequired,
            actor,
            now);

        document.SetTags(tagIds.Value, now, actor);
        documents.Add(document);

        var committed = await storage.CommitAsync(file.Id, cancellationToken);
        if (committed.IsFailure)
        {
            return Result.Failure<CreatedVersionDto>(committed.Error);
        }

        await acl.GrantAsync(
            ResourceRef.Document(document.Id.Value),
            SubjectType.User,
            actor.Value,
            DocumentRules.OwnerDefaults,
            "Owner defaults at document creation",
            cancellationToken);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentCreated,
                EntityType = "Document",
                EntityId = document.Id.Value,
                DocumentId = document.Id.Value,
                VersionId = version.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["title"] = document.Title,
                    ["categoryId"] = category.Id.Value,
                    ["documentTypeId"] = documentType.Value.Id.Value,
                },
            },
            cancellationToken);

        await audit.WriteAsync(VersionAudit.Created(version), cancellationToken);

        var result = new CreatedVersionDto(document.Id.Value, version.Id.Value, version.Label);
        if (command.IdempotencyKey is { } newKey)
        {
            idempotency.Record(actor, newKey, requestHash, JsonSerializer.Serialize(result));
        }

        return Result.Success(result);
    }
}

public sealed class AddVersionHandler(
    DocumentAccess access,
    IDocumentRepository documents,
    IDocumentTypeCatalog documentTypes,
    UploadAttachment uploads,
    IStorageService storage,
    IIdempotencyStore idempotency,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<AddVersionCommand, Result<CreatedVersionDto>>
{
    public async Task<Result<CreatedVersionDto>> HandleAsync(
        AddVersionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<CreatedVersionDto>(DocumentErrors.Unauthenticated);
        }

        var requestHash = DocumentRules.RequestHash(command with { IdempotencyKey = null });
        if (command.IdempotencyKey is { } key)
        {
            switch (await idempotency.FindAsync(actor, key, requestHash, cancellationToken))
            {
                case IdempotencyLookup.Replay replay:
                    return Result.Success(JsonSerializer.Deserialize<CreatedVersionDto>(replay.ResultJson)!);
                case IdempotencyLookup.Mismatch:
                    return Result.Failure<CreatedVersionDto>(DocumentRules.IdempotencyMismatch);
            }
        }

        var allowed = await access.RequireAsync(
            command.DocumentId,
            PermissionCodes.DocumentCreateVersion,
            cancellationToken);

        if (allowed.IsFailure)
        {
            return Result.Failure<CreatedVersionDto>(allowed.Error);
        }

        // Row locked from here to commit: concurrent uploads queue up and each gets the next number.
        var document = await documents.FindForUpdateAsync(new DocumentId(command.DocumentId), cancellationToken);
        if (document is null)
        {
            return Result.Failure<CreatedVersionDto>(DocumentErrors.DocumentNotFound);
        }

        // Section 4.11: "someone created V5 since you opened V4".
        if (command.BaseVersionId is { } baseVersion && document.CurrentVersionId?.Value != baseVersion)
        {
            var current = document.Versions.FirstOrDefault(version => version.Id == document.CurrentVersionId);
            return Result.Failure<CreatedVersionDto>(Error.Conflict(
                "version.stale",
                $"A newer version ({current?.Label}) was added after the one you started from."));
        }

        var documentType = await documentTypes.FindAsync(document.DocumentTypeId, cancellationToken);
        if (documentType is null)
        {
            return Result.Failure<CreatedVersionDto>(Error.Validation(
                "document_type.not_found",
                "The document type does not exist."));
        }

        var upload = await uploads.ResolveAsync(command.UploadId, documentType, cancellationToken);
        if (upload.IsFailure)
        {
            return Result.Failure<CreatedVersionDto>(upload.Error);
        }

        // A new file keeps the schema its document was written against. Moving a document to a
        // newer schema is a metadata decision that arrives with the field editor in phase 3.
        var previous = document.Versions.First(version => version.Id == document.CurrentVersionId);

        var file = upload.Value;
        var now = timeProvider.GetUtcNow();
        var version = document.AddContentVersion(
            file.Id,
            file.FileName,
            file.MimeType,
            file.Size,
            file.Sha256,
            previous.DocumentTypeVersionId,
            previous.DynamicData,
            command.ChangeDescription,
            ApprovalStatus.NotRequired,
            actor,
            now);

        var committed = await storage.CommitAsync(file.Id, cancellationToken);
        if (committed.IsFailure)
        {
            return Result.Failure<CreatedVersionDto>(committed.Error);
        }

        await audit.WriteAsync(VersionAudit.Created(version), cancellationToken);

        var result = new CreatedVersionDto(document.Id.Value, version.Id.Value, version.Label);
        if (command.IdempotencyKey is { } newKey)
        {
            idempotency.Record(actor, newKey, requestHash, JsonSerializer.Serialize(result));
        }

        return Result.Success(result);
    }
}

internal static class VersionAudit
{
    public static AuditRecord Created(DocumentVersion version) => new()
    {
        Action = version.ChangeKind == VersionChangeKind.Metadata
            ? AuditActions.RevisionCreated
            : AuditActions.VersionCreated,
        EntityType = "DocumentVersion",
        EntityId = version.Id.Value,
        DocumentId = version.DocumentId.Value,
        VersionId = version.Id.Value,
        Metadata = new Dictionary<string, object?>
        {
            ["versionNumber"] = version.VersionNumber,
            ["revisionNumber"] = version.RevisionNumber,
            ["fileName"] = version.FileName,
            ["size"] = version.FileSize,
            ["sha256"] = Convert.ToHexStringLower(version.Sha256),
            ["storageObjectId"] = version.StorageObjectId.Value,
        },
    };
}

public sealed class UpdateDocumentHandler(
    DocumentAccess access,
    IDocumentRepository documents,
    ICategoryRepository categories,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<UpdateDocumentCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateDocumentCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        var allowed = await access.RequireAsync(command.Id, PermissionCodes.DocumentEdit, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        if (DocumentRules.ValidateDetails(command.Title, command.Description) is { } invalid)
        {
            return Result.Failure(invalid);
        }

        var document = await documents.FindAsync(new DocumentId(command.Id), cancellationToken);
        if (document is null)
        {
            return Result.Failure(DocumentErrors.DocumentNotFound);
        }

        var targetCategory = new CategoryId(command.CategoryId);
        var moved = targetCategory != document.CategoryId;
        if (moved)
        {
            // Filing somewhere else is filing: it needs the same right as creating there, and it
            // changes who inherits access, so it is recorded separately.
            var category = await categories.FindAsync(targetCategory, cancellationToken);
            if (category is null)
            {
                return Result.Failure(DocumentErrors.CategoryNotFound);
            }

            var canFile = await access.RequireOnCategoryAsync(
                category.Id.Value,
                PermissionCodes.DocumentCreate,
                cancellationToken);

            if (canFile.IsFailure)
            {
                return canFile;
            }
        }

        var before = new Dictionary<string, object?>
        {
            ["title"] = document.Title,
            ["description"] = document.Description,
            ["categoryId"] = document.CategoryId.Value,
        };

        var updated = document.UpdateDetails(
            command.Title,
            string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
            targetCategory,
            actor,
            timeProvider.GetUtcNow());

        if (updated.IsFailure)
        {
            return updated;
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentUpdated,
                EntityType = "Document",
                EntityId = document.Id.Value,
                DocumentId = document.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["before"] = before,
                    ["after"] = new Dictionary<string, object?>
                    {
                        ["title"] = document.Title,
                        ["description"] = document.Description,
                        ["categoryId"] = document.CategoryId.Value,
                    },
                    ["moved"] = moved,
                },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class SetDocumentTagsHandler(
    DocumentAccess access,
    IDocumentRepository documents,
    TagResolver tagResolver,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<SetDocumentTagsCommand, Result>
{
    public async Task<Result> HandleAsync(SetDocumentTagsCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        var allowed = await access.RequireAsync(command.Id, PermissionCodes.DocumentEdit, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var document = await documents.FindAsync(new DocumentId(command.Id), cancellationToken);
        if (document is null)
        {
            return Result.Failure(DocumentErrors.DocumentNotFound);
        }

        var tagIds = await tagResolver.ResolveAsync(command.Tags, actor, cancellationToken);
        if (tagIds.IsFailure)
        {
            return tagIds;
        }

        document.SetTags(tagIds.Value, timeProvider.GetUtcNow(), actor);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentTagsChanged,
                EntityType = "Document",
                EntityId = document.Id.Value,
                DocumentId = document.Id.Value,
                Metadata = new Dictionary<string, object?> { ["tags"] = command.Tags },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class DeleteDocumentHandler(
    DocumentAccess access,
    IDocumentRepository documents,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<DeleteDocumentCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteDocumentCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        var allowed = await access.RequireAsync(command.Id, PermissionCodes.DocumentDelete, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var document = await documents.FindAsync(new DocumentId(command.Id), cancellationToken);
        if (document is null)
        {
            return Result.Failure(DocumentErrors.DocumentNotFound);
        }

        // Soft delete never touches storage (section 7.7): restoring is always possible.
        document.SoftDelete(actor, command.Reason?.Trim(), timeProvider.GetUtcNow());

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentDeleted,
                EntityType = "Document",
                EntityId = document.Id.Value,
                DocumentId = document.Id.Value,
                Metadata = new Dictionary<string, object?> { ["reason"] = document.DeleteReason },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class RestoreDocumentHandler(
    DocumentAccess access,
    IDocumentRepository documents,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<RestoreDocumentCommand, Result>
{
    public async Task<Result> HandleAsync(RestoreDocumentCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        var document = await documents.FindIncludingDeletedAsync(new DocumentId(command.Id), cancellationToken);
        if (document is null || !document.IsDeleted)
        {
            return Result.Failure(DocumentErrors.DocumentNotFound);
        }

        var allowed = await access.RequireOnDeletedAsync(command.Id, PermissionCodes.DocumentRestore, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        document.Restore(actor, timeProvider.GetUtcNow());

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentRestored,
                EntityType = "Document",
                EntityId = document.Id.Value,
                DocumentId = document.Id.Value,
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class PurgeDocumentHandler(
    DocumentAccess access,
    IDocumentRepository documents,
    IStorageService storage,
    IAuditWriter audit,
    ICurrentUser currentUser) : ICommandHandler<PurgeDocumentCommand, Result>
{
    public async Task<Result> HandleAsync(PurgeDocumentCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if (!await access.IsSystemAllowedAsync(PermissionCodes.DocumentPurge, cancellationToken))
        {
            return Result.Failure(DocumentErrors.Forbidden("Permanently deleting documents requires DOCUMENT_PURGE."));
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure(Error.Validation("purge.reason_required", "A reason is required to purge a document."));
        }

        var document = await documents.FindIncludingDeletedAsync(new DocumentId(command.Id), cancellationToken);
        if (document is null)
        {
            return Result.Failure(DocumentErrors.DocumentNotFound);
        }

        // Purge is the second step, never the first: the document has to sit in the recycle bin.
        if (!document.IsDeleted)
        {
            return Result.Failure(Error.Conflict(
                "purge.not_deleted",
                "Only documents in the recycle bin can be purged. Delete it first."));
        }

        // Metadata revisions share one stored file, and there is no cross-document deduplication,
        // so every file referenced here belongs to this document alone.
        var files = document.Versions
            .GroupBy(version => version.StorageObjectId)
            .Select(group => group.First())
            .ToList();

        foreach (var file in files)
        {
            await storage.MarkForDeletionAsync(file.StorageObjectId, cancellationToken);
        }

        // The audit row keeps what the tombstones keep: which bytes existed and their hashes.
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentPurged,
                EntityType = "Document",
                EntityId = document.Id.Value,
                DocumentId = document.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["title"] = document.Title,
                    ["reason"] = command.Reason.Trim(),
                    ["versions"] = document.Versions.Count,
                    ["files"] = files.Select(file => new Dictionary<string, object?>
                    {
                        ["storageObjectId"] = file.StorageObjectId.Value,
                        ["sha256"] = Convert.ToHexStringLower(file.Sha256),
                        ["fileName"] = file.FileName,
                    }).ToList(),
                },
            },
            cancellationToken);

        await documents.PurgeAsync(document, cancellationToken);
        return Result.Success();
    }
}

public sealed class OpenContentHandler(
    DocumentAccess access,
    IDocumentRepository documents,
    IStorageService storage,
    IAuditWriter audit) : ICommandHandler<OpenContentCommand, Result<StoredContent>>
{
    public async Task<Result<StoredContent>> HandleAsync(OpenContentCommand command, CancellationToken cancellationToken)
    {
        var visible = await access.RequireAsync(command.DocumentId, PermissionCodes.DocumentView, cancellationToken);
        if (visible.IsFailure)
        {
            return Result.Failure<StoredContent>(visible.Error);
        }

        var document = await documents.FindAsync(new DocumentId(command.DocumentId), cancellationToken);
        if (document is null)
        {
            return Result.Failure<StoredContent>(DocumentErrors.DocumentNotFound);
        }

        // Plain readers get the effective (published) version, decision D7.
        var versionId = command.VersionId
            ?? document.EffectiveVersionId?.Value
            ?? document.CurrentVersionId?.Value;

        if (versionId is not { } resolved)
        {
            return Result.Failure<StoredContent>(DocumentErrors.VersionNotFound);
        }

        var allowed = await access.RequireAsync(
            command.DocumentId,
            PermissionCodes.DocumentDownload,
            resolved,
            cancellationToken);

        if (allowed.IsFailure)
        {
            return Result.Failure<StoredContent>(allowed.Error);
        }

        var version = await documents.FindVersionAsync(new DocumentVersionId(resolved), cancellationToken);
        if (version is null || version.DocumentId != document.Id)
        {
            return Result.Failure<StoredContent>(DocumentErrors.VersionNotFound);
        }

        var content = await storage.OpenAsync(version.StorageObjectId, range: null, cancellationToken);
        if (content.IsFailure)
        {
            return content;
        }

        // Fail closed: this row commits before the endpoint writes a single byte.
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentDownloaded,
                EntityType = "DocumentVersion",
                EntityId = version.Id.Value,
                DocumentId = document.Id.Value,
                VersionId = version.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["label"] = version.Label,
                    ["fileName"] = version.FileName,
                },
            },
            cancellationToken);

        // The stored name of the version, not the storage row's: they only differ if someone
        // renamed the file in a later revision, and the reader asked for this version.
        return Result.Success(content.Value with { FileName = version.FileName, MimeType = version.MimeType });
    }
}
