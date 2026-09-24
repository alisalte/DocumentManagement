using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Dms.Infrastructure.Jobs;
using Dms.Search.Application;
using Dms.Search.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;

namespace Dms.IntegrationTests;

/// <summary>Files that real renderers can open, and a way to run the job queue on demand.</summary>
internal static class ProcessingTestKit
{
    /// <summary>A valid PDF with one line of Latin text per page, xref offsets and all.</summary>
    public static byte[] RealPdf(params string[] pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', pages.Select((_, index) => $"{4 + (index * 2)} 0 R"))}] /Count {pages.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };

        foreach (var (text, index) in pages.Select((text, index) => (text, index)))
        {
            var stream = $"BT /F1 24 Tf 72 720 Td ({text}) Tj ET";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + (index * 2)} 0 R >>");
            objects.Add($"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream");
        }

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var number = 1; number <= objects.Count; number++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n{objects[number - 1]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    public static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.DarkBlue };
            canvas.DrawRect(10, 10, width / 2f, height / 2f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Runs every due job, including the ones those jobs queue, as the worker would.</summary>
    public static Task<int> RunJobsAsync(this IServiceProvider services) =>
        services.GetRequiredService<JobRunner>().RunUntilIdleAsync(1000, CancellationToken.None);

    /// <summary>A second host on the same database, with the search engine and text extractor replaced.</summary>
    public static WebApplicationFactory<Program> WithSearchFakes(
        this DmsApiFactory factory,
        ISearchEngine engine,
        ITextExtractor extractor) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISearchEngine>();
            services.AddSingleton(engine);
            services.RemoveAll<ITextExtractor>();
            services.AddSingleton(extractor);
        }));

    /// <summary>A client on another host, signed in as the same user.</summary>
    public static HttpClient As(this WebApplicationFactory<Program> host, HttpClient signedIn)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;
        return client;
    }
}

/// <summary>
/// Keeps search documents in memory and, like a broken or lagging engine, returns every one of
/// them for any query: whatever the caller gets back must come from the application's own checks.
/// </summary>
internal sealed class FakeSearchEngine : ISearchEngine
{
    private readonly object _gate = new();

    public ConcurrentDictionary<string, List<JsonObject>> Indices { get; } = new() { ["alias"] = [] };

    public List<JsonObject> Queries { get; } = [];

    public string? SwappedTo { get; private set; }

    public bool IsEnabled => true;

    public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReplaceDocumentAsync(Guid documentId, IReadOnlyList<JsonObject> versions, string? index, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var documents = Indices.GetOrAdd(index ?? "alias", _ => []);
            documents.RemoveAll(existing => existing["document_id"]!.GetValue<string>() == documentId.ToString());
            documents.AddRange(versions.Select(version => version.DeepClone().AsObject()));
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<JsonObject> VersionsOf(Guid documentId, string index = "alias")
    {
        lock (_gate)
        {
            return Indices.GetOrAdd(index, _ => [])
                .Where(version => version["document_id"]!.GetValue<string>() == documentId.ToString())
                .OrderBy(version => version["version_number"]!.GetValue<int>())
                .ToList();
        }
    }

    public Task<EngineResult> SearchAsync(JsonObject query, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Queries.Add(query.DeepClone().AsObject());
            var hits = Indices["alias"]
                .Select(version => new EngineHit(version["version_id"]!.GetValue<string>(), version.DeepClone().AsObject(), null))
                .ToList();
            return Task.FromResult(new EngineResult(hits.Count, hits, null));
        }
    }

    public Task<string> CreateGenerationAsync(CancellationToken cancellationToken)
    {
        var name = $"generation-{Guid.NewGuid():N}";
        Indices[name] = [];
        return Task.FromResult(name);
    }

    public Task SwapAliasAsync(string index, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Indices["alias"] = Indices[index];
            SwappedTo = index;
        }

        return Task.CompletedTask;
    }

    public Task<(string? Index, long Count)> StatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult<(string?, long)>((SwappedTo ?? "alias", Indices["alias"].Count));
}

/// <summary>"Extracts" a file by reading its bytes as text, so a test controls exactly what is indexed.</summary>
internal sealed class FakeTextExtractor : ITextExtractor
{
    public bool IsEnabled => true;

    public async Task<ExtractedText> ExtractAsync(Stream content, string fileName, string mimeType, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content, Encoding.UTF8);
        return new ExtractedText(await reader.ReadToEndAsync(cancellationToken), ExtractionMethod.TextLayer, "fake");
    }
}
