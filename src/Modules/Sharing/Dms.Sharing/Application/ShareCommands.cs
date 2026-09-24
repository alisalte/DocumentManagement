using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Identity.Contracts;
using Dms.SharedKernel;
using Dms.Sharing.Domain;
using Microsoft.Extensions.Options;

namespace Dms.Sharing.Application;

public sealed record CreateShareCommand(
    Guid DocumentId,
    Guid VersionId,
    Guid RecipientId,
    SharePermissions Permissions,
    DateTimeOffset? ExpiresAt,
    string? Message) : ICommand<Result<Guid>>;

public sealed record RevokeShareCommand(Guid ShareId) : ICommand<Result>;

public sealed record CreateShareLinkCommand(
    Guid DocumentId,
    Guid VersionId,
    SharePermissions Permissions,
    DateTimeOffset ExpiresAt,
    int? MaxAccessCount,
    string? Password,
    string? Label) : ICommand<Result<CreatedShareLinkDto>>;

/// <param name="Token">Shown once. Only its digest is stored, so it can never be shown again.</param>
public sealed record CreatedShareLinkDto(Guid Id, string Token, string TokenPrefix, DateTimeOffset ExpiresAt);

public sealed record RevokeShareLinkCommand(Guid LinkId) : ICommand<Result>;

/// <summary>
/// Shares a published version with another user. Sharing the same version with the same person
/// again replaces the earlier share, so there is only ever one to reason about.
/// </summary>
public sealed class CreateShareHandler(
    SharingPolicy policy,
    IShareRepository shares,
    IUserDirectory users,
    SharingAudit audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<CreateShareCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateShareCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(SharingErrors.Unauthenticated);
        }

        var check = await policy.CheckSharerAsync(
            actor,
            command.DocumentId,
            command.VersionId,
            PermissionCodes.DocumentShare,
            command.Permissions,
            cancellationToken);
        if (!check.IsAllowed)
        {
            await audit.DeniedAsync(command.DocumentId, command.VersionId, PermissionCodes.DocumentShare, check.Error!.Code, cancellationToken);
            return Result.Failure<Guid>(check.Error!);
        }

        var recipientId = new UserId(command.RecipientId);
        var recipient = await users.FindAsync(recipientId, cancellationToken);
        if (recipient is not { IsActive: true })
        {
            return Result.Failure<Guid>(SharingErrors.RecipientNotFound);
        }

        var now = timeProvider.GetUtcNow();
        var created = DocumentShare.Create(
            new DocumentId(command.DocumentId),
            command.VersionId,
            actor,
            recipientId,
            command.Permissions,
            command.ExpiresAt,
            command.Message,
            now);
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        // Two requests sharing the same version with the same person must not both find no live
        // share and both insert one: the second waits here until the first has committed.
        await shares.LockAsync(command.VersionId, recipientId, cancellationToken);
        var previous = await shares.FindUnrevokedAsync(command.VersionId, recipientId, cancellationToken);
        if (previous is not null && previous.Revoke(actor, now))
        {
            await audit.WriteAsync(RevokedRecord(previous, "replaced"), cancellationToken);
            await shares.FlushAsync(cancellationToken);
        }

        var share = created.Value;
        shares.Add(share);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentShared,
                EntityType = "DocumentShare",
                EntityId = share.Id.Value,
                DocumentId = command.DocumentId,
                VersionId = command.VersionId,
                Metadata = new Dictionary<string, object?>
                {
                    ["kind"] = "internal",
                    ["recipient"] = recipientId.Value,
                    ["permissions"] = share.Permissions.ToString(),
                    ["expiresAt"] = share.ExpiresAt,
                },
            },
            cancellationToken);

        return Result.Success(share.Id.Value);
    }

    internal static AuditRecord RevokedRecord(DocumentShare share, string reason) => new()
    {
        Action = AuditActions.ShareRevoked,
        EntityType = "DocumentShare",
        EntityId = share.Id.Value,
        DocumentId = share.DocumentId.Value,
        VersionId = share.VersionId,
        Metadata = new Dictionary<string, object?>
        {
            ["kind"] = "internal",
            ["recipient"] = share.SharedWith.Value,
            ["reason"] = reason,
        },
    };
}

/// <summary>
/// The sharer, the recipient (declining it) or whoever manages the document's permissions may
/// revoke a share. Everyone else is told it does not exist.
/// </summary>
public sealed class RevokeShareHandler(
    IShareRepository shares,
    ShareManagement management,
    SharingAudit audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<RevokeShareCommand, Result>
{
    public async Task<Result> HandleAsync(RevokeShareCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(SharingErrors.Unauthenticated);
        }

        var share = await shares.FindAsync(new ShareId(command.ShareId), cancellationToken);
        if (share is null)
        {
            return Result.Failure(SharingErrors.ShareNotFound);
        }

        var reason = share.SharedBy == actor ? "sharer"
            : share.SharedWith == actor ? "recipient"
            : await management.CanManageAsync(share.DocumentId.Value, cancellationToken) ? "manager"
            : null;
        if (reason is null)
        {
            return Result.Failure(SharingErrors.ShareNotFound);
        }

        if (share.Revoke(actor, timeProvider.GetUtcNow()))
        {
            await audit.WriteAsync(CreateShareHandler.RevokedRecord(share, reason), cancellationToken);
        }

        return Result.Success();
    }
}

/// <summary>Creates an external link. The raw token leaves the server exactly once, here.</summary>
public sealed class CreateShareLinkHandler(
    SharingPolicy policy,
    IShareLinkRepository links,
    ISecureTokenGenerator tokens,
    IPasswordHasher passwords,
    SharingAudit audit,
    ICurrentUser currentUser,
    IOptions<SharingOptions> options,
    TimeProvider timeProvider) : ICommandHandler<CreateShareLinkCommand, Result<CreatedShareLinkDto>>
{
    public async Task<Result<CreatedShareLinkDto>> HandleAsync(CreateShareLinkCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<CreatedShareLinkDto>(SharingErrors.Unauthenticated);
        }

        var check = await policy.CheckSharerAsync(
            actor,
            command.DocumentId,
            command.VersionId,
            PermissionCodes.DocumentShareExternal,
            command.Permissions,
            cancellationToken);
        if (!check.IsAllowed)
        {
            await audit.DeniedAsync(command.DocumentId, command.VersionId, PermissionCodes.DocumentShareExternal, check.Error!.Code, cancellationToken);
            return Result.Failure<CreatedShareLinkDto>(check.Error!);
        }

        string? passwordHash = null;
        if (!string.IsNullOrEmpty(command.Password))
        {
            if (command.Password.Length < ShareLink.MinPasswordLength)
            {
                return Result.Failure<CreatedShareLinkDto>(SharingErrors.PasswordTooShort);
            }

            passwordHash = passwords.Hash(command.Password);
        }

        var token = tokens.Create();
        var now = timeProvider.GetUtcNow();
        var created = ShareLink.Create(
            new DocumentId(command.DocumentId),
            command.VersionId,
            token.Value,
            token.Hash,
            command.Permissions,
            passwordHash,
            command.ExpiresAt,
            command.MaxAccessCount,
            command.Label,
            actor,
            now,
            TimeSpan.FromDays(options.Value.MaxLinkLifetimeDays));
        if (created.IsFailure)
        {
            return Result.Failure<CreatedShareLinkDto>(created.Error);
        }

        var link = created.Value;
        links.Add(link);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.DocumentShared,
                EntityType = "ShareLink",
                EntityId = link.Id.Value,
                DocumentId = command.DocumentId,
                VersionId = command.VersionId,
                ShareLinkId = link.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["kind"] = "link",
                    ["prefix"] = link.TokenPrefix,
                    ["permissions"] = link.Permissions.ToString(),
                    ["expiresAt"] = link.ExpiresAt,
                    ["maxAccessCount"] = link.MaxAccessCount,
                    ["password"] = link.RequiresPassword,
                },
            },
            cancellationToken);

        return Result.Success(new CreatedShareLinkDto(link.Id.Value, token.Value, link.TokenPrefix, link.ExpiresAt));
    }
}

public sealed class RevokeShareLinkHandler(
    IShareLinkRepository links,
    ShareManagement management,
    SharingAudit audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<RevokeShareLinkCommand, Result>
{
    public async Task<Result> HandleAsync(RevokeShareLinkCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(SharingErrors.Unauthenticated);
        }

        var link = await links.FindAsync(new ShareLinkId(command.LinkId), cancellationToken);
        if (link is null)
        {
            return Result.Failure(SharingErrors.LinkNotFound);
        }

        var reason = link.CreatedBy == actor ? "creator"
            : await management.CanManageAsync(link.DocumentId.Value, cancellationToken) ? "manager"
            : null;
        if (reason is null)
        {
            return Result.Failure(SharingErrors.LinkNotFound);
        }

        if (link.Revoke(actor, timeProvider.GetUtcNow()))
        {
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.ShareRevoked,
                    EntityType = "ShareLink",
                    EntityId = link.Id.Value,
                    DocumentId = link.DocumentId.Value,
                    VersionId = link.VersionId,
                    ShareLinkId = link.Id.Value,
                    Metadata = new Dictionary<string, object?>
                    {
                        ["kind"] = "link",
                        ["prefix"] = link.TokenPrefix,
                        ["reason"] = reason,
                    },
                },
                cancellationToken);
        }

        return Result.Success();
    }
}

/// <summary>
/// Whether the caller manages the document's permissions, which lets them see and revoke every
/// share of it. A system administrator gets there through the audited bypass (decision D5).
/// </summary>
public sealed class ShareManagement(IDmsAuthorizer authorizer, SharingAudit audit)
{
    public async Task<bool> CanManageAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeAsync(
            PermissionCodes.DocumentManagePermission,
            ResourceRef.Document(documentId),
            cancellationToken);

        if (decision is { Allowed: true, Reason: DecisionReason.AllowedBySystemAdministrator })
        {
            await audit.WriteStandaloneAsync(
                new AuditRecord
                {
                    Action = AuditActions.AdminPermissionOverride,
                    EntityType = "Document",
                    EntityId = documentId,
                    DocumentId = documentId,
                    Metadata = new Dictionary<string, object?> { ["purpose"] = "shares" },
                },
                cancellationToken);
        }

        return decision.Allowed;
    }
}
