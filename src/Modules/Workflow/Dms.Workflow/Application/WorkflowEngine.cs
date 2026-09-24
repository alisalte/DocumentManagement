using System.Text.Json;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.Identity.Contracts;
using Dms.Notifications.Contracts;
using Dms.SharedKernel;
using Dms.SharedKernel.Rules;
using Dms.Workflow.Contracts;
using Dms.Workflow.Domain;

namespace Dms.Workflow.Application;

/// <summary>
/// Turns a step's assignee definition into concrete assignees when the step starts (section 6.5).
/// Inactive users never get a task, and unless the step allows self-approval the version's
/// author is left out: a task they could never complete would only stall the workflow.
/// </summary>
public sealed class AssigneeResolver(
    IUserDirectory users,
    IGroupMembershipReader groups,
    IRoleMembershipReader roles)
{
    public async Task<IReadOnlyList<Assignee>> ResolveAsync(
        WorkflowStepDefinition step,
        VersionForWorkflow version,
        CancellationToken cancellationToken)
    {
        bool Allowed(UserId user) => step.AllowSelfApproval || user != version.CreatedBy;

        switch (step.AssigneeType)
        {
            case AssigneeType.Group or AssigneeType.Role:
            {
                IEnumerable<UserId> members = step.AssigneeType == AssigneeType.Group
                    ? await groups.GetActiveMemberIdsAsync(new GroupId(step.AssigneeId!.Value), cancellationToken)
                    : await ActiveAsync(await roles.GetUserIdsAsync(new RoleId(step.AssigneeId!.Value), cancellationToken), cancellationToken);

                var eligible = members.Where(Allowed).ToList();
                if (eligible.Count == 0)
                {
                    return [];
                }

                // ANY: one shared task, membership checked when someone acts. ALL: one task per
                // member, from a snapshot taken now.
                return step.CompletionRule == CompletionRule.All
                    ? eligible.Select(Assignee.User).ToList()
                    : [step.AssigneeType == AssigneeType.Group
                        ? Assignee.Group(new GroupId(step.AssigneeId!.Value))
                        : Assignee.Role(new RoleId(step.AssigneeId!.Value))];
            }

            default:
            {
                var user = await SingleUserAsync(step, version, cancellationToken);
                return user is { } found && Allowed(found)
                    ? (await ActiveAsync(new HashSet<UserId> { found }, cancellationToken)).Select(Assignee.User).ToList()
                    : [];
            }
        }
    }

    private async Task<UserId?> SingleUserAsync(WorkflowStepDefinition step, VersionForWorkflow version, CancellationToken cancellationToken)
    {
        switch (step.AssigneeType)
        {
            case AssigneeType.User:
                return new UserId(step.AssigneeId!.Value);
            case AssigneeType.DocumentOwner:
                return version.OwnerId;
            case AssigneeType.Creator:
                return version.CreatedBy;
            case AssigneeType.Manager:
                return (await users.FindAsync(version.CreatedBy, cancellationToken))?.ManagerId;
            case AssigneeType.DynamicUserField:
                using (var metadata = JsonDocument.Parse(version.MetadataJson))
                {
                    return metadata.RootElement.TryGetProperty(step.AssigneeFieldCode!, out var value)
                        && value.ValueKind == JsonValueKind.String
                        && Guid.TryParse(value.GetString(), out var id)
                            ? new UserId(id)
                            : null;
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// Who can act on a task right now: its user, or the active members of its group or role.
    /// For telling people about it; who may actually act is still decided when they do.
    /// </summary>
    public async Task<IReadOnlyList<UserId>> RecipientsAsync(Assignee assignee, CancellationToken cancellationToken)
    {
        if (assignee.UserId is { } user)
        {
            return [user];
        }

        if (assignee.GroupId is { } group)
        {
            return [.. await groups.GetActiveMemberIdsAsync(group, cancellationToken)];
        }

        return assignee.RoleId is { } role
            ? await ActiveAsync(await roles.GetUserIdsAsync(role, cancellationToken), cancellationToken)
            : [];
    }

    private async Task<IReadOnlyList<UserId>> ActiveAsync(IReadOnlySet<UserId> candidates, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var found = await users.FindManyAsync(candidates.ToList(), cancellationToken);
        return found.Where(user => user.IsActive).Select(user => user.Id).ToList();
    }
}

/// <summary>
/// The workflow state machine (sections 6.2 to 6.7). Handlers load and lock the instance, check
/// who may act, and call this; the engine creates tasks, moves between sequences, and records the
/// outcome on the document version through the Documents contract, all in the caller's
/// transaction.
/// </summary>
public sealed class WorkflowEngine(
    AssigneeResolver assignees,
    IDocumentApprovalGateway documents,
    IWorkflowInstanceRepository instances,
    IAuditWriter audit,
    INotificationSender notifications,
    TimeProvider timeProvider)
{
    public async Task<WorkflowInstance> StartAsync(
        VersionForWorkflow version,
        WorkflowDefinitionVersion definition,
        UserId startedBy,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var instance = WorkflowInstance.Start(
            version.DocumentId,
            version.VersionId,
            version.SortKey,
            definition.Id,
            startedBy,
            now);

        instances.Add(instance);
        await documents.RecordAsync(version.DocumentId, version.VersionId, ApprovalStatus.InWorkflow, cancellationToken);
        await WriteAsync(AuditActions.WorkflowStarted, instance, version, new Dictionary<string, object?>
        {
            ["workflowVersionId"] = definition.Id.Value,
            ["label"] = version.Label,
        }, cancellationToken);

        await AdvanceAsync(instance, definition, version, fromSequence: 1, cancellationToken);
        return instance;
    }

    public async Task<Result> ActAsync(
        WorkflowInstance instance,
        WorkflowDefinitionVersion definition,
        VersionForWorkflow version,
        WorkflowTask task,
        WorkflowAction action,
        UserId actor,
        string? comment,
        string? targetStepCode,
        UserId? forwardTo,
        CancellationToken cancellationToken)
    {
        if (!instance.IsRunning || task.Status != WorkflowTaskStatus.Pending)
        {
            return Result.Failure(Error.Conflict("workflow.task_closed", "This task has already been completed or cancelled."));
        }

        var step = definition.FindStep(task.StepCode)!;
        if (step.FindAction(action) is not { } allowed)
        {
            return Result.Failure(Error.Validation("workflow.action_not_allowed", $"This step does not allow {action}."));
        }

        var needsComment = allowed.CommentRequired || action is WorkflowAction.Reject or WorkflowAction.RequestChanges;
        if (needsComment && string.IsNullOrWhiteSpace(comment))
        {
            return Result.Failure(Error.ValidationFields(
                "workflow.comment_required",
                "A comment is required for this action.",
                new Dictionary<string, string[]> { ["comment"] = ["توضیح لازم است."] }));
        }

        var now = timeProvider.GetUtcNow();
        switch (action)
        {
            case WorkflowAction.Approve:
            {
                task.Complete(action, actor, comment, now);
                await WriteAsync(AuditActions.WorkflowApproved, instance, version, TaskDetails(task, step), cancellationToken);

                var stepTasks = instance.CurrentTasksOf(step.Code);
                var stepDone = step.CompletionRule == CompletionRule.Any
                    || stepTasks.All(candidate => candidate.Status != WorkflowTaskStatus.Pending);

                if (stepDone)
                {
                    foreach (var open in stepTasks.Where(candidate => candidate.Status == WorkflowTaskStatus.Pending))
                    {
                        open.Cancel(now);
                    }
                }

                // Parallel steps: the sequence is done once nothing in it is still open.
                if (instance.PendingTasks.All(candidate => candidate.Sequence != instance.CurrentSequence))
                {
                    await AdvanceAsync(instance, definition, version, instance.CurrentSequence + 1, cancellationToken);
                }

                return Result.Success();
            }

            case WorkflowAction.Reject:
                task.Complete(action, actor, comment, now);
                await FinishAsync(instance, version, WorkflowInstanceStatus.Rejected, AuditActions.WorkflowRejected, comment, cancellationToken);
                return Result.Success();

            case WorkflowAction.RequestChanges:
                task.Complete(action, actor, comment, now);
                await FinishAsync(instance, version, WorkflowInstanceStatus.ChangesRequested, AuditActions.WorkflowRequestedChanges, comment, cancellationToken);
                return Result.Success();

            case WorkflowAction.Return:
            {
                var target = ResolveReturnTarget(instance, definition, step, targetStepCode ?? allowed.TargetStepCode);
                if (target is null)
                {
                    return Result.Failure(Error.Validation("workflow.no_return_target", "There is no earlier step to return to."));
                }

                task.Complete(action, actor, comment, now);
                instance.StartNewRound(now);
                await WriteAsync(AuditActions.WorkflowReturned, instance, version, new Dictionary<string, object?>
                {
                    ["from"] = step.Code,
                    ["to"] = target.Code,
                    ["comment"] = comment,
                }, cancellationToken);

                await AdvanceAsync(instance, definition, version, target.Sequence, cancellationToken);
                return Result.Success();
            }

            case WorkflowAction.Forward:
            {
                if (forwardTo is not { } receiver || receiver == actor)
                {
                    return Result.Failure(Error.Validation("workflow.forward_target", "Choose someone else to forward the task to."));
                }

                if (!step.AllowSelfApproval && receiver == version.CreatedBy)
                {
                    return Result.Failure(Error.Validation("workflow.self_approval", "The author of the version cannot review it."));
                }

                task.Complete(action, actor, comment, now);
                var forwarded = instance.AddTask(step.Code, task.Sequence, Assignee.User(receiver), task.DueAt, now, task.Id);
                await WriteAsync(AuditActions.WorkflowForwarded, instance, version, new Dictionary<string, object?>
                {
                    ["step"] = step.Code,
                    ["fromTaskId"] = task.Id.Value,
                    ["toTaskId"] = forwarded.Id.Value,
                    ["toUserId"] = receiver.Value,
                    ["comment"] = comment,
                }, cancellationToken);
                await NotifyTaskAsync(NotificationTypes.TaskAssigned, version, forwarded, step, cancellationToken);

                return Result.Success();
            }

            default:
                return Result.Failure(Error.Validation("workflow.action_unknown", "Unknown action."));
        }
    }

    public Task CancelAsync(WorkflowInstance instance, VersionForWorkflow version, string reason, CancellationToken cancellationToken) =>
        FinishAsync(instance, version, WorkflowInstanceStatus.Cancelled, AuditActions.WorkflowCancelled, reason, cancellationToken);

    /// <summary>
    /// Opens the first sequence at or after <paramref name="fromSequence"/> that has a step to
    /// work on. Steps whose condition is false are skipped; a required step nobody can take stops
    /// the instance for an administrator. With nothing left, the instance is approved.
    /// </summary>
    private async Task AdvanceAsync(
        WorkflowInstance instance,
        WorkflowDefinitionVersion definition,
        VersionForWorkflow version,
        int fromSequence,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        using var metadata = JsonDocument.Parse(version.MetadataJson);

        foreach (var sequence in definition.Sequences.Where(sequence => sequence >= fromSequence))
        {
            instance.EnterSequence(sequence);
            var opened = new List<(WorkflowTask Task, WorkflowStepDefinition Step)>();

            foreach (var step in definition.Steps.Where(step => step.Sequence == sequence).OrderBy(step => step.Code, StringComparer.Ordinal))
            {
                // Version metadata never changes under a running review, so this answer is stable.
                if (step.Condition is { } condition
                    && RuleParser.Parse(condition) is { IsSuccess: true } rule
                    && !RuleEvaluator.Evaluate(rule.Value, metadata.RootElement))
                {
                    instance.Skip(step.Code);
                    continue;
                }

                var resolved = await assignees.ResolveAsync(step, version, cancellationToken);
                if (resolved.Count == 0)
                {
                    if (!step.IsRequired)
                    {
                        instance.Skip(step.Code);
                        continue;
                    }

                    instance.NeedsAttention($"Step '{step.Code}' has no one to assign it to.");
                    await WriteAsync(AuditActions.WorkflowNeedsAttention, instance, version, new Dictionary<string, object?>
                    {
                        ["step"] = step.Code,
                        ["assigneeType"] = step.AssigneeType.ToString(),
                    }, cancellationToken);
                    return;
                }

                var dueAt = step.SlaHours is { } hours ? now.AddHours(hours) : (DateTimeOffset?)null;
                foreach (var assignee in resolved)
                {
                    opened.Add((instance.AddTask(step.Code, sequence, assignee, dueAt, now), step));
                }
            }

            if (opened.Count > 0)
            {
                foreach (var (task, step) in opened)
                {
                    await NotifyTaskAsync(NotificationTypes.TaskAssigned, version, task, step, cancellationToken);
                }

                return;
            }
        }

        await FinishAsync(instance, version, WorkflowInstanceStatus.Approved, AuditActions.WorkflowCompleted, null, cancellationToken);
    }

    /// <summary>
    /// Records the outcome on the instance and the version. An approval also supersedes: running
    /// instances on older versions of the same document are cancelled, because approving them
    /// later could only ever be a step backwards (and the effective pointer never moves back).
    /// Creating a new version cancels nothing (decision D7).
    /// </summary>
    private async Task FinishAsync(
        WorkflowInstance instance,
        VersionForWorkflow version,
        WorkflowInstanceStatus status,
        string auditAction,
        string? reason,
        CancellationToken cancellationToken)
    {
        instance.Finish(status, timeProvider.GetUtcNow(), status == WorkflowInstanceStatus.Cancelled ? reason : null);
        await documents.RecordAsync(version.DocumentId, version.VersionId, ToApproval(status), cancellationToken);
        await WriteAsync(auditAction, instance, version, new Dictionary<string, object?>
        {
            ["outcome"] = status.ToString(),
            ["comment"] = reason,
        }, cancellationToken);

        // The author of the version and whoever started the review hear how it ended (not the
        // person who ended it: the sender leaves the current user out).
        await notifications.SendAsync(
            new NotificationMessage(
                NotificationTypes.WorkflowFinished,
                [version.CreatedBy, instance.StartedBy],
                version.DocumentId,
                version.VersionId,
                new Dictionary<string, object?>
                {
                    ["documentTitle"] = version.DocumentTitle,
                    ["versionLabel"] = version.Label,
                    ["outcome"] = status.ToString(),
                    ["comment"] = reason,
                }),
            cancellationToken);

        if (status != WorkflowInstanceStatus.Approved)
        {
            return;
        }

        foreach (var older in await instances.ListRunningForDocumentAsync(version.DocumentId, cancellationToken))
        {
            if (older.Id == instance.Id || older.VersionSortKey >= instance.VersionSortKey)
            {
                continue;
            }

            var olderVersion = await documents.FindVersionAsync(older.DocumentVersionId, cancellationToken);
            if (olderVersion is null)
            {
                continue;
            }

            await FinishAsync(
                older,
                olderVersion,
                WorkflowInstanceStatus.Cancelled,
                AuditActions.WorkflowCancelled,
                $"Superseded by {version.Label}.",
                cancellationToken);
        }
    }

    /// <summary>
    /// Tells everyone who can take the task (TASK_ASSIGNED or TASK_OVERDUE). The version's author
    /// is left out unless the step lets them approve their own work, as when tasks are assigned:
    /// a group or role task is shared, and they could not act on it.
    /// </summary>
    public async Task NotifyTaskAsync(
        string type,
        VersionForWorkflow version,
        WorkflowTask task,
        WorkflowStepDefinition? step,
        CancellationToken cancellationToken)
    {
        var recipients = (await assignees.RecipientsAsync(task.Assignee, cancellationToken))
            .Where(user => step?.AllowSelfApproval == true || user != version.CreatedBy)
            .ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        await notifications.SendAsync(
            new NotificationMessage(
                type,
                recipients,
                version.DocumentId,
                version.VersionId,
                new Dictionary<string, object?>
                {
                    ["documentTitle"] = version.DocumentTitle,
                    ["versionLabel"] = version.Label,
                    ["taskId"] = task.Id.Value,
                    ["step"] = task.StepCode,
                    ["stepName"] = step?.Name,
                    ["dueAt"] = task.DueAt,
                }),
            cancellationToken);
    }

    private static WorkflowStepDefinition? ResolveReturnTarget(
        WorkflowInstance instance,
        WorkflowDefinitionVersion definition,
        WorkflowStepDefinition from,
        string? targetCode)
    {
        if (targetCode is not null)
        {
            return definition.FindStep(targetCode) is { } explicitTarget && explicitTarget.Sequence < from.Sequence
                ? explicitTarget
                : null;
        }

        // Default: the latest earlier step that actually had tasks in this instance.
        var worked = instance.Tasks
            .Where(task => task.Sequence < from.Sequence)
            .OrderByDescending(task => task.Sequence)
            .Select(task => task.StepCode)
            .FirstOrDefault();

        return worked is null ? null : definition.FindStep(worked);
    }

    private static ApprovalStatus ToApproval(WorkflowInstanceStatus status) => status switch
    {
        WorkflowInstanceStatus.Approved => ApprovalStatus.Approved,
        WorkflowInstanceStatus.Rejected => ApprovalStatus.Rejected,
        WorkflowInstanceStatus.ChangesRequested => ApprovalStatus.ChangesRequested,
        WorkflowInstanceStatus.Cancelled => ApprovalStatus.Cancelled,
        _ => ApprovalStatus.InWorkflow,
    };

    private static Dictionary<string, object?> TaskDetails(WorkflowTask task, WorkflowStepDefinition step) => new()
    {
        ["taskId"] = task.Id.Value,
        ["step"] = step.Code,
        ["comment"] = task.Comment,
    };

    private Task WriteAsync(
        string action,
        WorkflowInstance instance,
        VersionForWorkflow version,
        Dictionary<string, object?> details,
        CancellationToken cancellationToken)
    {
        details["instanceId"] = instance.Id.Value;
        return audit.WriteAsync(
            new AuditRecord
            {
                Action = action,
                EntityType = "WorkflowInstance",
                EntityId = instance.Id.Value,
                DocumentId = version.DocumentId,
                VersionId = version.VersionId,
                Metadata = details,
            },
            cancellationToken);
    }

    /// <summary>The resource permission a task action is checked against (section 6.6).</summary>
    public static string PermissionFor(WorkflowAction action) => action switch
    {
        WorkflowAction.Reject => PermissionCodes.WorkflowReject,
        WorkflowAction.Return => PermissionCodes.WorkflowReturn,
        WorkflowAction.RequestChanges => PermissionCodes.WorkflowRequestChanges,
        _ => PermissionCodes.WorkflowApprove,
    };
}
