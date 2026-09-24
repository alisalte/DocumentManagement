using Dms.SharedKernel;

namespace Dms.Notifications.Contracts;

/// <summary>
/// What a notification is about. The browser turns the type and the payload into text, in the
/// user's language, so the stored row stays language neutral.
/// </summary>
public static class NotificationTypes
{
    /// <summary>A workflow task is waiting for the recipient (directly, or through their group or role).</summary>
    public const string TaskAssigned = "TASK_ASSIGNED";

    /// <summary>A task the recipient could act on is past its due time.</summary>
    public const string TaskOverdue = "TASK_OVERDUE";

    /// <summary>A review the recipient started or whose version they wrote has ended; the payload says how.</summary>
    public const string WorkflowFinished = "WORKFLOW_FINISHED";

    /// <summary>Someone shared a version with the recipient.</summary>
    public const string DocumentShared = "DOCUMENT_SHARED";
}

/// <param name="Payload">
/// Small, display-only facts captured when the event happened (a title, a version label, a step).
/// Nothing in it grants anything: following a notification goes through the usual checks.
/// </param>
public sealed record NotificationMessage(
    string Type,
    IReadOnlyCollection<UserId> Recipients,
    Guid? DocumentId,
    Guid? VersionId,
    IReadOnlyDictionary<string, object?> Payload);

/// <summary>
/// Writes in-app notifications in the caller's transaction, so a notification exists exactly when
/// the change it announces committed. Whoever caused the event is not told about it, and inactive
/// users get nothing. Email can later be another channel behind the same call.
/// </summary>
public interface INotificationSender
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken);
}
