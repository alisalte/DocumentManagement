using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.DocumentTypes.Contracts;
using Dms.Identity.Contracts;
using Dms.SharedKernel;
using Dms.Sharing.Domain;
using Microsoft.Extensions.Options;

namespace Dms.Sharing.Application;

public sealed record ListDocumentSharesQuery(Guid DocumentId) : IQuery<Result<DocumentSharesDto>>;

public sealed record ListReceivedSharesQuery : IQuery<Result<IReadOnlyList<ReceivedShareDto>>>;

public sealed record PersonDto(Guid Id, string DisplayName);

public enum ShareState
{
    Active,
    Expired,
    Revoked,
    UsedUp,
}

public sealed record ShareDto(
    Guid Id,
    Guid VersionId,
    string VersionLabel,
    PersonDto SharedWith,
    PersonDto SharedBy,
    IReadOnlyList<SharePermissions> Permissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? Message,
    ShareState State);

/// <summary>A link as its creator sees it later: never the token, only its first characters.</summary>
public sealed record ShareLinkDto(
    Guid Id,
    Guid VersionId,
    string VersionLabel,
    string TokenPrefix,
    string? Label,
    PersonDto CreatedBy,
    IReadOnlyList<SharePermissions> Permissions,
    bool RequiresPassword,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int? MaxAccessCount,
    int AccessCount,
    DateTimeOffset? LastAccessedAt,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? RevokedAt,
    ShareState State);

/// <param name="CanShare">Whether the caller may share this document with a colleague (a UI hint).</param>
/// <param name="CanShareExternal">Whether the caller may create an external link (a UI hint).</param>
public sealed record DocumentSharesDto(
    IReadOnlyList<ShareDto> Shares,
    IReadOnlyList<ShareLinkDto> Links,
    bool CanShare,
    bool CanShareExternal,
    bool CanManageAll);

public sealed record ReceivedShareDto(
    Guid Id,
    Guid DocumentId,
    string DocumentTitle,
    Guid VersionId,
    string VersionLabel,
    string FileName,
    string MimeType,
    long FileSize,
    PersonDto SharedBy,
    IReadOnlyList<SharePermissions> Permissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? Message);

/// <summary>
/// The shares of one document. Whoever manages the document's permissions sees all of them;
/// everyone else who can see the document sees the ones they made.
/// </summary>
public sealed class ListDocumentSharesHandler(
    IDmsAuthorizer authorizer,
    ShareManagement management,
    IShareRepository shares,
    IShareLinkRepository links,
    IDocumentVersionReader versions,
    IDocumentTypeCatalog documentTypes,
    IUserDirectory users,
    SharingAudit audit,
    ICurrentUser currentUser,
    IOptions<SharingOptions> options,
    TimeProvider timeProvider) : IQueryHandler<ListDocumentSharesQuery, Result<DocumentSharesDto>>
{
    private const int Limit = 200;

    public async Task<Result<DocumentSharesDto>> HandleAsync(ListDocumentSharesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<DocumentSharesDto>(SharingErrors.Unauthenticated);
        }

        // Readers of the document see the shares they made. Permission managers see all of them,
        // and may do so without VIEW: an administrator's D5 bypass has to be able to find a leaked
        // link to revoke it, just as it may revoke one. The list holds no content.
        var resource = ResourceRef.Document(query.DocumentId);
        var view = await authorizer.AuthorizeAsync(PermissionCodes.DocumentView, resource, cancellationToken);
        var canManage = await management.CanManageAsync(query.DocumentId, cancellationToken);
        if (!view.Allowed && !canManage)
        {
            await audit.DeniedAsync(query.DocumentId, null, PermissionCodes.DocumentView, view.Reason.ToString(), cancellationToken);
            return Result.Failure<DocumentSharesDto>(SharingErrors.DocumentNotFound);
        }

        var documentId = new DocumentId(query.DocumentId);
        var now = timeProvider.GetUtcNow();

        // Filtered in the database, before the limit: a sharer's own rows must not be crowded out
        // by the newest shares other people made.
        UserId? ownOnly = canManage ? null : actor;
        var shareRows = await shares.ListForDocumentAsync(documentId, ownOnly, Limit, cancellationToken);
        var linkRows = await links.ListForDocumentAsync(documentId, ownOnly, Limit, cancellationToken);

        var labels = await versions.FindManyAsync(
            [.. shareRows.Select(share => share.VersionId), .. linkRows.Select(link => link.VersionId)],
            cancellationToken);
        var people = (await users.FindManyAsync(
                [.. shareRows.SelectMany(share => new[] { share.SharedBy, share.SharedWith }), .. linkRows.Select(link => link.CreatedBy)],
                cancellationToken))
            .ToDictionary(user => user.Id);

        PersonDto Person(UserId id) => new(id.Value, people.TryGetValue(id, out var user) ? user.DisplayName : id.Value.ToString());
        string Label(Guid versionId) => labels.TryGetValue(versionId, out var version) ? version.Label : string.Empty;

        var shareDtos = shareRows
            .Select(share => new ShareDto(
                share.Id.Value,
                share.VersionId,
                Label(share.VersionId),
                Person(share.SharedWith),
                Person(share.SharedBy),
                SharePermissionCodes.Split(share.Permissions),
                share.CreatedAt,
                share.ExpiresAt,
                share.RevokedAt,
                share.Message,
                share.RevokedAt is not null ? ShareState.Revoked
                    : share.IsActive(now) ? ShareState.Active
                    : ShareState.Expired))
            .ToList();

        var linkDtos = linkRows
            .Select(link => new ShareLinkDto(
                link.Id.Value,
                link.VersionId,
                Label(link.VersionId),
                link.TokenPrefix,
                link.Label,
                Person(link.CreatedBy),
                SharePermissionCodes.Split(link.Permissions),
                link.RequiresPassword,
                link.CreatedAt,
                link.ExpiresAt,
                link.MaxAccessCount,
                link.AccessCount,
                link.LastAccessedAt,
                link.IsLocked(now) ? link.LockedUntil : null,
                link.RevokedAt,
                link.RevokedAt is not null ? ShareState.Revoked
                    : link.ExpiresAt <= now ? ShareState.Expired
                    : link.MaxAccessCount is { } max && link.AccessCount >= max ? ShareState.UsedUp
                    : ShareState.Active))
            .ToList();

        var canShare = (await authorizer.AuthorizeAsync(PermissionCodes.DocumentShare, resource, cancellationToken)).Allowed;
        var canShareExternal = options.Value.ExternalLinksEnabled
            && (await authorizer.AuthorizeAsync(PermissionCodes.DocumentShareExternal, resource, cancellationToken)).Allowed
            && await AllowsExternalAsync(query.DocumentId, cancellationToken);

        return Result.Success(new DocumentSharesDto(shareDtos, linkDtos, canShare, canShareExternal, canManage));
    }

    private async Task<bool> AllowsExternalAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (await versions.FindDocumentTypeIdAsync(documentId, cancellationToken) is not { } typeId)
        {
            return false;
        }

        var type = await documentTypes.FindAsync(new DocumentTypeId(typeId), cancellationToken);
        return type?.Settings.AllowExternalSharing == true;
    }
}

/// <summary>
/// "Shared with me": active shares to the caller that still work right now. A share whose sharer
/// lost their rights, or that an explicit DENY overrides, is left out rather than shown broken.
/// </summary>
public sealed class ListReceivedSharesHandler(
    IDmsAuthorizer authorizer,
    IShareRepository shares,
    IDocumentVersionReader versions,
    IUserDirectory users,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IQueryHandler<ListReceivedSharesQuery, Result<IReadOnlyList<ReceivedShareDto>>>
{
    public async Task<Result<IReadOnlyList<ReceivedShareDto>>> HandleAsync(
        ListReceivedSharesQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<IReadOnlyList<ReceivedShareDto>>(SharingErrors.Unauthenticated);
        }

        var active = await shares.ListActiveForRecipientAsync(actor, null, timeProvider.GetUtcNow(), cancellationToken);
        var found = await versions.FindManyAsync([.. active.Select(share => share.VersionId)], cancellationToken);
        var people = (await users.FindManyAsync([.. active.Select(share => share.SharedBy)], cancellationToken))
            .ToDictionary(user => user.Id);

        var result = new List<ReceivedShareDto>();
        foreach (var share in active)
        {
            if (!found.TryGetValue(share.VersionId, out var version) || version.IsDocumentDeleted)
            {
                continue;
            }

            var usable = await authorizer.AuthorizeVersionAsync(
                PermissionCodes.DocumentView,
                ResourceRef.Document(share.DocumentId.Value),
                share.VersionId,
                cancellationToken);
            if (!usable.Allowed)
            {
                continue;
            }

            result.Add(new ReceivedShareDto(
                share.Id.Value,
                share.DocumentId.Value,
                version.DocumentTitle,
                share.VersionId,
                version.Label,
                version.FileName,
                version.MimeType,
                version.FileSize,
                new PersonDto(share.SharedBy.Value, people.TryGetValue(share.SharedBy, out var sharer) ? sharer.DisplayName : string.Empty),
                SharePermissionCodes.Split(share.Permissions),
                share.CreatedAt,
                share.ExpiresAt,
                share.Message));
        }

        return Result.Success<IReadOnlyList<ReceivedShareDto>>(result);
    }
}

/// <summary>
/// Share grants for the permission evaluator (section 5.4 step 8). Each grant is pinned to its
/// version and names its sharer, whose rights the authorizer re-checks on every use (D8).
/// Cached per scope, because one request asks about the same document several times.
/// </summary>
public sealed class ShareGrantSource(IShareRepository shares, TimeProvider timeProvider) : ITemporaryGrantSource
{
    private readonly Dictionary<(UserId, Guid), IReadOnlyCollection<TemporaryGrant>> _cache = [];

    public async Task<IReadOnlyCollection<TemporaryGrant>> GetGrantsAsync(
        UserId userId,
        ResourceRef resource,
        CancellationToken cancellationToken)
    {
        if (resource.Type != ResourceType.Document)
        {
            return [];
        }

        if (_cache.TryGetValue((userId, resource.Id), out var cached))
        {
            return cached;
        }

        var active = await shares.ListActiveForRecipientAsync(
            userId,
            new DocumentId(resource.Id),
            timeProvider.GetUtcNow(),
            cancellationToken);

        IReadOnlyCollection<TemporaryGrant> grants = active
            .SelectMany(share => SharePermissionRules.Each(share.Permissions).Select(flag => new TemporaryGrant(
                TemporaryGrantKind.Share,
                resource,
                SharePermissionCodes.For(flag),
                share.ExpiresAt,
                share.Id.Value,
                share.VersionId,
                share.SharedBy)))
            .ToList();

        _cache[(userId, resource.Id)] = grants;
        return grants;
    }
}
