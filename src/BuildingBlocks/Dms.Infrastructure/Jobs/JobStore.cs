using Npgsql;

namespace Dms.Infrastructure.Jobs;

public sealed record DequeuedJob(Guid Id, string Type, string Payload, int Attempts, int MaxAttempts);

/// <summary>
/// Worker-side access to the queue. Uses its own connection (not the request-scoped session) and
/// <c>FOR UPDATE SKIP LOCKED</c> so that several workers can poll the same queue without ever
/// handing the same job to two of them.
/// </summary>
public sealed class JobStore(NpgsqlDataSource dataSource)
{
    public async Task<DequeuedJob?> DequeueAsync(
        string worker,
        string queue,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE infra.jobs
               SET status = 'RUNNING',
                   locked_by = @worker,
                   locked_until = now() + @lease,
                   attempts = attempts + 1
             WHERE id = (
                     SELECT id
                       FROM infra.jobs
                      WHERE status = 'QUEUED'
                        AND queue = @queue
                        AND run_after <= now()
                      ORDER BY priority DESC, run_after
                      LIMIT 1
                      FOR UPDATE SKIP LOCKED)
            RETURNING id, type, payload, attempts, max_attempts;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("worker", worker);
        command.Parameters.AddWithValue("queue", queue);
        command.Parameters.AddWithValue("lease", lease);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DequeuedJob(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4));
    }

    public async Task MarkSucceededAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE infra.jobs
               SET status = 'SUCCEEDED', completed_at = now(), locked_by = NULL, locked_until = NULL
             WHERE id = @id;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Retries with exponential backoff until <c>max_attempts</c>, then parks the job as DEAD.</summary>
    public async Task MarkFailedAsync(Guid id, string error, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE infra.jobs
               SET status = CASE WHEN attempts >= max_attempts THEN 'DEAD' ELSE 'QUEUED' END,
                   run_after = now() + @delay,
                   last_error = @error,
                   locked_by = NULL,
                   locked_until = NULL,
                   completed_at = CASE WHEN attempts >= max_attempts THEN now() ELSE NULL END
             WHERE id = @id;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("delay", retryDelay);
        command.Parameters.AddWithValue("error", error.Length > 4000 ? error[..4000] : error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Returns jobs whose worker died (expired lease) to the queue.</summary>
    public async Task<int> ReleaseExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE infra.jobs
               SET status = 'QUEUED', locked_by = NULL, locked_until = NULL
             WHERE status = 'RUNNING' AND locked_until < now();
            """;

        await using var command = dataSource.CreateCommand(sql);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Enqueues a recurring job under a single-holder advisory lock, so only one instance in the
    /// cluster schedules a given tick. The idempotency key collapses duplicates for the period.
    /// </summary>
    public async Task<bool> TryEnqueueRecurringAsync(
        string type,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT pg_try_advisory_xact_lock(hashtext('dms.recurring'));
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand(sql, connection, transaction))
        {
            var acquired = (bool?)await lockCommand.ExecuteScalarAsync(cancellationToken) ?? false;
            if (!acquired)
            {
                return false;
            }
        }

        const string insert = """
            INSERT INTO infra.jobs (id, queue, type, payload, idempotency_key, status, priority,
                                    run_after, attempts, max_attempts, created_at)
            VALUES (@id, 'default', @type, '{}'::jsonb, @key, 'QUEUED', 0, now(), 0, 5, now())
            ON CONFLICT (idempotency_key) WHERE status IN ('QUEUED','RUNNING') DO NOTHING;
            """;

        await using (var insertCommand = new NpgsqlCommand(insert, connection, transaction))
        {
            insertCommand.Parameters.AddWithValue("id", Guid.CreateVersion7());
            insertCommand.Parameters.AddWithValue("type", type);
            insertCommand.Parameters.AddWithValue("key", idempotencyKey);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
