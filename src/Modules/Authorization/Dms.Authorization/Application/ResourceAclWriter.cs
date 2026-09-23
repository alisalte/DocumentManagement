using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Authorization.Domain;
using Dms.SharedKernel;

namespace Dms.Authorization.Application;

/// <summary>
/// System-initiated grants. Written as ordinary, visible ACL rows with a PERMISSION_GRANTED audit
/// record each, so an owner's rights can be seen, explained and revoked like any other.
/// </summary>
public sealed class ResourceAclWriter(
    IResourcePermissionRepository repository,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IResourceAclWriter
{
    public async Task GrantAsync(
        ResourceRef resource,
        SubjectType subjectType,
        Guid subjectId,
        IReadOnlyCollection<string> permissionCodes,
        string reason,
        CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId
            ?? throw new InvalidOperationException("Default grants are only written on behalf of a user.");
        var now = timeProvider.GetUtcNow();

        foreach (var permissionCode in permissionCodes.Distinct(StringComparer.Ordinal))
        {
            var existing = await repository.FindDuplicateAsync(
                resource,
                subjectType,
                subjectId,
                permissionCode,
                cancellationToken);

            // An existing entry, ALLOW or DENY, was put there deliberately; never overwrite it.
            if (existing is not null)
            {
                continue;
            }

            var created = ResourcePermissionEntry.Create(
                resource,
                subjectType,
                subjectId,
                permissionCode,
                PermissionEffect.Allow,
                inherit: false,
                reason,
                actor,
                now);

            if (created.IsFailure)
            {
                throw new InvalidOperationException(created.Error.Message);
            }

            repository.Add(created.Value);
            await audit.WriteAsync(
                new AuditRecord
                {
                    Action = AuditActions.PermissionGranted,
                    EntityType = "ResourcePermission",
                    EntityId = created.Value.Id.Value,
                    DocumentId = resource.Type == ResourceType.Document ? resource.Id : null,
                    Metadata = new Dictionary<string, object?>
                    {
                        ["resourceType"] = resource.Type.ToString(),
                        ["resourceId"] = resource.Id,
                        ["subjectType"] = subjectType.ToString(),
                        ["subjectId"] = subjectId,
                        ["permission"] = permissionCode,
                        ["effect"] = PermissionEffect.Allow.ToString(),
                        ["inherit"] = false,
                        ["reason"] = reason,
                    },
                },
                cancellationToken);
        }
    }
}
