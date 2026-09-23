namespace Dms.Workflow.Contracts;

public readonly record struct WorkflowId(Guid Value)
{
    public static WorkflowId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct WorkflowVersionId(Guid Value)
{
    public static WorkflowVersionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct WorkflowInstanceId(Guid Value)
{
    public static WorkflowInstanceId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct WorkflowTaskId(Guid Value)
{
    public static WorkflowTaskId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Who a step is assigned to (section 6.5).</summary>
public enum AssigneeType
{
    User,
    Group,
    Role,

    /// <summary>The document's owner when the step starts.</summary>
    DocumentOwner,

    /// <summary>Whoever created the version under review.</summary>
    Creator,

    /// <summary>The version creator's manager (users.manager_id) when the step starts.</summary>
    Manager,

    /// <summary>A USER field in the version's metadata.</summary>
    DynamicUserField,
}

/// <summary>For group and role steps: one shared task (Any) or one task per member (All).</summary>
public enum CompletionRule
{
    Any,
    All,
}

public enum WorkflowAction
{
    Approve,
    Reject,
    Return,
    RequestChanges,
    Forward,
}

public enum WorkflowInstanceStatus
{
    Running,
    Approved,
    Rejected,
    ChangesRequested,
    Cancelled,
}

public enum WorkflowTaskStatus
{
    Pending,
    Completed,
    Cancelled,
}
