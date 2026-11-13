using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dms.Documents.Application;
using Dms.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Copying the old archive straight from SQL Server, from the admin API through to imported
/// documents. SQL Server itself cannot run in this suite, so <see cref="ILegacySqlArchive"/> is
/// replaced by an in-memory archive; the runner, staging files, manifest, import job, database and
/// audit are all real.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LegacySqlImportTests(DmsApiFactory factory)
{
    private const string Password = "Old-Archive-P@ss-7781";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3, 4];
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\nold archive letter\n%%EOF");

    [Fact]
    public async Task Copies_every_file_from_the_old_server_then_imports_them_into_case_folders()
    {
        var ids = Random.Shared.Next(1_000_000, 9_000_000);
        var archive = new InMemoryArchive(ids);
        await using var host = WithArchive(archive);
        var admin = host.As(await factory.AdminAsync());

        var probe = await admin.PostAsJsonAsync("/api/v1/imports/legacy-sql/test", Request());
        probe.StatusCode.ShouldBe(HttpStatusCode.OK, await probe.Content.ReadAsStringAsync());
        (await probe.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("files").GetInt32().ShouldBe(3);

        var started = await admin.PostAsJsonAsync("/api/v1/imports/legacy-sql", Request());
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync());
        var copyId = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var copy = await WaitAsync(admin, copyId);
        copy.GetProperty("status").GetString().ShouldBe("Completed", copy.ToString());
        copy.GetProperty("filesTotal").GetInt32().ShouldBe(3);
        copy.GetProperty("filesDone").GetInt32().ShouldBe(3);
        copy.GetProperty("filesEmpty").GetInt32().ShouldBe(1);
        archive.Passwords.ShouldAllBe(password => password == Password);

        // The files are on disk under the import root, with a manifest the import engine accepts.
        var staging = Path.Combine(factory.ImportFilesRoot, copy.GetProperty("stagingFolder").GetString()!);
        File.ReadAllBytes(Path.Combine(staging, "files", $"sanad-{ids + 100}-file-{ids + 1}.png")).ShouldBe(Png);
        File.Exists(Path.Combine(staging, "manifest.json")).ShouldBeTrue();

        var jobId = copy.GetProperty("importJobId").GetGuid();
        var validated = await admin.PostAsync($"/api/v1/imports/{jobId}/validate", null);
        validated.StatusCode.ShouldBe(HttpStatusCode.OK, await validated.Content.ReadAsStringAsync());
        (await validated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("invalid").GetInt32().ShouldBe(0);
        (await admin.PostAsync($"/api/v1/imports/{jobId}/start", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await factory.Services.RunJobsAsync();

        var items = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/imports/{jobId}/items");
        items.EnumerateArray().Select(item => item.GetProperty("status").GetString()).ShouldAllBe(status => status == "Succeeded");
        var letter = items.EnumerateArray().Single(item => item.GetProperty("sourceId").GetString() == $"sanad-{ids + 100}-file-{ids + 2}");
        var document = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{letter.GetProperty("targetDocumentId").GetGuid()}");
        document.GetProperty("title").GetString().ShouldBe("نامه درخواست (فایل 2 از 2)");
        document.GetProperty("categoryName").GetString().ShouldBe($"پرونده {ids + 10} - قرارداد خرید");

        // The password is nowhere in the database: not on the copy, not in the audit log.
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT count(*) FROM documents.legacy_extractions e WHERE e::text LIKE '%' || @p || '%')
                 + (SELECT count(*) FROM audit.audit_logs a WHERE a::text LIKE '%' || @p || '%')
            """;
        command.Parameters.AddWithValue("p", Password);
        ((long)(await command.ExecuteScalarAsync())!).ShouldBe(0);
    }

    [Fact]
    public async Task A_failed_copy_can_be_resumed_and_reuses_the_files_already_copied()
    {
        var ids = Random.Shared.Next(1_000_000, 9_000_000);
        var archive = new InMemoryArchive(ids) { FailAfter = 1 };
        await using var host = WithArchive(archive);
        var admin = host.As(await factory.AdminAsync());

        var started = await admin.PostAsJsonAsync("/api/v1/imports/legacy-sql", Request());
        var copyId = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var failed = await WaitAsync(admin, copyId);
        failed.GetProperty("status").GetString().ShouldBe("Failed");
        failed.GetProperty("lastError").GetString()!.ShouldContain("legacy_sql.unreachable");
        failed.GetProperty("canResume").GetBoolean().ShouldBeTrue();

        archive.FailAfter = null;
        var resumed = await admin.PostAsJsonAsync($"/api/v1/imports/legacy-sql/{copyId}/resume", new { user = "sa", password = Password });
        resumed.StatusCode.ShouldBe(HttpStatusCode.OK, await resumed.Content.ReadAsStringAsync());

        var done = await WaitAsync(admin, copyId);
        done.GetProperty("status").GetString().ShouldBe("Completed", done.ToString());
        done.GetProperty("filesReused").GetInt32().ShouldBe(1);
        archive.Copied.Count.ShouldBe(3, "one file before the failure, then only the two still missing");
    }

    [Fact]
    public async Task Only_import_managers_may_connect_to_an_old_server()
    {
        await using var host = WithArchive(new InMemoryArchive(1));
        var (user, _) = await factory.CreateUserAsync("legacysql");
        var client = host.As(user);

        var probe = await client.PostAsJsonAsync("/api/v1/imports/legacy-sql/test", Request());
        probe.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var start = await client.PostAsJsonAsync("/api/v1/imports/legacy-sql", Request());
        start.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static object Request() => new
    {
        server = "old-server\\SQLEXPRESS",
        database = "ArchiveSaman",
        user = "sa",
        password = Password,
        encrypt = false,
    };

    private WebApplicationFactory<Program> WithArchive(ILegacySqlArchive archive) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILegacySqlArchive>();
            services.AddSingleton(archive);
        }));

    private static async Task<JsonElement> WaitAsync(HttpClient client, Guid id)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            var copy = await client.GetFromJsonAsync<JsonElement>($"/api/v1/imports/legacy-sql/{id}");
            if (copy.GetProperty("status").GetString() is "Completed" or "Failed" or "Cancelled")
            {
                return copy;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The copy did not finish.");
    }

    /// <summary>The old archive with one case, one letter of two pages, an empty row and a template.</summary>
    private sealed class InMemoryArchive(int ids) : ILegacySqlArchive
    {
        public ConcurrentBag<string?> Passwords { get; } = [];

        public ConcurrentQueue<string> Copied { get; } = [];

        /// <summary>Fail with «unreachable» after this many files.</summary>
        public int? FailAfter { get; set; }

        public Task<Result<LegacyArchiveProbeDto>> ProbeAsync(
            LegacySqlConnectionSettings settings,
            CancellationToken cancellationToken)
        {
            Passwords.Add(settings.Password);
            var files = Catalog().Files;
            return Task.FromResult(Result.Success(new LegacyArchiveProbeDto(
                "16.0 Express",
                files.Count,
                files.Sum(file => file.Length),
                [])));
        }

        public Task<ILegacySqlSession> OpenAsync(LegacySqlConnectionSettings settings, CancellationToken cancellationToken)
        {
            Passwords.Add(settings.Password);
            return Task.FromResult<ILegacySqlSession>(new Session(this));
        }

        private string Id(int offset) => (ids + offset).ToString();

        private LegacyArchiveCatalog Catalog()
        {
            static LegacyRow Row(params (string Column, object? Value)[] values) =>
                new(values.ToDictionary(value => value.Column.ToLowerInvariant(), value => value.Value));

            var catalog = new LegacyArchiveCatalog();
            catalog.Cases[Id(10)] = Row(("id", (decimal)(ids + 10)), ("MozoeParvandeh", "قرارداد خرید"));
            catalog.Sanads[Id(100)] = Row(("id", (decimal)(ids + 100)), ("idSanad", (decimal)(ids + 10)), ("Mozoe", "نامه درخواست"), ("DateSanad", 14021201m));
            catalog.Samples[Id(3)] = Row(("ID", (decimal)(ids + 3)), ("Title", "فرم مرخصی"));
            catalog.Files.Add(new LegacyFileRow(LegacyArchiveMapper.FileSanad, Id(1), Id(100), null, Png.Length, null, Png.Length));
            catalog.Files.Add(new LegacyFileRow(LegacyArchiveMapper.FileSanad, Id(2), Id(100), null, Pdf.Length, null, Pdf.Length));
            catalog.Files.Add(new LegacyFileRow(LegacyArchiveMapper.FilesSampleAsnad, Id(1), Id(3), null, null, null, Pdf.Length));
            catalog.EmptyFiles = 1;
            return catalog;
        }

        private sealed class Session(InMemoryArchive archive) : ILegacySqlSession
        {
            public Task<LegacyArchiveCatalog> ReadCatalogAsync(CancellationToken cancellationToken) =>
                Task.FromResult(archive.Catalog());

            public async Task<long> CopyFileAsync(LegacyFileRow file, Stream destination, CancellationToken cancellationToken)
            {
                if (archive.FailAfter is { } limit && archive.Copied.Count >= limit)
                {
                    throw new Dms.Documents.Infrastructure.LegacySql.LegacySqlException(
                        Error.Failure("legacy_sql.unreachable", "Cannot reach the SQL Server."),
                        new IOException("network down"));
                }

                archive.Copied.Enqueue(LegacyArchiveMapper.SourceId(file));
                var bytes = file.Table == LegacyArchiveMapper.FileSanad && file.Id == archive.Id(1) ? Png : Pdf;
                await destination.WriteAsync(bytes, cancellationToken);
                return bytes.Length;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
