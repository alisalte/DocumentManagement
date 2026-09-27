using Dms.Documents.Contracts;
using Dms.SharedKernel;

namespace Dms.Documents.Application;

/// <summary>Phase 10 (D12 + 10.2/10.3): purge waits, retention, and legal hold.</summary>
public static class RetentionGate
{
    public static readonly Error OnLegalHold = Error.Conflict(
        "purge.legal_hold",
        "This document is under Legal Hold and cannot be purged or destroyed.");

    public static Result EnsurePurgeAllowed(
        DateTimeOffset? deletedAt,
        int? retentionDaysAfterDelete,
        DateTimeOffset now)
    {
        if (retentionDaysAfterDelete is null or <= 0 || deletedAt is null)
        {
            return Result.Success();
        }

        var earliest = deletedAt.Value.AddDays(retentionDaysAfterDelete.Value);
        if (now < earliest)
        {
            return Result.Failure(Error.Conflict(
                "purge.retention",
                $"This document type keeps soft-deleted items for {retentionDaysAfterDelete.Value} day(s). Purge is allowed after {earliest:O}."));
        }

        return Result.Success();
    }

    public static Result EnsureNotOnHold(bool onHold) =>
        onHold ? Result.Failure(OnLegalHold) : Result.Success();
}
