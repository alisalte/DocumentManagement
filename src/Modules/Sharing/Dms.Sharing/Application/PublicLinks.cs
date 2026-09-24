using System.Globalization;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Documents.Contracts;
using Dms.SharedKernel;
using Dms.Sharing.Domain;
using Dms.Storage.Contracts;
using Microsoft.Extensions.Options;

namespace Dms.Sharing.Application;

/// <summary>Before opening: whether a password is needed. Reveals nothing about the document.</summary>
public sealed record GetPublicLinkQuery(string Token) : IQuery<Result<PublicLinkInfoDto>>;

public sealed record PublicLinkInfoDto(bool RequiresPassword, DateTimeOffset? LockedUntil);

/// <summary>Opens a link: checks the password, spends one opening and starts a session.</summary>
public sealed record OpenPublicLinkCommand(string Token, string? Password) : ICommand<Result<OpenedLinkDto>>;

public sealed record OpenedLinkDto(
    string SessionToken,
    DateTimeOffset SessionExpiresAt,
    string Title,
    string VersionLabel,
    string FileName,
    string MimeType,
    long FileSize,
    bool CanDownload,
    bool CanPrint,
    RenditionStatus PreviewStatus,
    int PageCount);

public sealed record GetPublicLinkPageQuery(string Token, string? SessionToken, int Page, bool ForPrint) : IQuery<Result<StoredContent>>;

public sealed record DownloadPublicLinkCommand(string Token, string? SessionToken) : ICommand<Result<StoredContent>>;

public sealed record StartPublicLinkPrintCommand(string Token, string? SessionToken) : ICommand<Result>;

/// <summary>
/// Resolves a link and, for everything after the opening, its session. Every request re-checks
/// the link (not revoked, not expired) and its creator's rights (decision D8); only the opening
/// counts against the link's limit.
/// </summary>
public sealed class PublicLinkResolver(
    IShareLinkRepository links,
    SharingPolicy policy,
    ISecureTokenGenerator tokens,
    TimeProvider timeProvider)
{
    internal Task<ShareLink?> FindAsync(string token, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(token) || token.Length < ShareLink.PrefixLength || token.Length > 200
            ? Task.FromResult<ShareLink?>(null)
            : links.FindByTokenHashAsync(tokens.ComputeHash(token), cancellationToken);

    internal async Task<Result<ResolvedLink>> ResolveSessionAsync(
        string token,
        string? sessionToken,
        CancellationToken cancellationToken)
    {
        var link = await FindAsync(token, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (link is null || link.RevokedAt is not null || link.ExpiresAt <= now)
        {
            return Result.Failure<ResolvedLink>(SharingErrors.LinkInvalid);
        }

        var session = string.IsNullOrWhiteSpace(sessionToken) || sessionToken.Length > 200
            ? null
            : await links.FindSessionAsync(tokens.ComputeHash(sessionToken), cancellationToken);
        if (session is null || session.LinkId != link.Id || session.ExpiresAt <= now)
        {
            return Result.Failure<ResolvedLink>(SharingErrors.SessionExpired);
        }

        var backed = await policy.CheckLinkAsync(link, cancellationToken);
        return backed is { } ok
            ? Result.Success(new ResolvedLink(link, session, ok.Version, ok.Effective))
            : Result.Failure<ResolvedLink>(SharingErrors.LinkInvalid);
    }
}

internal sealed record ResolvedLink(ShareLink Link, ShareLinkSession Session, VersionSummary Version, SharePermissions Effective);

internal static class LinkAudit
{
    public static AuditRecord Record(ShareLink link, string action, AuditOutcome outcome = AuditOutcome.Success, string? reason = null) => new()
    {
        Action = action,
        Outcome = outcome,
        ActorType = AuditActorType.ShareLink,
        EntityType = "ShareLink",
        EntityId = link.Id.Value,
        DocumentId = link.DocumentId.Value,
        VersionId = link.VersionId,
        ShareLinkId = link.Id.Value,
        Metadata = reason is null
            ? new Dictionary<string, object?> { ["prefix"] = link.TokenPrefix }
            : new Dictionary<string, object?> { ["prefix"] = link.TokenPrefix, ["reason"] = reason },
    };

    /// <summary>Page images of a link say which link and when, since there is no user name to stamp.</summary>
    public static string Watermark(ShareLink link, TimeProvider timeProvider) =>
        $"link {link.TokenPrefix} · {timeProvider.GetUtcNow().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)}";
}

public sealed class GetPublicLinkHandler(PublicLinkResolver resolver, SharingPolicy policy, TimeProvider timeProvider)
    : IQueryHandler<GetPublicLinkQuery, Result<PublicLinkInfoDto>>
{
    public async Task<Result<PublicLinkInfoDto>> HandleAsync(GetPublicLinkQuery query, CancellationToken cancellationToken)
    {
        var link = await resolver.FindAsync(query.Token, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (link is null || !link.IsUsable(now) || await policy.CheckLinkAsync(link, cancellationToken) is null)
        {
            return Result.Failure<PublicLinkInfoDto>(SharingErrors.LinkInvalid);
        }

        return Result.Success(new PublicLinkInfoDto(link.RequiresPassword, link.IsLocked(now) ? link.LockedUntil : null));
    }
}

public sealed class OpenPublicLinkHandler(
    PublicLinkResolver resolver,
    SharingPolicy policy,
    IShareLinkRepository links,
    IPasswordHasher passwords,
    ISecureTokenGenerator tokens,
    IRenditionService renditions,
    IAuditWriter audit,
    IOptions<SharingOptions> options,
    TimeProvider timeProvider) : ICommandHandler<OpenPublicLinkCommand, Result<OpenedLinkDto>>
{
    public async Task<Result<OpenedLinkDto>> HandleAsync(OpenPublicLinkCommand command, CancellationToken cancellationToken)
    {
        var link = await resolver.FindAsync(command.Token, cancellationToken);
        if (link is null)
        {
            return Result.Failure<OpenedLinkDto>(SharingErrors.LinkInvalid);
        }

        var now = timeProvider.GetUtcNow();
        if (!link.IsUsable(now))
        {
            await DeniedAsync(link, link.RevokedAt is not null ? "revoked" : link.ExpiresAt <= now ? "expired" : "used_up", cancellationToken);
            return Result.Failure<OpenedLinkDto>(SharingErrors.LinkInvalid);
        }

        if (link.IsLocked(now))
        {
            await DeniedAsync(link, "locked", cancellationToken);
            return Result.Failure<OpenedLinkDto>(SharingErrors.Locked(link.LockedUntil!.Value));
        }

        if (link.PasswordHash is { } passwordHash)
        {
            if (string.IsNullOrEmpty(command.Password))
            {
                return Result.Failure<OpenedLinkDto>(SharingErrors.PasswordRequired);
            }

            if (!passwords.Verify(command.Password, passwordHash))
            {
                var settings = options.Value;
                var lockUntil = now.AddMinutes(settings.LinkPasswordLockoutMinutes);
                var attempts = await links.RegisterFailedPasswordAsync(link.Id, settings.LinkPasswordMaxAttempts, lockUntil, cancellationToken);
                await audit.WriteAsync(
                    LinkAudit.Record(link, AuditActions.ShareLinkPasswordFailed, AuditOutcome.Denied, $"attempt {attempts}"),
                    cancellationToken);

                return Result.Failure<OpenedLinkDto>(attempts >= settings.LinkPasswordMaxAttempts
                    ? SharingErrors.Locked(lockUntil)
                    : SharingErrors.PasswordIncorrect);
            }
        }

        // Decision D8: the creator must still be able to hand this out, and the file must still be
        // releasable. Checked before an opening is spent.
        if (await policy.CheckLinkAsync(link, cancellationToken) is not { } backed)
        {
            await DeniedAsync(link, "not_backed", cancellationToken);
            return Result.Failure<OpenedLinkDto>(SharingErrors.LinkInvalid);
        }

        if (!await links.TryConsumeOpeningAsync(link.Id, now, cancellationToken))
        {
            await DeniedAsync(link, "used_up", cancellationToken);
            return Result.Failure<OpenedLinkDto>(SharingErrors.LinkInvalid);
        }

        if (link.PasswordHash is not null && link.FailedAttempts > 0)
        {
            await links.ResetFailedPasswordsAsync(link.Id, cancellationToken);
        }

        var session = tokens.Create();
        var sessionEnds = now.AddMinutes(options.Value.LinkSessionMinutes);
        if (sessionEnds > link.ExpiresAt)
        {
            sessionEnds = link.ExpiresAt;
        }

        links.AddSession(new ShareLinkSession(link.Id, session.Hash, now, sessionEnds));
        await audit.WriteAsync(LinkAudit.Record(link, AuditActions.ShareLinkAccessed), cancellationToken);

        var (version, effective) = backed;
        var preview = await renditions.GetPreviewAsync(new StorageObjectId(version.StorageObjectId), cancellationToken);

        return Result.Success(new OpenedLinkDto(
            session.Value,
            sessionEnds,
            version.DocumentTitle,
            version.Label,
            version.FileName,
            version.MimeType,
            version.FileSize,
            effective.HasFlag(SharePermissions.Download),
            effective.HasFlag(SharePermissions.Print),
            preview.Status,
            preview.PageCount));
    }

    private Task DeniedAsync(ShareLink link, string reason, CancellationToken cancellationToken) =>
        audit.WriteAsync(LinkAudit.Record(link, AuditActions.ShareLinkAccessed, AuditOutcome.Denied, reason), cancellationToken);
}

public sealed class GetPublicLinkPageHandler(
    PublicLinkResolver resolver,
    IRenditionService renditions,
    TimeProvider timeProvider) : IQueryHandler<GetPublicLinkPageQuery, Result<StoredContent>>
{
    public async Task<Result<StoredContent>> HandleAsync(GetPublicLinkPageQuery query, CancellationToken cancellationToken)
    {
        var resolved = await resolver.ResolveSessionAsync(query.Token, query.SessionToken, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure<StoredContent>(resolved.Error);
        }

        var (link, session, version, effective) = resolved.Value;
        if (query.ForPrint && !effective.HasFlag(SharePermissions.Print))
        {
            return Result.Failure<StoredContent>(SharingErrors.NotIncluded);
        }

        // Print pages only after POST /print, which is what writes DOCUMENT_PRINTED.
        if (query.ForPrint && session.PrintStartedAt is null)
        {
            return Result.Failure<StoredContent>(SharingErrors.PrintNotStarted);
        }

        return await renditions.OpenPageAsync(
            new StorageObjectId(version.StorageObjectId),
            query.Page,
            LinkAudit.Watermark(link, timeProvider),
            cancellationToken);
    }
}

/// <summary>The original file, when the link includes it. Audited before a single byte is sent.</summary>
public sealed class DownloadPublicLinkHandler(
    PublicLinkResolver resolver,
    IStorageService storage,
    IAuditWriter audit) : ICommandHandler<DownloadPublicLinkCommand, Result<StoredContent>>
{
    public async Task<Result<StoredContent>> HandleAsync(DownloadPublicLinkCommand command, CancellationToken cancellationToken)
    {
        var resolved = await resolver.ResolveSessionAsync(command.Token, command.SessionToken, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure<StoredContent>(resolved.Error);
        }

        var (link, _, version, effective) = resolved.Value;
        if (!effective.HasFlag(SharePermissions.Download))
        {
            await audit.WriteAsync(
                LinkAudit.Record(link, AuditActions.DocumentDownloaded, AuditOutcome.Denied, "not_included"),
                cancellationToken);
            return Result.Failure<StoredContent>(SharingErrors.NotIncluded);
        }

        var content = await storage.OpenAsync(new StorageObjectId(version.StorageObjectId), range: null, cancellationToken);
        if (content.IsFailure)
        {
            return content;
        }

        await audit.WriteAsync(LinkAudit.Record(link, AuditActions.DocumentDownloaded), cancellationToken);
        return Result.Success(content.Value with { FileName = version.FileName });
    }
}

/// <summary>
/// Starts printing within a session. Every start is audited, and the session's print pages are
/// refused until the first one.
/// </summary>
public sealed class StartPublicLinkPrintHandler(
    PublicLinkResolver resolver,
    IShareLinkRepository links,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<StartPublicLinkPrintCommand, Result>
{
    public async Task<Result> HandleAsync(StartPublicLinkPrintCommand command, CancellationToken cancellationToken)
    {
        var resolved = await resolver.ResolveSessionAsync(command.Token, command.SessionToken, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var (link, session, _, effective) = resolved.Value;
        if (!effective.HasFlag(SharePermissions.Print))
        {
            return Result.Failure(SharingErrors.NotIncluded);
        }

        await links.MarkPrintStartedAsync(session.Id, timeProvider.GetUtcNow(), cancellationToken);
        await audit.WriteAsync(LinkAudit.Record(link, AuditActions.DocumentPrinted), cancellationToken);

        return Result.Success();
    }
}

/// <summary>Removes ended link sessions. Links themselves are kept: they are the audit trail's subject.</summary>
public sealed class SharingCleanupJob(IShareLinkRepository links, TimeProvider timeProvider) : IJobHandler
{
    public const string Type = "sharing.cleanup";

    public string JobType => Type;

    public Task HandleAsync(string payload, CancellationToken cancellationToken) =>
        links.DeleteExpiredSessionsAsync(timeProvider.GetUtcNow(), cancellationToken);
}
