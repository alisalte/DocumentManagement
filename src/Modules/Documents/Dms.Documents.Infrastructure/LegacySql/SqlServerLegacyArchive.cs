using System.Data;
using Dms.Documents.Application;
using Dms.SharedKernel;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Documents.Infrastructure.LegacySql;

/// <summary>
/// Reads the old archive (ArchiveSaman) on SQL Server. Every statement is a SELECT over a fixed
/// table and column list; nothing the user types ends up in SQL text except as a parameter or as
/// the server/database of the connection string builder.
/// </summary>
public sealed class SqlServerLegacyArchive(IOptions<LegacySqlOptions> options, ILogger<SqlServerLegacyArchive> logger)
    : ILegacySqlArchive
{
    internal sealed record FileTable(string Name, string ParentColumn, string SizeColumn, string BlobColumn);

    internal static readonly FileTable[] FileTables =
    [
        new(LegacyArchiveMapper.FileSanad, "IDSanad", "FileSize", "Files"),
        new(LegacyArchiveMapper.FilePeyvast, "IDPeyvast", "FileSize", "Files"),
        new(LegacyArchiveMapper.FilesSampleAsnad, "IDSampleAsnad", "SizeFile", "FileSample"),
    ];

    /// <summary>Lookup tables; small, read whole.</summary>
    internal static readonly string[] CatalogTables =
    [
        "tbl_Parvandeh",
        "tbl_ParvandehSanadPeyvaste3",
        "tbl_PeyvasteSanad",
        "tbl_SampleAsnad",
        "tbl_AnvaeFile",
        "tbl_ErsalKonandegan",
    ];

    /// <summary>Columns read from the lookup tables (the old tbl_User, with its passwords, is never read).</summary>
    private static readonly Dictionary<string, string[]> CatalogColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tbl_Parvandeh"] = ["id", "ShomareParvandeh", "MozoeParvandeh", "KholaseParvandeh"],
        ["tbl_ParvandehSanadPeyvaste3"] =
            ["id", "idSanad", "Mozoe", "ShomareName", "SanadNumber", "DateSanad", "TarikhSabt", "IDErsalKonnadeh", "IDDaryaftKonnadeh", "SharhPeyvast"],
        ["tbl_PeyvasteSanad"] = ["ID", "IDSanad", "Title", "Sharh", "DatePeyvast", "DateSabt"],
        ["tbl_SampleAsnad"] = ["ID", "Title", "Description"],
        ["tbl_AnvaeFile"] = ["ID", "Title", "PasvandFile"],
        ["tbl_ErsalKonandegan"] = ["ID", "Title"],
    };

    public async Task<Result<LegacyArchiveProbeDto>> ProbeAsync(
        LegacySqlConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await ConnectAsync(settings, cancellationToken);
            var version = Convert.ToString(
                await Scalar(connection, "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) + N' ' + CAST(SERVERPROPERTY('Edition') AS nvarchar(128))", cancellationToken))
                ?? string.Empty;

            var tables = new List<LegacyTableCountDto>();
            foreach (var table in FileTables)
            {
                if (!await TableExistsAsync(connection, table.Name, cancellationToken))
                {
                    tables.Add(new LegacyTableCountDto(table.Name, false, 0, 0, 0));
                    continue;
                }

                await using var command = Command(connection, $"""
                    SELECT COUNT_BIG(*),
                           COUNT_BIG(CASE WHEN DATALENGTH([{table.BlobColumn}]) > 0 THEN 1 END),
                           COALESCE(SUM(CAST(DATALENGTH([{table.BlobColumn}]) AS bigint)), 0)
                      FROM [dbo].[{table.Name}]
                    """);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                tables.Add(new LegacyTableCountDto(
                    table.Name,
                    true,
                    (int)reader.GetInt64(0),
                    (int)reader.GetInt64(1),
                    reader.GetInt64(2)));
            }

            foreach (var name in new[] { "tbl_Parvandeh", "tbl_ParvandehSanadPeyvaste3" })
            {
                var exists = await TableExistsAsync(connection, name, cancellationToken);
                var rows = exists
                    ? Convert.ToInt32(await Scalar(connection, $"SELECT COUNT(*) FROM [dbo].[{name}]", cancellationToken))
                    : 0;
                tables.Add(new LegacyTableCountDto(name, exists, rows, 0, 0));
            }

            if (tables.Where(table => FileTables.Any(file => file.Name == table.Table)).All(table => !table.Exists))
            {
                return Result.Failure<LegacyArchiveProbeDto>(Error.Validation(
                    "legacy_sql.not_archive",
                    "Connected, but this database has none of the old archive's file tables (tbl_FileSanad, tbl_FilePeyvast, tbl_FilesSampleAsnad). Check the database name."));
            }

            return Result.Success(new LegacyArchiveProbeDto(
                version,
                tables.Sum(table => table.Files),
                tables.Sum(table => table.Bytes),
                tables));
        }
        catch (SqlException exception)
        {
            logger.LogWarning("Legacy SQL probe of {Target} failed: {Number} {Message}", settings, exception.Number, exception.Message);
            return Result.Failure<LegacyArchiveProbeDto>(Describe(exception));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<LegacyArchiveProbeDto>(Error.Failure("legacy_sql.connect", exception.Message));
        }
    }

    public async Task<ILegacySqlSession> OpenAsync(LegacySqlConnectionSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            return new Session(await ConnectAsync(settings, cancellationToken), options.Value.CommandTimeoutSeconds);
        }
        catch (SqlException exception)
        {
            throw new LegacySqlException(Describe(exception), exception);
        }
    }

    public static string BuildConnectionString(LegacySqlConnectionSettings settings)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = settings.Server.Trim(),
            InitialCatalog = settings.Database.Trim(),
            ApplicationName = "DMS legacy import (read-only)",
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ConnectTimeout = 20,
            Encrypt = settings.Encrypt ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = settings.TrustServerCertificate,
            Pooling = false,
        };
        if (string.IsNullOrWhiteSpace(settings.User))
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = settings.User.Trim();
            builder.Password = settings.Password ?? string.Empty;
        }

        return builder.ConnectionString;
    }

    /// <summary>Turns SQL Server errors into codes the admin page explains in Persian.</summary>
    public static Error Describe(SqlException exception)
    {
        var (code, message) = exception.Number switch
        {
            18456 => ("legacy_sql.login_failed", "Login failed: wrong user name or password, or SQL logins are disabled on the server."),
            4060 => ("legacy_sql.database", "The database does not exist or this login cannot open it."),
            229 or 230 => ("legacy_sql.permission", "This login may not read the archive tables (SELECT permission is needed)."),
            -2 => ("legacy_sql.timeout", "The server did not answer in time."),
            _ when exception.Message.Contains("SSL Provider", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("pre-login handshake", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase) => (
                "legacy_sql.tls",
                "The encrypted connection could not be set up. Old SQL Servers often need «Encrypt» switched off."),
            _ when exception.Number is 2 or 53 or 26 or 40 or 10060 or 10061 or 11001 or -1
                || exception.Message.Contains("network-related", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("server was not found", StringComparison.OrdinalIgnoreCase) => (
                "legacy_sql.unreachable",
                "Cannot reach the SQL Server. Check the address, the instance name, that TCP/IP is enabled and that port 1433 (or SQL Browser, UDP 1434) is open."),
            _ => ("legacy_sql.error", $"SQL Server error {exception.Number}: {exception.Message}"),
        };
        // The address or credentials the administrator typed lead nowhere: a 400 for them to fix,
        // not a 500 that reads like a bug in this server.
        return Error.Validation(code, message);
    }

    private async Task<SqlConnection> ConnectAsync(LegacySqlConnectionSettings settings, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(BuildConnectionString(settings));
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private SqlCommand Command(SqlConnection connection, string sql) =>
        new(sql, connection) { CommandTimeout = options.Value.CommandTimeoutSeconds };

    private async Task<object?> Scalar(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, sql);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DBNull ? null : value;
    }

    internal static async Task<bool> TableExistsAsync(SqlConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT OBJECT_ID(@name, N'U')", connection);
        command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 256) { Value = $"dbo.{table}" });
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null and not DBNull;
    }

    private sealed class Session(SqlConnection connection, int commandTimeout) : ILegacySqlSession
    {
        public async Task<LegacyArchiveCatalog> ReadCatalogAsync(CancellationToken cancellationToken)
        {
            var catalog = new LegacyArchiveCatalog();
            foreach (var table in CatalogTables)
            {
                if (!await TableExistsAsync(connection, table, cancellationToken))
                {
                    catalog.Warnings.Add($"{table} is missing; its details are left out of titles and descriptions.");
                    continue;
                }

                var target = table switch
                {
                    "tbl_Parvandeh" => catalog.Cases,
                    "tbl_ParvandehSanadPeyvaste3" => catalog.Sanads,
                    "tbl_PeyvasteSanad" => catalog.Peyvasts,
                    "tbl_SampleAsnad" => catalog.Samples,
                    "tbl_AnvaeFile" => catalog.FileTypes,
                    _ => catalog.Parties,
                };

                var columns = await ExistingColumnsAsync(table, CatalogColumns[table], cancellationToken);
                var select = string.Join(", ", columns.Select(column => $"[{column}]"));
                await using var command = new SqlCommand($"SELECT {select} FROM [dbo].[{table}]", connection)
                {
                    CommandTimeout = commandTimeout,
                };
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var values = new Dictionary<string, object?>(reader.FieldCount);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        values[reader.GetName(i).ToLowerInvariant()] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }

                    if (LegacyArchiveMapper.Id(values.GetValueOrDefault("id")) is { } id)
                    {
                        target[id] = new LegacyRow(values);
                    }
                }
            }

            foreach (var table in FileTables)
            {
                if (!await TableExistsAsync(connection, table.Name, cancellationToken))
                {
                    catalog.Warnings.Add($"{table.Name} is missing in the old database.");
                    continue;
                }

                await using var command = new SqlCommand($"""
                    SELECT [ID], [IDTypeFile], [{table.ParentColumn}], [{table.SizeColumn}], [Sharh],
                           CAST(DATALENGTH([{table.BlobColumn}]) AS bigint)
                      FROM [dbo].[{table.Name}]
                     ORDER BY [ID]
                    """, connection)
                {
                    CommandTimeout = commandTimeout,
                };
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var length = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
                    if (length <= 0)
                    {
                        catalog.EmptyFiles++;
                        continue;
                    }

                    catalog.Files.Add(new LegacyFileRow(
                        table.Name,
                        LegacyArchiveMapper.Id(reader.GetValue(0))!,
                        LegacyArchiveMapper.Id(Value(reader, 2)),
                        LegacyArchiveMapper.Id(Value(reader, 1)),
                        Value(reader, 3) is { } declared ? Convert.ToInt64(declared, System.Globalization.CultureInfo.InvariantCulture) : null,
                        LegacyArchiveMapper.Text(Value(reader, 4)),
                        length));
                }
            }

            return catalog;
        }

        public async Task<long> CopyFileAsync(LegacyFileRow file, Stream destination, CancellationToken cancellationToken)
        {
            var table = FileTables.First(candidate => candidate.Name == file.Table);
            await using var command = new SqlCommand(
                $"SELECT [{table.BlobColumn}] FROM [dbo].[{table.Name}] WHERE [ID] = @id",
                connection)
            {
                CommandTimeout = commandTimeout,
            };
            command.Parameters.Add(new SqlParameter("@id", SqlDbType.Decimal)
            {
                Precision = 18,
                Scale = 0,
                Value = decimal.Parse(file.Id, System.Globalization.CultureInfo.InvariantCulture),
            });

            // SequentialAccess streams the blob instead of buffering it, so a 2 GB file needs no 2 GB of memory.
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || await reader.IsDBNullAsync(0, cancellationToken))
            {
                return 0;
            }

            var before = destination.CanSeek ? destination.Position : 0;
            await using var blob = reader.GetStream(0);
            await blob.CopyToAsync(destination, 1 << 20, cancellationToken);
            return destination.CanSeek ? destination.Position - before : file.Length;
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();

        private static object? Value(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);

        private async Task<IReadOnlyList<string>> ExistingColumnsAsync(
            string table,
            IReadOnlyList<string> wanted,
            CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand(
                "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@table, N'U')",
                connection);
            command.Parameters.Add(new SqlParameter("@table", SqlDbType.NVarChar, 256) { Value = $"dbo.{table}" });
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    present.Add(reader.GetString(0));
                }
            }

            return wanted.Where(present.Contains).ToList();
        }
    }
}

/// <summary>A SQL Server failure already described for the admin page.</summary>
public sealed class LegacySqlException(Error error, Exception inner) : Exception(error.Message, inner)
{
    public Error Error { get; } = error;
}
