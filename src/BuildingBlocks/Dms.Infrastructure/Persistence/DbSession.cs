using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Dms.Infrastructure.Persistence;

/// <summary>
/// Holds the single physical connection shared by every module DbContext in the current scope,
/// plus the ambient transaction.
///
/// Why one connection: each module owns its own schema and its own DbContext, but a command often
/// spans modules (write an ACL row, write the audit row, queue the job). Sharing one connection
/// lets all of those commit or roll back together without a distributed transaction.
///
/// Consequence: contexts must not be used concurrently within a scope. PostgreSQL has no MARS, so
/// handlers await their database calls one at a time. This is enforced by convention, not the type
/// system; the integration tests cover the transactional behaviour.
/// </summary>
public sealed class DbSession(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    private readonly List<DbContext> _contexts = [];
    private NpgsqlConnection? _connection;
    private DbTransaction? _transaction;

    /// <summary>The shared connection. Created lazily and opened on first use by EF or by <see cref="BeginAsync"/>.</summary>
    public NpgsqlConnection Connection => _connection ??= dataSource.CreateConnection();

    public bool HasActiveTransaction => _transaction is not null;

    /// <summary>Called from each module DbContext constructor.</summary>
    public void Register(DbContext context)
    {
        _contexts.Add(context);
        if (_transaction is not null)
        {
            context.Database.UseTransaction(_transaction);
        }
    }

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            return;
        }

        if (Connection.State != ConnectionState.Open)
        {
            await Connection.OpenAsync(cancellationToken);
        }

        _transaction = await Connection.BeginTransactionAsync(cancellationToken);
        foreach (var context in _contexts)
        {
            await context.Database.UseTransactionAsync(_transaction, cancellationToken);
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        var affected = 0;
        foreach (var context in _contexts)
        {
            if (context.ChangeTracker.HasChanges())
            {
                affected += await context.SaveChangesAsync(cancellationToken);
            }
        }

        return affected;
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("There is no active transaction to commit.");
        }

        await _transaction.CommitAsync(cancellationToken);
        await DisposeTransactionAsync();
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        await DisposeTransactionAsync();
    }

    public IReadOnlyList<DbContext> Contexts => _contexts;

    public async ValueTask DisposeAsync()
    {
        await DisposeTransactionAsync();
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    private async Task DisposeTransactionAsync()
    {
        if (_transaction is not null)
        {
            // Detach every context, or the next query in this scope (a query handler that wrote
            // an audit row in its own short transaction, say) runs against the finished one.
            foreach (var context in _contexts)
            {
                await context.Database.UseTransactionAsync(null);
            }

            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
