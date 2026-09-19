using System.Text.Json;
using Dms.Application;
using Microsoft.EntityFrameworkCore;

namespace Dms.Infrastructure.Jobs;

/// <summary>
/// Enqueues work through the module DbContext that shares the caller's transaction, so the job is
/// committed together with the change that caused it.
/// </summary>
public sealed class JobQueue(InfraDbContext context, TimeProvider timeProvider) : IJobQueue
{
    public async Task EnqueueAsync(JobRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();

        if (request.IdempotencyKey is not null)
        {
            var pending = await context.Jobs
                .AnyAsync(
                    job => job.IdempotencyKey == request.IdempotencyKey
                        && (job.Status == JobStatus.Queued || job.Status == JobStatus.Running),
                    cancellationToken);

            if (pending)
            {
                return;
            }
        }

        var payload = request.Payload is null ? "{}" : JsonSerializer.Serialize(request.Payload);
        context.Jobs.Add(JobRecord.Create(
            request.Type,
            payload,
            request.Queue,
            request.Priority,
            request.RunAfter ?? now,
            request.IdempotencyKey,
            request.MaxAttempts,
            now));
    }
}
