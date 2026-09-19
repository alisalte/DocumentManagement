using Dms.Application;
using Dms.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dms.Infrastructure.Events;

/// <summary>
/// Collects domain events from every tracked aggregate and invokes their handlers before the commit.
/// Handlers may raise further events; the loop is bounded so a cycle cannot hang a request.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider serviceProvider)
{
    private const int MaxRounds = 5;

    public async Task DispatchAsync(IReadOnlyList<DbContext> contexts, CancellationToken cancellationToken)
    {
        for (var round = 0; round < MaxRounds; round++)
        {
            var roots = contexts
                .SelectMany(context => context.ChangeTracker.Entries<IHasDomainEvents>())
                .Select(entry => entry.Entity)
                .Where(entity => entity.DomainEvents.Count > 0)
                .ToList();

            if (roots.Count == 0)
            {
                return;
            }

            var events = roots.SelectMany(root => root.DomainEvents).ToList();
            foreach (var root in roots)
            {
                root.ClearDomainEvents();
            }

            foreach (var domainEvent in events)
            {
                var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
                foreach (var handler in serviceProvider.GetServices(handlerType))
                {
                    var method = handlerType.GetMethod("HandleAsync")!;
                    await (Task)method.Invoke(handler, [domainEvent, cancellationToken])!;
                }
            }
        }

        throw new InvalidOperationException(
            $"Domain event dispatch did not settle after {MaxRounds} rounds; check for a handler raising events in a cycle.");
    }
}
