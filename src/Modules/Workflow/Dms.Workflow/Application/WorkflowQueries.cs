using Dms.Application;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.Identity.Contracts;
using Dms.SharedKernel;
using Dms.Workflow.Contracts;
using Dms.Workflow.Domain;

namespace Dms.Workflow.Application;

public sealed record WorkflowDto(Guid Id, string Code, string Name, string? Description, bool IsActive, Guid? LatestPublishedVersionId);

public sealed record WorkflowVersionDto(Guid Id, int VersionNumber, string Status, DateTimeOffset? PublishedAt, int StepCount);

public sealed record WorkflowAdminDto(WorkflowDto Workflow, IReadOnlyList<WorkflowVersionDto> Versions, IReadOnlyList<WorkflowStepSchema>? Draft);

public sealed record TaskDto(
    Guid Id,
    Guid InstanceId,
    Guid DocumentId,
    string DocumentTitle,
    Guid VersionId,
    string VersionLabel,
    string StepCode,
    string StepName,
    string Status,
    Guid? AssignedUserId,
    Guid? AssignedGroupId,
    Guid? AssignedRoleId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DueAt,
    DateTimeOffset? CompletedAt,
    Guid? CompletedBy,
    string? Action,
    string? Comment,
    Guid? ForwardedFromTaskId,
    IReadOnlyList<string> AllowedActions)
{
    /// <summary>Whether the caller may act on this task now. Advisory for the UI; the server re-checks.</summary>
    public bool CanAct { get; init; }

    public bool IsOverdue { get; init; }
}

public sealed record InstanceDto(
    Guid Id,
    Guid VersionId,
    string VersionLabel,
    Guid WorkflowVersionId,
    string Status,
    int CurrentSequence,
    Guid StartedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? CancelReason,
    string? AttentionReason,
    IReadOnlyList<string> SkippedSteps,
    IReadOnlyList<TaskDto> Tasks)
{
    public bool CanCancel { get; init; }
}

public sealed record ListWorkflowsQuery : IQuery<Result<IReadOnlyList<WorkflowDto>>>;

public sealed record GetWorkflowAdminQuery(Guid Id) : IQuery<Result<WorkflowAdminDto>>;

public sealed record MyTasksQuery : IQuery<Result<IReadOnlyList<TaskDto>>>;

public sealed record DocumentWorkflowQuery(Guid DocumentId) : IQuery<Result<IReadOnlyList<InstanceDto>>>;

public sealed class ListWorkflowsHandler(IWorkflowDefinitionRepository repository, ICurrentUser currentUser)
    : IQueryHandler<ListWorkflowsQuery, Result<IReadOnlyList<WorkflowDto>>>
{
    public async Task<Result<IReadOnlyList<WorkflowDto>>> HandleAsync(ListWorkflowsQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<IReadOnlyList<WorkflowDto>>(WorkflowErrors.Unauthenticated);
        }

        // Names only, like document types: needed to configure types and read histories.
        var workflows = await repository.ListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<WorkflowDto>>(workflows.Select(ToDto).ToList());
    }

    internal static WorkflowDto ToDto(WorkflowDefinition workflow) =>
        new(workflow.Id.Value, workflow.Code, workflow.Name, workflow.Description, workflow.IsActive, workflow.LatestPublishedVersionId?.Value);
}

public sealed class GetWorkflowAdminHandler(IDmsAuthorizer authorizer, IWorkflowDefinitionRepository repository)
    : IQueryHandler<GetWorkflowAdminQuery, Result<WorkflowAdminDto>>
{
    public async Task<Result<WorkflowAdminDto>> HandleAsync(GetWorkflowAdminQuery query, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageWorkflows, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<WorkflowAdminDto>(WorkflowErrors.Forbidden(decision.Explanation));
        }

        var workflow = await repository.FindAsync(new WorkflowId(query.Id), cancellationToken);
        if (workflow is null)
        {
            return Result.Failure<WorkflowAdminDto>(WorkflowErrors.NotFound);
        }

        return Result.Success(new WorkflowAdminDto(
            ListWorkflowsHandler.ToDto(workflow),
            workflow.Versions
                .OrderByDescending(version => version.VersionNumber)
                .Select(version => new WorkflowVersionDto(
                    version.Id.Value,
                    version.VersionNumber,
                    version.Status.ToString(),
                    version.PublishedAt,
                    version.Steps.Count))
                .ToList(),
            workflow.Draft?.Steps.OrderBy(step => step.Sequence).ThenBy(step => step.Code, StringComparer.Ordinal)
                .Select(step => step.ToSchema()).ToList()));
    }
}

/// <summary>Builds task rows: step names from the pinned definition, document titles from Documents.</summary>
public sealed class TaskPresenter(
    IWorkflowDefinitionRepository definitions,
    IDocumentApprovalGateway documents,
    TimeProvider timeProvider)
{
    private readonly Dictionary<WorkflowVersionId, WorkflowDefinitionVersion?> _definitions = [];
    private readonly Dictionary<Guid, VersionForWorkflow?> _versions = [];

    public async Task<TaskDto?> PresentAsync(WorkflowInstance instance, WorkflowTask task, bool canAct, CancellationToken cancellationToken)
    {
        var definition = await DefinitionAsync(instance.WorkflowVersionId, cancellationToken);
        var version = await VersionAsync(instance.DocumentVersionId, cancellationToken);
        if (version is null)
        {
            return null;
        }

        var step = definition?.FindStep(task.StepCode);
        return new TaskDto(
            task.Id.Value,
            instance.Id.Value,
            instance.DocumentId,
            version.DocumentTitle,
            instance.DocumentVersionId,
            version.Label,
            task.StepCode,
            step?.Name ?? task.StepCode,
            task.Status.ToString(),
            task.AssignedUserId?.Value,
            task.AssignedGroupId?.Value,
            task.AssignedRoleId?.Value,
            task.CreatedAt,
            task.DueAt,
            task.CompletedAt,
            task.CompletedBy?.Value,
            task.Action?.ToString(),
            task.Comment,
            task.ForwardedFromTaskId?.Value,
            task.Status == WorkflowTaskStatus.Pending ? (step?.Actions.Select(action => action.Action.ToString()).ToList() ?? []) : [])
        {
            CanAct = canAct && task.Status == WorkflowTaskStatus.Pending && instance.IsRunning,
            IsOverdue = task.Status == WorkflowTaskStatus.Pending && task.DueAt < timeProvider.GetUtcNow(),
        };
    }

    public async Task<VersionForWorkflow?> VersionAsync(Guid versionId, CancellationToken cancellationToken)
    {
        if (!_versions.TryGetValue(versionId, out var version))
        {
            version = await documents.FindVersionAsync(versionId, cancellationToken);
            _versions[versionId] = version;
        }

        return version;
    }

    private async Task<WorkflowDefinitionVersion?> DefinitionAsync(WorkflowVersionId id, CancellationToken cancellationToken)
    {
        if (!_definitions.TryGetValue(id, out var definition))
        {
            definition = await definitions.FindVersionAsync(id, cancellationToken);
            _definitions[id] = definition;
        }

        return definition;
    }
}

/// <summary>The task inbox: everything waiting on the caller, directly or through a group or role.</summary>
public sealed class MyTasksHandler(
    IWorkflowInstanceRepository instances,
    IGroupMembershipReader groups,
    IRoleMembershipReader roles,
    IDmsAuthorizer authorizer,
    TaskPresenter presenter,
    ICurrentUser currentUser) : IQueryHandler<MyTasksQuery, Result<IReadOnlyList<TaskDto>>>
{
    public async Task<Result<IReadOnlyList<TaskDto>>> HandleAsync(MyTasksQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } me)
        {
            return Result.Failure<IReadOnlyList<TaskDto>>(WorkflowErrors.Unauthenticated);
        }

        var pending = await instances.ListPendingForAsync(
            me,
            await groups.GetGroupIdsAsync(me, cancellationToken),
            await roles.GetRoleIdsAsync(me, cancellationToken),
            documentId: null,
            cancellationToken);

        var result = new List<TaskDto>();
        foreach (var (instance, task) in pending.Take(200))
        {
            // A task on a document the caller may not even see (an explicit DENY) is not shown.
            if (!(await authorizer.AuthorizeAsync(PermissionCodes.DocumentView, ResourceRef.Document(instance.DocumentId), cancellationToken)).Allowed)
            {
                continue;
            }

            var version = await presenter.VersionAsync(instance.DocumentVersionId, cancellationToken);
            if (version is null || version.IsDocumentDeleted)
            {
                continue;
            }

            if (await presenter.PresentAsync(instance, task, canAct: true, cancellationToken) is { } row)
            {
                result.Add(row);
            }
        }

        return Result.Success<IReadOnlyList<TaskDto>>(result
            .OrderByDescending(task => task.IsOverdue)
            .ThenBy(task => task.DueAt ?? DateTimeOffset.MaxValue)
            .ThenBy(task => task.CreatedAt)
            .ToList());
    }
}

/// <summary>The workflow history of one document: every instance, every task, oldest task first.</summary>
public sealed class DocumentWorkflowHandler(
    IDmsAuthorizer authorizer,
    IWorkflowInstanceRepository instances,
    IGroupMembershipReader groups,
    IRoleMembershipReader roles,
    TaskPresenter presenter,
    ICurrentUser currentUser) : IQueryHandler<DocumentWorkflowQuery, Result<IReadOnlyList<InstanceDto>>>
{
    public async Task<Result<IReadOnlyList<InstanceDto>>> HandleAsync(DocumentWorkflowQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } me)
        {
            return Result.Failure<IReadOnlyList<InstanceDto>>(WorkflowErrors.Unauthenticated);
        }

        var resource = ResourceRef.Document(query.DocumentId);
        if (!(await authorizer.AuthorizeAsync(PermissionCodes.DocumentView, resource, cancellationToken)).Allowed)
        {
            return Result.Failure<IReadOnlyList<InstanceDto>>(Error.NotFound("document.not_found", "The document does not exist."));
        }

        var myGroups = await groups.GetGroupIdsAsync(me, cancellationToken);
        var myRoles = await roles.GetRoleIdsAsync(me, cancellationToken);
        var isAdmin = (await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageWorkflows, cancellationToken)).Allowed;

        bool Mine(WorkflowTask task) =>
            task.AssignedUserId == me
            || (task.AssignedGroupId is { } group && myGroups.Contains(group))
            || (task.AssignedRoleId is { } role && myRoles.Contains(role));

        var result = new List<InstanceDto>();
        foreach (var instance in await instances.ListForDocumentAsync(query.DocumentId, cancellationToken))
        {
            var version = await presenter.VersionAsync(instance.DocumentVersionId, cancellationToken);
            var tasks = new List<TaskDto>();
            foreach (var task in instance.Tasks.OrderBy(task => task.CreatedAt))
            {
                if (await presenter.PresentAsync(instance, task, Mine(task), cancellationToken) is { } row)
                {
                    tasks.Add(row);
                }
            }

            result.Add(new InstanceDto(
                instance.Id.Value,
                instance.DocumentVersionId,
                version?.Label ?? string.Empty,
                instance.WorkflowVersionId.Value,
                instance.Status.ToString(),
                instance.CurrentSequence,
                instance.StartedBy.Value,
                instance.StartedAt,
                instance.CompletedAt,
                instance.CancelReason,
                instance.AttentionReason,
                instance.SkippedSteps,
                tasks)
            {
                CanCancel = instance.IsRunning && (instance.StartedBy == me || isAdmin),
            });
        }

        return Result.Success<IReadOnlyList<InstanceDto>>(result);
    }
}
