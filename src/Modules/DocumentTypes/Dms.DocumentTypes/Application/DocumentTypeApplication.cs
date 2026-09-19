using System.Text.Json;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.DocumentTypes.Contracts;
using Dms.DocumentTypes.Domain;
using Dms.SharedKernel;

namespace Dms.DocumentTypes.Application;

public interface IDocumentTypeRepository
{
    Task<DocumentType?> FindAsync(DocumentTypeId id, CancellationToken cancellationToken);

    Task<DocumentType?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentType>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    Task<DocumentTypeId?> FindTypeOfVersionAsync(
        DocumentTypeVersionId versionId,
        CancellationToken cancellationToken);

    void Add(DocumentType documentType);
}

public sealed record CreateDocumentTypeCommand(string Code, string Name, string? Description)
    : ICommand<Result<Guid>>;

public sealed record UpdateDocumentTypeCommand(
    Guid Id,
    string Name,
    string? Description,
    DocumentTypeSettings Settings) : ICommand<Result>;

public sealed record PublishDocumentTypeVersionCommand(Guid Id) : ICommand<Result<Guid>>;

public sealed record DocumentTypeDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    Guid? LatestPublishedVersionId,
    DocumentTypeSettings Settings);

public sealed record ListDocumentTypesQuery(bool IncludeInactive = false)
    : IQuery<Result<IReadOnlyList<DocumentTypeDto>>>;

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
        var decision = await authorizer.AuthorizeSystemAsync(
            PermissionCodes.AdminManageDocumentTypes,
            cancellationToken);

        if (!decision.Allowed)
        {
            return Result.Failure<Guid>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var code = command.Code.Trim().ToUpperInvariant();
        if (await repository.FindByCodeAsync(code, cancellationToken) is not null)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "document_type.duplicate",
                "A document type with this code already exists."));
        }

        var documentType = DocumentType.Create(
            command.Code,
            command.Name,
            command.Description,
            timeProvider.GetUtcNow());

        repository.Add(documentType);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = "DOCUMENT_TYPE_CREATED",
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
        var decision = await authorizer.AuthorizeSystemAsync(
            PermissionCodes.AdminManageDocumentTypes,
            cancellationToken);

        if (!decision.Allowed)
        {
            return Result.Failure(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var documentType = await repository.FindAsync(new DocumentTypeId(command.Id), cancellationToken);
        if (documentType is null)
        {
            return Result.Failure(Error.NotFound("document_type.not_found", "The document type does not exist."));
        }

        documentType.Update(command.Name, command.Description, command.Settings, timeProvider.GetUtcNow());

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = "DOCUMENT_TYPE_UPDATED",
                EntityType = "DocumentType",
                EntityId = command.Id,
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

        var decision = await authorizer.AuthorizeSystemAsync(
            PermissionCodes.AdminManageDocumentTypes,
            cancellationToken);

        if (!decision.Allowed)
        {
            return Result.Failure<Guid>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var documentType = await repository.FindAsync(new DocumentTypeId(command.Id), cancellationToken);
        if (documentType is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "document_type.not_found",
                "The document type does not exist."));
        }

        var published = documentType.PublishDraft(actor, timeProvider.GetUtcNow());
        if (published.IsFailure)
        {
            return Result.Failure<Guid>(published.Error);
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = "DOCUMENT_TYPE_PUBLISHED",
                EntityType = "DocumentType",
                EntityId = command.Id,
                Metadata = new Dictionary<string, object?> { ["versionId"] = published.Value.Value },
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
        IReadOnlyList<DocumentTypeDto> result = types
            .Select(type => new DocumentTypeDto(
                type.Id.Value,
                type.Code,
                type.Name,
                type.Description,
                type.IsActive,
                type.LatestPublishedVersionId?.Value,
                type.Settings))
            .ToList();

        return Result.Success(result);
    }
}

/// <summary>
/// Phase 2 implementation of the catalogue other modules use. Metadata validation is a placeholder
/// until field definitions exist in phase 3: it only rejects input that no schema could accept.
/// </summary>
public sealed class DocumentTypeCatalog(IDocumentTypeRepository repository) : IDocumentTypeCatalog
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
            return Result.Failure<DocumentTypeVersionId>(Error.NotFound(
                "document_type.not_found",
                "The document type does not exist."));
        }

        if (!documentType.IsActive)
        {
            return Result.Failure<DocumentTypeVersionId>(Error.Validation(
                "document_type.inactive",
                "This document type is no longer available."));
        }

        if (documentType.LatestPublishedVersionId is not { } versionId)
        {
            return Result.Failure<DocumentTypeVersionId>(Error.Validation(
                "document_type.not_published",
                "This document type has no published schema version yet."));
        }

        return Result.Success(versionId);
    }

    public async Task<Result> ValidateMetadataAsync(
        DocumentTypeVersionId versionId,
        string metadataJson,
        CancellationToken cancellationToken)
    {
        if (await repository.FindTypeOfVersionAsync(versionId, cancellationToken) is null)
        {
            return Result.Failure(Error.Validation(
                "document_type.version_not_found",
                "The schema version does not exist."));
        }

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return Result.Success();
        }

        try
        {
            using var parsed = JsonDocument.Parse(metadataJson);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Result.Failure(Error.Validation(
                    "metadata.not_an_object",
                    "Document metadata must be a JSON object."));
            }
        }
        catch (JsonException)
        {
            return Result.Failure(Error.Validation("metadata.invalid_json", "Document metadata is not valid JSON."));
        }

        return Result.Success();
    }
}
