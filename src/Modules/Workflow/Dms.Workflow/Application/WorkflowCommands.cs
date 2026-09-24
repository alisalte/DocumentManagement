using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.DocumentTypes.Contracts;
using Dms.Documents.Contracts;
using Dms.Identity.Contracts;
using Dms.Notifications.Contracts;
using Dms.SharedKernel;
using Dms.Workflow.Contracts;
using Dms.Workflow.Domain;

namespace Dms.Workflow.Application;

public sealed record CreateWorkflowCommand(string Code, string Name, string? Description) : ICommand<Result<Guid>>;

public sealed record UpdateWorkflowCommand(Guid Id, string Name, string? Description, bool IsActive) : ICommand<Result>;

public sealed record SaveWorkflowDraftCommand(Guid Id, IReadOnlyList<WorkflowStepSchema> Steps) : ICommand<Result>;

public sealed record PublishWorkflowCommand(Guid Id) : ICommand<Result<Guid>>;

public sealed record StartWorkflowCommand(Guid DocumentId, Guid VersionId) : ICommand<Result<Guid>>;

public sealed record ActOnTaskCommand(
    Guid TaskId,
    WorkflowAction Action,
    string? Comment,
    string? TargetStepCode = null,
    Guid? ForwardToUserId = null) : ICommand<Result>;

public sealed record CancelWorkflowCommand(Guid InstanceId, string Reason) : ICommand<Result>;

internal static class WorkflowErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Not authenticated.");

    public static readonly Error NotFound = Error.NotFound("workflow.not_found", "The workflow does not exist.");

    /// <summary>A task or instance the caller may not see looks exactly like one that does not exist.</summary>
    public static readonly Error TaskNotFound = Error.NotFound("workflow.task_not_found", "The task does not exist.");

    public static Error Forbidden(string explanation) => Error.Forbidden("auth.forbidden", explanation);
}

/// <summary>Finds the published definition a document type points at.</summary>
public sealed class WorkflowSelector(IDocumentTypeCatalog documentTypes, IWorkflowDefinitionRepository definitions)
{
    public async Task<Result<WorkflowDefinitionVersion>> ForTypeAsync(Guid documentTypeId, CancellationToken cancellationToken)
    {
        var type = await documentTypes.FindAsync(new DocumentTypeId(documentTypeId), cancellationToken);
        if (type is null || type.Settings.WorkflowMode == WorkflowMode.None)
        {
            return Result.Failure<WorkflowDefinitionVersion>(Error.Validation(
                "workflow.not_configured",
                "Documents of this type do not go through a workflow."));
        }

        var workflow = type.Settings.WorkflowId is { } id ? await definitions.FindAsync(new WorkflowId(id), cancellationToken) : null;
        if (workflow is not { IsActive: true, LatestPublishedVersionId: { } versionId })
        {
            return Result.Failure<WorkflowDefinitionVersion>(Error.Validation(
                "workflow.not_published",
                "The document type's workflow is missing, inactive or not published yet."));
        }

        return Result.Success(workflow.Versions.First(version => version.Id == versionId));
    }
}

public sealed class CreateWorkflowHandler(
    IDmsAuthorizer authorizer,
    IWorkflowDefinitionRepository repository,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<CreateWorkflowCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(CreateWorkflowCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageWorkflows, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<Guid>(WorkflowErrors.Forbidden(decision.Explanation));
        }

        if (string.IsNullOrWhiteSpace(command.Code) || string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure<Guid>(Error.Validation("workflow.invalid", "A code and a name are required."));
        }

        if (await repository.FindByCodeAsync(command.Code.Trim().ToUpperInvariant(), cancellationToken) is not null)
        {
            return Result.Failure<Guid>(Error.Conflict("workflow.duplicate", "A workflow with this code already exists."));
        }

        var workflow = WorkflowDefinition.Create(command.Code, command.Name, command.Description, timeProvider.GetUtcNow());
        repository.Add(workflow);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.WorkflowCreated,
                EntityType = "Workflow",
                EntityId = workflow.Id.Value,
                Metadata = new Dictionary<string, object?> { ["code"] = workflow.Code },
            },
            cancellationToken);

        return Result.Success(workflow.Id.Value);
    }
}

public sealed class UpdateWorkflowHandler(
    IDmsAuthorizer authorizer,
    IWorkflowDefinitionRepository repository,
    TimeProvider timeProvider) : ICommandHandler<UpdateWorkflowCommand, Result>
{
    public async Task<Result> HandleAsync(UpdateWorkflowCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageWorkflows, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure(WorkflowErrors.Forbidden(decision.Explanation));
        }

        var workflow = await repository.FindAsync(new WorkflowId(command.Id), cancellationToken);
        if (workflow is null)
        {
            return Result.Failure(WorkflowErrors.NotFound);
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Failure(Error.Validation("workflow.invalid", "A name is required."));
        }

        workflow.Update(command.Name, command.Description, command.IsActive, timeProvider.GetUtcNow());
        return Result.Success();
    }
}

public sealed class SaveWorkflowDraftHandler(
    IDmsAuthorizer authorizer,
    IWorkflowDefinitionRepository repository,
    IAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<SaveWorkflowDraftCommand, Result>
{
    public async Task<Result> HandleAsync(SaveWorkflowDraftCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageWorkflows, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure(WorkflowErrors.Forbidden(decision.Explanation));
        }

        var workflow = await repository.FindAsync(new WorkflowId(command.Id), cancellationToken);
        if (workflow is null)
        {
            return Result.Failure(WorkflowErrors.NotFound);
        }

        var steps = command.Steps ?? [];
        var valid = StepDesignValidator.Validate(steps);
        if (valid.IsFailure)
        {
            return valid;
        }

        var saved = workflow.ReplaceDraftSteps(steps, timeProvider.GetUtcNow());
        if (saved.IsFailure)
        {
            return saved;
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.WorkflowDraftSaved,
                EntityType = "Workflow",
                EntityId = command.Id,
                Metadata = new Dictionary<string, object?> { ["steps"] = steps.Select(step => step.Code).ToList() },
            },
            cancellationToken);

        return Result.Success();
    }
}

public sealed class PublishWorkflowHandler(
    IDmsAuthorizer authorizer,
    IWorkflowDefinitionRepository repository,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<PublishWorkflowCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(PublishWorkflowCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(WorkflowErrors.Unauthenticated);
        }

        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageWorkflows, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<Guid>(WorkflowErrors.Forbidden(decision.Explanation));
        }

        var workflow = await repository.FindAsync(new WorkflowId(command.Id), cancellationToken);
        if (workflow?.Draft is not { } draft)
        {
            return Result.Failure<Guid>(workflow is null
                ? WorkflowErrors.NotFound
                : Error.Conflict("workflow.no_draft", "There is no draft version to publish."));
        }

        var valid = StepDesignValidator.Validate(draft.Steps.Select(step => step.ToSchema()).ToList());
        if (valid.IsFailure)
        {
            return Result.Failure<Guid>(valid.Error);
        }

        var published = workflow.PublishDraft(actor, timeProvider.GetUtcNow());
        if (published.IsFailure)
        {
            return Result.Failure<Guid>(published.Error);
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.WorkflowPublished,
                EntityType = "Workflow",
                EntityId = command.Id,
                Metadata = new Dictionary<string, object?> { ["versionId"] = published.Value.Value },
            },
            cancellationToken);

        return Result.Success(published.Value.Value);
    }
}

/// <summary>Shared by the manual start command and the AUTO_ON_VERSION hook.</summary>
public sealed class WorkflowStarter(
    WorkflowSelector selector,
    IWorkflowInstanceRepository instances,
    WorkflowEngine engine)
{
    public async Task<Result<WorkflowInstance>> StartAsync(VersionForWorkflow version, UserId startedBy, CancellationToken cancellationToken)
    {
        if (version.IsDocumentDeleted)
        {
            return Result.Failure<WorkflowInstance>(Error.Conflict("document.deleted", "A deleted document cannot enter a workflow."));
        }

        // A draft, or a version whose earlier run was cancelled, may (re)start. Approved, rejected
        // and changes-requested versions are finished; changes mean a new version (section 6.3).
        if (version.ApprovalStatus is not (ApprovalStatus.Draft or ApprovalStatus.Cancelled))
        {
            return Result.Failure<WorkflowInstance>(Error.Conflict(
                "workflow.not_startable",
                $"{version.Label} is {version.ApprovalStatus}; only a draft can enter a workflow."));
        }

        if (await instances.HasRunningForVersionAsync(version.VersionId, cancellationToken))
        {
            return Result.Failure<WorkflowInstance>(Error.Conflict("workflow.already_running", "This version is already in a workflow."));
        }

        var definition = await selector.ForTypeAsync(version.DocumentTypeId, cancellationToken);
        if (definition.IsFailure)
        {
            return Result.Failure<WorkflowInstance>(definition.Error);
        }

        return Result.Success(await engine.StartAsync(version, definition.Value, startedBy, cancellationToken));
    }
}

public sealed class StartWorkflowHandler(
    IDmsAuthorizer authorizer,
    IDocumentApprovalGateway documents,
    WorkflowStarter starter,
    ICurrentUser currentUser) : ICommandHandler<StartWorkflowCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(StartWorkflowCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(WorkflowErrors.Unauthenticated);
        }

        var resource = ResourceRef.Document(command.DocumentId);
        if (!(await authorizer.AuthorizeAsync(PermissionCodes.DocumentView, resource, cancellationToken)).Allowed)
        {
            return Result.Failure<Guid>(Error.NotFound("document.not_found", "The document does not exist."));
        }

        // Sending a version for review is an edit of the document's state.
        var edit = await authorizer.AuthorizeAsync(PermissionCodes.DocumentEdit, resource, cancellationToken);
        if (!edit.Allowed)
        {
            return Result.Failure<Guid>(WorkflowErrors.Forbidden(edit.Explanation));
        }

        var version = await documents.FindVersionAsync(command.VersionId, cancellationToken);
        if (version is null || version.DocumentId != command.DocumentId)
        {
            return Result.Failure<Guid>(Error.NotFound("version.not_found", "The version does not exist."));
        }

        var started = await starter.StartAsync(version, actor, cancellationToken);
        return started.IsFailure ? Result.Failure<Guid>(started.Error) : Result.Success(started.Value.Id.Value);
    }
}

/// <summary>AUTO_ON_VERSION: a new version or revision enters the workflow in the same transaction.</summary>
public sealed class StartWorkflowOnVersionCreated(
    IDocumentTypeCatalog documentTypes,
    WorkflowSelector selector,
    IDocumentApprovalGateway documents,
    WorkflowStarter starter,
    ICurrentUser currentUser) : IVersionCreatedHook
{
    public async Task<Result> EnsureReadyAsync(Guid documentTypeId, CancellationToken cancellationToken)
    {
        var type = await documentTypes.FindAsync(new DocumentTypeId(documentTypeId), cancellationToken);
        if (type is null || type.Settings.WorkflowMode == WorkflowMode.None)
        {
            return Result.Success();
        }

        // Both modes need a published workflow: a MANUAL draft could otherwise never be approved.
        var definition = await selector.ForTypeAsync(documentTypeId, cancellationToken);
        return definition.IsFailure ? Result.Failure(definition.Error) : Result.Success();
    }

    public async Task OnVersionCreatedAsync(Guid documentTypeId, Guid documentId, Guid versionId, CancellationToken cancellationToken)
    {
        var type = await documentTypes.FindAsync(new DocumentTypeId(documentTypeId), cancellationToken);
        if (type?.Settings.WorkflowMode != WorkflowMode.AutoOnVersion)
        {
            return;
        }

        var version = await documents.FindVersionAsync(versionId, cancellationToken)
            ?? throw new InvalidOperationException($"Version {versionId} was just created but cannot be found.");

        var started = await starter.StartAsync(version, currentUser.UserId ?? version.CreatedBy, cancellationToken);
        if (started.IsFailure)
        {
            // EnsureReadyAsync ran moments ago in this transaction; failing here is a bug, and the
            // exception rolls the version back rather than leaving it without its review.
            throw new InvalidOperationException(started.Error.Message);
        }
    }
}

public sealed class ActOnTaskHandler(
    IDmsAuthorizer authorizer,
    IWorkflowInstanceRepository instances,
    IWorkflowDefinitionRepository definitions,
    IDocumentApprovalGateway documents,
    IGroupMembershipReader groups,
    IRoleMembershipReader roles,
    IUserDirectory users,
    WorkflowEngine engine,
    IAuditWriter audit,
    ICurrentUser currentUser) : ICommandHandler<ActOnTaskCommand, Result>
{
    public async Task<Result> HandleAsync(ActOnTaskCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(WorkflowErrors.Unauthenticated);
        }

        // Locked: of two group members approving at once, the second finds the task closed (409).
        var instance = await instances.FindByTaskForUpdateAsync(new WorkflowTaskId(command.TaskId), cancellationToken);
        var task = instance?.FindTask(new WorkflowTaskId(command.TaskId));
        if (instance is null || task is null)
        {
            return Result.Failure(WorkflowErrors.TaskNotFound);
        }

        // Section 6.6: eligible now, not merely when the task was created.
        if (!await IsAssigneeAsync(task, actor, cancellationToken))
        {
            return Result.Failure(WorkflowErrors.TaskNotFound);
        }

        // Checked before permissions: a closed task no longer grants anything, and its assignee
        // deserves "someone was faster" (409), not "you may not" (403).
        if (!instance.IsRunning || task.Status != WorkflowTaskStatus.Pending)
        {
            return Result.Failure(Error.Conflict("workflow.task_closed", "This task has already been completed or cancelled."));
        }

        var version = await documents.FindVersionAsync(instance.DocumentVersionId, cancellationToken);
        var definition = await definitions.FindVersionAsync(instance.WorkflowVersionId, cancellationToken);
        if (version is null || definition is null)
        {
            return Result.Failure(WorkflowErrors.TaskNotFound);
        }

        // An explicit DENY on the workflow permission beats the task (section 5.4 step 6).
        var decision = await authorizer.AuthorizeAsync(
            WorkflowEngine.PermissionFor(command.Action),
            ResourceRef.Document(instance.DocumentId),
            cancellationToken);

        if (!decision.Allowed)
        {
            await DeniedAsync(instance, command.Action, decision.Reason.ToString(), cancellationToken);
            return Result.Failure(WorkflowErrors.Forbidden(decision.Explanation));
        }

        var step = definition.FindStep(task.StepCode)!;
        if (command.Action == WorkflowAction.Approve && version.CreatedBy == actor && !step.AllowSelfApproval)
        {
            await DeniedAsync(instance, command.Action, "SelfApproval", cancellationToken);
            return Result.Failure(Error.Forbidden("workflow.self_approval", "The author of a version cannot approve it."));
        }

        UserId? forwardTo = null;
        if (command.ForwardToUserId is { } target)
        {
            var receiver = await users.FindAsync(new UserId(target), cancellationToken);
            if (receiver is not { IsActive: true })
            {
                return Result.Failure(Error.Validation("workflow.forward_target", "The user to forward to does not exist or is inactive."));
            }

            forwardTo = receiver.Id;
        }

        return await engine.ActAsync(
            instance,
            definition,
            version,
            task,
            command.Action,
            actor,
            command.Comment,
            command.TargetStepCode,
            forwardTo,
            cancellationToken);
    }

    private async Task<bool> IsAssigneeAsync(WorkflowTask task, UserId actor, CancellationToken cancellationToken)
    {
        if (task.AssignedUserId is { } user)
        {
            return user == actor;
        }

        if (task.AssignedGroupId is { } group)
        {
            return (await groups.GetGroupIdsAsync(actor, cancellationToken)).Contains(group);
        }

        return task.AssignedRoleId is { } role && (await roles.GetRoleIdsAsync(actor, cancellationToken)).Contains(role);
    }

    private Task DeniedAsync(WorkflowInstance instance, WorkflowAction action, string reason, CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.AccessDenied,
                Outcome = AuditOutcome.Denied,
                EntityType = "WorkflowInstance",
                EntityId = instance.Id.Value,
                DocumentId = instance.DocumentId,
                VersionId = instance.DocumentVersionId,
                Metadata = new Dictionary<string, object?> { ["action"] = action.ToString(), ["reason"] = reason },
            },
            cancellationToken);
}

public sealed class CancelWorkflowHandler(
    IDmsAuthorizer authorizer,
    IWorkflowInstanceRepository instances,
    IDocumentApprovalGateway documents,
    WorkflowEngine engine,
    ICurrentUser currentUser) : ICommandHandler<CancelWorkflowCommand, Result>
{
    public async Task<Result> HandleAsync(CancelWorkflowCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure(WorkflowErrors.Unauthenticated);
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure(Error.Validation("workflow.reason_required", "A reason is required to cancel a workflow."));
        }

        var instance = await instances.FindForUpdateAsync(new WorkflowInstanceId(command.InstanceId), cancellationToken);
        if (instance is null
            || !(await authorizer.AuthorizeAsync(PermissionCodes.DocumentView, ResourceRef.Document(instance.DocumentId), cancellationToken)).Allowed)
        {
            return Result.Failure(Error.NotFound("workflow.instance_not_found", "The workflow instance does not exist."));
        }

        // Section 6.3: the initiator, or an administrator of workflows.
        var isAdmin = (await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageWorkflows, cancellationToken)).Allowed;
        if (instance.StartedBy != actor && !isAdmin)
        {
            return Result.Failure(WorkflowErrors.Forbidden("Only whoever started the workflow, or a workflow administrator, can cancel it."));
        }

        if (!instance.IsRunning)
        {
            return Result.Failure(Error.Conflict("workflow.finished", "The workflow has already finished."));
        }

        var version = await documents.FindVersionAsync(instance.DocumentVersionId, cancellationToken);
        await engine.CancelAsync(instance, version!, command.Reason.Trim(), cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Open tasks as temporary grants (section 5.4 step 8): an assignee may see the document, its
/// drafts and the workflow, and take the actions the step allows. An explicit DENY still wins.
/// </summary>
public sealed class WorkflowTaskGrantSource(
    IWorkflowInstanceRepository instances,
    IWorkflowDefinitionRepository definitions,
    IGroupMembershipReader groups,
    IRoleMembershipReader roles) : ITemporaryGrantSource
{
    public async Task<IReadOnlyCollection<TemporaryGrant>> GetGrantsAsync(
        UserId userId,
        ResourceRef resource,
        CancellationToken cancellationToken)
    {
        if (resource.Type != ResourceType.Document)
        {
            return [];
        }

        var open = await instances.ListPendingForAsync(
            userId,
            await groups.GetGroupIdsAsync(userId, cancellationToken),
            await roles.GetRoleIdsAsync(userId, cancellationToken),
            resource.Id,
            cancellationToken);

        var grants = new List<TemporaryGrant>();
        foreach (var (instance, task) in open)
        {
            var definition = await definitions.FindVersionAsync(instance.WorkflowVersionId, cancellationToken);
            var step = definition?.FindStep(task.StepCode);

            var permissions = new HashSet<string>(StringComparer.Ordinal)
            {
                PermissionCodes.DocumentView,
                PermissionCodes.DocumentViewDraft,
                PermissionCodes.WorkflowView,
            };

            foreach (var action in step?.Actions ?? [])
            {
                permissions.Add(WorkflowEngine.PermissionFor(action.Action));
            }

            grants.AddRange(permissions.Select(permission => new TemporaryGrant(
                TemporaryGrantKind.WorkflowTask,
                resource,
                permission,
                ExpiresAt: null,
                SourceId: task.Id.Value)));
        }

        return grants;
    }
}

/// <summary>
/// Records overdue tasks once each (section 6.8) and tells whoever can take them. Escalation
/// comes later.
/// </summary>
public sealed class WorkflowSlaJob(
    IWorkflowInstanceRepository instances,
    IWorkflowDefinitionRepository definitions,
    IDocumentApprovalGateway documents,
    WorkflowEngine engine,
    IAuditWriter audit,
    TimeProvider timeProvider) : IJobHandler
{
    public const string Type = "workflow.sla-check";

    public string JobType => Type;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var instance in await instances.ListWithOverdueTasksAsync(now, 200, cancellationToken))
        {
            foreach (var task in instance.PendingTasks.Where(task => task.DueAt < now && task.OverdueNotifiedAt is null))
            {
                task.MarkOverdueNotified(now);
                await audit.WriteAsync(
                    new AuditRecord
                    {
                        Action = AuditActions.WorkflowTaskOverdue,
                        ActorType = AuditActorType.System,
                        EntityType = "WorkflowTask",
                        EntityId = task.Id.Value,
                        DocumentId = instance.DocumentId,
                        VersionId = instance.DocumentVersionId,
                        Metadata = new Dictionary<string, object?> { ["step"] = task.StepCode, ["dueAt"] = task.DueAt },
                    },
                    cancellationToken);

                if (await documents.FindVersionAsync(instance.DocumentVersionId, cancellationToken) is { } version)
                {
                    var definition = await definitions.FindVersionAsync(instance.WorkflowVersionId, cancellationToken);
                    await engine.NotifyTaskAsync(NotificationTypes.TaskOverdue, version, task, definition?.FindStep(task.StepCode), cancellationToken);
                }
            }
        }
    }
}
