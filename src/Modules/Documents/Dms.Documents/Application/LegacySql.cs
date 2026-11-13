using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.Documents.Contracts;
using Dms.Documents.Domain;
using Dms.SharedKernel;
using Microsoft.Extensions.Options;

namespace Dms.Documents.Application;

// ---------------------------------------------------------------------------------------------
// Importing the old archive (SQL Server database "ArchiveSaman") straight from the server.
//
// The old program keeps every file as VARBINARY(MAX). A copy reads the tables, streams each file
// into Dms:Import:FilesRoot/legacy-sql/<id>/files, writes a manifest and creates an ordinary import
// job, which the administrator then validates and starts as before. Only SELECTs are sent to the
// old server, and the SQL password is never stored: it stays in the memory of the copying process.
// ---------------------------------------------------------------------------------------------

public sealed class LegacySqlOptions
{
    public const string SectionName = "Dms:Import:LegacySql";

    /// <summary>Lets administrators connect to an old SQL Server from the admin UI.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When not empty, only these server names may be used (case-insensitive).</summary>
    public List<string> AllowedServers { get; set; } = [];

    /// <summary>Seconds a single SQL command may take (reading one large file included).</summary>
    public int CommandTimeoutSeconds { get; set; } = 1800;
}

public sealed record LegacySqlConnectionSettings(
    string Server,
    string Database,
    string? User,
    string? Password,
    bool Encrypt = true,
    bool TrustServerCertificate = true)
{
    /// <summary>Never print the password, not even in a debugger or a log line.</summary>
    public override string ToString() => $"{Database} @ {Server} ({User ?? "Windows login"})";
}

/// <summary>One stored file of the old archive, without its bytes.</summary>
/// <param name="Table">tbl_FileSanad, tbl_FilePeyvast or tbl_FilesSampleAsnad.</param>
/// <param name="ParentId">IDSanad, IDPeyvast or IDSampleAsnad.</param>
/// <param name="Length">DATALENGTH of the blob.</param>
public sealed record LegacyFileRow(
    string Table,
    string Id,
    string? ParentId,
    string? TypeId,
    long? DeclaredSize,
    string? Sharh,
    long Length);

/// <summary>A row of a small lookup table, keyed by lower-case column name.</summary>
public sealed class LegacyRow(IReadOnlyDictionary<string, object?> values)
{
    public object? this[string column] => values.TryGetValue(column.ToLowerInvariant(), out var value) ? value : null;
}

public sealed class LegacyArchiveCatalog
{
    public Dictionary<string, LegacyRow> Cases { get; } = [];

    public Dictionary<string, LegacyRow> Sanads { get; } = [];

    public Dictionary<string, LegacyRow> Peyvasts { get; } = [];

    public Dictionary<string, LegacyRow> Samples { get; } = [];

    public Dictionary<string, LegacyRow> FileTypes { get; } = [];

    public Dictionary<string, LegacyRow> Parties { get; } = [];

    public List<LegacyFileRow> Files { get; } = [];

    public int EmptyFiles { get; set; }

    public List<string> Warnings { get; } = [];
}

public sealed record LegacyTableCountDto(string Table, bool Exists, int Rows, int Files, long Bytes);

public sealed record LegacyArchiveProbeDto(
    string ServerVersion,
    int Files,
    long Bytes,
    IReadOnlyList<LegacyTableCountDto> Tables);

public interface ILegacySqlArchive
{
    /// <summary>Connects and counts the files; reads no file contents.</summary>
    Task<Result<LegacyArchiveProbeDto>> ProbeAsync(LegacySqlConnectionSettings settings, CancellationToken cancellationToken);

    Task<ILegacySqlSession> OpenAsync(LegacySqlConnectionSettings settings, CancellationToken cancellationToken);
}

public interface ILegacySqlSession : IAsyncDisposable
{
    Task<LegacyArchiveCatalog> ReadCatalogAsync(CancellationToken cancellationToken);

    /// <summary>Streams one file into <paramref name="destination"/>; returns the bytes written.</summary>
    Task<long> CopyFileAsync(LegacyFileRow file, Stream destination, CancellationToken cancellationToken);
}

/// <summary>Hands the password to the process that runs the copy. Never persisted.</summary>
public interface ILegacyExtractionQueue
{
    void Submit(LegacyExtractionId id, LegacySqlConnectionSettings settings);

    /// <summary>Stops a copy running in this process; false when it runs elsewhere (or not at all).</summary>
    bool CancelLocal(LegacyExtractionId id);
}

public interface ILegacyExtractionRepository
{
    Task<LegacyExtraction?> FindAsync(LegacyExtractionId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LegacyExtraction>> ListAsync(int take, CancellationToken cancellationToken);

    void Add(LegacyExtraction extraction);
}

public sealed record LegacyExtractionDto(
    Guid Id,
    string Name,
    string Server,
    string Database,
    string? SqlUser,
    string Status,
    bool Interrupted,
    bool CanResume,
    bool CancelRequested,
    int FilesTotal,
    int FilesDone,
    int FilesReused,
    int FilesEmpty,
    long BytesTotal,
    long BytesDone,
    string? Warnings,
    string? LastError,
    Guid? ImportJobId,
    string StagingFolder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt)
{
    public static LegacyExtractionDto From(LegacyExtraction extraction, DateTimeOffset now) => new(
        extraction.Id.Value,
        extraction.Name,
        extraction.Server,
        extraction.Database,
        extraction.SqlUser,
        extraction.Status.ToString(),
        extraction.IsStale(now),
        extraction.CanResume(now),
        extraction.CancelRequested,
        extraction.FilesTotal,
        extraction.FilesDone,
        extraction.FilesReused,
        extraction.FilesEmpty,
        extraction.BytesTotal,
        extraction.BytesDone,
        extraction.Warnings,
        extraction.LastError,
        extraction.ImportJobId?.Value,
        extraction.StagingFolder,
        extraction.CreatedAt,
        extraction.UpdatedAt,
        extraction.StartedAt,
        extraction.CompletedAt);
}

// ---------------------------------------------------------------------------------------------
// Old schema → manifest entry
// ---------------------------------------------------------------------------------------------

public static partial class LegacyArchiveMapper
{
    public const string SourceSystem = "archive-saman";
    public const string RootFolder = "واردات";
    public const string NoCaseFolder = "بدون پرونده";
    public const string SamplesFolder = "نمونه اسناد";

    public const string FileSanad = "tbl_FileSanad";
    public const string FilePeyvast = "tbl_FilePeyvast";
    public const string FilesSampleAsnad = "tbl_FilesSampleAsnad";

    private static readonly PersianCalendar Persian = new();

    /// <summary>Same ids as scripts/legacy-export-sql-blobs.py, so either path skips what the other imported.</summary>
    public static string SourceId(LegacyFileRow file)
    {
        var prefix = file.Table switch
        {
            FileSanad => "sanad",
            FilePeyvast => "peyvast",
            FilesSampleAsnad => "sample",
            _ => throw new ArgumentOutOfRangeException(nameof(file), file.Table, "Unknown file table."),
        };
        return $"{prefix}-{file.ParentId ?? "?"}-file-{file.Id}";
    }

    public static LegacyManifestEntry Map(
        LegacyArchiveCatalog catalog,
        LegacyFileRow file,
        int partNumber,
        int partCount,
        string relativePath,
        string fileName,
        string contentType,
        long size,
        string sha256)
    {
        var lines = new List<string>();
        DateOnly? created = null;
        string title;
        string folder;
        var parentId = file.ParentId ?? "?";

        switch (file.Table)
        {
            case FileSanad:
            {
                var sanad = catalog.Sanads.GetValueOrDefault(parentId);
                title = Text(file.Sharh) ?? Text(sanad?["mozoe"]) ?? $"سند {parentId}";
                folder = CaseFolder(catalog, Id(sanad?["idsanad"]));
                if (sanad is not null)
                {
                    created = AddSanadLines(catalog, sanad, lines);
                }

                break;
            }

            case FilePeyvast:
            {
                var peyvast = catalog.Peyvasts.GetValueOrDefault(parentId);
                var sanadId = Id(peyvast?["idsanad"]);
                var sanad = sanadId is null ? null : catalog.Sanads.GetValueOrDefault(sanadId);
                title = Text(file.Sharh)
                    ?? Text(peyvast?["title"])
                    ?? $"پیوست {parentId}" + (sanadId is null ? string.Empty : $" سند {sanadId}");
                folder = CaseFolder(catalog, Id(sanad?["idsanad"]));
                if (sanadId is not null)
                {
                    var subject = Text(sanad?["mozoe"]);
                    lines.Add($"پیوستِ سند {sanadId}" + (subject is null ? string.Empty : $" ({subject})"));
                }

                if (Text(peyvast?["sharh"]) is { } note)
                {
                    lines.Add($"شرح پیوست: {note}");
                }

                foreach (var (column, label) in new[] { ("datepeyvast", "تاریخ پیوست"), ("datesabt", "تاریخ ثبت") })
                {
                    if (ParseDate(peyvast?[column]) is { } date)
                    {
                        lines.Add($"{label}: {date.Display}");
                        created ??= date.Gregorian;
                    }
                }

                break;
            }

            default:
            {
                var sample = catalog.Samples.GetValueOrDefault(parentId);
                title = Text(file.Sharh) ?? Text(sample?["title"]) ?? $"نمونه سند {parentId}";
                folder = $"{RootFolder}/{SamplesFolder}";
                if (Text(sample?["description"]) is { } description)
                {
                    lines.Add(description);
                }

                break;
            }
        }

        if (file.TypeId is not null && Text(catalog.FileTypes.GetValueOrDefault(file.TypeId)?["title"]) is { } typeTitle)
        {
            lines.Add($"نوع فایل: {typeTitle}");
        }

        lines.Add($"منبع: {file.Table} شناسه {file.Id}");
        if (partCount > 1)
        {
            title += $" (فایل {partNumber} از {partCount})";
        }

        var text = string.Join('\n', lines);
        return new LegacyManifestEntry
        {
            SourceId = SourceId(file),
            Title = Truncate(title, 300),
            Description = Truncate(text, 4000),
            CategoryPath = folder,
            DocumentTypeCode = "GENERAL",
            File = relativePath,
            FileName = fileName,
            ContentType = contentType,
            Size = size,
            Sha256 = sha256,
            CreatedAt = created is { } day ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
        };
    }

    /// <summary>The extension stored for a file type in tbl_AnvaeFile, if any (e.g. "docx").</summary>
    public static string? TypeExtension(LegacyArchiveCatalog catalog, LegacyFileRow file)
    {
        if (file.TypeId is null || Text(catalog.FileTypes.GetValueOrDefault(file.TypeId)?["pasvandfile"]) is not { } raw)
        {
            return null;
        }

        var cleaned = raw.Trim().TrimStart('.').ToLowerInvariant();
        return SafeExtension().IsMatch(cleaned) ? cleaned : null;
    }

    public static string CaseFolder(LegacyArchiveCatalog catalog, string? caseId)
    {
        if (caseId is null || !catalog.Cases.TryGetValue(caseId, out var row))
        {
            return $"{RootFolder}/{NoCaseFolder}";
        }

        // The folder code is the first 12 letters/digits of the name, so the id goes first.
        var name = new StringBuilder($"پرونده {caseId}");
        var number = Id(row["shomareparvandeh"]);
        if (number is not null && number != caseId)
        {
            name.Append($" (شماره {number})");
        }

        if ((Text(row["mozoeparvandeh"]) ?? Text(row["kholaseparvandeh"])) is { } label)
        {
            name.Append($" - {label}");
        }

        return $"{RootFolder}/{FolderName(name.ToString())}";
    }

    public static (string Display, DateOnly Gregorian)? ParseDate(object? value)
    {
        var raw = Id(value);
        if (raw is null)
        {
            return null;
        }

        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != 8)
        {
            return null;
        }

        var year = int.Parse(digits[..4], CultureInfo.InvariantCulture);
        var month = int.Parse(digits[4..6], CultureInfo.InvariantCulture);
        var day = int.Parse(digits[6..], CultureInfo.InvariantCulture);
        try
        {
            if (year is >= 1300 and <= 1500 && month is >= 1 and <= 12 && day >= 1
                && day <= Persian.GetDaysInMonth(year, month))
            {
                var gregorian = Persian.ToDateTime(year, month, day, 0, 0, 0, 0);
                return ($"{year:D4}/{month:D2}/{day:D2}", DateOnly.FromDateTime(gregorian));
            }

            if (year is >= 1900 and <= 2100)
            {
                var date = new DateOnly(year, month, day);
                return (date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), date);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        return null;
    }

    public static string? Id(object? value)
    {
        var text = value switch
        {
            null => null,
            decimal number when number == decimal.Truncate(number) => decimal.Truncate(number).ToString(CultureInfo.InvariantCulture),
            double number when number == Math.Truncate(number) => ((long)number).ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static string? Text(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = Whitespace().Replace(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static DateOnly? AddSanadLines(LegacyArchiveCatalog catalog, LegacyRow sanad, List<string> lines)
    {
        DateOnly? created = null;
        if (Text(sanad["shomarename"]) is { } letter)
        {
            lines.Add($"شماره نامه: {letter}");
        }

        if (Id(sanad["sanadnumber"]) is { } number)
        {
            lines.Add($"شماره سند: {number}");
        }

        foreach (var (column, label) in new[] { ("datesanad", "تاریخ سند"), ("tarikhsabt", "تاریخ ثبت") })
        {
            if (ParseDate(sanad[column]) is { } date)
            {
                lines.Add($"{label}: {date.Display}");
                created ??= date.Gregorian;
            }
        }

        foreach (var (column, label) in new[] { ("idersalkonnadeh", "فرستنده"), ("iddaryaftkonnadeh", "گیرنده") })
        {
            if (Id(sanad[column]) is { } partyId && Text(catalog.Parties.GetValueOrDefault(partyId)?["title"]) is { } party)
            {
                lines.Add($"{label}: {party}");
            }
        }

        if (Text(sanad["sharhpeyvast"]) is { } note)
        {
            lines.Add($"شرح: {note}");
        }

        return created;
    }

    private static string FolderName(string text)
    {
        var cleaned = Slashes().Replace(text, "-");
        cleaned = Whitespace().Replace(cleaned, " ").Trim(' ', '-', '.');
        cleaned = cleaned.Length > 80 ? cleaned[..80].Trim() : cleaned;
        return cleaned.Length == 0 ? "بدون نام" : cleaned;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[/\\]+")]
    private static partial Regex Slashes();

    [GeneratedRegex("^[a-z0-9]{1,10}$")]
    private static partial Regex SafeExtension();
}

/// <summary>Picks extension and content type from the first bytes of a file.</summary>
public static class FileSignature
{
    private static readonly (byte[] Prefix, string Extension, string ContentType)[] Signatures =
    [
        ([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "png", "image/png"),
        ("%PDF"u8.ToArray(), "pdf", "application/pdf"),
        ([0xFF, 0xD8, 0xFF], "jpg", "image/jpeg"),
        ("GIF87a"u8.ToArray(), "gif", "image/gif"),
        ("GIF89a"u8.ToArray(), "gif", "image/gif"),
        ([0x50, 0x4B, 0x03, 0x04], "zip", "application/zip"),
        ([0xD0, 0xCF, 0x11, 0xE0], "doc", "application/msword"),
        ("BM"u8.ToArray(), "bmp", "image/bmp"),
        ([0x49, 0x49, 0x2A, 0x00], "tif", "image/tiff"),
        ([0x4D, 0x4D, 0x00, 0x2A], "tif", "image/tiff"),
    ];

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = "application/pdf",
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["gif"] = "image/gif",
        ["bmp"] = "image/bmp",
        ["tif"] = "image/tiff",
        ["tiff"] = "image/tiff",
        ["webp"] = "image/webp",
        ["doc"] = "application/msword",
        ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ["xls"] = "application/vnd.ms-excel",
        ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ["ppt"] = "application/vnd.ms-powerpoint",
        ["pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ["txt"] = "text/plain",
        ["rtf"] = "application/rtf",
        ["zip"] = "application/zip",
        ["rar"] = "application/vnd.rar",
    };

    /// <param name="typeExtension">tbl_AnvaeFile's extension, used when the header says nothing,
    /// or to tell a .docx/.xlsx (a zip) or .xls (an OLE file) apart.</param>
    public static (string Extension, string ContentType) Detect(ReadOnlySpan<byte> head, string? typeExtension)
    {
        foreach (var (prefix, extension, contentType) in Signatures)
        {
            if (head.StartsWith(prefix))
            {
                var container = extension is "zip" or "doc";
                if (container && typeExtension is not null && ContentTypes.TryGetValue(typeExtension, out var specific)
                    && IsSameContainer(extension, typeExtension))
                {
                    return (typeExtension, specific);
                }

                return (extension, contentType);
            }
        }

        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
        {
            return ("webp", "image/webp");
        }

        if (typeExtension is not null)
        {
            return (typeExtension, ContentTypes.GetValueOrDefault(typeExtension, "application/octet-stream"));
        }

        return ("bin", "application/octet-stream");
    }

    private static bool IsSameContainer(string sniffed, string typeExtension) => sniffed switch
    {
        "zip" => typeExtension is "docx" or "xlsx" or "pptx" or "zip",
        "doc" => typeExtension is "doc" or "xls" or "ppt",
        _ => false,
    };
}

public static class LegacyManifestWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Write(IReadOnlyList<LegacyManifestEntry> entries) => JsonSerializer.Serialize(
        new LegacyManifest { SchemaVersion = 2, Source = LegacyArchiveMapper.SourceSystem, Entries = [.. entries] },
        Options);
}

// ---------------------------------------------------------------------------------------------
// Commands and queries
// ---------------------------------------------------------------------------------------------

public sealed record TestLegacySqlConnectionCommand(LegacySqlConnectionSettings Settings)
    : ICommand<Result<LegacyArchiveProbeDto>>;

public sealed record StartLegacyExtractionCommand(string? Name, string Server, string Database, string? SqlUser)
    : ICommand<Result<Guid>>;

public sealed record ResumeLegacyExtractionCommand(Guid Id, string? SqlUser) : ICommand<Result>;

public sealed record CancelLegacyExtractionCommand(Guid Id) : ICommand<Result>;

public sealed record ListLegacyExtractionsQuery : IQuery<Result<IReadOnlyList<LegacyExtractionDto>>>;

public sealed record GetLegacyExtractionQuery(Guid Id) : IQuery<Result<LegacyExtractionDto>>;

/// <summary>Who may use the old-server connection, and to which servers.</summary>
public sealed class LegacySqlGate(
    IDmsAuthorizer authorizer,
    IOptions<LegacySqlOptions> legacyOptions,
    IOptions<ImportOptions> importOptions)
{
    public async Task<Result> CheckAsync(string server, bool needsStaging, CancellationToken cancellationToken)
    {
        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportManage, cancellationToken)).Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden("Connecting to an old archive requires IMPORT_MANAGE."));
        }

        var options = legacyOptions.Value;
        if (!options.Enabled)
        {
            return Result.Failure(Error.Forbidden(
                "legacy_sql.disabled",
                "Connecting to an old SQL Server is switched off on this server (Dms:Import:LegacySql:Enabled)."));
        }

        if (options.AllowedServers.Count > 0
            && !options.AllowedServers.Any(allowed => string.Equals(allowed.Trim(), server.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure(Error.Forbidden(
                "legacy_sql.server_not_allowed",
                "This SQL Server is not in Dms:Import:LegacySql:AllowedServers."));
        }

        if (needsStaging && string.IsNullOrWhiteSpace(importOptions.Value.FilesRoot))
        {
            return Result.Failure(Error.Validation(
                "import.files_root",
                "Import files root is not configured on the server (Dms:Import:FilesRoot)."));
        }

        return Result.Success();
    }
}

public sealed class TestLegacySqlConnectionHandler(
    LegacySqlGate gate,
    ILegacySqlArchive archive,
    IAuditWriter audit) : ICommandHandler<TestLegacySqlConnectionCommand, Result<LegacyArchiveProbeDto>>
{
    public async Task<Result<LegacyArchiveProbeDto>> HandleAsync(
        TestLegacySqlConnectionCommand command,
        CancellationToken cancellationToken)
    {
        var settings = command.Settings;
        var allowed = await gate.CheckAsync(settings.Server, needsStaging: false, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<LegacyArchiveProbeDto>(allowed.Error);
        }

        var probe = await archive.ProbeAsync(settings, cancellationToken);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.LegacySqlConnectionTested,
                Outcome = probe.IsSuccess ? AuditOutcome.Success : AuditOutcome.Failed,
                EntityType = "LegacyExtraction",
                Metadata = new Dictionary<string, object?>
                {
                    ["server"] = settings.Server,
                    ["database"] = settings.Database,
                    ["sqlUser"] = settings.User,
                    ["files"] = probe.IsSuccess ? probe.Value.Files : null,
                },
            },
            cancellationToken);
        return probe;
    }
}

public sealed class StartLegacyExtractionHandler(
    LegacySqlGate gate,
    ILegacyExtractionRepository extractions,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<StartLegacyExtractionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(StartLegacyExtractionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor)
        {
            return Result.Failure<Guid>(DocumentErrors.Unauthenticated);
        }

        var allowed = await gate.CheckAsync(command.Server, needsStaging: true, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<Guid>(allowed.Error);
        }

        var created = LegacyExtraction.Create(
            command.Name ?? string.Empty,
            command.Server,
            command.Database,
            command.SqlUser,
            actor,
            timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        extractions.Add(created.Value);
        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.LegacySqlCopyStarted,
                EntityType = "LegacyExtraction",
                EntityId = created.Value.Id.Value,
                Metadata = new Dictionary<string, object?>
                {
                    ["server"] = created.Value.Server,
                    ["database"] = created.Value.Database,
                    ["sqlUser"] = created.Value.SqlUser,
                },
            },
            cancellationToken);
        return Result.Success(created.Value.Id.Value);
    }
}

public sealed class ResumeLegacyExtractionHandler(
    LegacySqlGate gate,
    ILegacyExtractionRepository extractions,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<ResumeLegacyExtractionCommand, Result>
{
    public async Task<Result> HandleAsync(ResumeLegacyExtractionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        var extraction = await extractions.FindAsync(new LegacyExtractionId(command.Id), cancellationToken);
        if (extraction is null)
        {
            return Result.Failure(Error.NotFound("legacy_sql.not_found", "Copy not found."));
        }

        var allowed = await gate.CheckAsync(extraction.Server, needsStaging: true, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var requeued = extraction.Requeue(command.SqlUser, timeProvider.GetUtcNow());
        if (requeued.IsFailure)
        {
            return requeued;
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.LegacySqlCopyResumed,
                EntityType = "LegacyExtraction",
                EntityId = extraction.Id.Value,
            },
            cancellationToken);
        return Result.Success();
    }
}

public sealed class CancelLegacyExtractionHandler(
    IDmsAuthorizer authorizer,
    ILegacyExtractionRepository extractions,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<CancelLegacyExtractionCommand, Result>
{
    public async Task<Result> HandleAsync(CancelLegacyExtractionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return Result.Failure(DocumentErrors.Unauthenticated);
        }

        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportManage, cancellationToken)).Allowed)
        {
            return Result.Failure(DocumentErrors.Forbidden("Stopping a copy requires IMPORT_MANAGE."));
        }

        var extraction = await extractions.FindAsync(new LegacyExtractionId(command.Id), cancellationToken);
        if (extraction is null)
        {
            return Result.Failure(Error.NotFound("legacy_sql.not_found", "Copy not found."));
        }

        var cancelled = extraction.RequestCancel(timeProvider.GetUtcNow());
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        await audit.WriteAsync(
            new AuditRecord
            {
                Action = AuditActions.LegacySqlCopyCancelled,
                EntityType = "LegacyExtraction",
                EntityId = extraction.Id.Value,
            },
            cancellationToken);
        return Result.Success();
    }
}

public sealed class ListLegacyExtractionsHandler(
    IDmsAuthorizer authorizer,
    ILegacyExtractionRepository extractions,
    TimeProvider timeProvider) : IQueryHandler<ListLegacyExtractionsQuery, Result<IReadOnlyList<LegacyExtractionDto>>>
{
    public async Task<Result<IReadOnlyList<LegacyExtractionDto>>> HandleAsync(
        ListLegacyExtractionsQuery query,
        CancellationToken cancellationToken)
    {
        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportView, cancellationToken)).Allowed
            && !(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportManage, cancellationToken)).Allowed)
        {
            return Result.Failure<IReadOnlyList<LegacyExtractionDto>>(
                DocumentErrors.Forbidden("Viewing copies requires IMPORT_VIEW."));
        }

        var now = timeProvider.GetUtcNow();
        var list = await extractions.ListAsync(50, cancellationToken);
        return Result.Success<IReadOnlyList<LegacyExtractionDto>>(
            list.Select(extraction => LegacyExtractionDto.From(extraction, now)).ToList());
    }
}

public sealed class GetLegacyExtractionHandler(
    IDmsAuthorizer authorizer,
    ILegacyExtractionRepository extractions,
    TimeProvider timeProvider) : IQueryHandler<GetLegacyExtractionQuery, Result<LegacyExtractionDto>>
{
    public async Task<Result<LegacyExtractionDto>> HandleAsync(
        GetLegacyExtractionQuery query,
        CancellationToken cancellationToken)
    {
        if (!(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportView, cancellationToken)).Allowed
            && !(await authorizer.AuthorizeSystemAsync(PermissionCodes.ImportManage, cancellationToken)).Allowed)
        {
            return Result.Failure<LegacyExtractionDto>(DocumentErrors.Forbidden("Viewing copies requires IMPORT_VIEW."));
        }

        var extraction = await extractions.FindAsync(new LegacyExtractionId(query.Id), cancellationToken);
        return extraction is null
            ? Result.Failure<LegacyExtractionDto>(Error.NotFound("legacy_sql.not_found", "Copy not found."))
            : Result.Success(LegacyExtractionDto.From(extraction, timeProvider.GetUtcNow()));
    }
}
