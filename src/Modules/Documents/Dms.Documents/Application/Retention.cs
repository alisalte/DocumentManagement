using Dms.SharedKernel;

namespace Dms.Documents.Application;

/// <summary>
/// Phase 10 (D12): purge wait after soft-delete, driven by document-type settings.
/// Legal-hold on individual documents attaches here next.
/// </summary>
public static class RetentionGate
{
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
}
