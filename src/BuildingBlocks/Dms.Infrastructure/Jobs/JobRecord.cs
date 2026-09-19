namespace Dms.Infrastructure.Jobs;

public static class JobStatus
{
    public const string Queued = "QUEUED";
    public const string Running = "RUNNING";
    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";
    public const string Dead = "DEAD";
}

/// <summary>
/// A unit of background work. Rows are inserted inside the caller's transaction, which makes this
/// table the transactional outbox: work can never be queued for a change that was rolled back, and
/// a committed change always has its follow-up work queued.
/// </summary>
public sealed class JobRecord
{
    public Guid Id { get; private set; }

    public string Queue { get; private set; } = "default";

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = "{}";

    public string? IdempotencyKey { get; private set; }

    public string Status { get; private set; } = JobStatus.Queued;

    public int Priority { get; private set; }

    public DateTimeOffset RunAfter { get; private set; }

    public int Attempts { get; private set; }

    public int MaxAttempts { get; private set; } = 5;

    public string? LockedBy { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static JobRecord Create(
        string type,
        string payload,
        string queue,
        int priority,
        DateTimeOffset runAfter,
        string? idempotencyKey,
        int maxAttempts,
        DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Payload = payload,
            Queue = queue,
            Priority = priority,
            RunAfter = runAfter,
            IdempotencyKey = idempotencyKey,
            MaxAttempts = maxAttempts,
            Status = JobStatus.Queued,
            CreatedAt = now,
        };
}
