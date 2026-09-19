using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Identity.Domain;
using Dms.SharedKernel;
using Microsoft.Extensions.Options;

namespace Dms.Identity.Application;

public sealed record CreateUserCommand(
    string Username,
    string DisplayName,
    string? Email,
    string Password,
    bool IsSystemAdmin,
    bool MustChangePassword) : ICommand<Result<Guid>>;

public sealed record SetUserActiveCommand(Guid UserId, bool IsActive) : ICommand<Result>;

public sealed record CreateGroupCommand(string Code, string Name, GroupKind Kind) : ICommand<Result<Guid>>;

public sealed record AddUserToGroupCommand(Guid UserId, Guid GroupId) : ICommand<Result>;

public sealed record RemoveUserFromGroupCommand(Guid UserId, Guid GroupId) : ICommand<Result>;

public sealed class CreateUserHandler(
    IDmsAuthorizer authorizer,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IAuditWriter audit,
    IOptions<IdentityModuleOptions> options,
    TimeProvider timeProvider) : ICommandHandler<CreateUserCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageUsers, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<Guid>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        if (command.Password.Length < options.Value.MinimumPasswordLength)
        {
            return Result.Failure<Guid>(Error.Validation(
                "password.too_short",
                $"The password must be at least {options.Value.MinimumPasswordLength} characters long."));
        }

        if (await users.UsernameExistsAsync(command.Username, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict("user.duplicate", "This username is already taken."));
        }

        var user = User.CreateLocal(
            command.Username,
            command.DisplayName,
            command.Email,
            passwordHasher.Hash(command.Password),
            command.IsSystemAdmin,
            command.MustChangePassword,
            timeProvider.GetUtcNow());

        users.Add(user);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.UserCreated,
                EntityType = "User",
                EntityId = user.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["username"] = user.Username,
                    ["isSystemAdmin"] = user.IsSystemAdmin,
                },
            },
            cancellationToken);

        return Result.Success(user.Id.Value);
    }
}

public sealed class SetUserActiveHandler(
    IDmsAuthorizer authorizer,
    IUserRepository users,
    IUserSessionRepository sessions,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<SetUserActiveCommand, Result>
{
    public async Task<Result> HandleAsync(SetUserActiveCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageUsers, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var userId = new UserId(command.UserId);
        var user = await users.FindAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        var now = timeProvider.GetUtcNow();
        user.SetActive(command.IsActive, now);

        if (!command.IsActive)
        {
            foreach (var session in await sessions.ListActiveAsync(userId, cancellationToken))
            {
                session.Revoke(now);
            }
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = command.IsActive ? AuditActions.UserActivated : AuditActions.UserDeactivated,
                EntityType = "User",
                EntityId = command.UserId,
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class CreateGroupHandler(
    IDmsAuthorizer authorizer,
    IGroupRepository groups,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<CreateGroupCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateGroupCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageGroups, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<Guid>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var code = command.Code.Trim().ToUpperInvariant();
        if (await groups.FindByCodeAsync(code, cancellationToken) is not null)
        {
            return Result.Failure<Guid>(Error.Conflict("group.duplicate", "A group with this code already exists."));
        }

        var group = Group.Create(command.Code, command.Name, command.Kind, timeProvider.GetUtcNow());
        groups.Add(group);

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.GroupCreated,
                EntityType = "Group",
                EntityId = group.Id.Value,
                Metadata = new Dictionary<string, object?> { ["code"] = group.Code },
            },
            cancellationToken);

        return Result.Success(group.Id.Value);
    }
}

public sealed class AddUserToGroupHandler(
    IDmsAuthorizer authorizer,
    IUserRepository users,
    IGroupRepository groups,
    IMembershipRepository memberships,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<AddUserToGroupCommand, Result>
{
    public async Task<Result> HandleAsync(AddUserToGroupCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(IdentityErrors.Unauthenticated);
        }

        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageGroups, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var userId = new UserId(command.UserId);
        var groupId = new GroupId(command.GroupId);

        if (await users.FindAsync(userId, cancellationToken) is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        if (await groups.FindAsync(groupId, cancellationToken) is null)
        {
            return Result.Failure(Error.NotFound("group.not_found", "The group does not exist."));
        }

        if (await memberships.FindAsync(userId, groupId, cancellationToken) is not null)
        {
            return Result.Success();
        }

        memberships.Add(new UserGroupMembership(userId, groupId, actor, timeProvider.GetUtcNow()));
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.GroupMemberAdded,
                EntityType = "Group",
                EntityId = command.GroupId,
                Metadata = new Dictionary<string, object?> { ["userId"] = command.UserId },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class RemoveUserFromGroupHandler(
    IDmsAuthorizer authorizer,
    IMembershipRepository memberships,
    IAuditWriter audit) : ICommandHandler<RemoveUserFromGroupCommand, Result>
{
    public async Task<Result> HandleAsync(RemoveUserFromGroupCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageGroups, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var membership = await memberships.FindAsync(
            new UserId(command.UserId),
            new GroupId(command.GroupId),
            cancellationToken);

        if (membership is null)
        {
            return Result.Success();
        }

        memberships.Remove(membership);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.GroupMemberRemoved,
                EntityType = "Group",
                EntityId = command.GroupId,
                Metadata = new Dictionary<string, object?> { ["userId"] = command.UserId },
            },
            cancellationToken);

        return Result.Success();
    }
}
