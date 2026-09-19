using System.Collections.Concurrent;
using System.Reflection;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dms.Application;

/// <summary>
/// Minimal command/query dispatcher. Deliberately hand written instead of taking a mediator
/// dependency: MediatR moved to a commercial licence and the pipeline we need is three steps long.
///
/// Command pipeline: validate -> begin transaction -> handle -> dispatch domain events -> commit.
/// An expected failure (a <see cref="SharedKernel.Result"/> carrying an error) still commits, so
/// that audit rows and counters written by the handler survive. Only exceptions roll back.
/// </summary>
public sealed class Dispatcher(IServiceProvider serviceProvider, IUnitOfWork unitOfWork, ILogger<Dispatcher> logger)
    : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> HandleMethods = new();

    public async Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var commandType = command.GetType();

        await ValidateAsync(command, commandType, cancellationToken);

        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(commandType, typeof(TResult));
        var handler = serviceProvider.GetService(handlerType)
            ?? throw new InvalidOperationException($"No command handler registered for {commandType.Name}.");

        var ownsTransaction = !unitOfWork.HasActiveTransaction;
        if (ownsTransaction)
        {
            await unitOfWork.BeginAsync(cancellationToken);
        }

        try
        {
            var result = await InvokeAsync<TResult>(handler, handlerType, command, cancellationToken);

            if (ownsTransaction)
            {
                await unitOfWork.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Command {Command} failed and was rolled back.", commandType.Name);
            if (ownsTransaction)
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
    }

    public async Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var queryType = query.GetType();
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(queryType, typeof(TResult));
        var handler = serviceProvider.GetService(handlerType)
            ?? throw new InvalidOperationException($"No query handler registered for {queryType.Name}.");

        return await InvokeAsync<TResult>(handler, handlerType, query, cancellationToken);
    }

    private static Task<TResult> InvokeAsync<TResult>(
        object handler,
        Type handlerType,
        object message,
        CancellationToken cancellationToken)
    {
        var method = HandleMethods.GetOrAdd(handlerType, static type => type.GetMethod("HandleAsync")!);
        return (Task<TResult>)method.Invoke(handler, [message, cancellationToken])!;
    }

    private async Task ValidateAsync(object message, Type messageType, CancellationToken cancellationToken)
    {
        var validatorType = typeof(IValidator<>).MakeGenericType(messageType);
        var validators = serviceProvider.GetServices(validatorType).OfType<IValidator>().ToList();
        if (validators.Count == 0)
        {
            return;
        }

        var context = new ValidationContext<object>(message);
        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }
}
