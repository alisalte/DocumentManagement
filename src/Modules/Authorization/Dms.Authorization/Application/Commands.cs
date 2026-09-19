using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Authorization.Domain;
using Dms.SharedKernel;

namespace Dms.Authorization.Application;

public sealed record GrantResourcePermissionCommand(
    ResourceType ResourceType,
    Guid ResourceId,
    SubjectType SubjectType,
    Guid SubjectId,
    string PermissionCode,
    PermissionEffect Effect,
    bool Inherit,
    string? Reason) : ICommand<Result<Guid>>;

public sealed record RevokeResourcePermissionCommand(Guid EntryId) : ICommand<Result>;

public sealed record CreateRoleCommand(string Code, string Name, string? Description) : ICommand<Result<Guid>>;

public sealed record SetRolePermissionsCommand(Guid RoleId, IReadOnlyList<string> PermissionCodes) : ICommand<Result>;

public sealed record AssignRoleCommand(Guid UserId, Guid RoleId) : ICommand<Result>;

public sealed record UnassignRoleCommand(Guid UserId, Guid RoleId) : ICommand<Result>;

internal static class AuthorizationErrors
{
    public static readonly Error Unauthenticated =
        Error.Unauthorized("auth.unauthenticated", "The request is not authenticated.");

    public static Error Forbidden(string explanation) => Error.Forbidden("auth.forbidden", explanation);

    public static readonly Error RoleNotFound = Error.NotFound("role.not_found", "The role does not exist.");

    public static readonly Error EntryNotFound =
        Error.NotFound("permission.not_found", "The permission entry does not exist.");
}

public sealed class GrantResourcePermissionHandler(
    IDmsAuthorizer authorizer,
    IResourcePermissionRepository repository,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<GrantResourcePermissionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(
        GrantResourcePermissionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(AuthorizationErrors.Unauthenticated);
        }

        var resource = new ResourceRef(command.ResourceType, command.ResourceId);
        var decision = await authorizer.AuthorizeAsync(
            PermissionCodes.DocumentManagePermission,
            resource,
            cancellationToken);

        if (!decision.Allowed)
        {
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.AccessDenied,
                    Outcome = AuditOutcome.Denied,
                    EntityType = command.ResourceType.ToString(),
                    EntityId = command.ResourceId,
                    Metadata = new Dictionary<string, object?>
                    {
                        ["permission"] = PermissionCodes.DocumentManagePermission,
                        ["reason"] = decision.Reason.ToString(),
                    },
                },
                cancellationToken);

            return Result.Failure<Guid>(AuthorizationErrors.Forbidden(decision.Explanation));
        }

        // Decision D5: administrators reaching a resource through the bypass leave a trace.
        if (decision.Reason == DecisionReason.AllowedBySystemAdministrator)
        {
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.AdminPermissionOverride,
                    EntityType = command.ResourceType.ToString(),
                    EntityId = command.ResourceId,
                    Metadata = new Dictionary<string, object?> { ["operation"] = "grant" },
                },
                cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var existing = await repository.FindDuplicateAsync(
            resource,
            command.SubjectType,
            command.SubjectId,
            command.PermissionCode,
            cancellationToken);

        if (existing is not null)
        {
            // One effect per (resource, subject, permission). Changing it is a revoke plus a grant.
            repository.Remove(existing);
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.PermissionRevoked,
                    EntityType = "ResourcePermission",
                    EntityId = existing.Id.Value,
                    Metadata = Describe(existing),
                },
                cancellationToken);
        }

        var created = ResourcePermissionEntry.Create(
            resource,
            command.SubjectType,
            command.SubjectId,
            command.PermissionCode,
            command.Effect,
            command.Inherit,
            command.Reason,
            actor,
            now);

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        repository.Add(created.Value);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.PermissionGranted,
                EntityType = "ResourcePermission",
                EntityId = created.Value.Id.Value,
                Metadata = Describe(created.Value),
            },
            cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }

    private static Dictionary<string, object?> Describe(ResourcePermissionEntry entry) => new()
    {
        ["resourceType"] = entry.ResourceType.ToString(),
        ["resourceId"] = entry.ResourceId,
        ["subjectType"] = entry.SubjectType.ToString(),
        ["subjectId"] = entry.SubjectId,
        ["permission"] = entry.PermissionCode,
        ["effect"] = entry.Effect.ToString(),
        ["inherit"] = entry.Inherit,
    };
}

public sealed class RevokeResourcePermissionHandler(
    IDmsAuthorizer authorizer,
    IResourcePermissionRepository repository,
    IAuditWriter audit) : ICommandHandler<RevokeResourcePermissionCommand, Result>
{
    public async Task<Result> HandleAsync(
        RevokeResourcePermissionCommand command,
        CancellationToken cancellationToken)
    {
        var entry = await repository.FindAsync(new AclEntryId(command.EntryId), cancellationToken);
        if (entry is null)
        {
            return Result.Failure(AuthorizationErrors.EntryNotFound);
        }

        var decision = await authorizer.AuthorizeAsync(
            PermissionCodes.DocumentManagePermission,
            new ResourceRef(entry.ResourceType, entry.ResourceId),
            cancellationToken);

        if (!decision.Allowed)
        {
            return Result.Failure(AuthorizationErrors.Forbidden(decision.Explanation));
        }

        repository.Remove(entry);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.PermissionRevoked,
                EntityType = "ResourcePermission",
                EntityId = entry.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["resourceType"] = entry.ResourceType.ToString(),
                    ["resourceId"] = entry.ResourceId,
                    ["subjectType"] = entry.SubjectType.ToString(),
                    ["subjectId"] = entry.SubjectId,
                    ["permission"] = entry.PermissionCode,
                    ["effect"] = entry.Effect.ToString(),
                },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class CreateRoleHandler(
    IDmsAuthorizer authorizer,
    IRoleRepository repository,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<CreateRoleCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRoles, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<Guid>(AuthorizationErrors.Forbidden(decision.Explanation));
        }

        if (await repository.FindByCodeAsync(command.Code.Trim().ToUpperInvariant(), cancellationToken) is not null)
        {
            return Result.Failure<Guid>(Error.Conflict("role.duplicate", "A role with this code already exists."));
        }

        var role = Role.Create(command.Code, command.Name, command.Description, false, timeProvider.GetUtcNow());
        repository.Add(role);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RoleCreated,
                EntityType = "Role",
                EntityId = role.Id.Value,
                Metadata = new Dictionary<string, object?> { ["code"] = role.Code },
            },
            cancellationToken);

        return Result.Success(role.Id.Value);
    }
}

public sealed class SetRolePermissionsHandler(
    IDmsAuthorizer authorizer,
    IRoleRepository repository,
    IAuditWriter audit) : ICommandHandler<SetRolePermissionsCommand, Result>
{
    public async Task<Result> HandleAsync(SetRolePermissionsCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRoles, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure(AuthorizationErrors.Forbidden(decision.Explanation));
        }

        var role = await repository.FindAsync(new RoleId(command.RoleId), cancellationToken);
        if (role is null)
        {
            return Result.Failure(AuthorizationErrors.RoleNotFound);
        }

        foreach (var existing in role.Permissions.Select(permission => permission.PermissionCode).ToList())
        {
            role.Revoke(existing);
        }

        foreach (var code in command.PermissionCodes)
        {
            var granted = role.Grant(code);
            if (granted.IsFailure)
            {
                return granted;
            }
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RolePermissionsChanged,
                EntityType = "Role",
                EntityId = role.Id.Value,
                Metadata = new Dictionary<string, object?> { ["permissions"] = command.PermissionCodes },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class AssignRoleHandler(
    IDmsAuthorizer authorizer,
    IRoleRepository roles,
    IUserRoleRepository userRoles,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<AssignRoleCommand, Result>
{
    public async Task<Result> HandleAsync(AssignRoleCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(AuthorizationErrors.Unauthenticated);
        }

        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRoles, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure(AuthorizationErrors.Forbidden(decision.Explanation));
        }

        var roleId = new RoleId(command.RoleId);
        if (await roles.FindAsync(roleId, cancellationToken) is null)
        {
            return Result.Failure(AuthorizationErrors.RoleNotFound);
        }

        var userId = new UserId(command.UserId);
        if (await userRoles.FindAsync(userId, roleId, cancellationToken) is not null)
        {
            return Result.Success();
        }

        userRoles.Add(new UserRoleAssignment(userId, roleId, actor, timeProvider.GetUtcNow()));
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RoleAssigned,
                EntityType = "User",
                EntityId = command.UserId,
                Metadata = new Dictionary<string, object?> { ["roleId"] = command.RoleId },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class UnassignRoleHandler(
    IDmsAuthorizer authorizer,
    IUserRoleRepository userRoles,
    IAuditWriter audit) : ICommandHandler<UnassignRoleCommand, Result>
{
    public async Task<Result> HandleAsync(UnassignRoleCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageRoles, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure(AuthorizationErrors.Forbidden(decision.Explanation));
        }

        var assignment = await userRoles.FindAsync(
            new UserId(command.UserId),
            new RoleId(command.RoleId),
            cancellationToken);

        if (assignment is null)
        {
            return Result.Success();
        }

        userRoles.Remove(assignment);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.RoleUnassigned,
                EntityType = "User",
                EntityId = command.UserId,
                Metadata = new Dictionary<string, object?> { ["roleId"] = command.RoleId },
            },
            cancellationToken);

        return Result.Success();
    }
}
