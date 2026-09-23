using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.Infrastructure;
using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Dms.Web;
using Dms.Workflow.Application;
using Dms.Workflow.Contracts;
using Dms.Workflow.Domain;
using Dms.Workflow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dms.Workflow.Infrastructure;

public static class WorkflowModule
{
    /// <summary>After documents (30): instances reference document versions.</summary>
    public const int MigrationOrder = 35;

    public static IServiceCollection AddWorkflowModule(this IServiceCollection services)
    {
        services.AddDmsModuleDbContext<WorkflowDbContext>(MigrationOrder, WorkflowDbContext.Schema);
        services.AddSingleton<PublishedWorkflowCache>();
        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
        services.AddScoped<IWorkflowInstanceRepository, WorkflowInstanceRepository>();

        services.AddScoped<AssigneeResolver>();
        services.AddScoped<WorkflowEngine>();
        services.AddScoped<WorkflowSelector>();
        services.AddScoped<WorkflowStarter>();
        services.AddScoped<TaskPresenter>();

        // Workflow owns what happens after a version is created, whatever the registration order.
        services.RemoveAll<IVersionCreatedHook>();
        services.AddScoped<IVersionCreatedHook, StartWorkflowOnVersionCreated>();
        services.AddScoped<ITemporaryGrantSource, WorkflowTaskGrantSource>();

        services.AddScoped<ICommandHandler<CreateWorkflowCommand, Result<Guid>>, CreateWorkflowHandler>();
        services.AddScoped<ICommandHandler<UpdateWorkflowCommand, Result>, UpdateWorkflowHandler>();
        services.AddScoped<ICommandHandler<SaveWorkflowDraftCommand, Result>, SaveWorkflowDraftHandler>();
        services.AddScoped<ICommandHandler<PublishWorkflowCommand, Result<Guid>>, PublishWorkflowHandler>();
        services.AddScoped<ICommandHandler<StartWorkflowCommand, Result<Guid>>, StartWorkflowHandler>();
        services.AddScoped<ICommandHandler<ActOnTaskCommand, Result>, ActOnTaskHandler>();
        services.AddScoped<ICommandHandler<CancelWorkflowCommand, Result>, CancelWorkflowHandler>();

        services.AddScoped<IQueryHandler<ListWorkflowsQuery, Result<IReadOnlyList<WorkflowDto>>>, ListWorkflowsHandler>();
        services.AddScoped<IQueryHandler<GetWorkflowAdminQuery, Result<WorkflowAdminDto>>, GetWorkflowAdminHandler>();
        services.AddScoped<IQueryHandler<MyTasksQuery, Result<IReadOnlyList<TaskDto>>>, MyTasksHandler>();
        services.AddScoped<IQueryHandler<DocumentWorkflowQuery, Result<IReadOnlyList<InstanceDto>>>, DocumentWorkflowHandler>();

        services.AddScoped<IJobHandler, WorkflowSlaJob>();
        services.AddRecurringJob(WorkflowSlaJob.Type, TimeSpan.FromMinutes(15));

        return services;
    }

    public sealed record CreateWorkflowRequest(string Code, string Name, string? Description);

    public sealed record UpdateWorkflowRequest(string Name, string? Description, bool IsActive = true);

    public sealed record SaveDraftRequest(IReadOnlyList<WorkflowStepSchema>? Steps);

    public sealed record TaskActionRequest(string? Comment, string? TargetStepCode, Guid? ForwardToUserId);

    public sealed record CancelRequest(string? Reason);

    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var workflow = endpoints.MapGroup("/api/v1/workflow").WithTags("Workflow").RequireAuthorization();

        workflow.MapGet("/tasks", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new MyTasksQuery(), ct)).ToHttpResult())
            .WithSummary("The caller's inbox: pending tasks assigned to them, their groups or their roles.");

        foreach (var (route, action) in new[]
        {
            ("approve", WorkflowAction.Approve),
            ("reject", WorkflowAction.Reject),
            ("return", WorkflowAction.Return),
            ("request-changes", WorkflowAction.RequestChanges),
            ("forward", WorkflowAction.Forward),
        })
        {
            workflow.MapPost($"/tasks/{{id:guid}}/{route}", async (
                    Guid id,
                    TaskActionRequest? request,
                    IDispatcher dispatcher,
                    CancellationToken ct) =>
                {
                    var command = new ActOnTaskCommand(id, action, request?.Comment, request?.TargetStepCode, request?.ForwardToUserId);
                    return (await dispatcher.SendAsync(command, ct)).ToHttpResult();
                })
                .WithSummary($"{action} a task.");
        }

        workflow.MapPost("/instances/{id:guid}/cancel", async (Guid id, CancelRequest request, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.SendAsync(new CancelWorkflowCommand(id, request.Reason ?? string.Empty), ct)).ToHttpResult())
            .WithSummary("Cancel a running workflow (its initiator or a workflow administrator).");

        workflow.MapGet("/definitions", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new ListWorkflowsQuery(), ct)).ToHttpResult())
            .WithSummary("Workflow definitions by name, for configuring document types.");

        var documents = endpoints.MapGroup("/api/v1/documents").WithTags("Workflow").RequireAuthorization();

        documents.MapGet("/{id:guid}/workflow", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.QueryAsync(new DocumentWorkflowQuery(id), ct)).ToHttpResult())
            .WithSummary("Every workflow run of the document, with its tasks.");

        documents.MapPost("/{id:guid}/versions/{versionId:guid}/workflow/start", async (
                Guid id,
                Guid versionId,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.SendAsync(new StartWorkflowCommand(id, versionId), ct);
                return result.ToHttpResult(instanceId => Results.Created($"/api/v1/documents/{id}/workflow", new { instanceId }));
            })
            .WithSummary("Send a draft version for review (MANUAL mode).");

        var admin = endpoints.MapGroup("/api/v1/admin/workflows")
            .WithTags("Administration: workflows")
            .RequireAuthorization()
            .RequireSystemPermission(PermissionCodes.AdminManageWorkflows);

        admin.MapPost("", async (CreateWorkflowRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new CreateWorkflowCommand(request.Code, request.Name, request.Description), ct);
            return result.ToHttpResult(id => Results.Created($"/api/v1/admin/workflows/{id}", new { id }));
        });

        admin.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.QueryAsync(new GetWorkflowAdminQuery(id), ct)).ToHttpResult());

        admin.MapPut("/{id:guid}", async (Guid id, UpdateWorkflowRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.SendAsync(new UpdateWorkflowCommand(id, request.Name, request.Description, request.IsActive), ct)).ToHttpResult());

        admin.MapPut("/{id:guid}/draft", async (Guid id, SaveDraftRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.SendAsync(new SaveWorkflowDraftCommand(id, request.Steps ?? []), ct)).ToHttpResult());

        admin.MapPost("/{id:guid}/publish", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(new PublishWorkflowCommand(id), ct);
            return result.ToHttpResult(versionId => Results.Ok(new { versionId }));
        });

        return endpoints;
    }
}

public sealed class WorkflowDbContextFactory : IDesignTimeDbContextFactory<WorkflowDbContext>
{
    public WorkflowDbContext CreateDbContext(string[] args) => new(
        DesignTimeSupport.CreateOptions<WorkflowDbContext>(WorkflowDbContext.Schema),
        DesignTimeSupport.CreateSession());
}
