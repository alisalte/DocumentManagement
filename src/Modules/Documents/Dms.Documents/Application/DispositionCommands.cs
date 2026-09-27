using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;
using Dms.Storage.Contracts;

namespace Dms.Documents.Application;

public sealed record RequestDispositionCommand(Guid RecordId, string? Reason) : ICommand<Result<Guid>>;

public sealed record ApproveDispositionCommand(Guid DispositionId, string? Reason) : ICommand<Result>;

public sealed record RejectDispositionCommand(Guid DispositionId, string Reason) : ICommand<Result>;

public sealed record DestroyDispositionCommand(Guid DispositionId, string? Reason) : ICommand<Result<Guid>>;

public sealed record GetDispositionQuery(Guid DispositionId) : IQuery<Result<DispositionDto>>;

public sealed record GetDispositionByRecordQuery(Guid RecordId) : IQuery<Result<DispositionDto>>;

public sealed record ListPendingDisposalQuery : IQuery<Result<IReadOnlyList<PendingDisposalItemDto>>>;

public sealed record GetDestructionCertificateQuery(Guid CertificateId) : IQuery<Result<DestructionCertificateDto>>;

public sealed record GetDestructionCertificateByRecordQuery(Guid RecordId) : IQuery<Result<DestructionCertificateDto>>;

public sealed record DispositionDto(
    Guid Id,
    Guid RecordId,
    Guid DocumentId,
    string Status,
    string? RequestReason,
    Guid RequestedBy,
    DateTimeOffset RequestedAt,
    Guid? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? DecisionReason,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    Guid? DestroyedBy,
    DateTimeOffset? DestroyedAt);

public sealed record PendingDisposalItemDto(
    Guid RecordId,
    Guid DocumentId,
    string Title,
    string Status,
    DateTimeOffset? RetentionExpiresAt,
    Guid? RetentionPolicyId,
    int? RetentionPolicyVersion,
    bool OnLegalHold,
    Guid? ActiveDispositionId,
    string? ActiveDispositionStatus,
    Guid FinalVersionId,
    string FinalVersionLabel);

public sealed record DestructionCertificateDto(
    Guid Id,
    string CertificateNumber,
    Guid DispositionId,
    Guid RecordId,
    Guid DocumentId,
    Guid FinalVersionId,
    string FinalVersionLabel,
    string RecordTitle,
    string ContentSha256,
    Guid? RetentionPolicyId,
    int? RetentionPolicyVersion,
    DateTimeOffset? RetentionExpiresAt,
    DateTimeOffset LegalHoldCheckedAt,
    Guid ApprovedBy,
    DateTimeOffset ApprovedAt,
    Guid DestroyedBy,
    DateTimeOffset DestroyedAt,
    string? Reason,
    string CertificateHash,
    DateTimeOffset CreatedAt);

public interface IDispositionRepository
{
    Task<Disposition?> FindAsync(DispositionId id, CancellationToken cancellationToken);

    Task<Disposition?> FindActiveForRecordAsync(RecordId recordId, CancellationToken cancellationToken);

    Task<Disposition?> FindLatestForRecordAsync(RecordId recordId, CancellationToken cancellationToken);

    void Add(Disposition disposition);
}

public interface IDestructionCertificateRepository
{
    Task<DestructionCertificate?> FindAsync(DestructionCertificateId id, CancellationToken cancellationToken);

    Task<DestructionCertificate?> FindByDispositionAsync(DispositionId dispositionId, CancellationToken cancellationToken);

    Task<DestructionCertificate?> FindByRecordAsync(RecordId recordId, CancellationToken cancellationToken);

    void Add(DestructionCertificate certificate);
}

public interface IDispositionReadModel
{
    Task<DispositionDto?> GetAsync(DispositionId id, CancellationToken cancellationToken);

    Task<DispositionDto?> GetByRecordAsync(RecordId recordId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingDisposalItemDto>> ListPendingDisposalAsync(CancellationToken cancellationToken);

    Task<DestructionCertificateDto?> GetCertificateAsync(DestructionCertificateId id, CancellationToken cancellationToken);

    Task<DestructionCertificateDto?> GetCertificateByRecordAsync(RecordId recordId, CancellationToken cancellationToken);
}

public sealed class RequestDispositionHandler(
    IDmsAuthorizer authorizer,
    IRecordRepository records,
    IDispositionRepository dispositions,
    ILegalHoldGuard legalHolds,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<RequestDispositionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(RequestDispositionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionRequest, cancellationToken)).Allowed)
        {
            return Result.Failure<Guid>(DocumentErrors.Forbidden("Requesting disposition requires DISPOSITION_REQUEST."));
        }

        var record = await records.FindAsync(new RecordId(command.RecordId), cancellationToken);
        if (record is null)
        {
            return Result.Failure<Guid>(Error.NotFound("record.not_found", "The Record does not exist."));
        }

        if (record.Status == RecordStatus.Destroyed)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "disposition.already_destroyed",
                "A destroyed Record cannot enter disposition."));
        }

        var existing = await dispositions.FindActiveForRecordAsync(record.Id, cancellationToken);
        if (existing is not null)
        {
            // Idempotent: return the active disposition.
            return Result.Success(existing.Id.Value);
        }

        var hold = await legalHolds.EnsureNotOnHoldAsync(record.DocumentId, cancellationToken);
        if (hold.IsFailure)
        {
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.DispositionBlockedByLegalHold,
                    Outcome = AuditOutcome.Denied,
                    EntityType = "Record",
                    EntityId = record.Id.Value,
                    DocumentId = record.DocumentId.Value,
                    Metadata = new Dictionary<string, object?> { ["operation"] = "request" },
                },
                cancellationToken);
            return Result.Failure<Guid>(hold.Error);
        }

        var created = Disposition.Request(record, command.Reason, actor, timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        dispositions.Add(created.Value);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DispositionRequested,
                EntityType = "Disposition",
                EntityId = created.Value.Id.Value,
                DocumentId = record.DocumentId.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["recordId"] = record.Id.Value,
                    ["reason"] = created.Value.RequestReason,
                },
            },
            cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }
}

public sealed class ApproveDispositionHandler(
    IDmsAuthorizer authorizer,
    IDispositionRepository dispositions,
    IRecordRepository records,
    ILegalHoldGuard legalHolds,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<ApproveDispositionCommand, Result>
{
    public async Task<Result> HandleAsync(ApproveDispositionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionApprove, cancellationToken)).Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden("Approving disposition requires DISPOSITION_APPROVE."));
        }

        var disposition = await dispositions.FindAsync(new DispositionId(command.DispositionId), cancellationToken);
        if (disposition is null)
        {
            return Result.Failure(Error.NotFound("disposition.not_found", "The disposition does not exist."));
        }

        if (disposition.Status == DispositionStatus.Approved)
        {
            return Result.Success();
        }

        if (disposition.Status != DispositionStatus.PendingReview)
        {
            return Result.Failure(Error.Conflict(
                "disposition.invalid_state",
                $"Cannot approve a disposition in status {disposition.Status}."));
        }

        var record = await records.FindAsync(disposition.RecordId, cancellationToken);
        if (record is null || record.Status != RecordStatus.PendingDisposal)
        {
            return Result.Failure(Error.Conflict(
                "disposition.not_eligible",
                "The Record must be in PendingDisposal to approve disposition."));
        }

        var hold = await legalHolds.EnsureNotOnHoldAsync(record.DocumentId, cancellationToken);
        if (hold.IsFailure)
        {
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.DispositionBlockedByLegalHold,
                    Outcome = AuditOutcome.Denied,
                    EntityType = "Disposition",
                    EntityId = disposition.Id.Value,
                    DocumentId = record.DocumentId.Value,
                    Metadata = new Dictionary<string, object?> { ["operation"] = "approve" },
                },
                cancellationToken);
            return hold;
        }

        var approved = disposition.Approve(command.Reason, actor, timeProvider.GetUtcNow());
        if (approved.IsFailure)
        {
            return approved;
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DispositionApproved,
                EntityType = "Disposition",
                EntityId = disposition.Id.Value,
                DocumentId = record.DocumentId.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["recordId"] = record.Id.Value,
                    ["reason"] = disposition.DecisionReason,
                },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class RejectDispositionHandler(
    IDmsAuthorizer authorizer,
    IDispositionRepository dispositions,
    IRecordRepository records,
    IAuditWriter audit,
    DocumentChanges changes,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<RejectDispositionCommand, Result>
{
    public async Task<Result> HandleAsync(RejectDispositionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionApprove, cancellationToken)).Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden("Rejecting disposition requires DISPOSITION_APPROVE."));
        }

        var disposition = await dispositions.FindAsync(new DispositionId(command.DispositionId), cancellationToken);
        if (disposition is null)
        {
            return Result.Failure(Error.NotFound("disposition.not_found", "The disposition does not exist."));
        }

        if (disposition.Status == DispositionStatus.Rejected)
        {
            return Result.Success();
        }

        var rejected = disposition.Reject(command.Reason, actor, timeProvider.GetUtcNow());
        if (rejected.IsFailure)
        {
            return rejected;
        }

        var record = await records.FindAsync(disposition.RecordId, cancellationToken);
        if (record is { Status: RecordStatus.PendingDisposal })
        {
            record.TransitionTo(RecordStatus.Expired, "Disposition rejected", actor, timeProvider.GetUtcNow());
            await changes.NotifyAsync(record.DocumentId.Value, cancellationToken);
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DispositionRejected,
                EntityType = "Disposition",
                EntityId = disposition.Id.Value,
                DocumentId = disposition.DocumentId.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["recordId"] = disposition.RecordId.Value,
                    ["reason"] = disposition.DecisionReason,
                },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class DestroyDispositionHandler(
    IDmsAuthorizer authorizer,
    IDispositionRepository dispositions,
    IDestructionCertificateRepository certificates,
    IRecordRepository records,
    IDocumentRepository documents,
    IStorageService storage,
    ILegalHoldGuard legalHolds,
    IAuditWriter audit,
    DocumentChanges changes,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<DestroyDispositionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(DestroyDispositionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionDestroy, cancellationToken)).Allowed)
        {
            return Result.Failure<Guid>(DocumentErrors.Forbidden("Destroying a Record requires DISPOSITION_DESTROY."));
        }

        var disposition = await dispositions.FindAsync(new DispositionId(command.DispositionId), cancellationToken);
        if (disposition is null)
        {
            return Result.Failure<Guid>(Error.NotFound("disposition.not_found", "The disposition does not exist."));
        }

        var existingCertificate = await certificates.FindByDispositionAsync(disposition.Id, cancellationToken);
        if (disposition.Status == DispositionStatus.Destroyed && existingCertificate is not null)
        {
            return Result.Success(existingCertificate.Id.Value);
        }

        if (disposition.Status != DispositionStatus.Approved)
        {
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.DestructionBlocked,
                    Outcome = AuditOutcome.Denied,
                    EntityType = "Disposition",
                    EntityId = disposition.Id.Value,
                    DocumentId = disposition.DocumentId.Value,
                    Metadata = new Dictionary<string, object?> { ["reason"] = "not_approved", ["status"] = disposition.Status.ToString() },
                },
                cancellationToken);
            return Result.Failure<Guid>(Error.Conflict(
                "disposition.not_approved",
                "Destruction requires an approved disposition."));
        }

        var record = await records.FindAsync(disposition.RecordId, cancellationToken);
        if (record is null)
        {
            return Result.Failure<Guid>(Error.NotFound("record.not_found", "The Record does not exist."));
        }

        if (record.Status == RecordStatus.Destroyed && existingCertificate is not null)
        {
            return Result.Success(existingCertificate.Id.Value);
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DestructionAttempted,
                EntityType = "Disposition",
                EntityId = disposition.Id.Value,
                DocumentId = record.DocumentId.Value,
                Metadata = new Dictionary<string, object?> { ["recordId"] = record.Id.Value },
            },
            cancellationToken);

        var holdCheckedAt = timeProvider.GetUtcNow();
        var hold = await legalHolds.EnsureNotOnHoldAsync(record.DocumentId, cancellationToken);
        if (hold.IsFailure)
        {
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.DispositionBlockedByLegalHold,
                    Outcome = AuditOutcome.Denied,
                    EntityType = "Disposition",
                    EntityId = disposition.Id.Value,
                    DocumentId = record.DocumentId.Value,
                    Metadata = new Dictionary<string, object?> { ["operation"] = "destroy" },
                },
                cancellationToken);
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.DestructionBlocked,
                    Outcome = AuditOutcome.Denied,
                    EntityType = "Disposition",
                    EntityId = disposition.Id.Value,
                    DocumentId = record.DocumentId.Value,
                    Metadata = new Dictionary<string, object?> { ["reason"] = "legal_hold" },
                },
                cancellationToken);
            return Result.Failure<Guid>(hold.Error);
        }

        var document = await documents.FindIncludingDeletedAsync(record.DocumentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<Guid>(DocumentErrors.DocumentNotFound);
        }

        var finalVersion = document.Versions.FirstOrDefault(version => version.Id == record.FinalVersionId);
        if (finalVersion is null)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "disposition.final_version_missing",
                "The Record's final version is missing; destruction aborted."));
        }

        // Mark every stored object of this document for deletion (same path as purge). Bytes are
        // removed by the storage worker; the DB transaction below records Destroyed + certificate.
        var files = document.Versions
            .GroupBy(version => version.StorageObjectId)
            .Select(group => group.First())
            .ToList();

        foreach (var file in files)
        {
            await storage.MarkForDeletionAsync(file.StorageObjectId, cancellationToken);
        }

        if (!document.IsDeleted)
        {
            document.SoftDelete(actor, "Destroyed via disposition", holdCheckedAt);
        }

        var destroyedRecord = record.MarkDestroyed(command.Reason, actor, holdCheckedAt);
        if (destroyedRecord.IsFailure)
        {
            return Result.Failure<Guid>(destroyedRecord.Error);
        }

        var marked = disposition.MarkDestroyed(actor, holdCheckedAt);
        if (marked.IsFailure)
        {
            return Result.Failure<Guid>(marked.Error);
        }

        var certificate = DestructionCertificate.Create(
            disposition,
            record,
            finalVersion,
            holdCheckedAt,
            command.Reason,
            holdCheckedAt);
        certificates.Add(certificate);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RecordDestroyed,
                EntityType = "Record",
                EntityId = record.Id.Value,
                DocumentId = record.DocumentId.Value,
                VersionId = finalVersion.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["dispositionId"] = disposition.Id.Value,
                    ["certificateId"] = certificate.Id.Value,
                    ["certificateNumber"] = certificate.CertificateNumber,
                    ["contentSha256"] = Convert.ToHexStringLower(finalVersion.Sha256),
                    ["files"] = files.Count,
                },
            },
            cancellationToken);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.CertificateCreated,
                EntityType = "DestructionCertificate",
                EntityId = certificate.Id.Value,
                DocumentId = record.DocumentId.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["certificateNumber"] = certificate.CertificateNumber,
                    ["dispositionId"] = disposition.Id.Value,
                    ["recordId"] = record.Id.Value,
                    ["certificateHash"] = Convert.ToHexStringLower(certificate.CertificateHash),
                },
            },
            cancellationToken);

        await changes.NotifyAsync(record.DocumentId.Value, cancellationToken);
        return Result.Success(certificate.Id.Value);
    }
}

public sealed class GetDispositionHandler(
    IDmsAuthorizer authorizer,
    IDispositionReadModel readModel,
    ICurrentUser currentUser) : IQueryHandler<GetDispositionQuery, Result<DispositionDto>>
{
    public async Task<Result<DispositionDto>> HandleAsync(GetDispositionQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<DispositionDto>(DocumentErrors.Unauthenticated);
        }

        if (!await CanViewDispositionAsync(authorizer, cancellationToken))
        {
            return Result.Failure<DispositionDto>(DocumentErrors.Forbidden("Viewing disposition requires a disposition permission."));
        }

        var dto = await readModel.GetAsync(new DispositionId(query.DispositionId), cancellationToken);
        return dto is null
            ? Result.Failure<DispositionDto>(Error.NotFound("disposition.not_found", "The disposition does not exist."))
            : Result.Success(dto);
    }

    internal static async Task<bool> CanViewDispositionAsync(IDmsAuthorizer authorizer, CancellationToken cancellationToken) =>
        (await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionRequest, cancellationToken)).Allowed
        || (await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionApprove, cancellationToken)).Allowed
        || (await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionDestroy, cancellationToken)).Allowed
        || (await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionViewCertificate, cancellationToken)).Allowed
        || (await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRecords, cancellationToken)).Allowed;
}

public sealed class GetDispositionByRecordHandler(
    IDmsAuthorizer authorizer,
    IDispositionReadModel readModel,
    ICurrentUser currentUser) : IQueryHandler<GetDispositionByRecordQuery, Result<DispositionDto>>
{
    public async Task<Result<DispositionDto>> HandleAsync(GetDispositionByRecordQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<DispositionDto>(DocumentErrors.Unauthenticated);
        }

        if (!await GetDispositionHandler.CanViewDispositionAsync(authorizer, cancellationToken))
        {
            return Result.Failure<DispositionDto>(DocumentErrors.Forbidden("Viewing disposition requires a disposition permission."));
        }

        var dto = await readModel.GetByRecordAsync(new RecordId(query.RecordId), cancellationToken);
        return dto is null
            ? Result.Failure<DispositionDto>(Error.NotFound("disposition.not_found", "No disposition exists for this Record."))
            : Result.Success(dto);
    }
}

public sealed class ListPendingDisposalHandler(
    IDmsAuthorizer authorizer,
    IDispositionReadModel readModel,
    ICurrentUser currentUser) : IQueryHandler<ListPendingDisposalQuery, Result<IReadOnlyList<PendingDisposalItemDto>>>
{
    public async Task<Result<IReadOnlyList<PendingDisposalItemDto>>> HandleAsync(
        ListPendingDisposalQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<IReadOnlyList<PendingDisposalItemDto>>(DocumentErrors.Unauthenticated);
        }

        if (!await GetDispositionHandler.CanViewDispositionAsync(authorizer, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<PendingDisposalItemDto>>(
                DocumentErrors.Forbidden("Listing disposal candidates requires a disposition permission."));
        }

        return Result.Success(await readModel.ListPendingDisposalAsync(cancellationToken));
    }
}

public sealed class GetDestructionCertificateHandler(
    IDmsAuthorizer authorizer,
    IDispositionReadModel readModel,
    ICurrentUser currentUser) : IQueryHandler<GetDestructionCertificateQuery, Result<DestructionCertificateDto>>
{
    public async Task<Result<DestructionCertificateDto>> HandleAsync(
        GetDestructionCertificateQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<DestructionCertificateDto>(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionViewCertificate, cancellationToken)).Allowed
            && !(await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionDestroy, cancellationToken)).Allowed
            && !(await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionApprove, cancellationToken)).Allowed)
        {
            return Result.Failure<DestructionCertificateDto>(
                DocumentErrors.Forbidden("Viewing a destruction certificate requires DISPOSITION_VIEW_CERTIFICATE."));
        }

        var dto = await readModel.GetCertificateAsync(new DestructionCertificateId(query.CertificateId), cancellationToken);
        return dto is null
            ? Result.Failure<DestructionCertificateDto>(Error.NotFound("certificate.not_found", "The certificate does not exist."))
            : Result.Success(dto);
    }
}

public sealed class GetDestructionCertificateByRecordHandler(
    IDmsAuthorizer authorizer,
    IDispositionReadModel readModel,
    ICurrentUser currentUser) : IQueryHandler<GetDestructionCertificateByRecordQuery, Result<DestructionCertificateDto>>
{
    public async Task<Result<DestructionCertificateDto>> HandleAsync(
        GetDestructionCertificateByRecordQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure<DestructionCertificateDto>(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionViewCertificate, cancellationToken)).Allowed
            && !(await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionDestroy, cancellationToken)).Allowed
            && !(await authorizer.AuthorizeSystemAsync(PermissionCodes.DispositionApprove, cancellationToken)).Allowed)
        {
            return Result.Failure<DestructionCertificateDto>(
                DocumentErrors.Forbidden("Viewing a destruction certificate requires DISPOSITION_VIEW_CERTIFICATE."));
        }

        var dto = await readModel.GetCertificateByRecordAsync(new RecordId(query.RecordId), cancellationToken);
        return dto is null
            ? Result.Failure<DestructionCertificateDto>(Error.NotFound("certificate.not_found", "No certificate exists for this Record."))
            : Result.Success(dto);
    }
}
