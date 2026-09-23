using Dms.SharedKernel;
using Dms.Workflow.Contracts;
using Dms.Workflow.Domain;

namespace Dms.Workflow.Application;

public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition?> FindAsync(WorkflowId id, CancellationToken cancellationToken);

    Task<WorkflowDefinition?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken cancellationToken);

    /// <summary>One version with its steps, read-only. Published versions never change, so this may be cached.</summary>
    Task<WorkflowDefinitionVersion?> FindVersionAsync(WorkflowVersionId id, CancellationToken cancellationToken);

    void Add(WorkflowDefinition workflow);
}

public interface IWorkflowInstanceRepository
{
    /// <summary>The instance with its tasks, row locked until the transaction ends (section 6.8).</summary>
    Task<WorkflowInstance?> FindForUpdateAsync(WorkflowInstanceId id, CancellationToken cancellationToken);

    /// <summary>The instance that owns a task, row locked.</summary>
    Task<WorkflowInstance?> FindByTaskForUpdateAsync(WorkflowTaskId taskId, CancellationToken cancellationToken);

    Task<bool> HasRunningForVersionAsync(Guid documentVersionId, CancellationToken cancellationToken);

    /// <summary>Running instances of a document, tracked, for superseding older ones.</summary>
    Task<IReadOnlyList<WorkflowInstance>> ListRunningForDocumentAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>Every instance of a document with its tasks, newest first, read-only.</summary>
    Task<IReadOnlyList<WorkflowInstance>> ListForDocumentAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>Pending tasks of running instances assigned to the user directly, to one of their groups or to one of their roles.</summary>
    Task<IReadOnlyList<(WorkflowInstance Instance, WorkflowTask Task)>> ListPendingForAsync(
        UserId userId,
        IReadOnlySet<GroupId> groups,
        IReadOnlySet<RoleId> roles,
        Guid? documentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowInstance>> ListWithOverdueTasksAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    void Add(WorkflowInstance instance);
}
