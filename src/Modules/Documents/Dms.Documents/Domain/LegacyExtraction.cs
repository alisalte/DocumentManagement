using Dms.Documents.Contracts;
using Dms.SharedKernel;

namespace Dms.Documents.Domain;

/// <summary>
/// Copies every file of the old archive (SQL Server, database ArchiveSaman) into the import
/// staging folder and then creates an <see cref="ImportJob"/> for them. The SQL password is never
/// part of this row: it lives only in the memory of the process that runs the copy.
/// </summary>
public sealed class LegacyExtraction : AggregateRoot<LegacyExtractionId>
{
    /// <summary>A running copy reports at least this often; older means its process is gone.</summary>
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMinutes(2);

    private LegacyExtraction()
    {
    }

    private LegacyExtraction(
        LegacyExtractionId id,
        string name,
        string server,
        string database,
        string? sqlUser,
        string stagingFolder,
        UserId createdBy,
        DateTimeOffset now)
        : base(id)
    {
        Name = name;
        Server = server;
        Database = database;
        SqlUser = sqlUser;
        StagingFolder = stagingFolder;
        Status = LegacyExtractionStatus.Queued;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string Name { get; private set; } = string.Empty;

    public string Server { get; private set; } = string.Empty;

    public string Database { get; private set; } = string.Empty;

    /// <summary>Null for Windows (integrated) login.</summary>
    public string? SqlUser { get; private set; }

    /// <summary>Relative to Dms:Import:FilesRoot.</summary>
    public string StagingFolder { get; private set; } = string.Empty;

    public LegacyExtractionStatus Status { get; private set; }

    public bool CancelRequested { get; private set; }

    public int FilesTotal { get; private set; }

    public int FilesDone { get; private set; }

    public int FilesReused { get; private set; }

    public int FilesEmpty { get; private set; }

    public long BytesTotal { get; private set; }

    public long BytesDone { get; private set; }

    public string? Warnings { get; private set; }

    public string? LastError { get; private set; }

    public ImportJobId? ImportJobId { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? HeartbeatAt { get; private set; }

    public uint RowVersion { get; private set; }

    public static Result<LegacyExtraction> Create(
        string name,
        string server,
        string database,
        string? sqlUser,
        UserId actor,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return Result.Failure<LegacyExtraction>(Error.Validation("legacy_sql.server", "SQL Server address is required."));
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            return Result.Failure<LegacyExtraction>(Error.Validation("legacy_sql.database", "Database name is required."));
        }

        var id = LegacyExtractionId.New();
        return Result.Success(new LegacyExtraction(
            id,
            string.IsNullOrWhiteSpace(name) ? $"{database.Trim()} @ {server.Trim()}" : name.Trim(),
            server.Trim(),
            database.Trim(),
            string.IsNullOrWhiteSpace(sqlUser) ? null : sqlUser.Trim(),
            $"legacy-sql/{id.Value:N}",
            actor,
            now));
    }

    /// <summary>Running, but nobody has reported for a while: the process stopped (restart, crash).</summary>
    public bool IsStale(DateTimeOffset now) =>
        Status is LegacyExtractionStatus.Queued or LegacyExtractionStatus.Running
        && (HeartbeatAt ?? UpdatedAt) < now - HeartbeatTimeout;

    public bool CanResume(DateTimeOffset now) =>
        Status is LegacyExtractionStatus.Failed or LegacyExtractionStatus.Cancelled || IsStale(now);

    /// <summary>Queues a stopped run again. Files already copied are kept and reused.</summary>
    public Result Requeue(string? sqlUser, DateTimeOffset now)
    {
        if (!CanResume(now))
        {
            return Result.Failure(Error.Conflict(
                "legacy_sql.invalid_state",
                Status == LegacyExtractionStatus.Completed
                    ? "This copy has already finished."
                    : "This copy is still running."));
        }

        SqlUser = string.IsNullOrWhiteSpace(sqlUser) ? null : sqlUser.Trim();
        Status = LegacyExtractionStatus.Queued;
        CancelRequested = false;
        LastError = null;
        CompletedAt = null;
        HeartbeatAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Start(DateTimeOffset now)
    {
        if (Status != LegacyExtractionStatus.Queued)
        {
            return Result.Failure(Error.Conflict("legacy_sql.invalid_state", $"Cannot start from status {Status}."));
        }

        Status = LegacyExtractionStatus.Running;
        StartedAt ??= now;
        HeartbeatAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    public void Plan(int filesTotal, long bytesTotal, int filesEmpty, string? warnings, DateTimeOffset now)
    {
        FilesTotal = filesTotal;
        BytesTotal = bytesTotal;
        FilesEmpty = filesEmpty;
        FilesDone = 0;
        FilesReused = 0;
        BytesDone = 0;
        Warnings = Truncate(warnings, 4000);
        HeartbeatAt = now;
        UpdatedAt = now;
    }

    public void Progress(int filesDone, int filesReused, long bytesDone, DateTimeOffset now)
    {
        FilesDone = filesDone;
        FilesReused = filesReused;
        BytesDone = bytesDone;
        HeartbeatAt = now;
        UpdatedAt = now;
    }

    public Result RequestCancel(DateTimeOffset now)
    {
        if (Status is not (LegacyExtractionStatus.Queued or LegacyExtractionStatus.Running))
        {
            return Result.Failure(Error.Conflict("legacy_sql.invalid_state", "Only a running copy can be stopped."));
        }

        if (IsStale(now))
        {
            MarkCancelled(now);
            return Result.Success();
        }

        CancelRequested = true;
        UpdatedAt = now;
        return Result.Success();
    }

    public void MarkCancelled(DateTimeOffset now)
    {
        Status = LegacyExtractionStatus.Cancelled;
        CancelRequested = false;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void Complete(ImportJobId importJobId, string? warnings, DateTimeOffset now)
    {
        Status = LegacyExtractionStatus.Completed;
        ImportJobId = importJobId;
        Warnings = Truncate(warnings, 4000);
        CompletedAt = now;
        HeartbeatAt = now;
        UpdatedAt = now;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        Status = LegacyExtractionStatus.Failed;
        LastError = Truncate(error, 2000);
        CompletedAt = now;
        UpdatedAt = now;
    }

    private static string? Truncate(string? text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max];
}
