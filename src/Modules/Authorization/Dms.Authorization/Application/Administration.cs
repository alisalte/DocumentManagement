using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Identity.Contracts;
using Dms.SharedKernel;

namespace Dms.Authorization.Application;

/// <summary>
/// The gate to a resource's ACL: DOCUMENT_MANAGE_PERMISSION. A system administrator always passes
/// through the one ACL bypass (decision D5), and every such pass is audited as
/// ADMIN_PERMISSION_OVERRIDE, whether it granted, revoked, listed or explained. Queries have no
/// transaction of their own, so the audit row then gets a short one.
/// </summary>
public sealed class AclAdministration(IDmsAuthorizer authorizer, IAuditWriter audit, IUnitOfWork unitOfWork)
{
    public async Task<AuthorizationDecision> RequireManageAsync(
        ResourceRef resource,
        string operation,
        CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeAsync(PermissionCodes.DocumentManagePermission, resource, cancellationToken);
        if (decision is { Allowed: true, Reason: DecisionReason.AllowedBySystemAdministrator })
        {
            await WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.AdminPermissionOverride,
                    EntityType = resource.Type.ToString(),
                    EntityId = resource.Id,
                    DocumentId = resource.Type == ResourceType.Document ? resource.Id : null,
                    Metadata = new Dictionary<string, object?> { ["operation"] = operation },
                },
                cancellationToken);
        }

        return decision;
    }

    private async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        if (unitOfWork.HasActiveTransaction)
        {
            await audit.WriteAsync(record, cancellationToken);
            return;
        }

        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            await audit.WriteAsync(record, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

/// <summary>Display names for ACL subjects, so the editor shows people, groups and roles rather than ids.</summary>
public sealed class SubjectNames(IUserDirectory users, IGroupDirectory groups, IRoleRepository roles)
{
    public async Task<IReadOnlyDictionary<(SubjectType Type, Guid Id), string>> ResolveAsync(
        IEnumerable<(SubjectType Type, Guid Id)> subjects,
        CancellationToken cancellationToken)
    {
        var wanted = subjects.Distinct().ToList();
        var names = new Dictionary<(SubjectType, Guid), string>();

        var userIds = wanted.Where(subject => subject.Type == SubjectType.User).Select(subject => new UserId(subject.Id)).ToList();
        if (userIds.Count > 0)
        {
            foreach (var user in await users.FindManyAsync(userIds, cancellationToken))
            {
                names[(SubjectType.User, user.Id.Value)] = user.DisplayName;
            }
        }

        var groupIds = wanted.Where(subject => subject.Type == SubjectType.Group).Select(subject => new GroupId(subject.Id)).ToList();
        if (groupIds.Count > 0)
        {
            foreach (var group in await groups.FindManyAsync(groupIds, cancellationToken))
            {
                names[(SubjectType.Group, group.Id.Value)] = group.Name;
            }
        }

        if (wanted.Any(subject => subject.Type == SubjectType.Role))
        {
            foreach (var role in await roles.ListAsync(cancellationToken))
            {
                names[(SubjectType.Role, role.Id.Value)] = role.Name;
            }
        }

        return names;
    }
}

/// <summary>
/// ADMIN_MANAGE_ROLES runs roles, but may not become a way to hand out system permissions the
/// caller does not hold (to others or to themselves). A system administrator holds them all.
/// </summary>
public sealed class RoleEscalationGuard(IDmsAuthorizer authorizer, ICurrentUser currentUser)
{
    public static Error Exceeds(IEnumerable<string> codes) => Error.Forbidden(
        "role.exceeds_rights",
        $"You can only give out permissions you hold yourself: {string.Join(", ", codes)}.");

    /// <summary>The codes in <paramref name="requested"/> the caller does not hold; empty when all is well.</summary>
    public async Task<IReadOnlyList<string>> MissingAsync(IEnumerable<string> requested, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return [.. requested];
        }

        var principal = await authorizer.GetPrincipalsAsync(actor, cancellationToken);
        return principal.IsSystemAdmin
            ? []
            : requested.Where(code => !principal.SystemPermissions.Contains(code)).Distinct().Order(StringComparer.Ordinal).ToList();
    }
}
