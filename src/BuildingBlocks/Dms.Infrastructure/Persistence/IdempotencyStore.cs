using Dms.Application;
using Dms.Infrastructure.Jobs;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Dms.Infrastructure.Persistence;

public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
    }

    public IdempotencyRecord(
        Guid userId,
        string key,
        string requestHash,
        string response,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        UserId = userId;
        Key = key;
        RequestHash = requestHash;
        Response = response;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public string Response { get; private set; } = "null";

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }
}

public sealed class IdempotencyStore(InfraDbContext context, TimeProvider timeProvider) : IIdempotencyStore
{
    /// <summary>Long enough to cover any realistic client retry window.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    public async Task<IdempotencyLookup> FindAsync(
        UserId userId,
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var record = await context.IdempotencyKeys.AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.UserId == userId.Value && item.Key == key && item.ExpiresAt > now,
                cancellationToken);

        if (record is null)
        {
            return new IdempotencyLookup.NotSeen();
        }

        return record.RequestHash == requestHash
            ? new IdempotencyLookup.Replay(record.Response)
            : new IdempotencyLookup.Mismatch();
    }

    public void Record(UserId userId, string key, string requestHash, string resultJson)
    {
        var now = timeProvider.GetUtcNow();
        context.IdempotencyKeys.Add(new IdempotencyRecord(
            userId.Value,
            key,
            requestHash,
            resultJson,
            now,
            now.Add(Retention)));
    }
}
