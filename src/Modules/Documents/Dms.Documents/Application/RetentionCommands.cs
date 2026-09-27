using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.DocumentTypes.Contracts;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Dms.Documents.Application;

public sealed record CreateRetentionPolicyCommand(
    string Code,
    string Name,
    string? Description,
    int RetentionPeriodDays,
    string StartEvent) : ICommand<Result<Guid>>;

public sealed record UpdateRetentionPolicyCommand(
    Guid Id,
    string Name,
    string? Description,
    int RetentionPeriodDays,
    string StartEvent,
    bool IsActive) : ICommand<Result>;

public sealed record AssignRetentionPolicyCommand(Guid RecordId, Guid RetentionPolicyId) : ICommand<Result>;

public sealed record SetRetentionExceptionCommand(Guid RecordId, string Reason) : ICommand<Result>;

public sealed record ClearRetentionExceptionCommand(Guid RecordId) : ICommand<Result>;

public sealed record PlaceLegalHoldCommand(Guid DocumentId, string Reason) : ICommand<Result<Guid>>;

public sealed record ReleaseLegalHoldCommand(Guid HoldId, string? Reason) : ICommand<Result>;

public sealed record RetentionPolicyDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int RetentionPeriodDays,
    string StartEvent,
    int VersionNumber,
    bool IsActive);

public sealed record LegalHoldDto(
    Guid Id,
    Guid DocumentId,
    string Reason,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReleasedAt,
    Guid? ReleasedBy,
    string? ReleaseReason,
    bool IsActive);

public sealed record ListRetentionPoliciesQuery : IQuery<Result<IReadOnlyList<RetentionPolicyDto>>>;

public sealed record ListLegalHoldsQuery(Guid DocumentId, bool IncludeReleased = false)
    : IQuery<Result<IReadOnlyList<LegalHoldDto>>>;

public interface IRetentionPolicyRepository
{
    Task<RetentionPolicy?> FindAsync(RetentionPolicyId id, CancellationToken cancellationToken);

    Task<RetentionPolicy?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<RetentionPolicy>> ListAsync(CancellationToken cancellationToken);

    void Add(RetentionPolicy policy);
}

public interface ILegalHoldRepository
{
    Task<LegalHold?> FindAsync(LegalHoldId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LegalHold>> ListForDocumentAsync(
        DocumentId documentId,
        bool includeReleased,
        CancellationToken cancellationToken);

    Task<bool> HasActiveHoldAsync(DocumentId documentId, CancellationToken cancellationToken);

    void Add(LegalHold hold);
}

public sealed class LegalHoldGuard(ILegalHoldRepository holds) : ILegalHoldGuard
{
    public async Task<Result> EnsureNotOnHoldAsync(DocumentId documentId, CancellationToken cancellationToken) =>
        RetentionGate.EnsureNotOnHold(await holds.HasActiveHoldAsync(documentId, cancellationToken));

    public Task<bool> IsOnHoldAsync(DocumentId documentId, CancellationToken cancellationToken) =>
        holds.HasActiveHoldAsync(documentId, cancellationToken);
}

public sealed class CreateRetentionPolicyHandler(
    IDmsAuthorizer authorizer,
    IRetentionPolicyRepository policies,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<CreateRetentionPolicyCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateRetentionPolicyCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRecords, cancellationToken)).Allowed)
        {
            return Result.Failure<Guid>(DocumentErrors.Forbidden("Managing retention requires ADMIN_MANAGE_RECORDS."));
        }

        if (!Enum.TryParse<RetentionStartEvent>(command.StartEvent, ignoreCase: true, out var startEvent))
        {
            return Result.Failure<Guid>(Error.Validation("retention.start_event", "Unknown retention start event."));
        }

        if (await policies.FindByCodeAsync(command.Code.Trim().ToUpperInvariant(), cancellationToken) is not null)
        {
            return Result.Failure<Guid>(Error.Conflict("retention.code_taken", "A retention policy with this code already exists."));
        }

        var created = RetentionPolicy.Create(
            command.Code,
            command.Name,
            command.Description,
            command.RetentionPeriodDays,
            startEvent,
            actor,
            timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        policies.Add(created.Value);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RetentionPolicyCreated,
                EntityType = "RetentionPolicy",
                EntityId = created.Value.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["code"] = created.Value.Code,
                    ["days"] = created.Value.RetentionPeriodDays,
                    ["version"] = created.Value.VersionNumber,
                },
            },
            cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }
}

public sealed class UpdateRetentionPolicyHandler(
    IDmsAuthorizer authorizer,
    IRetentionPolicyRepository policies,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<UpdateRetentionPolicyCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateRetentionPolicyCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRecords, cancellationToken)).Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden("Managing retention requires ADMIN_MANAGE_RECORDS."));
        }

        if (!Enum.TryParse<RetentionStartEvent>(command.StartEvent, ignoreCase: true, out var startEvent))
        {
            return Result.Failure(Error.Validation("retention.start_event", "Unknown retention start event."));
        }

        var policy = await policies.FindAsync(new RetentionPolicyId(command.Id), cancellationToken);
        if (policy is null)
        {
            return Result.Failure(Error.NotFound("retention.not_found", "The retention policy does not exist."));
        }

        var beforeVersion = policy.VersionNumber;
        var updated = policy.Update(
            command.Name,
            command.Description,
            command.RetentionPeriodDays,
            startEvent,
            command.IsActive,
            timeProvider.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated;
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RetentionPolicyUpdated,
                EntityType = "RetentionPolicy",
                EntityId = policy.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["version"] = policy.VersionNumber,
                    ["versionBumped"] = policy.VersionNumber != beforeVersion,
                    ["days"] = policy.RetentionPeriodDays,
                    ["actor"] = actor.Value,
                },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class AssignRetentionPolicyHandler(
    IDmsAuthorizer authorizer,
    DocumentAccess access,
    IRecordRepository records,
    IRetentionPolicyRepository policies,
    IDocumentRepository documents,
    IAuditWriter audit,
    DocumentChanges changes,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<AssignRetentionPolicyCommand, Result>
{
    public async Task<Result> HandleAsync(AssignRetentionPolicyCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        var record = await records.FindAsync(new RecordId(command.RecordId), cancellationToken);
        if (record is null)
        {
            return Result.Failure(Error.NotFound("record.not_found", "The Record does not exist."));
        }

        var canManage = (await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRecords, cancellationToken)).Allowed;
        if (!canManage)
        {
            var onDoc = await access.RequireAsync(record.DocumentId.Value, PermissionCodes.RecordDeclare, cancellationToken);
            if (onDoc.IsFailure)
            {
                return onDoc;
            }
        }

        var policy = await policies.FindAsync(new RetentionPolicyId(command.RetentionPolicyId), cancellationToken);
        if (policy is null)
        {
            return Result.Failure(Error.NotFound("retention.not_found", "The retention policy does not exist."));
        }

        var document = await documents.FindAsync(record.DocumentId, cancellationToken)
            ?? await documents.FindIncludingDeletedAsync(record.DocumentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure(DocumentErrors.DocumentNotFound);
        }

        var applied = record.ApplyRetentionPolicy(policy, document.CreatedAt, actor, timeProvider.GetUtcNow());
        if (applied.IsFailure)
        {
            return applied;
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RetentionAssigned,
                EntityType = "Record",
                EntityId = record.Id.Value,
                DocumentId = record.DocumentId.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["policyId"] = policy.Id.Value,
                    ["policyCode"] = policy.Code,
                    ["policyVersion"] = policy.VersionNumber,
                    ["expiresAt"] = record.RetentionExpiresAt,
                    ["status"] = record.Status.ToString(),
                },
            },
            cancellationToken);

        await changes.NotifyAsync(record.DocumentId.Value, cancellationToken);
        return Result.Success();
    }
}

public sealed class SetRetentionExceptionHandler(
    IDmsAuthorizer authorizer,
    IRecordRepository records,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<SetRetentionExceptionCommand, Result>
{
    public async Task<Result> HandleAsync(SetRetentionExceptionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRecords, cancellationToken)).Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden("Retention exceptions require ADMIN_MANAGE_RECORDS."));
        }

        var record = await records.FindAsync(new RecordId(command.RecordId), cancellationToken);
        if (record is null)
        {
            return Result.Failure(Error.NotFound("record.not_found", "The Record does not exist."));
        }

        var set = record.SetRetentionException(command.Reason, actor, timeProvider.GetUtcNow());
        if (set.IsFailure)
        {
            return set;
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RetentionExceptionSet,
                EntityType = "Record",
                EntityId = record.Id.Value,
                DocumentId = record.DocumentId.Value,
                Metadata = new Dictionary<string, object?> { ["reason"] = record.RetentionExceptionReason },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class ClearRetentionExceptionHandler(
    IDmsAuthorizer authorizer,
    IRecordRepository records,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<ClearRetentionExceptionCommand, Result>
{
    public async Task<Result> HandleAsync(ClearRetentionExceptionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRecords, cancellationToken)).Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden("Retention exceptions require ADMIN_MANAGE_RECORDS."));
        }

        var record = await records.FindAsync(new RecordId(command.RecordId), cancellationToken);
        if (record is null)
        {
            return Result.Failure(Error.NotFound("record.not_found", "The Record does not exist."));
        }

        record.ClearRetentionException(actor, timeProvider.GetUtcNow());
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RetentionExceptionCleared,
                EntityType = "Record",
                EntityId = record.Id.Value,
                DocumentId = record.DocumentId.Value,
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class PlaceLegalHoldHandler(
    IDmsAuthorizer authorizer,
    DocumentAccess access,
    IDocumentRepository documents,
    ILegalHoldRepository holds,
    IDocumentTypeCatalog documentTypes,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<PlaceLegalHoldCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(PlaceLegalHoldCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(DocumentErrors.Unauthenticated);
        }

        var canManage = (await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageLegalHold, cancellationToken)).Allowed;
        if (!canManage)
        {
            var onDoc = await access.RequireAsync(command.DocumentId, PermissionCodes.DocumentManagePermission, cancellationToken);
            if (onDoc.IsFailure)
            {
                return Result.Failure<Guid>(onDoc.Error);
            }
        }

        var document = await documents.FindIncludingDeletedAsync(new DocumentId(command.DocumentId), cancellationToken);
        if (document is null)
        {
            return Result.Failure<Guid>(DocumentErrors.DocumentNotFound);
        }

        var type = await documentTypes.FindAsync(document.DocumentTypeId, cancellationToken);
        if (type is { Settings.SupportsLegalHold: false })
        {
            return Result.Failure<Guid>(Error.Conflict(
                "legal_hold.unsupported",
                "This document type does not support Legal Hold."));
        }

        var placed = LegalHold.Place(document.Id, command.Reason, actor, timeProvider.GetUtcNow());
        if (placed.IsFailure)
        {
            return Result.Failure<Guid>(placed.Error);
        }

        holds.Add(placed.Value);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.LegalHoldPlaced,
                EntityType = "LegalHold",
                EntityId = placed.Value.Id.Value,
                DocumentId = document.Id.Value,
                Metadata = new Dictionary<string, object?> { ["reason"] = placed.Value.Reason },
            },
            cancellationToken);

        return Result.Success(placed.Value.Id.Value);
    }
}

public sealed class ReleaseLegalHoldHandler(
    IDmsAuthorizer authorizer,
    ILegalHoldRepository holds,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<ReleaseLegalHoldCommand, Result>
{
    public async Task<Result> HandleAsync(ReleaseLegalHoldCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageLegalHold, cancellationToken)).Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden("Releasing a Legal Hold requires ADMIN_MANAGE_LEGAL_HOLD."));
        }

        var hold = await holds.FindAsync(new LegalHoldId(command.HoldId), cancellationToken);
        if (hold is null)
        {
            return Result.Failure(Error.NotFound("legal_hold.not_found", "The Legal Hold does not exist."));
        }

        hold.Release(command.Reason, actor, timeProvider.GetUtcNow());
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.LegalHoldReleased,
                EntityType = "LegalHold",
                EntityId = hold.Id.Value,
                DocumentId = hold.DocumentId.Value,
                Metadata = new Dictionary<string, object?> { ["releaseReason"] = hold.ReleaseReason },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class ListRetentionPoliciesHandler(
    IRetentionPolicyRepository policies,
    ICurrentUser currentUser) : IQueryHandler<ListRetentionPoliciesQuery, Result<IReadOnlyList<RetentionPolicyDto>>>
{
    public async Task<Result<IReadOnlyList<RetentionPolicyDto>>> HandleAsync(
        ListRetentionPoliciesQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<IReadOnlyList<RetentionPolicyDto>>(DocumentErrors.Unauthenticated);
        }

        var list = await policies.ListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<RetentionPolicyDto>>(list
            .OrderBy(item => item.Code)
            .Select(item => new RetentionPolicyDto(
                item.Id.Value,
                item.Code,
                item.Name,
                item.Description,
                item.RetentionPeriodDays,
                item.StartEvent.ToString(),
                item.VersionNumber,
                item.IsActive))
            .ToList());
    }
}

public sealed class ListLegalHoldsHandler(
    DocumentAccess access,
    ILegalHoldRepository holds,
    ICurrentUser currentUser) : IQueryHandler<ListLegalHoldsQuery, Result<IReadOnlyList<LegalHoldDto>>>
{
    public async Task<Result<IReadOnlyList<LegalHoldDto>>> HandleAsync(
        ListLegalHoldsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<IReadOnlyList<LegalHoldDto>>(DocumentErrors.Unauthenticated);
        }

        var visible = await access.RequireAsync(query.DocumentId, PermissionCodes.DocumentView, cancellationToken);
        if (visible.IsFailure)
        {
            return Result.Failure<IReadOnlyList<LegalHoldDto>>(visible.Error);
        }

        var list = await holds.ListForDocumentAsync(
            new DocumentId(query.DocumentId),
            query.IncludeReleased,
            cancellationToken);

        return Result.Success<IReadOnlyList<LegalHoldDto>>(list
            .Select(item => new LegalHoldDto(
                item.Id.Value,
                item.DocumentId.Value,
                item.Reason,
                item.CreatedBy.Value,
                item.CreatedAt,
                item.ReleasedAt,
                item.ReleasedBy?.Value,
                item.ReleaseReason,
                item.IsActive))
            .ToList());
    }
}

/// <summary>
/// Phase 10.2 worker: advances retention clocks. Never destroys Records — only Expired / PendingDisposal.
/// </summary>
public sealed class RetentionAdvanceJob(
    IRecordRepository records,
    IAuditWriter audit,
    TimeProvider timeProvider,
    ILogger<RetentionAdvanceJob> logger) : IJobHandler
{
    public const string Type = "documents.retention-advance";

    private static readonly UserId SystemActor = new(Guid.Parse("00000000-0000-7000-8000-000000000001"));

    public string JobType => Type;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var candidates = await records.ListRetentionCandidatesAsync(cancellationToken);

        var advanced = 0;
        foreach (var record in candidates)
        {
            var before = record.Status;
            var result = record.AdvanceRetentionClock(now, SystemActor);
            if (result.IsFailure || record.Status == before)
            {
                continue;
            }

            advanced++;
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.RecordStatusChanged,
                    ActorType = AuditActorType.System,
                    EntityType = "Record",
                    EntityId = record.Id.Value,
                    DocumentId = record.DocumentId.Value,
                    Metadata = new Dictionary<string, object?>
                    {
                        ["from"] = before.ToString(),
                        ["to"] = record.Status.ToString(),
                        ["reason"] = record.LastTransitionReason,
                        ["source"] = Type,
                    },
                },
                cancellationToken);
        }

        if (advanced > 0)
        {
            logger.LogInformation("Retention advance moved {Count} Record(s).", advanced);
        }
    }
}
