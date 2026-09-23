using Dms.SharedKernel;
using Dms.Workflow.Contracts;

namespace Dms.Workflow.Domain;

/// <summary>
/// One run of a workflow on one document version (section 6). It owns its tasks, pins the
/// workflow version it started on, and is the only place where tasks change state, so the
/// transition rules live in one aggregate.
///
/// <see cref="Round"/> counts RETURNs: tasks from before a return stay in the history but no
/// longer count towards completing a step.
/// </summary>
public sealed class WorkflowInstance : AggregateRoot<WorkflowInstanceId>
{
    private readonly List<WorkflowTask> _tasks = [];

    private WorkflowInstance()
    {
    }

    private WorkflowInstance(WorkflowInstanceId id)
        : base(id)
    {
    }

    public Guid DocumentId { get; private set; }

    public Guid DocumentVersionId { get; private set; }

    /// <summary>Orders instances of one document, to cancel older ones when a newer version is approved.</summary>
    public long VersionSortKey { get; private set; }

    public WorkflowVersionId WorkflowVersionId { get; private set; }

    public WorkflowInstanceStatus Status { get; private set; }

    public int CurrentSequence { get; private set; }

    public int Round { get; private set; }

    public UserId StartedBy { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? CancelReason { get; private set; }

    /// <summary>Set when a required step resolved to nobody: the instance waits for an administrator.</summary>
    public string? AttentionReason { get; private set; }

    /// <summary>Steps skipped because their condition was false, for the history view.</summary>
    public List<string> SkippedSteps { get; private set; } = [];

    public IReadOnlyList<WorkflowTask> Tasks => _tasks;

    public bool IsRunning => Status == WorkflowInstanceStatus.Running;

    public static WorkflowInstance Start(
        Guid documentId,
        Guid documentVersionId,
        long versionSortKey,
        WorkflowVersionId workflowVersionId,
        UserId startedBy,
        DateTimeOffset now) => new(WorkflowInstanceId.New())
    {
        DocumentId = documentId,
        DocumentVersionId = documentVersionId,
        VersionSortKey = versionSortKey,
        WorkflowVersionId = workflowVersionId,
        Status = WorkflowInstanceStatus.Running,
        StartedBy = startedBy,
        StartedAt = now,
    };

    public IEnumerable<WorkflowTask> PendingTasks => _tasks.Where(task => task.Status == WorkflowTaskStatus.Pending);

    public WorkflowTask? FindTask(WorkflowTaskId id) => _tasks.FirstOrDefault(task => task.Id == id);

    internal void EnterSequence(int sequence)
    {
        CurrentSequence = sequence;
        AttentionReason = null;
    }

    internal void Skip(string stepCode)
    {
        if (!SkippedSteps.Contains(stepCode))
        {
            SkippedSteps = [.. SkippedSteps, stepCode];
        }
    }

    internal WorkflowTask AddTask(
        string stepCode,
        int sequence,
        Assignee assignee,
        DateTimeOffset? dueAt,
        DateTimeOffset now,
        WorkflowTaskId? forwardedFrom = null)
    {
        var task = WorkflowTask.Create(Id, stepCode, sequence, Round, assignee, dueAt, now, forwardedFrom);
        _tasks.Add(task);
        return task;
    }

    internal void NeedsAttention(string reason) => AttentionReason = reason;

    /// <summary>Tasks of this step that still count: this round, not cancelled.</summary>
    internal IReadOnlyList<WorkflowTask> CurrentTasksOf(string stepCode) =>
        _tasks.Where(task => task.StepCode == stepCode && task.Round == Round && task.Status != WorkflowTaskStatus.Cancelled).ToList();

    internal void CancelPending(DateTimeOffset now)
    {
        foreach (var task in PendingTasks.ToList())
        {
            task.Cancel(now);
        }
    }

    /// <summary>RETURN: everything open is cancelled and the step is worked again as a new round.</summary>
    internal void StartNewRound(DateTimeOffset now)
    {
        CancelPending(now);
        Round++;
    }

    internal void Finish(WorkflowInstanceStatus status, DateTimeOffset now, string? reason = null)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("The workflow has already finished.");
        }

        CancelPending(now);
        Status = status;
        CompletedAt = now;
        CancelReason = reason;
        AttentionReason = null;
    }
}

/// <summary>Exactly one of the three is set, like the CHECK constraint on the table.</summary>
public readonly record struct Assignee(UserId? UserId, GroupId? GroupId, RoleId? RoleId)
{
    public static Assignee User(UserId id) => new(id, null, null);

    public static Assignee Group(GroupId id) => new(null, id, null);

    public static Assignee Role(RoleId id) => new(null, null, id);
}

public sealed class WorkflowTask : Entity<WorkflowTaskId>
{
    private WorkflowTask()
    {
    }

    private WorkflowTask(WorkflowTaskId id)
        : base(id)
    {
    }

    public WorkflowInstanceId InstanceId { get; private set; }

    public string StepCode { get; private set; } = string.Empty;

    public int Sequence { get; private set; }

    public int Round { get; private set; }

    public UserId? AssignedUserId { get; private set; }

    public GroupId? AssignedGroupId { get; private set; }

    public RoleId? AssignedRoleId { get; private set; }

    public WorkflowTaskStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    public DateTimeOffset? OverdueNotifiedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public UserId? CompletedBy { get; private set; }

    public WorkflowAction? Action { get; private set; }

    public string? Comment { get; private set; }

    public WorkflowTaskId? ForwardedFromTaskId { get; private set; }

    public Assignee Assignee => new(AssignedUserId, AssignedGroupId, AssignedRoleId);

    internal static WorkflowTask Create(
        WorkflowInstanceId instanceId,
        string stepCode,
        int sequence,
        int round,
        Assignee assignee,
        DateTimeOffset? dueAt,
        DateTimeOffset now,
        WorkflowTaskId? forwardedFrom) => new(WorkflowTaskId.New())
    {
        InstanceId = instanceId,
        StepCode = stepCode,
        Sequence = sequence,
        Round = round,
        AssignedUserId = assignee.UserId,
        AssignedGroupId = assignee.GroupId,
        AssignedRoleId = assignee.RoleId,
        Status = WorkflowTaskStatus.Pending,
        CreatedAt = now,
        DueAt = dueAt,
        ForwardedFromTaskId = forwardedFrom,
    };

    internal void Complete(WorkflowAction action, UserId actor, string? comment, DateTimeOffset now)
    {
        if (Status != WorkflowTaskStatus.Pending)
        {
            throw new InvalidOperationException("The task is no longer open.");
        }

        Status = WorkflowTaskStatus.Completed;
        Action = action;
        CompletedBy = actor;
        CompletedAt = now;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    internal void Cancel(DateTimeOffset now)
    {
        Status = WorkflowTaskStatus.Cancelled;
        CompletedAt = now;
    }

    public void MarkOverdueNotified(DateTimeOffset now) => OverdueNotifiedAt = now;
}
