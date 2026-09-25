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

public sealed record UpdateUserCommand(Guid UserId, string DisplayName, string? Email) : ICommand<Result>;

/// <summary>An administrator sets a new password for someone who lost theirs. Their sessions end.</summary>
public sealed record ResetUserPasswordCommand(Guid UserId, string NewPassword, bool MustChangePassword) : ICommand<Result>;

/// <summary>Grants or takes away system administration. Only a system administrator may.</summary>
public sealed record SetUserAdminCommand(Guid UserId, bool IsSystemAdmin) : ICommand<Result>;

public sealed record UpdateGroupCommand(Guid GroupId, string Name, bool IsActive) : ICommand<Result>;

/// <summary>Sets whom a user reports to. Workflow steps with a MANAGER assignee resolve through it.</summary>
public sealed record SetUserManagerCommand(Guid UserId, Guid? ManagerId) : ICommand<Result>;

public sealed record CreateGroupCommand(string Code, string Name, GroupKind Kind) : ICommand<Result<Guid>>;

public sealed record AddUserToGroupCommand(Guid UserId, Guid GroupId) : ICommand<Result>;

public sealed record RemoveUserFromGroupCommand(Guid UserId, Guid GroupId) : ICommand<Result>;

/// <summary>
/// The rules every user administration command shares (phase 1 review, fixed in phase 8):
/// ADMIN_MANAGE_USERS lets someone run the directory, but not make or unmake a system
/// administrator, nor touch one. Only a system administrator may do that, never to themselves,
/// and never so that no active administrator is left.
/// </summary>
public sealed class UserAdministration(IDmsAuthorizer authorizer, ICurrentUser currentUser, IUserRepository users)
{
    public static readonly Error AdminProtected = Error.Forbidden(
        "user.admin_protected",
        "Only a system administrator can create, change or deactivate a system administrator.");

    public static readonly Error NotYourself = Error.Validation(
        "user.not_yourself",
        "You cannot do this to your own account.");

    public static readonly Error LastAdmin = Error.Conflict(
        "user.last_admin",
        "At least one active system administrator must remain.");

    /// <summary>The caller, if they hold ADMIN_MANAGE_USERS, and whether they are a system administrator.</summary>
    public async Task<Result<(UserId Actor, bool IsAdmin)>> RequireAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<(UserId, bool)>(IdentityErrors.Unauthenticated);
        }

        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageUsers, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<(UserId, bool)>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var principal = await authorizer.GetPrincipalsAsync(actor, cancellationToken);
        return Result.Success((actor, principal.IsSystemAdmin));
    }

    /// <summary>Refuses changes to an administrator by someone who is not one.</summary>
    public static Error? CheckTarget(User target, bool actorIsAdmin) =>
        target.IsSystemAdmin && !actorIsAdmin ? AdminProtected : null;

    /// <summary>Whether taking <paramref name="target"/> out of the active administrators leaves none.</summary>
    public async Task<bool> WouldRemoveLastAdminAsync(User target, CancellationToken cancellationToken) =>
        target.IsSystemAdmin && target.IsActive && await users.CountActiveAdminsAsync(cancellationToken) <= 1;
}

public sealed class CreateUserHandler(
    UserAdministration administration,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IAuditWriter audit,
    IOptions<IdentityModuleOptions> options,
    TimeProvider timeProvider) : ICommandHandler<CreateUserCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var caller = await administration.RequireAsync(cancellationToken);
        if (caller.IsFailure)
        {
            return Result.Failure<Guid>(caller.Error);
        }

        if (command.IsSystemAdmin && !caller.Value.IsAdmin)
        {
            return Result.Failure<Guid>(UserAdministration.AdminProtected);
        }

        if (string.IsNullOrWhiteSpace(command.Username) || string.IsNullOrWhiteSpace(command.DisplayName))
        {
            return Result.Failure<Guid>(Error.Validation("user.required", "A username and a display name are required."));
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

public sealed class SetUserManagerHandler(
    IDmsAuthorizer authorizer,
    IUserRepository users,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<SetUserManagerCommand, Result>
{
    public async Task<Result> HandleAsync(SetUserManagerCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageUsers, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var user = await users.FindAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        UserId? managerId = command.ManagerId is { } id ? new UserId(id) : null;
        if (managerId is { } manager)
        {
            if (manager == user.Id)
            {
                return Result.Failure(Error.Validation("user.manager_self", "A user cannot be their own manager."));
            }

            // Walk up the chain: a loop would make "the manager's manager" undefined forever.
            var seen = new HashSet<UserId> { user.Id };
            for (UserId? current = manager; current is { } step;)
            {
                if (!seen.Add(step))
                {
                    return Result.Failure(Error.Validation("user.manager_cycle", "This would create a reporting loop."));
                }

                var next = await users.FindAsync(step, cancellationToken);
                if (next is null)
                {
                    return Result.Failure(IdentityErrors.UserNotFound);
                }

                current = next.ManagerId;
            }
        }

        user.SetManager(managerId, timeProvider.GetUtcNow());
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.UserUpdated,
                EntityType = "User",
                EntityId = command.UserId,
                Metadata = new Dictionary<string, object?> { ["managerId"] = command.ManagerId },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class SetUserActiveHandler(
    UserAdministration administration,
    IUserRepository users,
    IUserSessionRepository sessions,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<SetUserActiveCommand, Result>
{
    public async Task<Result> HandleAsync(SetUserActiveCommand command, CancellationToken cancellationToken)
    {
        var caller = await administration.RequireAsync(cancellationToken);
        if (caller.IsFailure)
        {
            return Result.Failure(caller.Error);
        }

        var userId = new UserId(command.UserId);
        var user = await users.FindAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        if (UserAdministration.CheckTarget(user, caller.Value.IsAdmin) is { } protectedError)
        {
            return Result.Failure(protectedError);
        }

        if (!command.IsActive)
        {
            if (userId == caller.Value.Actor)
            {
                return Result.Failure(UserAdministration.NotYourself);
            }

            if (await administration.WouldRemoveLastAdminAsync(user, cancellationToken))
            {
                return Result.Failure(UserAdministration.LastAdmin);
            }
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

public sealed class UpdateUserHandler(
    UserAdministration administration,
    IUserRepository users,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<UpdateUserCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        var caller = await administration.RequireAsync(cancellationToken);
        if (caller.IsFailure)
        {
            return Result.Failure(caller.Error);
        }

        var user = await users.FindAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        if (UserAdministration.CheckTarget(user, caller.Value.IsAdmin) is { } protectedError)
        {
            return Result.Failure(protectedError);
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName))
        {
            return Result.Failure(Error.Validation("user.required", "A display name is required."));
        }

        var email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim();
        user.ChangeProfile(command.DisplayName, email, timeProvider.GetUtcNow());
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.UserUpdated,
                EntityType = "User",
                EntityId = command.UserId,
                Metadata = new Dictionary<string, object?> { ["displayName"] = user.DisplayName, ["email"] = user.Email },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class ResetUserPasswordHandler(
    UserAdministration administration,
    IUserRepository users,
    IUserSessionRepository sessions,
    IPasswordHasher passwordHasher,
    IAuditWriter audit,
    IOptions<IdentityModuleOptions> options,
    TimeProvider timeProvider) : ICommandHandler<ResetUserPasswordCommand, Result>
{
    public async Task<Result> HandleAsync(ResetUserPasswordCommand command, CancellationToken cancellationToken)
    {
        var caller = await administration.RequireAsync(cancellationToken);
        if (caller.IsFailure)
        {
            return Result.Failure(caller.Error);
        }

        var userId = new UserId(command.UserId);
        if (userId == caller.Value.Actor)
        {
            // Your own password goes through change-password, which asks for the current one.
            return Result.Failure(UserAdministration.NotYourself);
        }

        var user = await users.FindAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        if (UserAdministration.CheckTarget(user, caller.Value.IsAdmin) is { } protectedError)
        {
            return Result.Failure(protectedError);
        }

        if (command.NewPassword.Length < options.Value.MinimumPasswordLength)
        {
            return Result.Failure(Error.Validation(
                "password.too_short",
                $"The password must be at least {options.Value.MinimumPasswordLength} characters long."));
        }

        var now = timeProvider.GetUtcNow();
        user.SetPassword(passwordHasher.Hash(command.NewPassword), command.MustChangePassword, now);
        foreach (var session in await sessions.ListActiveAsync(userId, cancellationToken))
        {
            session.Revoke(now);
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.UserPasswordReset,
                EntityType = "User",
                EntityId = command.UserId,
                Metadata = new Dictionary<string, object?> { ["mustChangePassword"] = command.MustChangePassword },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class SetUserAdminHandler(
    UserAdministration administration,
    IUserRepository users,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<SetUserAdminCommand, Result>
{
    public async Task<Result> HandleAsync(SetUserAdminCommand command, CancellationToken cancellationToken)
    {
        var caller = await administration.RequireAsync(cancellationToken);
        if (caller.IsFailure)
        {
            return Result.Failure(caller.Error);
        }

        if (!caller.Value.IsAdmin)
        {
            return Result.Failure(UserAdministration.AdminProtected);
        }

        var userId = new UserId(command.UserId);
        if (userId == caller.Value.Actor)
        {
            return Result.Failure(UserAdministration.NotYourself);
        }

        var user = await users.FindAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(IdentityErrors.UserNotFound);
        }

        if (user.IsSystemAdmin == command.IsSystemAdmin)
        {
            return Result.Success();
        }

        if (!command.IsSystemAdmin && await administration.WouldRemoveLastAdminAsync(user, cancellationToken))
        {
            return Result.Failure(UserAdministration.LastAdmin);
        }

        user.SetSystemAdmin(command.IsSystemAdmin, timeProvider.GetUtcNow());
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = command.IsSystemAdmin ? AuditActions.UserAdminGranted : AuditActions.UserAdminRevoked,
                EntityType = "User",
                EntityId = command.UserId,
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class UpdateGroupHandler(
    IDmsAuthorizer authorizer,
    IGroupRepository groups,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<UpdateGroupCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateGroupCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageGroups, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        var group = await groups.FindAsync(new GroupId(command.GroupId), cancellationToken);
        if (group is null)
        {
            return Result.Failure(Error.NotFound("group.not_found", "The group does not exist."));
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure(Error.Validation("group.required", "A group name is required."));
        }

        var now = timeProvider.GetUtcNow();
        group.Rename(command.Name.Trim(), now);
        group.SetActive(command.IsActive, now);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.GroupUpdated,
                EntityType = "Group",
                EntityId = command.GroupId,
                Metadata = new Dictionary<string, object?> { ["name"] = group.Name, ["isActive"] = group.IsActive },
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
