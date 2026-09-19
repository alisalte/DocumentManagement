using System.Text.Json;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Audit.Domain;
using Dms.Audit.Infrastructure.Persistence;

namespace Dms.Audit.Infrastructure;

/// <summary>
/// Writes audit rows through the shared session, so they commit with the change they describe.
/// Failures are not swallowed: if the audit row cannot be written the operation fails, because an
/// unaudited privileged action is worse than a failed one.
/// </summary>
public sealed class AuditWriter(
    AuditDbContext context,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IAuditWriter
{
    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var metadata = record.Metadata is null or { Count: 0 }
            ? "{}"
            : JsonSerializer.Serialize(record.Metadata);

        var entry = AuditEntry.Create(
            record,
            currentUser.UserId,
            currentUser.IpAddress,
            currentUser.UserAgent,
            currentUser.CorrelationId,
            metadata,
            timeProvider.GetUtcNow());

        context.AuditEntries.Add(entry);
        return Task.CompletedTask;
    }
}
