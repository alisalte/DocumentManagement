using System.Collections.Concurrent;
using System.Text.Json;
using Dms.Infrastructure.Persistence;
using Dms.SharedKernel;
using Dms.Workflow.Application;
using Dms.Workflow.Contracts;
using Dms.Workflow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Dms.Workflow.Infrastructure.Persistence;

public sealed class WorkflowDbContext : DbContext
{
    public const string Schema = "workflow";

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    public WorkflowDbContext(DbContextOptions<WorkflowDbContext> options, DbSession session)
        : base(options) => session.Register(this);

    public DbSet<WorkflowDefinition> Workflows => Set<WorkflowDefinition>();

    public DbSet<WorkflowDefinitionVersion> WorkflowVersions => Set<WorkflowDefinitionVersion>();

    public DbSet<WorkflowInstance> Instances => Set<WorkflowInstance>();

    public DbSet<WorkflowTask> Tasks => Set<WorkflowTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<WorkflowDefinition>(entity =>
        {
            entity.ToTable("workflows");
            entity.Ignore(workflow => workflow.Draft);
            entity.HasKey(workflow => workflow.Id);
            entity.Property(workflow => workflow.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new WorkflowId(value));
            entity.Property(workflow => workflow.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            entity.Property(workflow => workflow.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(workflow => workflow.Description).HasColumnName("description").HasMaxLength(1000);
            entity.Property(workflow => workflow.IsActive).HasColumnName("is_active");
            entity.Property(workflow => workflow.LatestPublishedVersionId).HasColumnName("latest_published_version_id")
                .HasConversion(id => id!.Value.Value, value => new WorkflowVersionId(value));
            entity.Property(workflow => workflow.CreatedAt).HasColumnName("created_at");
            entity.Property(workflow => workflow.UpdatedAt).HasColumnName("updated_at");
            entity.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();
            entity.HasIndex(workflow => workflow.Code).IsUnique().HasDatabaseName("ux_workflows_code");

            entity.HasMany(workflow => workflow.Versions).WithOne()
                .HasForeignKey(version => version.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(workflow => workflow.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<WorkflowDefinitionVersion>(entity =>
        {
            entity.ToTable("workflow_versions");
            entity.Ignore(version => version.Sequences);
            entity.HasKey(version => version.Id);
            entity.Property(version => version.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new WorkflowVersionId(value));
            entity.Property(version => version.WorkflowId).HasColumnName("workflow_id")
                .HasConversion(id => id.Value, value => new WorkflowId(value));
            entity.Property(version => version.VersionNumber).HasColumnName("version_number");
            entity.Property(version => version.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(version => version.CreatedAt).HasColumnName("created_at");
            entity.Property(version => version.PublishedAt).HasColumnName("published_at");
            entity.Property(version => version.PublishedBy).HasColumnName("published_by")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));

            entity.HasIndex(version => new { version.WorkflowId, version.VersionNumber })
                .IsUnique().HasDatabaseName("ux_workflow_versions_number");
            entity.HasIndex(version => version.WorkflowId).IsUnique().HasFilter("status = 'Draft'")
                .HasDatabaseName("ux_workflow_versions_single_draft");

            entity.HasMany(version => version.Steps).WithOne()
                .HasForeignKey(step => step.WorkflowVersionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_workflow_steps_version");
            entity.Navigation(version => version.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<WorkflowStepDefinition>(entity =>
        {
            entity.ToTable("workflow_steps", table =>
            {
                table.HasCheckConstraint("ck_workflow_steps_sequence", "sequence BETWEEN 1 AND 1000");
                table.HasCheckConstraint(
                    "ck_workflow_steps_assignee",
                    "(assignee_type IN ('User','Group','Role')) = (assignee_id IS NOT NULL) "
                    + "AND (assignee_type = 'DynamicUserField') = (assignee_field_code IS NOT NULL)");
            });

            entity.HasKey(step => step.Id);
            entity.Property(step => step.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(step => step.WorkflowVersionId).HasColumnName("workflow_version_id")
                .HasConversion(id => id.Value, value => new WorkflowVersionId(value));
            entity.Property(step => step.Code).HasColumnName("code").HasMaxLength(63).IsRequired();
            entity.Property(step => step.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(step => step.Sequence).HasColumnName("sequence");
            entity.Property(step => step.AssigneeType).HasColumnName("assignee_type").HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(step => step.AssigneeId).HasColumnName("assignee_id");
            entity.Property(step => step.AssigneeFieldCode).HasColumnName("assignee_field_code").HasMaxLength(63);
            entity.Property(step => step.CompletionRule).HasColumnName("completion_rule").HasConversion<string>().HasMaxLength(8).IsRequired();
            entity.Property(step => step.IsRequired).HasColumnName("is_required");
            entity.Property(step => step.SlaHours).HasColumnName("sla_hours");
            entity.Property(step => step.AllowSelfApproval).HasColumnName("allow_self_approval");
            entity.Property(step => step.Condition).HasColumnName("condition").HasColumnType("jsonb");

            // The allowed actions of a step are part of its definition and change with it; one
            // JSONB column keeps them frozen together when the version is published.
            entity.Property(step => step.Actions).HasColumnName("actions").HasColumnType("jsonb")
                .HasConversion(
                    actions => JsonSerializer.Serialize(actions, Json),
                    json => JsonSerializer.Deserialize<List<StepActionSchema>>(json, Json) ?? new List<StepActionSchema>(),
                    new ValueComparer<List<StepActionSchema>>(
                        (left, right) => JsonSerializer.Serialize(left, Json) == JsonSerializer.Serialize(right, Json),
                        actions => JsonSerializer.Serialize(actions, Json).GetHashCode(StringComparison.Ordinal),
                        actions => actions.ToList()))
                .IsRequired();

            entity.HasIndex(step => new { step.WorkflowVersionId, step.Code }).IsUnique().HasDatabaseName("ux_workflow_steps_code");
            entity.HasIndex(step => new { step.WorkflowVersionId, step.Sequence }).HasDatabaseName("ix_workflow_steps_sequence");
        });

        modelBuilder.Entity<WorkflowInstance>(entity =>
        {
            entity.ToTable("workflow_instances");
            entity.Ignore(instance => instance.PendingTasks);
            entity.Ignore(instance => instance.IsRunning);
            entity.HasKey(instance => instance.Id);
            entity.Property(instance => instance.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new WorkflowInstanceId(value));
            entity.Property(instance => instance.DocumentId).HasColumnName("document_id");
            entity.Property(instance => instance.DocumentVersionId).HasColumnName("document_version_id");
            entity.Property(instance => instance.VersionSortKey).HasColumnName("version_sort_key");
            entity.Property(instance => instance.WorkflowVersionId).HasColumnName("workflow_version_id")
                .HasConversion(id => id.Value, value => new WorkflowVersionId(value));
            entity.Property(instance => instance.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(instance => instance.CurrentSequence).HasColumnName("current_sequence");
            entity.Property(instance => instance.Round).HasColumnName("round");
            entity.Property(instance => instance.StartedBy).HasColumnName("started_by")
                .HasConversion(id => id.Value, value => new UserId(value));
            entity.Property(instance => instance.StartedAt).HasColumnName("started_at");
            entity.Property(instance => instance.CompletedAt).HasColumnName("completed_at");
            entity.Property(instance => instance.CancelReason).HasColumnName("cancel_reason").HasMaxLength(2000);
            entity.Property(instance => instance.AttentionReason).HasColumnName("attention_reason").HasMaxLength(500);
            entity.Property(instance => instance.SkippedSteps).HasColumnName("skipped_steps").IsRequired();
            entity.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();

            entity.HasOne<WorkflowDefinitionVersion>().WithMany()
                .HasForeignKey(instance => instance.WorkflowVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_workflow_instances_workflow_version");

            // One open instance per version; several versions of a document may be in flight (D7).
            entity.HasIndex(instance => instance.DocumentVersionId).IsUnique().HasFilter("status = 'Running'")
                .HasDatabaseName("ux_workflow_instances_running_version");
            entity.HasIndex(instance => instance.DocumentId).HasDatabaseName("ix_workflow_instances_document");
            entity.HasIndex(instance => instance.Status).HasDatabaseName("ix_workflow_instances_status");

            entity.HasMany(instance => instance.Tasks).WithOne()
                .HasForeignKey(task => task.InstanceId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_workflow_tasks_instance");
            entity.Navigation(instance => instance.Tasks).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<WorkflowTask>(entity =>
        {
            entity.ToTable("workflow_tasks", table => table.HasCheckConstraint(
                "ck_workflow_tasks_one_assignee",
                "num_nonnulls(assigned_user_id, assigned_group_id, assigned_role_id) = 1"));
            entity.Ignore(task => task.Assignee);

            entity.HasKey(task => task.Id);
            entity.Property(task => task.Id).HasColumnName("id")
                .HasConversion(id => id.Value, value => new WorkflowTaskId(value));
            entity.Property(task => task.InstanceId).HasColumnName("instance_id")
                .HasConversion(id => id.Value, value => new WorkflowInstanceId(value));
            entity.Property(task => task.StepCode).HasColumnName("step_code").HasMaxLength(63).IsRequired();
            entity.Property(task => task.Sequence).HasColumnName("sequence");
            entity.Property(task => task.Round).HasColumnName("round");
            entity.Property(task => task.AssignedUserId).HasColumnName("assigned_user_id")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property(task => task.AssignedGroupId).HasColumnName("assigned_group_id")
                .HasConversion(id => id!.Value.Value, value => new GroupId(value));
            entity.Property(task => task.AssignedRoleId).HasColumnName("assigned_role_id")
                .HasConversion(id => id!.Value.Value, value => new RoleId(value));
            entity.Property(task => task.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(task => task.CreatedAt).HasColumnName("created_at");
            entity.Property(task => task.DueAt).HasColumnName("due_at");
            entity.Property(task => task.OverdueNotifiedAt).HasColumnName("overdue_notified_at");
            entity.Property(task => task.CompletedAt).HasColumnName("completed_at");
            entity.Property(task => task.CompletedBy).HasColumnName("completed_by")
                .HasConversion(id => id!.Value.Value, value => new UserId(value));
            entity.Property(task => task.Action).HasColumnName("action").HasConversion<string>().HasMaxLength(24);
            entity.Property(task => task.Comment).HasColumnName("comment").HasMaxLength(4000);
            entity.Property(task => task.ForwardedFromTaskId).HasColumnName("forwarded_from_task_id")
                .HasConversion(id => id!.Value.Value, value => new WorkflowTaskId(value));
            entity.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();

            // Inbox lookups, one partial index per assignee kind (section 4.7).
            entity.HasIndex(task => task.AssignedUserId).HasFilter("status = 'Pending'").HasDatabaseName("ix_workflow_tasks_user");
            entity.HasIndex(task => task.AssignedGroupId).HasFilter("status = 'Pending'").HasDatabaseName("ix_workflow_tasks_group");
            entity.HasIndex(task => task.AssignedRoleId).HasFilter("status = 'Pending'").HasDatabaseName("ix_workflow_tasks_role");
            entity.HasIndex(task => task.DueAt).HasFilter("status = 'Pending'").HasDatabaseName("ix_workflow_tasks_due");
            entity.HasIndex(task => task.InstanceId).HasDatabaseName("ix_workflow_tasks_instance");
        });
    }
}

/// <summary>Published definitions never change, so they are loaded once per process.</summary>
public sealed class PublishedWorkflowCache
{
    private readonly ConcurrentDictionary<WorkflowVersionId, WorkflowDefinitionVersion> _versions = new();

    public bool TryGet(WorkflowVersionId id, out WorkflowDefinitionVersion version) => _versions.TryGetValue(id, out version!);

    public void Add(WorkflowDefinitionVersion version)
    {
        if (version.Status != WorkflowVersionStatus.Draft)
        {
            _versions.TryAdd(version.Id, version);
        }
    }
}

public sealed class WorkflowDefinitionRepository(WorkflowDbContext context, PublishedWorkflowCache cache) : IWorkflowDefinitionRepository
{
    public Task<WorkflowDefinition?> FindAsync(WorkflowId id, CancellationToken cancellationToken) =>
        context.Workflows.Include(workflow => workflow.Versions).ThenInclude(version => version.Steps)
            .AsSplitQuery()
            .FirstOrDefaultAsync(workflow => workflow.Id == id, cancellationToken);

    public Task<WorkflowDefinition?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.Workflows.FirstOrDefaultAsync(workflow => workflow.Code == code, cancellationToken);

    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken cancellationToken) =>
        await context.Workflows.AsNoTracking().OrderBy(workflow => workflow.Code).ToListAsync(cancellationToken);

    public async Task<WorkflowDefinitionVersion?> FindVersionAsync(WorkflowVersionId id, CancellationToken cancellationToken)
    {
        if (cache.TryGet(id, out var cached))
        {
            return cached;
        }

        var version = await context.WorkflowVersions.AsNoTracking()
            .Include(candidate => candidate.Steps)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (version is not null)
        {
            cache.Add(version);
        }

        return version;
    }

    public void Add(WorkflowDefinition workflow) => context.Workflows.Add(workflow);
}

public sealed class WorkflowInstanceRepository(WorkflowDbContext context) : IWorkflowInstanceRepository
{
    public async Task<WorkflowInstance?> FindForUpdateAsync(WorkflowInstanceId id, CancellationToken cancellationToken)
    {
        await LockAsync(id.Value, cancellationToken);
        return await context.Instances.Include(instance => instance.Tasks)
            .FirstOrDefaultAsync(instance => instance.Id == id, cancellationToken);
    }

    public async Task<WorkflowInstance?> FindByTaskForUpdateAsync(WorkflowTaskId taskId, CancellationToken cancellationToken)
    {
        var instanceId = await context.Tasks.AsNoTracking()
            .Where(task => task.Id == taskId)
            .Select(task => (WorkflowInstanceId?)task.InstanceId)
            .FirstOrDefaultAsync(cancellationToken);

        return instanceId is { } id ? await FindForUpdateAsync(id, cancellationToken) : null;
    }

    public Task<bool> HasRunningForVersionAsync(Guid documentVersionId, CancellationToken cancellationToken) =>
        context.Instances.AnyAsync(
            instance => instance.DocumentVersionId == documentVersionId && instance.Status == WorkflowInstanceStatus.Running,
            cancellationToken);

    public async Task<IReadOnlyList<WorkflowInstance>> ListRunningForDocumentAsync(Guid documentId, CancellationToken cancellationToken) =>
        await context.Instances.Include(instance => instance.Tasks)
            .Where(instance => instance.DocumentId == documentId && instance.Status == WorkflowInstanceStatus.Running)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkflowInstance>> ListForDocumentAsync(Guid documentId, CancellationToken cancellationToken) =>
        await context.Instances.AsNoTracking().Include(instance => instance.Tasks)
            .Where(instance => instance.DocumentId == documentId)
            .OrderByDescending(instance => instance.StartedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(WorkflowInstance Instance, WorkflowTask Task)>> ListPendingForAsync(
        UserId userId,
        IReadOnlySet<GroupId> groups,
        IReadOnlySet<RoleId> roles,
        Guid? documentId,
        CancellationToken cancellationToken)
    {
        UserId? user = userId;
        var groupIds = groups.Select(group => (GroupId?)group).ToArray();
        var roleIds = roles.Select(role => (RoleId?)role).ToArray();

        var query = context.Instances.AsNoTracking().Include(instance => instance.Tasks)
            .Where(instance => instance.Status == WorkflowInstanceStatus.Running);

        if (documentId is { } document)
        {
            query = query.Where(instance => instance.DocumentId == document);
        }

        var instances = await query
            .Where(instance => instance.Tasks.Any(task => task.Status == WorkflowTaskStatus.Pending
                && (task.AssignedUserId == user || groupIds.Contains(task.AssignedGroupId) || roleIds.Contains(task.AssignedRoleId))))
            .ToListAsync(cancellationToken);

        return instances
            .SelectMany(instance => instance.PendingTasks
                .Where(task => task.AssignedUserId == userId
                    || (task.AssignedGroupId is { } group && groups.Contains(group))
                    || (task.AssignedRoleId is { } role && roles.Contains(role)))
                .Select(task => (instance, task)))
            .ToList();
    }

    public async Task<IReadOnlyList<WorkflowInstance>> ListWithOverdueTasksAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        await context.Instances.Include(instance => instance.Tasks)
            .Where(instance => instance.Status == WorkflowInstanceStatus.Running
                && instance.Tasks.Any(task => task.Status == WorkflowTaskStatus.Pending && task.DueAt < now && task.OverdueNotifiedAt == null))
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void Add(WorkflowInstance instance) => context.Instances.Add(instance);

    private async Task LockAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM workflow.workflow_instances WHERE id = {id} FOR UPDATE")
            .ToListAsync(cancellationToken);
}
