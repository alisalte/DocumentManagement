using Dms.SharedKernel;

namespace Dms.Application;

/// <summary>
/// The caller of the current operation. Populated from the HTTP context in the API host and from a
/// system principal in the migrator and the background worker.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    UserId? UserId { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    string CorrelationId { get; }
}

/// <summary>
/// One transaction spanning every module DbContext of the current scope. Modules keep their own
/// DbContext, but they share a single connection so that a command touching several modules
/// (for example: write an ACL entry + an audit row + queue a job) is atomic.
/// </summary>
public interface IUnitOfWork
{
    bool HasActiveTransaction { get; }

    Task BeginAsync(CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}

public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>Enqueues background work inside the caller's transaction (transactional outbox).</summary>
public interface IJobQueue
{
    Task EnqueueAsync(JobRequest request, CancellationToken cancellationToken);
}

/// <param name="Type">Handler key, for example <c>audit.ensure-partitions</c>.</param>
/// <param name="Payload">Serialised as JSONB.</param>
/// <param name="IdempotencyKey">When set, an identical queued/running job is not enqueued twice.</param>
public sealed record JobRequest(
    string Type,
    object? Payload = null,
    string Queue = "default",
    int Priority = 0,
    DateTimeOffset? RunAfter = null,
    string? IdempotencyKey = null,
    int MaxAttempts = 5);

public interface IJobHandler
{
    string JobType { get; }

    Task HandleAsync(string payload, CancellationToken cancellationToken);
}
