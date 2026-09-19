using Dms.Application;
using Dms.Infrastructure.Events;

namespace Dms.Infrastructure.Persistence;

public sealed class UnitOfWork(DbSession session, DomainEventDispatcher domainEvents) : IUnitOfWork
{
    public bool HasActiveTransaction => session.HasActiveTransaction;

    public Task BeginAsync(CancellationToken cancellationToken) => session.BeginAsync(cancellationToken);

    /// <summary>
    /// Domain events are dispatched before the commit, inside the same transaction, so handlers that
    /// write audit rows or queue jobs are part of the same atomic change.
    /// </summary>
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await domainEvents.DispatchAsync(session.Contexts, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
        await session.CommitAsync(cancellationToken);
    }

    public Task RollbackAsync(CancellationToken cancellationToken) => session.RollbackAsync(cancellationToken);
}
