using Dms.Application;
using Dms.Infrastructure;
using Dms.Infrastructure.Persistence;
using Dms.Notifications.Application;
using Dms.Notifications.Contracts;
using Dms.Notifications.Infrastructure.Persistence;
using Dms.SharedKernel;
using Dms.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Dms.Notifications.Infrastructure;

public static class NotificationsModule
{
    /// <summary>Last, after audit (40): section 9's order ends with notify.</summary>
    public const int MigrationOrder = 45;

    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddDmsModuleDbContext<NotificationsDbContext>(MigrationOrder, NotificationsDbContext.Schema);
        services.AddOptions<NotificationOptions>().BindConfiguration(NotificationOptions.SectionName);

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationSender, NotificationSender>();
        // Phase 10: replace or add a real SMTP channel; Null keeps in-app-only behaviour.
        services.AddSingleton<INotificationChannel, NullNotificationChannel>();

        services.AddScoped<IQueryHandler<ListNotificationsQuery, Result<NotificationPageDto>>, ListNotificationsHandler>();
        services.AddScoped<IQueryHandler<CountUnreadNotificationsQuery, Result<int>>, CountUnreadNotificationsHandler>();
        services.AddScoped<ICommandHandler<MarkNotificationsReadCommand, Result<int>>, MarkNotificationsReadHandler>();

        services.AddScoped<IJobHandler, NotificationCleanupJob>();
        services.AddRecurringJob(NotificationCleanupJob.Type, TimeSpan.FromDays(1));

        return services;
    }

    public sealed record MarkReadRequest(IReadOnlyList<Guid>? Ids);

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization();

        notifications.MapGet("", async (bool? unreadOnly, string? cursor, int? take, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListNotificationsQuery(unreadOnly ?? false, cursor, take ?? 20), ct)).ToHttpResult())
            .WithSummary("The caller's notifications, newest first, with the unread count. Page with 'cursor' (the previous page's nextCursor).");

        notifications.MapGet("/unread-count", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new CountUnreadNotificationsQuery(), ct)).ToHttpResult(count => Results.Ok(new { count })))
            .WithSummary("How many unread notifications the caller has (for the bell).");

        notifications.MapPost("/read", async (MarkReadRequest? request, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new MarkNotificationsReadCommand(request?.Ids), ct)).ToHttpResult(count => Results.Ok(new { count })))
            .WithSummary("Mark the given notifications read, or all of them when no ids are given.");

        return endpoints;
    }
}

public sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) => new(
        DesignTimeSupport.CreateOptions<NotificationsDbContext>(NotificationsDbContext.Schema),
        DesignTimeSupport.CreateSession());
}
