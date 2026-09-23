using System.Collections.Concurrent;
using System.Text.Json;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.DocumentTypes.Contracts;
using Dms.DocumentTypes.Domain;
using Dms.Identity.Contracts;
using Dms.SharedKernel;

namespace Dms.DocumentTypes.Application;

public interface IDocumentTypeRepository
{
    /// <summary>The aggregate with every version and its schema, for commands.</summary>
    Task<DocumentType?> FindAsync(DocumentTypeId id, CancellationToken cancellationToken);

    Task<DocumentType?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentType>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    /// <summary>One version with its fields, options and rules, read-only.</summary>
    Task<DocumentTypeVersion?> FindVersionAsync(DocumentTypeVersionId versionId, CancellationToken cancellationToken);

    void Add(DocumentType documentType);
}

public sealed record CreateDocumentTypeCommand(string Code, string Name, string? Description)
    : ICommand<Result<Guid>>;

public sealed record UpdateDocumentTypeCommand(
    Guid Id,
    string Name,
    string? Description,
    DocumentTypeSettings Settings,
    bool IsActive = true) : ICommand<Result>;

public sealed record SaveDraftSchemaCommand(
    Guid Id,
    IReadOnlyList<FieldSchema> Fields,
    IReadOnlyList<FieldRuleSchema> Rules) : ICommand<Result>;

public sealed record PublishDocumentTypeVersionCommand(Guid Id) : ICommand<Result<Guid>>;

public sealed record DocumentTypeDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    Guid? LatestPublishedVersionId,
    DocumentTypeSettings Settings);

public sealed record DocumentTypeVersionDto(Guid Id, int VersionNumber, string Status, DateTimeOffset? PublishedAt, int FieldCount);

/// <summary>Everything the schema editor needs in one response.</summary>
public sealed record DocumentTypeAdminDto(
    DocumentTypeDto Type,
    IReadOnlyList<DocumentTypeVersionDto> Versions,
    DocumentTypeSchema? Draft);

public sealed record ListDocumentTypesQuery(bool IncludeInactive = false)
    : IQuery<Result<IReadOnlyList<DocumentTypeDto>>>;

public sealed record GetDocumentTypeAdminQuery(Guid Id) : IQuery<Result<DocumentTypeAdminDto>>;

/// <summary>A published schema by version id, or the latest published one of a type.</summary>
public sealed record GetSchemaQuery(Guid? VersionId, Guid? DocumentTypeId) : IQuery<Result<DocumentTypeSchema>>;

internal static class DocumentTypeErrors
{
    public static readonly Error NotFound =
        Error.NotFound("document_type.not_found", "The document type does not exist.");

    public static Error Forbidden(string explanation) => Error.Forbidden("auth.forbidden", explanation);

    public static DocumentTypeDto ToDto(DocumentType type) => new(
        type.Id.Value,
        type.Code,
        type.Name,
        type.Description,
        type.IsActive,
        type.LatestPublishedVersionId?.Value,
        type.Settings);
}

public sealed class CreateDocumentTypeHandler(
    IDmsAuthorizer authorizer,
    IDocumentTypeRepository repository,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<CreateDocumentTypeCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateDocumentTypeCommand command,
        CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageDocumentTypes, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<Guid>(DocumentTypeErrors.Forbidden(decision.Explanation));
        }

        if (string.IsNullOrWhiteSpace(command.Code) || string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure<Guid>(Error.Validation("document_type.invalid", "A code and a name are required."));
        }

        var code = command.Code.Trim().ToUpperInvariant();
        if (await repository.FindByCodeAsync(code, cancellationToken) is not null)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "document_type.duplicate",
                "A document type with this code already exists."));
        }

        var documentType = DocumentType.Create(code, command.Name, command.Description, timeProvider.GetUtcNow());
        repository.Add(documentType);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentTypeCreated,
                EntityType = "DocumentType",
                EntityId = documentType.Id.Value,
                Metadata = new Dictionary<string, object?> { ["code"] = documentType.Code },
            },
            cancellationToken);

        return Result.Success(documentType.Id.Value);
    }
}

public sealed class UpdateDocumentTypeHandler(
    IDmsAuthorizer authorizer,
    IDocumentTypeRepository repository,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<UpdateDocumentTypeCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateDocumentTypeCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageDocumentTypes, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure(DocumentTypeErrors.Forbidden(decision.Explanation));
        }

        var documentType = await repository.FindAsync(new DocumentTypeId(command.Id), cancellationToken);
        if (documentType is null)
        {
            return Result.Failure(DocumentTypeErrors.NotFound);
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure(Error.Validation("document_type.invalid", "A name is required."));
        }

        var now = timeProvider.GetUtcNow();
        documentType.Update(command.Name, command.Description, command.Settings, now);
        documentType.SetActive(command.IsActive, now);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentTypeUpdated,
                EntityType = "DocumentType",
                EntityId = command.Id,
                Metadata = new Dictionary<string, object?>
                {
                    ["settings"] = command.Settings,
                    ["isActive"] = command.IsActive,
                },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class SaveDraftSchemaHandler(
    IDmsAuthorizer authorizer,
    IDocumentTypeRepository repository,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<SaveDraftSchemaCommand, Result>
{
    public async Task<Result> HandleAsync(SaveDraftSchemaCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageDocumentTypes, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure(DocumentTypeErrors.Forbidden(decision.Explanation));
        }

        var documentType = await repository.FindAsync(new DocumentTypeId(command.Id), cancellationToken);
        if (documentType is null)
        {
            return Result.Failure(DocumentTypeErrors.NotFound);
        }

        var fields = command.Fields ?? [];
        var rules = command.Rules ?? [];

        // Checked on save, not only on publish, so the editor shows problems while they are fresh.
        var valid = SchemaDesignValidator.Validate(fields, rules, documentType.PublishedFieldTypes());
        if (valid.IsFailure)
        {
            return valid;
        }

        var saved = documentType.ReplaceDraftSchema(fields, rules, timeProvider.GetUtcNow());
        if (saved.IsFailure)
        {
            return saved;
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentTypeDraftSaved,
                EntityType = "DocumentType",
                EntityId = command.Id,
                Metadata = new Dictionary<string, object?>
                {
                    ["fields"] = fields.Select(field => field.Code).ToList(),
                    ["rules"] = rules.Count,
                },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class PublishDocumentTypeVersionHandler(
    IDmsAuthorizer authorizer,
    IDocumentTypeRepository repository,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<PublishDocumentTypeVersionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        PublishDocumentTypeVersionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(Error.Unauthorized("auth.unauthenticated", "Not authenticated."));
        }

        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageDocumentTypes, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<Guid>(DocumentTypeErrors.Forbidden(decision.Explanation));
        }

        var documentType = await repository.FindAsync(new DocumentTypeId(command.Id), cancellationToken);
        if (documentType?.Draft is not { } draft)
        {
            return Result.Failure<Guid>(documentType is null
                ? DocumentTypeErrors.NotFound
                : Error.Conflict("document_type.no_draft", "There is no draft version to publish."));
        }

        // Again at publish: another version may have been published since the draft was saved.
        var schema = draft.ToSchema();
        var valid = SchemaDesignValidator.Validate(schema.Fields, schema.Rules, documentType.PublishedFieldTypes());
        if (valid.IsFailure)
        {
            return Result.Failure<Guid>(valid.Error);
        }

        var published = documentType.PublishDraft(actor, timeProvider.GetUtcNow());
        if (published.IsFailure)
        {
            return Result.Failure<Guid>(published.Error);
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentTypePublished,
                EntityType = "DocumentType",
                EntityId = command.Id,
                Metadata = new Dictionary<string, object?>
                {
                    ["versionId"] = published.Value.Value,
                    ["versionNumber"] = schema.VersionNumber,
                    ["fields"] = schema.Fields.Select(field => field.Code).ToList(),
                },
            },
            cancellationToken);

        return Result.Success(published.Value.Value);
    }
}

public sealed class ListDocumentTypesHandler(IDocumentTypeRepository repository, ICurrentUser currentUser)
    : IQueryHandler<ListDocumentTypesQuery, Result<IReadOnlyList<DocumentTypeDto>>>
{
    public async Task<Result<IReadOnlyList<DocumentTypeDto>>> HandleAsync(
        ListDocumentTypesQuery query,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<IReadOnlyList<DocumentTypeDto>>(
                Error.Unauthorized("auth.unauthenticated", "Not authenticated."));
        }

        // The catalogue itself is not sensitive: any signed-in user needs it to file a document.
        var types = await repository.ListAsync(query.IncludeInactive, cancellationToken);
        return Result.Success<IReadOnlyList<DocumentTypeDto>>(types.Select(DocumentTypeErrors.ToDto).ToList());
    }
}

public sealed class GetDocumentTypeAdminHandler(IDmsAuthorizer authorizer, IDocumentTypeRepository repository)
    : IQueryHandler<GetDocumentTypeAdminQuery, Result<DocumentTypeAdminDto>>
{
    public async Task<Result<DocumentTypeAdminDto>> HandleAsync(
        GetDocumentTypeAdminQuery query,
        CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageDocumentTypes, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<DocumentTypeAdminDto>(DocumentTypeErrors.Forbidden(decision.Explanation));
        }

        var type = await repository.FindAsync(new DocumentTypeId(query.Id), cancellationToken);
        if (type is null)
        {
            return Result.Failure<DocumentTypeAdminDto>(DocumentTypeErrors.NotFound);
        }

        return Result.Success(new DocumentTypeAdminDto(
            DocumentTypeErrors.ToDto(type),
            type.Versions
                .OrderByDescending(version => version.VersionNumber)
                .Select(version => new DocumentTypeVersionDto(
                    version.Id.Value,
                    version.VersionNumber,
                    version.Status.ToString(),
                    version.PublishedAt,
                    version.Fields.Count))
                .ToList(),
            type.Draft?.ToSchema()));
    }
}

public sealed class GetSchemaHandler(IDocumentTypeCatalog catalog, ICurrentUser currentUser)
    : IQueryHandler<GetSchemaQuery, Result<DocumentTypeSchema>>
{
    public async Task<Result<DocumentTypeSchema>> HandleAsync(GetSchemaQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<DocumentTypeSchema>(Error.Unauthorized("auth.unauthenticated", "Not authenticated."));
        }

        DocumentTypeVersionId? versionId = query.VersionId is { } id ? new DocumentTypeVersionId(id) : null;
        if (versionId is null && query.DocumentTypeId is { } typeId)
        {
            versionId = (await catalog.FindAsync(new DocumentTypeId(typeId), cancellationToken))?.LatestPublishedVersionId;
        }

        // Published schemas describe forms, not documents; any signed-in user may read them.
        var schema = versionId is { } resolved ? await catalog.GetSchemaAsync(resolved, cancellationToken) : null;
        return schema is null
            ? Result.Failure<DocumentTypeSchema>(Error.NotFound("schema.not_found", "No published schema was found."))
            : Result.Success(schema);
    }
}

/// <summary>
/// Published schemas never change, so once loaded they are kept for the life of the process.
/// Registered as a singleton; the catalog itself stays scoped.
/// </summary>
public sealed class PublishedSchemaCache
{
    private readonly ConcurrentDictionary<DocumentTypeVersionId, DocumentTypeSchema> _schemas = new();

    public bool TryGet(DocumentTypeVersionId id, out DocumentTypeSchema schema) => _schemas.TryGetValue(id, out schema!);

    public void Add(DocumentTypeSchema schema)
    {
        if (schema.IsPublished)
        {
            _schemas.TryAdd(schema.VersionId, schema);
        }
    }
}

/// <summary>What other modules use: schema lookup and authoritative metadata validation.</summary>
public sealed class DocumentTypeCatalog(
    IDocumentTypeRepository repository,
    PublishedSchemaCache cache,
    IUserDirectory users,
    IGroupDirectory groups) : IDocumentTypeCatalog
{
    public async Task<DocumentTypeSummary?> FindAsync(DocumentTypeId id, CancellationToken cancellationToken) =>
        (await repository.FindAsync(id, cancellationToken))?.ToSummary();

    public async Task<Result<DocumentTypeVersionId>> ResolveVersionForNewDocumentAsync(
        DocumentTypeId id,
        CancellationToken cancellationToken)
    {
        var documentType = await repository.FindAsync(id, cancellationToken);
        if (documentType is null)
        {
            return Result.Failure<DocumentTypeVersionId>(DocumentTypeErrors.NotFound);
        }

        if (!documentType.IsActive)
        {
            return Result.Failure<DocumentTypeVersionId>(Error.Validation(
                "document_type.inactive",
                "This document type is no longer available."));
        }

        return documentType.LatestPublishedVersionId is { } versionId
            ? Result.Success(versionId)
            : Result.Failure<DocumentTypeVersionId>(Error.Validation(
                "document_type.not_published",
                "This document type has no published schema version yet."));
    }

    public async Task<DocumentTypeSchema?> GetSchemaAsync(DocumentTypeVersionId versionId, CancellationToken cancellationToken)
    {
        if (cache.TryGet(versionId, out var cached))
        {
            return cached;
        }

        var version = await repository.FindVersionAsync(versionId, cancellationToken);
        if (version is null || version.Status == DocumentTypeVersionStatus.Draft)
        {
            return null;
        }

        var schema = version.ToSchema();
        cache.Add(schema);
        return schema;
    }

    public async Task<Result<ValidatedMetadata>> ValidateMetadataAsync(
        DocumentTypeVersionId versionId,
        JsonElement? metadata,
        CancellationToken cancellationToken)
    {
        var schema = await GetSchemaAsync(versionId, cancellationToken);
        if (schema is null)
        {
            return Result.Failure<ValidatedMetadata>(Error.Validation(
                "document_type.version_not_found",
                "The schema version does not exist or is not published."));
        }

        var outcome = MetadataValidator.Validate(schema, metadata);

        // References to people and groups must point at real, active ones.
        if (outcome.UserReferences.Count > 0)
        {
            var found = await users.FindManyAsync(outcome.UserReferences.Select(id => new UserId(id)).ToList(), cancellationToken);
            var active = found.Where(user => user.IsActive).Select(user => user.Id.Value).ToHashSet();
            MarkMissing(schema, FieldType.User, outcome, active, "کاربر پیدا نشد یا غیرفعال است.");
        }

        if (outcome.GroupReferences.Count > 0)
        {
            var found = await groups.FindManyAsync(outcome.GroupReferences.Select(id => new GroupId(id)).ToList(), cancellationToken);
            var active = found.Where(group => group.IsActive).Select(group => group.Id.Value).ToHashSet();
            MarkMissing(schema, FieldType.Group, outcome, active, "گروه پیدا نشد یا غیرفعال است.");
        }

        if (!outcome.IsValid)
        {
            return Result.Failure<ValidatedMetadata>(Error.ValidationFields(
                "metadata.invalid",
                "Some fields are not valid.",
                outcome.Errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray())));
        }

        return Result.Success(new ValidatedMetadata(
            outcome.Normalized.ToJsonString(),
            outcome.DocumentReferences.ToList()));
    }

    private static void MarkMissing(
        DocumentTypeSchema schema,
        FieldType type,
        MetadataValidationOutcome outcome,
        HashSet<Guid> existing,
        string message)
    {
        foreach (var field in schema.Fields.Where(field => field.Type == type))
        {
            if (outcome.Normalized[field.Code]?.GetValue<string>() is { } text
                && Guid.TryParse(text, out var id)
                && !existing.Contains(id))
            {
                outcome.Add(field.Code, message);
            }
        }
    }
}
