using Dms.SharedKernel;

namespace Dms.Notifications.Domain;

public readonly record struct NotificationId(Guid Value)
{
    public static NotificationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>One in-app notification for one user. It is only ever read and then marked read.</summary>
public sealed class Notification : Entity<NotificationId>
{
    public const int MaxTypeLength = 64;

    private Notification()
    {
    }

    private Notification(
        NotificationId id,
        UserId userId,
        string type,
        UserId? actorId,
        Guid? documentId,
        Guid? versionId,
        string payload,
        DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        Type = type;
        ActorId = actorId;
        DocumentId = documentId;
        VersionId = versionId;
        Payload = payload;
        CreatedAt = now;
    }

    public UserId UserId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    /// <summary>Who caused it; null for the system (an overdue task).</summary>
    public UserId? ActorId { get; private set; }

    public Guid? DocumentId { get; private set; }

    public Guid? VersionId { get; private set; }

    /// <summary>JSON: display-only facts captured when the event happened.</summary>
    public string Payload { get; private set; } = "{}";

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public static Notification Create(
        UserId userId,
        string type,
        UserId? actorId,
        Guid? documentId,
        Guid? versionId,
        string payload,
        DateTimeOffset now) =>
        string.IsNullOrWhiteSpace(type) || type.Length > MaxTypeLength
            ? throw new ArgumentException("A notification type is required and short.", nameof(type))
            : new(NotificationId.New(), userId, type, actorId, documentId, versionId, payload, now);
}
