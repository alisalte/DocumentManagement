using System.Text.Json;
using Dms.Application;
using Dms.Identity.Contracts;
using Dms.Notifications.Contracts;
using Dms.Notifications.Domain;
using Dms.SharedKernel;
using Microsoft.Extensions.Options;

namespace Dms.Notifications.Application;

public sealed class NotificationOptions
{
    public const string SectionName = "Dms:Notifications";

    /// <summary>Read notifications are removed after this many days.</summary>
    public int KeepReadDays { get; set; } = 90;

    /// <summary>Unread ones too, eventually: nobody reads a year-old notification.</summary>
    public int KeepUnreadDays { get; set; } = 365;
}

public interface INotificationRepository
{
    void Add(Notification notification);

    /// <summary>Newest first, in (created_at, id) order, starting after the cursor.</summary>
    Task<IReadOnlyList<Notification>> ListAsync(
        UserId userId,
        bool unreadOnly,
        NotificationCursor? after,
        int take,
        CancellationToken cancellationToken);

    Task<int> CountUnreadAsync(UserId userId, CancellationToken cancellationToken);

    /// <summary>Marks the given notifications (all unread ones when null) of this user read. Returns how many changed.</summary>
    Task<int> MarkReadAsync(UserId userId, IReadOnlyCollection<NotificationId>? ids, DateTimeOffset now, CancellationToken cancellationToken);

    Task<int> DeleteOldAsync(DateTimeOffset readBefore, DateTimeOffset unreadBefore, CancellationToken cancellationToken);
}

public sealed class NotificationSender(
    INotificationRepository notifications,
    IUserDirectory users,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IEnumerable<INotificationChannel> channels) : INotificationSender
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId;
        var candidates = message.Recipients.Where(recipient => recipient != actor).Distinct().ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var active = (await users.FindManyAsync(candidates, cancellationToken))
            .Where(user => user.IsActive)
            .Select(user => user.Id)
            .ToList();

        var payload = JsonSerializer.Serialize(message.Payload, Json);
        var now = timeProvider.GetUtcNow();
        foreach (var recipient in active)
        {
            notifications.Add(Notification.Create(recipient, message.Type, actor, message.DocumentId, message.VersionId, payload, now));
            foreach (var channel in channels)
            {
                await channel.DeliverAsync(message, recipient, cancellationToken);
            }
        }
    }
}

public sealed record NotificationDto(
    Guid Id,
    string Type,
    Guid? ActorId,
    string? ActorName,
    Guid? DocumentId,
    Guid? VersionId,
    JsonElement Payload,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

/// <param name="NextCursor">Pass back as <c>cursor</c> for the next page; null on the last one.</param>
public sealed record NotificationPageDto(IReadOnlyList<NotificationDto> Items, int UnreadCount, string? NextCursor);

/// <summary>
/// Where a page ended. The time alone is not enough: several notifications can share a
/// timestamp, and a page boundary between them would skip the rest.
/// </summary>
public sealed record NotificationCursor(DateTimeOffset CreatedAt, Guid Id)
{
    public override string ToString() => $"{CreatedAt.UtcTicks}_{Id:N}";

    public static NotificationCursor? Parse(string? value)
    {
        var parts = value?.Split('_');
        return parts is [var ticks, var id]
            && long.TryParse(ticks, out var utcTicks)
            && utcTicks is >= 0 and <= 3155378975999999999
            && Guid.TryParseExact(id, "N", out var guid)
                ? new NotificationCursor(new DateTimeOffset(utcTicks, TimeSpan.Zero), guid)
                : null;
    }
}

/// <param name="Cursor">From the previous page's <see cref="NotificationPageDto.NextCursor"/>.</param>
public sealed record ListNotificationsQuery(bool UnreadOnly, string? Cursor, int Take) : IQuery<Result<NotificationPageDto>>;

public sealed record CountUnreadNotificationsQuery : IQuery<Result<int>>;

/// <param name="Ids">Null marks every unread notification of the caller.</param>
public sealed record MarkNotificationsReadCommand(IReadOnlyCollection<Guid>? Ids) : ICommand<Result<int>>;

internal static class NotificationErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Not authenticated.");
}

/// <summary>The caller's own notifications, newest first. Nobody can read anyone else's.</summary>
public sealed class ListNotificationsHandler(
    INotificationRepository notifications,
    IUserDirectory users,
    ICurrentUser currentUser) : IQueryHandler<ListNotificationsQuery, Result<NotificationPageDto>>
{
    public async Task<Result<NotificationPageDto>> HandleAsync(ListNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<NotificationPageDto>(NotificationErrors.Unauthenticated);
        }

        var take = Math.Clamp(query.Take, 1, 100);
        var cursor = NotificationCursor.Parse(query.Cursor);
        if (query.Cursor is not null && cursor is null)
        {
            return Result.Failure<NotificationPageDto>(Error.Validation("notification.cursor", "The cursor is not valid."));
        }

        var rows = await notifications.ListAsync(userId, query.UnreadOnly, cursor, take + 1, cancellationToken);
        var page = rows.Take(take).ToList();

        var actorIds = page.Where(row => row.ActorId is not null).Select(row => row.ActorId!.Value).Distinct().ToList();
        var names = actorIds.Count == 0
            ? new Dictionary<UserId, string>()
            : (await users.FindManyAsync(actorIds, cancellationToken)).ToDictionary(user => user.Id, user => user.DisplayName);

        var items = page.Select(row =>
        {
            using var payload = JsonDocument.Parse(row.Payload);
            return new NotificationDto(
                row.Id.Value,
                row.Type,
                row.ActorId?.Value,
                row.ActorId is { } actor && names.TryGetValue(actor, out var name) ? name : null,
                row.DocumentId,
                row.VersionId,
                payload.RootElement.Clone(),
                row.CreatedAt,
                row.ReadAt);
        }).ToList();

        var unread = await notifications.CountUnreadAsync(userId, cancellationToken);
        var next = rows.Count > take ? new NotificationCursor(page[^1].CreatedAt, page[^1].Id.Value).ToString() : null;
        return Result.Success(new NotificationPageDto(items, unread, next));
    }
}

public sealed class CountUnreadNotificationsHandler(INotificationRepository notifications, ICurrentUser currentUser)
    : IQueryHandler<CountUnreadNotificationsQuery, Result<int>>
{
    public async Task<Result<int>> HandleAsync(CountUnreadNotificationsQuery query, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? Result.Success(await notifications.CountUnreadAsync(userId, cancellationToken))
            : Result.Failure<int>(NotificationErrors.Unauthenticated);
}

/// <summary>Marks the caller's notifications read. Ids of other people's notifications simply match nothing.</summary>
public sealed class MarkNotificationsReadHandler(
    INotificationRepository notifications,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<MarkNotificationsReadCommand, Result<int>>
{
    public async Task<Result<int>> HandleAsync(MarkNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<int>(NotificationErrors.Unauthenticated);
        }

        if (command.Ids is { Count: > 200 })
        {
            return Result.Failure<int>(Error.Validation("notification.too_many", "Mark at most 200 notifications at once."));
        }

        var ids = command.Ids?.Select(id => new NotificationId(id)).ToList();
        return Result.Success(await notifications.MarkReadAsync(userId, ids, timeProvider.GetUtcNow(), cancellationToken));
    }
}

/// <summary>Removes old notifications; they are a convenience, not a record (the audit log is the record).</summary>
public sealed class NotificationCleanupJob(
    INotificationRepository notifications,
    IOptions<NotificationOptions> options,
    TimeProvider timeProvider) : IJobHandler
{
    public const string Type = "notifications.cleanup";

    public string JobType => Type;

    public Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return notifications.DeleteOldAsync(
            now.AddDays(-options.Value.KeepReadDays),
            now.AddDays(-options.Value.KeepUnreadDays),
            cancellationToken);
    }
}
