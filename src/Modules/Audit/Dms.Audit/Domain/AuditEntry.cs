using Dms.Audit.Contracts;
using Dms.SharedKernel;

namespace Dms.Audit.Domain;

/// <summary>
/// An append-only audit record. There are no mutating methods on purpose, and the database refuses
/// UPDATE and DELETE from the application role as well.
/// </summary>
public sealed class AuditEntry : Entity<AuditEntryId>
{
    private AuditEntry()
    {
    }

    private AuditEntry(AuditEntryId id, DateTimeOffset occurredAt)
        : base(id) => OccurredAt = occurredAt;

    public DateTimeOffset OccurredAt { get; private set; }

    public string ActorType { get; private set; } = nameof(AuditActorType.User);

    public UserId? UserId { get; private set; }

    public Guid? ShareLinkId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string Outcome { get; private set; } = nameof(AuditOutcome.Success);

    public string? EntityType { get; private set; }

    public Guid? EntityId { get; private set; }

    public Guid? DocumentId { get; private set; }

    public Guid? VersionId { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string? CorrelationId { get; private set; }

    /// <summary>Free-form context, stored as JSONB.</summary>
    public string Metadata { get; private set; } = "{}";

    public static AuditEntry Create(
        AuditRecord record,
        UserId? actor,
        string? ipAddress,
        string? userAgent,
        string? correlationId,
        string metadataJson,
        DateTimeOffset occurredAt) => new(AuditEntryId.New(), occurredAt)
        {
            Action = record.Action,
            Outcome = record.Outcome.ToString().ToUpperInvariant(),
            ActorType = record.ActorType.ToString().ToUpperInvariant(),
            UserId = record.UserId ?? actor,
            ShareLinkId = record.ShareLinkId,
            EntityType = record.EntityType,
            EntityId = record.EntityId,
            DocumentId = record.DocumentId,
            VersionId = record.VersionId,
            IpAddress = Truncate(ipAddress, 64),
            UserAgent = Truncate(userAgent, 512),
            CorrelationId = Truncate(correlationId, 64),
            Metadata = metadataJson,
        };

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
