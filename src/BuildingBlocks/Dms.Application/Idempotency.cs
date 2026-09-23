using Dms.SharedKernel;

namespace Dms.Application;

/// <summary>
/// Outcome of looking up an <c>Idempotency-Key</c>. Mobile clients retry on flaky networks, and a
/// retried "create document" must return the first document rather than file a second copy.
/// </summary>
public abstract record IdempotencyLookup
{
    public sealed record NotSeen : IdempotencyLookup;

    /// <summary>The same key and the same request were already handled; replay the stored result.</summary>
    public sealed record Replay(string ResultJson) : IdempotencyLookup;

    /// <summary>The key was already used for a different request.</summary>
    public sealed record Mismatch : IdempotencyLookup;
}

/// <summary>
/// Keys are scoped per user, and the record is written in the command's own transaction, so the
/// stored result exists exactly when the change it describes was committed.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyLookup> FindAsync(
        UserId userId,
        string key,
        string requestHash,
        CancellationToken cancellationToken);

    void Record(UserId userId, string key, string requestHash, string resultJson);
}
