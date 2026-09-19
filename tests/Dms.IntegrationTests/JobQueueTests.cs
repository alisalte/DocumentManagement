using Dms.Application;
using Dms.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// The job table is the transactional outbox. These tests pin down the two properties the rest of
/// the system will rely on: work is never queued for a change that rolled back, and no job is ever
/// handed to two workers.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class JobQueueTests(DmsApiFactory factory)
{
    private async Task<long> CountJobsAsync(string type)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM infra.jobs WHERE type = @type;";
        command.Parameters.AddWithValue("type", type);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task A_job_queued_in_a_rolled_back_transaction_is_not_queued_at_all()
    {
        const string type = "test.rollback";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

            await unitOfWork.BeginAsync(CancellationToken.None);
            await queue.EnqueueAsync(new JobRequest(type), CancellationToken.None);
            await unitOfWork.RollbackAsync(CancellationToken.None);
        }

        (await CountJobsAsync(type)).ShouldBe(0);
    }

    [Fact]
    public async Task A_job_queued_in_a_committed_transaction_is_picked_up_once()
    {
        const string type = "test.commit";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

            await unitOfWork.BeginAsync(CancellationToken.None);
            await queue.EnqueueAsync(new JobRequest(type, new { sample = 1 }), CancellationToken.None);
            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        (await CountJobsAsync(type)).ShouldBe(1);

        var store = factory.Services.GetRequiredService<JobStore>();
        var first = await DequeueAsync(store, type);
        first.ShouldNotBeNull();
        first.Payload.ShouldContain("sample");

        // Already leased by the first worker, so a second worker must not see it.
        (await DequeueAsync(store, type)).ShouldBeNull();

        await store.MarkSucceededAsync(first.Id, CancellationToken.None);
        (await StatusOfAsync(first.Id)).ShouldBe("SUCCEEDED");
    }

    [Fact]
    public async Task Concurrent_workers_never_receive_the_same_job()
    {
        const string type = "test.concurrent";
        const int jobCount = 12;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

            await unitOfWork.BeginAsync(CancellationToken.None);
            for (var index = 0; index < jobCount; index++)
            {
                await queue.EnqueueAsync(new JobRequest(type, new { index }), CancellationToken.None);
            }

            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        var store = factory.Services.GetRequiredService<JobStore>();

        // Four "workers" racing on FOR UPDATE SKIP LOCKED.
        var workers = Enumerable.Range(0, 4).Select(async worker =>
        {
            var taken = new List<Guid>();
            while (await store.DequeueAsync($"worker-{worker}", "default", TimeSpan.FromMinutes(1),
                       CancellationToken.None) is { } job)
            {
                if (job.Type == type)
                {
                    taken.Add(job.Id);
                }

                await store.MarkSucceededAsync(job.Id, CancellationToken.None);
            }

            return taken;
        });

        var results = await Task.WhenAll(workers);
        var claimed = results.SelectMany(ids => ids).ToList();

        claimed.Count.ShouldBe(jobCount);
        claimed.Distinct().Count().ShouldBe(jobCount);
    }

    [Fact]
    public async Task A_failed_job_is_retried_and_eventually_parked()
    {
        const string type = "test.failing";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

            await unitOfWork.BeginAsync(CancellationToken.None);
            await queue.EnqueueAsync(new JobRequest(type, MaxAttempts: 2), CancellationToken.None);
            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        var store = factory.Services.GetRequiredService<JobStore>();

        var first = await DequeueAsync(store, type);
        first.ShouldNotBeNull();
        await store.MarkFailedAsync(first.Id, "boom", TimeSpan.Zero, CancellationToken.None);
        (await StatusOfAsync(first.Id)).ShouldBe("QUEUED");

        var second = await DequeueAsync(store, type);
        second.ShouldNotBeNull();
        second.Attempts.ShouldBe(2);
        await store.MarkFailedAsync(second.Id, "boom again", TimeSpan.Zero, CancellationToken.None);

        // Out of attempts: parked as DEAD instead of spinning forever.
        (await StatusOfAsync(second.Id)).ShouldBe("DEAD");
    }

    [Fact]
    public async Task An_idempotency_key_prevents_a_duplicate_pending_job()
    {
        const string type = "test.idempotent";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

            await unitOfWork.BeginAsync(CancellationToken.None);
            await queue.EnqueueAsync(new JobRequest(type, IdempotencyKey: "only-once"), CancellationToken.None);
            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        (await CountJobsAsync(type)).ShouldBe(1);
    }

    [Fact]
    public async Task An_expired_lease_returns_the_job_to_the_queue()
    {
        const string type = "test.lease";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();

            await unitOfWork.BeginAsync(CancellationToken.None);
            await queue.EnqueueAsync(new JobRequest(type), CancellationToken.None);
            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        var store = factory.Services.GetRequiredService<JobStore>();

        // A worker that dies holds a lease that has already expired.
        var job = await DequeueAsync(store, type);
        job.ShouldNotBeNull();
        await ExpireLeaseAsync(job.Id);

        (await store.ReleaseExpiredLeasesAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
        (await StatusOfAsync(job.Id)).ShouldBe("QUEUED");
    }

    private static async Task<DequeuedJob?> DequeueAsync(JobStore store, string type)
    {
        // Other tests share the queue, so skip past jobs that are not ours.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var job = await store.DequeueAsync("test-worker", "default", TimeSpan.FromMinutes(1),
                CancellationToken.None);

            if (job is null)
            {
                return null;
            }

            if (job.Type == type)
            {
                return job;
            }

            await store.MarkSucceededAsync(job.Id, CancellationToken.None);
        }

        return null;
    }

    private async Task<string> StatusOfAsync(Guid id)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM infra.jobs WHERE id = @id;";
        command.Parameters.AddWithValue("id", id);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExpireLeaseAsync(Guid id)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE infra.jobs SET locked_until = now() - interval '1 minute' WHERE id = @id;";
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }
}
