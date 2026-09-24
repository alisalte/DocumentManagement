using Dms.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Infrastructure.Jobs;

public sealed class JobWorkerOptions
{
    public bool Enabled { get; set; }

    public string Queue { get; set; } = "default";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Recurring work: enqueued by <see cref="RecurringJobScheduler"/> on the given interval.</summary>
public sealed record RecurringJob(string Type, TimeSpan Interval);

/// <summary>
/// Takes one due job and runs it in its own scope and transaction. Shared by the background
/// worker and by tests, which run the queue on demand instead of racing a worker.
/// </summary>
public sealed class JobRunner(
    IServiceScopeFactory scopeFactory,
    JobStore store,
    IOptions<JobWorkerOptions> options,
    ILogger<JobRunner> logger)
{
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}";

    /// <summary>Runs at most one job; false when nothing was due.</summary>
    public async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await store.ReleaseExpiredLeasesAsync(cancellationToken);
        var job = await store.DequeueAsync(_workerId, settings.Queue, settings.Lease, cancellationToken);
        if (job is null)
        {
            return false;
        }

        await RunAsync(job, settings, cancellationToken);
        return true;
    }

    /// <summary>Runs due jobs until none is left (or the limit is reached). Jobs may queue more jobs.</summary>
    public async Task<int> RunUntilIdleAsync(int limit, CancellationToken cancellationToken)
    {
        var ran = 0;
        while (ran < limit && await RunNextAsync(cancellationToken))
        {
            ran++;
        }

        return ran;
    }

    private async Task RunAsync(DequeuedJob job, JobWorkerOptions settings, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IJobHandler>()
            .FirstOrDefault(candidate => candidate.JobType == job.Type);

        if (handler is null)
        {
            await store.MarkFailedAsync(
                job.Id,
                $"No handler registered for job type '{job.Type}'.",
                settings.BaseRetryDelay,
                cancellationToken);
            return;
        }

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        try
        {
            await unitOfWork.BeginAsync(cancellationToken);
            await handler.HandleAsync(job.Payload, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            await store.MarkSucceededAsync(job.Id, cancellationToken);
        }
        catch (Exception exception)
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            var delay = settings.BaseRetryDelay * Math.Pow(2, Math.Min(job.Attempts, 6));
            logger.LogError(exception, "Job {JobId} of type {JobType} failed (attempt {Attempt}).",
                job.Id, job.Type, job.Attempts);
            await store.MarkFailedAsync(job.Id, exception.ToString(), delay, CancellationToken.None);
        }
    }
}

public sealed class JobWorker(
    JobRunner runner,
    IOptions<JobWorkerOptions> options,
    ILogger<JobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        logger.LogInformation("Job worker started on queue {Queue}.", settings.Queue);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await runner.RunNextAsync(stoppingToken))
                {
                    await Task.Delay(settings.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Job worker loop failed; backing off.");
                await Task.Delay(settings.PollInterval, CancellationToken.None);
            }
        }
    }
}

public sealed class RecurringJobScheduler(
    JobStore store,
    IEnumerable<RecurringJob> jobs,
    TimeProvider timeProvider,
    ILogger<RecurringJobScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var definitions = jobs.ToList();
        if (definitions.Count == 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var job in definitions)
            {
                try
                {
                    // The bucket turns "every N minutes" into a stable key, so concurrent schedulers
                    // cannot enqueue the same tick twice.
                    var bucket = timeProvider.GetUtcNow().ToUnixTimeSeconds() / (long)job.Interval.TotalSeconds;
                    await store.TryEnqueueRecurringAsync(job.Type, $"{job.Type}:{bucket}", stoppingToken);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Failed to schedule recurring job {JobType}.", job.Type);
                }
            }
        }
    }
}
