using Dms.Application;
using Dms.Audit.Application;
using Dms.Audit.Contracts;
using Dms.Audit.Domain;
using Dms.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Audit.Infrastructure;

/// <summary>The configured sealing keys: the current one seals, all of them verify.</summary>
public sealed class AuditSealKeys
{
    private readonly Dictionary<string, AuditSealKey> _byId = [];

    public AuditSealKeys(IOptions<AuditOptions> options)
    {
        var settings = options.Value;
        if (!string.IsNullOrWhiteSpace(settings.SealKey))
        {
            Current = Parse(settings.SealKey, "Dms:Audit:SealKey");
            _byId[Current.Id] = Current;
        }

        foreach (var previous in settings.PreviousSealKeys.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var key = Parse(previous, "Dms:Audit:PreviousSealKeys");
            _byId.TryAdd(key.Id, key);
        }
    }

    /// <summary>Null when no key is configured: seals are then plain SHA-256.</summary>
    public AuditSealKey? Current { get; }

    public AuditSealKey? Find(string? id) => id is not null && _byId.TryGetValue(id, out var key) ? key : null;

    private static AuditSealKey Parse(string value, string setting)
    {
        try
        {
            return AuditSealKey.FromBase64(value);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidOperationException($"{setting} must be base64 of at least 32 random bytes.", exception);
        }
    }
}

/// <summary>
/// Seals the audit log period by period and checks the seals again (see <see cref="AuditSeal"/>).
/// Rows are streamed in (occurred_at, id) order, the order both sides use, so a busy period never
/// sits in memory.
/// </summary>
public sealed class AuditSealing(
    AuditDbContext context,
    AuditSealKeys keys,
    IOptions<AuditOptions> options,
    TimeProvider timeProvider,
    ILogger<AuditSealing> logger) : IAuditSealing
{
    /// <summary>Enough to catch up a day or two of hourly seals per run without holding the lock for long.</summary>
    private const int MaxSealsPerRun = 500;

    public async Task<int> SealAsync(CancellationToken cancellationToken)
    {
        // One sealer at a time, across instances. The job runs inside a transaction, which holds the lock.
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('dms.audit.seal'))", cancellationToken);

        var settings = options.Value;
        var period = settings.SealPeriod;
        if (period < TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException("Dms:Audit:SealPeriod must be at least a minute.");
        }

        var last = await context.Seals.AsNoTracking().OrderByDescending(seal => seal.Sequence).FirstOrDefaultAsync(cancellationToken);
        DateTimeOffset start;
        if (last is null)
        {
            var earliest = await context.AuditEntries.MinAsync(entry => (DateTimeOffset?)entry.OccurredAt, cancellationToken);
            if (earliest is null)
            {
                return 0;
            }

            start = Floor(earliest.Value, period);
        }
        else
        {
            start = last.PeriodEnd;
        }

        var now = timeProvider.GetUtcNow();
        var cutoff = now - settings.SealGrace;
        var key = keys.Current;
        var added = 0;

        while (start + period <= cutoff && added < MaxSealsPerRun)
        {
            var end = start + period;
            var (digest, count) = await DigestAsync(start, end, cancellationToken);
            var seal = AuditSeal.Create(start, end, count, digest, last, key, now);
            context.Seals.Add(seal);
            await context.SaveChangesAsync(cancellationToken);

            last = seal;
            start = end;
            added++;
        }

        if (added > 0)
        {
            logger.LogInformation("Sealed {Count} audit period(s) up to {Until:O}.", added, start);
        }

        return added;
    }

    public async Task<SealVerificationDto> VerifyAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var query = context.Seals.AsNoTracking();
        if (from is { } lower)
        {
            query = query.Where(seal => seal.PeriodEnd > lower);
        }

        if (to is { } upper)
        {
            query = query.Where(seal => seal.PeriodStart < upper);
        }

        var seals = await query.OrderBy(seal => seal.Sequence).ToListAsync(cancellationToken);
        var problems = new List<SealProblemDto>();
        long rows = 0;

        if (seals.Count == 0)
        {
            return Result(true, 0, 0);
        }

        var first = seals[0];
        var previous = await context.Seals.AsNoTracking()
            .Where(seal => seal.Sequence < first.Sequence)
            .OrderByDescending(seal => seal.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

        // From the very beginning: nothing may be dated before the first seal.
        if (previous is null)
        {
            var early = await context.AuditEntries.CountAsync(entry => entry.OccurredAt < first.PeriodStart, cancellationToken);
            if (early > 0)
            {
                problems.Add(new SealProblemDto(null, DateTimeOffset.MinValue, first.PeriodStart, SealProblemKind.RowsBeforeFirstSeal,
                    $"{early} row(s) are dated before the first seal."));
            }
        }

        var keyedSeen = previous?.Algorithm == AuditSeal.KeyedAlgorithm;
        foreach (var seal in seals)
        {
            void Add(SealProblemKind kind, string detail) =>
                problems.Add(new SealProblemDto(seal.Sequence, seal.PeriodStart, seal.PeriodEnd, kind, detail));

            if (previous is null)
            {
                if (seal.PreviousHash is not null)
                {
                    Add(SealProblemKind.ChainBroken, "The first seal claims a predecessor.");
                }
            }
            else if (seal.PeriodStart != previous.PeriodEnd)
            {
                Add(SealProblemKind.ChainBroken, $"Starts at {seal.PeriodStart:O} but the seal before ends at {previous.PeriodEnd:O}.");
            }
            else if (seal.PreviousHash is null || !seal.PreviousHash.AsSpan().SequenceEqual(previous.SealHash))
            {
                Add(SealProblemKind.ChainBroken, "Does not carry the previous seal's hash.");
            }

            AuditSealKey? key = null;
            var checkHash = true;
            if (seal.Algorithm == AuditSeal.KeyedAlgorithm)
            {
                keyedSeen = true;
                key = keys.Find(seal.KeyId);
                if (key is null)
                {
                    Add(SealProblemKind.UnknownKey, $"Sealed with key {seal.KeyId}, which is not configured.");
                    checkHash = false;
                }
            }
            else if (keyedSeen)
            {
                // Swapping keyed seals for recomputed plain ones would otherwise pass.
                Add(SealProblemKind.SealInvalid, "An unkeyed seal after keyed ones.");
            }

            if (checkHash)
            {
                var expected = AuditSealHasher.SealHash(seal.PeriodStart, seal.PeriodEnd, seal.RowCount, seal.RowsDigest, seal.PreviousHash, key);
                if (!expected.AsSpan().SequenceEqual(seal.SealHash))
                {
                    Add(SealProblemKind.SealInvalid, "The seal's own hash does not match its contents.");
                }
            }

            var (digest, count) = await DigestAsync(seal.PeriodStart, seal.PeriodEnd, cancellationToken);
            rows += count;
            if (count != seal.RowCount)
            {
                Add(SealProblemKind.RowsChanged, $"Sealed {seal.RowCount} row(s), found {count}.");
            }
            else if (!digest.AsSpan().SequenceEqual(seal.RowsDigest))
            {
                Add(SealProblemKind.RowsChanged, "The rows differ from what was sealed.");
            }

            previous = seal;
        }

        if (problems.Count > 0)
        {
            logger.LogCritical("Audit seal verification found {Count} problem(s); the first: {Problem}.", problems.Count, problems[0]);
        }

        return Result(problems.Count == 0, seals.Count, rows);

        SealVerificationDto Result(bool intact, int sealCount, long rowCount)
        {
            var checkedAt = timeProvider.GetUtcNow();
            return new SealVerificationDto(intact, sealCount, rowCount, from, to, problems, checkedAt, Proof(intact, checkedAt.UtcTicks));
        }
    }

    public async Task<SealStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var count = await context.Seals.CountAsync(cancellationToken);
        var firstFrom = await context.Seals.MinAsync(seal => (DateTimeOffset?)seal.PeriodStart, cancellationToken);
        var latest = await context.Seals.AsNoTracking().OrderByDescending(seal => seal.Sequence).FirstOrDefaultAsync(cancellationToken);
        var recent = await context.AuditEntries.AsNoTracking()
            .Where(entry => entry.Action == AuditActions.AuditSealsVerified)
            .OrderByDescending(entry => entry.OccurredAt)
            .Select(entry => new { entry.OccurredAt, entry.Outcome, entry.Metadata })
            .Take(50)
            .ToListAsync(cancellationToken);

        // With a key, only checks that prove themselves count, and the newest proven time wins:
        // a forged row has no valid proof, and a copied old one carries its old time.
        var verified = recent
            .Select(entry => (Intact: entry.Outcome == "SUCCESS", At: (DateTimeOffset?)entry.OccurredAt, Metadata: entry.Metadata))
            .Select(entry => keys.Current is null ? entry : Proven(entry.Intact, entry.Metadata) is { } at ? entry with { At = at } : entry with { At = null })
            .Where(entry => entry.At is not null)
            .OrderByDescending(entry => entry.At)
            .Select(entry => new { OccurredAt = entry.At!.Value, entry.Intact })
            .FirstOrDefault();

        return new SealStatusDto(
            count,
            firstFrom,
            latest?.PeriodEnd,
            latest?.Algorithm,
            keys.Current is not null,
            keys.Current?.Id,
            verified?.OccurredAt,
            verified?.Intact);
    }

    private string? Proof(bool intact, long checkedAtTicks) =>
        keys.Current is { } key
            ? Convert.ToBase64String(key.Sign(System.Text.Encoding.UTF8.GetBytes($"dms-audit-verified-v1|{intact}|{checkedAtTicks}")))
            : null;

    /// <summary>The time of a verification record whose proof matches its outcome, or null.</summary>
    private DateTimeOffset? Proven(bool intact, string metadata)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(metadata);
            var root = document.RootElement;
            if (!root.TryGetProperty("checkedAt", out var at) || !at.TryGetInt64(out var ticks)
                || !root.TryGetProperty("proof", out var proof) || proof.GetString() is not { } given
                || ticks is < 0 or > 3155378975999999999)
            {
                return null;
            }

            var expected = Proof(intact, ticks);
            return expected is not null
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(expected),
                    System.Text.Encoding.UTF8.GetBytes(given))
                ? new DateTimeOffset(ticks, TimeSpan.Zero)
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task<(byte[] Digest, long Count)> DigestAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        using var digest = new AuditSealHasher.RowDigest();
        var rows = context.AuditEntries.AsNoTracking()
            .Where(entry => entry.OccurredAt >= start && entry.OccurredAt < end)
            .OrderBy(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Id)
            .Select(entry => new SealedRow(
                entry.Id.Value,
                entry.OccurredAt,
                entry.ActorType,
                (Guid?)entry.UserId!.Value.Value,
                entry.ShareLinkId,
                entry.Action,
                entry.Outcome,
                entry.EntityType,
                entry.EntityId,
                entry.DocumentId,
                entry.VersionId,
                entry.IpAddress,
                entry.UserAgent,
                entry.CorrelationId,
                entry.Metadata))
            .AsAsyncEnumerable();

        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            digest.Add(row);
        }

        return (digest.Finish(), digest.Count);
    }

    private static DateTimeOffset Floor(DateTimeOffset value, TimeSpan period) =>
        new(value.UtcTicks - (value.UtcTicks % period.Ticks), TimeSpan.Zero);
}

/// <summary>Seals complete periods of the log (every quarter of an hour; each run catches up).</summary>
public sealed class AuditSealJob(IAuditSealing sealing) : IJobHandler
{
    public const string Type = "audit.seal";

    public string JobType => Type;

    public Task HandleAsync(string payload, CancellationToken cancellationToken) => sealing.SealAsync(cancellationToken);
}

/// <summary>
/// Re-checks the recent seals once a day and audits the outcome. A failure is logged as critical;
/// the audit viewer shows the last outcome.
/// </summary>
public sealed class AuditVerifyJob(
    IAuditSealing sealing,
    IAuditWriter audit,
    IOptions<AuditOptions> options,
    TimeProvider timeProvider) : IJobHandler
{
    public const string Type = "audit.verify-seals";

    public string JobType => Type;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var from = timeProvider.GetUtcNow().AddDays(-options.Value.VerifyDays);
        var result = await sealing.VerifyAsync(from, null, cancellationToken);
        await audit.WriteAsync(SealAudit.Verified(result, AuditActorType.System), cancellationToken);
    }
}
