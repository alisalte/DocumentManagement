using System.Text.Json;
using Dms.SharedKernel;
using Dms.Workflow.Contracts;

namespace Dms.Workflow.Domain;

public enum WorkflowVersionStatus
{
    Draft,
    Published,
    Retired,
}

/// <summary>What one step allows, and where a RETURN goes (section 6.3).</summary>
public sealed record StepActionSchema(WorkflowAction Action, bool CommentRequired = false, string? TargetStepCode = null);

/// <summary>The editable shape of a step; also what the admin API exchanges.</summary>
public sealed record WorkflowStepSchema
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    /// <summary>Steps sharing a sequence run in parallel; sequences run in ascending order.</summary>
    public required int Sequence { get; init; }

    public required AssigneeType AssigneeType { get; init; }

    /// <summary>User, group or role id for the fixed assignee types.</summary>
    public Guid? AssigneeId { get; init; }

    /// <summary>The USER field to read for DynamicUserField.</summary>
    public string? AssigneeFieldCode { get; init; }

    public CompletionRule CompletionRule { get; init; } = CompletionRule.Any;

    /// <summary>A required step that resolves to nobody stops the instance for an administrator; an optional one is skipped.</summary>
    public bool IsRequired { get; init; } = true;

    public int? SlaHours { get; init; }

    public bool AllowSelfApproval { get; init; }

    /// <summary>Entry condition in the shared rule language, over the version's metadata. False skips the step.</summary>
    public JsonElement? Condition { get; init; }

    public IReadOnlyList<StepActionSchema> Actions { get; init; } =
    [
        new(WorkflowAction.Approve),
        new(WorkflowAction.Reject, CommentRequired: true),
    ];
}

/// <summary>
/// An approval process (section 6.1). Versioned like document types: a draft is edited freely,
/// publishing freezes it, and every running instance stays pinned to the version it started on.
/// </summary>
public sealed class WorkflowDefinition : AggregateRoot<WorkflowId>
{
    private readonly List<WorkflowDefinitionVersion> _versions = [];

    private WorkflowDefinition()
    {
    }

    private WorkflowDefinition(WorkflowId id, string code, string name, string? description, DateTimeOffset now)
        : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public WorkflowVersionId? LatestPublishedVersionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<WorkflowDefinitionVersion> Versions => _versions;

    public WorkflowDefinitionVersion? Draft => _versions.FirstOrDefault(version => version.Status == WorkflowVersionStatus.Draft);

    public static WorkflowDefinition Create(string code, string name, string? description, DateTimeOffset now)
    {
        var workflow = new WorkflowDefinition(WorkflowId.New(), code.Trim().ToUpperInvariant(), name.Trim(), description, now);
        workflow._versions.Add(WorkflowDefinitionVersion.CreateDraft(workflow.Id, 1, now));
        return workflow;
    }

    public void Update(string name, string? description, bool isActive, DateTimeOffset now)
    {
        Name = name.Trim();
        Description = description;
        IsActive = isActive;
        UpdatedAt = now;
    }

    public Result ReplaceDraftSteps(IReadOnlyList<WorkflowStepSchema> steps, DateTimeOffset now)
    {
        if (Draft is not { } draft)
        {
            return Result.Failure(Error.Conflict("workflow.no_draft", "There is no draft version to edit."));
        }

        draft.ReplaceSteps(steps);
        UpdatedAt = now;
        return Result.Success();
    }

    public Result<WorkflowVersionId> PublishDraft(UserId publishedBy, DateTimeOffset now)
    {
        if (Draft is not { } draft)
        {
            return Result.Failure<WorkflowVersionId>(Error.Conflict("workflow.no_draft", "There is no draft version to publish."));
        }

        draft.Publish(publishedBy, now);
        LatestPublishedVersionId = draft.Id;
        UpdatedAt = now;

        var next = WorkflowDefinitionVersion.CreateDraft(Id, draft.VersionNumber + 1, now);
        next.ReplaceSteps(draft.Steps.Select(step => step.ToSchema()).ToList());
        _versions.Add(next);
        return Result.Success(draft.Id);
    }
}

/// <summary>One frozen (or draft) set of steps.</summary>
public sealed class WorkflowDefinitionVersion : Entity<WorkflowVersionId>
{
    private readonly List<WorkflowStepDefinition> _steps = [];

    private WorkflowDefinitionVersion()
    {
    }

    private WorkflowDefinitionVersion(WorkflowVersionId id, WorkflowId workflowId, int versionNumber, DateTimeOffset now)
        : base(id)
    {
        WorkflowId = workflowId;
        VersionNumber = versionNumber;
        Status = WorkflowVersionStatus.Draft;
        CreatedAt = now;
    }

    public WorkflowId WorkflowId { get; private set; }

    public int VersionNumber { get; private set; }

    public WorkflowVersionStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public UserId? PublishedBy { get; private set; }

    public IReadOnlyList<WorkflowStepDefinition> Steps => _steps;

    public IReadOnlyList<int> Sequences => _steps.Select(step => step.Sequence).Distinct().Order().ToList();

    public WorkflowStepDefinition? FindStep(string code) => _steps.FirstOrDefault(step => step.Code == code);

    internal static WorkflowDefinitionVersion CreateDraft(WorkflowId workflowId, int number, DateTimeOffset now) =>
        new(WorkflowVersionId.New(), workflowId, number, now);

    internal void ReplaceSteps(IReadOnlyList<WorkflowStepSchema> steps)
    {
        if (Status != WorkflowVersionStatus.Draft)
        {
            throw new InvalidOperationException("A published workflow version never changes.");
        }

        _steps.Clear();
        _steps.AddRange(steps.Select(step => WorkflowStepDefinition.From(Id, step)));
    }

    internal void Publish(UserId publishedBy, DateTimeOffset now)
    {
        Status = WorkflowVersionStatus.Published;
        PublishedBy = publishedBy;
        PublishedAt = now;
    }
}

public sealed class WorkflowStepDefinition : Entity<Guid>
{
    private WorkflowStepDefinition()
    {
    }

    private WorkflowStepDefinition(Guid id)
        : base(id)
    {
    }

    public WorkflowVersionId WorkflowVersionId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int Sequence { get; private set; }

    public AssigneeType AssigneeType { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public string? AssigneeFieldCode { get; private set; }

    public CompletionRule CompletionRule { get; private set; }

    public bool IsRequired { get; private set; }

    public int? SlaHours { get; private set; }

    public bool AllowSelfApproval { get; private set; }

    public string? Condition { get; private set; }

    public List<StepActionSchema> Actions { get; private set; } = [];

    public StepActionSchema? FindAction(WorkflowAction action) => Actions.FirstOrDefault(candidate => candidate.Action == action);

    internal static WorkflowStepDefinition From(WorkflowVersionId versionId, WorkflowStepSchema schema) => new(Guid.CreateVersion7())
    {
        WorkflowVersionId = versionId,
        Code = schema.Code,
        Name = schema.Name,
        Sequence = schema.Sequence,
        AssigneeType = schema.AssigneeType,
        AssigneeId = schema.AssigneeId,
        AssigneeFieldCode = schema.AssigneeFieldCode,
        CompletionRule = schema.CompletionRule,
        IsRequired = schema.IsRequired,
        SlaHours = schema.SlaHours,
        AllowSelfApproval = schema.AllowSelfApproval,
        Condition = schema.Condition is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } condition
            ? condition.GetRawText()
            : null,
        Actions = [.. schema.Actions],
    };

    public WorkflowStepSchema ToSchema() => new()
    {
        Code = Code,
        Name = Name,
        Sequence = Sequence,
        AssigneeType = AssigneeType,
        AssigneeId = AssigneeId,
        AssigneeFieldCode = AssigneeFieldCode,
        CompletionRule = CompletionRule,
        IsRequired = IsRequired,
        SlaHours = SlaHours,
        AllowSelfApproval = AllowSelfApproval,
        Condition = Condition is null ? null : JsonDocument.Parse(Condition).RootElement.Clone(),
        Actions = [.. Actions],
    };
}
