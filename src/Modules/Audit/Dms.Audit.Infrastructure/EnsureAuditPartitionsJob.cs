using Dms.Application;
using Dms.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dms.Audit.Infrastructure;

/// <summary>
/// Creates next month's audit partition ahead of time. Audit volume is the highest in the system
/// (every view is a row), so partitions keep indexes small and make retention a matter of detaching
/// a partition rather than a mass delete.
/// </summary>
public sealed class EnsureAuditPartitionsJob(
    AuditDbContext context,
    TimeProvider timeProvider,
    ILogger<EnsureAuditPartitionsJob> logger) : IJobHandler
{
    public const string Type = "audit.ensure-partitions";

    public string JobType => Type;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // Current month plus the next three, so a stalled worker cannot cause a failed insert.
        for (var offset = 0; offset <= 3; offset++)
        {
            var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(offset);
            await context.Database.ExecuteSqlRawAsync(
                "SELECT audit.ensure_partition({0}::date)",
                [month.Date],
                cancellationToken);
        }

        logger.LogInformation("Audit partitions verified up to {Month:yyyy-MM}.", now.AddMonths(3));
    }
}
