using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dms.Application;
using Dms.DocumentTypes.Contracts;
using Dms.Documents.Contracts;
using Dms.Search.Domain;
using Dms.Storage.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Search.Application;

/// <summary>
/// One search document per version-revision (section 8.2), so a hit says which version matched:
/// "amount = 12 billion" finds V2, not the document in general.
///
/// Metadata goes in twice: as full text in <c>meta_text</c> (searchable fields only), and typed
/// under <c>meta.{TYPE}.{field}__{kind}</c> for filters and ranges. The type code in the path keeps
/// equal field codes of different types apart, and because a code never changes its type
/// (enforced at publish), a mapping never conflicts.
/// </summary>
public static class SearchDocumentBuilder
{
    public static string Suffix(FieldType type) => type switch
    {
        FieldType.Integer or FieldType.Decimal => "num",
        FieldType.Boolean => "bool",
        FieldType.Date or FieldType.DateTime => "date",
        FieldType.Text or FieldType.LongText => "txt",
        _ => "kw",
    };

    public static string MetaPath(string typeCode, string fieldCode, FieldType type) =>
        $"meta.{typeCode}.{fieldCode}__{Suffix(type)}";

    public static JsonObject Build(
        DocumentIndexData document,
        VersionIndexData version,
        string typeCode,
        DocumentTypeSchema? schema,
        string? content)
    {
        var (meta, metaText) = Metadata(version.MetadataJson, schema);

        return new JsonObject
        {
            ["document_id"] = document.DocumentId.ToString(),
            ["version_id"] = version.VersionId.ToString(),
            ["version_number"] = version.VersionNumber,
            ["revision_number"] = version.RevisionNumber,
            ["label"] = version.Label,
            ["is_current"] = version.VersionId == document.CurrentVersionId,
            ["is_effective"] = version.VersionId == document.EffectiveVersionId,
            ["is_published"] = version.IsPublished,
            ["approval_status"] = version.ApprovalStatus.ToString(),
            ["is_deleted"] = document.IsDeleted,
            ["category_id"] = document.CategoryId.ToString(),
            ["category_path"] = new JsonArray(document.CategoryPath.Select(id => (JsonNode?)JsonValue.Create(id.ToString())).ToArray()),
            ["document_type_id"] = document.DocumentTypeId.ToString(),
            ["document_type_code"] = typeCode,
            ["owner_id"] = document.OwnerId.ToString(),
            ["created_by"] = version.CreatedBy.ToString(),
            ["created_at"] = version.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            ["updated_at"] = document.UpdatedAt.ToString("O", CultureInfo.InvariantCulture),
            ["title"] = document.Title,
            ["description"] = document.Description,
            ["tags"] = new JsonArray(document.Tags.Select(tag => (JsonNode?)JsonValue.Create(tag)).ToArray()),
            ["file_name"] = version.FileName,
            ["mime_type"] = version.MimeType,
            ["meta_text"] = metaText,
            ["meta"] = new JsonObject { [typeCode] = meta },
            ["content"] = content,
        };
    }

    private static (JsonObject Meta, string Text) Metadata(string json, DocumentTypeSchema? schema)
    {
        var meta = new JsonObject();
        var text = new StringBuilder();
        if (schema is null || string.IsNullOrWhiteSpace(json))
        {
            return (meta, string.Empty);
        }

        using var parsed = JsonDocument.Parse(json);
        foreach (var field in schema.Fields)
        {
            if (!parsed.RootElement.TryGetProperty(field.Code, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var key = $"{field.Code}__{Suffix(field.Type)}";
            switch (field.Type)
            {
                case FieldType.Integer or FieldType.Decimal when value.ValueKind == JsonValueKind.Number:
                    meta[key] = value.GetDouble();
                    break;

                // Decimals are stored as strings to keep their precision; the index compares doubles.
                case FieldType.Integer or FieldType.Decimal
                    when value.ValueKind == JsonValueKind.String
                    && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number):
                    meta[key] = number;
                    break;
                case FieldType.Boolean:
                    meta[key] = value.ValueKind == JsonValueKind.True;
                    break;
                case FieldType.MultiSelect when value.ValueKind == JsonValueKind.Array:
                    meta[key] = new JsonArray(value.EnumerateArray().Select(item => (JsonNode?)JsonValue.Create(item.GetString())).ToArray());
                    break;
                default:
                    meta[key] = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
                    break;
            }

            if (!field.IsSearchable)
            {
                continue;
            }

            // Full text gets what a person would type: the option's label as well as its value.
            foreach (var item in value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [value])
            {
                var raw = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
                var label = field.Options.FirstOrDefault(option => option.Value == raw)?.Label.Fa;
                text.Append(label ?? raw).Append(' ');
            }
        }

        return (meta, text.ToString().Trim());
    }
}

/// <summary>Reads, builds and writes the search documents of one document.</summary>
public sealed class DocumentIndexer(
    IDocumentIndexSource source,
    IDocumentTypeCatalog documentTypes,
    IContentExtractionRepository extractions,
    IStorageService storage,
    ISearchEngine engine,
    IOptions<SearchOptions> options)
{
    public async Task IndexAsync(Guid documentId, string? index, CancellationToken cancellationToken)
    {
        var document = await source.GetAsync(documentId, cancellationToken);
        if (document is null)
        {
            // Purged: nothing of it may remain searchable.
            await engine.ReplaceDocumentAsync(documentId, [], index, cancellationToken);
            return;
        }

        var type = await documentTypes.FindAsync(new DocumentTypeId(document.DocumentTypeId), cancellationToken);
        var texts = await extractions.FindManyAsync(document.Versions.Select(version => version.StorageObjectId).Distinct().ToList(), cancellationToken);
        var textCache = new Dictionary<Guid, string?>();
        var built = new List<JsonObject>();

        foreach (var version in document.Versions)
        {
            var schema = await documentTypes.GetSchemaAsync(new DocumentTypeVersionId(version.SchemaVersionId), cancellationToken);

            if (!textCache.TryGetValue(version.StorageObjectId, out var content))
            {
                content = texts.TryGetValue(version.StorageObjectId, out var extraction) && extraction.TextObjectId is { } textId
                    ? await ReadTextAsync(textId, cancellationToken)
                    : null;
                textCache[version.StorageObjectId] = content;
            }

            built.Add(SearchDocumentBuilder.Build(document, version, type?.Code ?? "UNKNOWN", schema, content));
        }

        await engine.ReplaceDocumentAsync(documentId, built, index, cancellationToken);
    }

    private async Task<string> ReadTextAsync(Guid textObjectId, CancellationToken cancellationToken)
    {
        await using var stored = await storage.OpenForProcessingAsync(new StorageObjectId(textObjectId), cancellationToken);
        await using var gzip = new GZipStream(stored, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        var buffer = new char[options.Value.MaxIndexedChars];
        var read = await reader.ReadBlockAsync(buffer, cancellationToken);
        return new string(buffer, 0, read);
    }
}

/// <summary>Step 2 and 3 of section 8.1: text (with OCR) for one clean, committed file.</summary>
public sealed class ExtractTextJob(
    IContentExtractionRepository extractions,
    IStorageService storage,
    ITextExtractor extractor,
    IDocumentIndexSource documents,
    IJobQueue jobs,
    IOptions<SearchOptions> options,
    TimeProvider timeProvider,
    ILogger<ExtractTextJob> logger) : IJobHandler
{
    public const string Type = "search.extract-text";

    public string JobType => Type;

    public static JobRequest For(Guid storageObjectId) =>
        new(Type, new { storageObjectId }, IdempotencyKey: $"{Type}:{storageObjectId}");

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var objectId = JsonSerializer.Deserialize<Payload>(payload, JsonSerializerOptions.Web)!.StorageObjectId;
        var file = await storage.FindAsync(new StorageObjectId(objectId), cancellationToken);

        // Never read an unscanned or infected file (decision D9).
        if (file is null || file.Status != StorageObjectStatus.Committed || !file.IsContentAvailable)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var extraction = await extractions.FindAsync(objectId, cancellationToken);
        if (extraction is null)
        {
            extraction = ContentExtraction.Start(objectId, now);
            extractions.Add(extraction);
        }

        if (extraction.Status is ExtractionStatus.Pending or ExtractionStatus.Failed)
        {
            await ExtractAsync(extraction, file, cancellationToken);
        }

        foreach (var documentId in await documents.DocumentsUsingObjectAsync(objectId, cancellationToken))
        {
            await jobs.EnqueueAsync(IndexDocumentJob.For(documentId), cancellationToken);
        }
    }

    private async Task ExtractAsync(ContentExtraction extraction, StorageObjectInfo file, CancellationToken cancellationToken)
    {
        if (!extractor.IsEnabled)
        {
            extraction.Skip("No text extractor is configured.", timeProvider.GetUtcNow());
            return;
        }

        extraction.BeginAttempt();
        try
        {
            await using var content = await storage.OpenForProcessingAsync(file.Id, cancellationToken);
            var extracted = await extractor.ExtractAsync(content, file.FileName, file.MimeType, cancellationToken);

            var text = extracted.Text.Length > options.Value.MaxTextChars
                ? extracted.Text[..options.Value.MaxTextChars]
                : extracted.Text;

            Guid? textObject = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                // Stored as gzip, so the index can be rebuilt without running OCR again (section 8.4).
                using var packed = new MemoryStream();
                await using (var gzip = new GZipStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
                {
                    await gzip.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
                }

                packed.Position = 0;
                textObject = (await storage.StoreDerivedAsync(file.Id, packed, $"{file.Id}.txt.gz", "application/gzip", DerivedPurpose.ExtractedText, cancellationToken)).Value;
            }

            extraction.Complete(extracted.Method, textObject, text.Length, extracted.Engine, timeProvider.GetUtcNow());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Metadata stays searchable without the text; an administrator can re-run it later.
            logger.LogWarning(exception, "Text extraction of {ObjectId} failed.", file.Id);
            extraction.Fail(exception.Message, timeProvider.GetUtcNow());
        }
    }

    private sealed record Payload(Guid StorageObjectId);
}

/// <summary>Brings one document's search documents in line with Postgres.</summary>
public sealed class IndexDocumentJob(DocumentIndexer indexer, ISearchEngine engine) : IJobHandler
{
    public const string Type = "search.index-document";

    public string JobType => Type;

    /// <summary>
    /// No idempotency key on purpose: a key also blocks while a job is running, and a change made
    /// during a run must still trigger one more run, or the index keeps the older state.
    /// </summary>
    public static JobRequest For(Guid documentId) => new(Type, new { documentId });

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        if (!engine.IsEnabled)
        {
            return;
        }

        var documentId = JsonSerializer.Deserialize<Payload>(payload, JsonSerializerOptions.Web)!.DocumentId;
        await engine.EnsureReadyAsync(cancellationToken);
        await indexer.IndexAsync(documentId, index: null, cancellationToken);
    }

    private sealed record Payload(Guid DocumentId);
}

/// <summary>
/// Rebuilds the whole index into a new generation and swaps the alias when done (section 8.4):
/// search keeps answering from the old index meanwhile, and nothing is OCR'd again.
/// </summary>
public sealed class ReindexJob(
    IDocumentIndexSource documents,
    DocumentIndexer indexer,
    ISearchEngine engine,
    ILogger<ReindexJob> logger) : IJobHandler
{
    public const string Type = "search.reindex";

    public string JobType => Type;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        if (!engine.IsEnabled)
        {
            return;
        }

        var generation = await engine.CreateGenerationAsync(cancellationToken);
        var count = 0;
        Guid? after = null;
        while (true)
        {
            var batch = await documents.ListIdsAsync(after, 200, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var id in batch)
            {
                await indexer.IndexAsync(id, generation, cancellationToken);
                count++;
            }

            after = batch[^1];
        }

        await engine.SwapAliasAsync(generation, cancellationToken);
        logger.LogInformation("Reindexed {Count} documents into {Index}.", count, generation);
    }
}

public sealed class SearchObjectProcessedListener(IJobQueue jobs) : IObjectProcessedListener
{
    public Task OnProcessedAsync(StorageObjectInfo file, CancellationToken cancellationToken) =>
        jobs.EnqueueAsync(ExtractTextJob.For(file.Id.Value), cancellationToken);
}

public sealed class SearchDocumentChangeListener(IJobQueue jobs) : IDocumentChangeListener
{
    public Task OnDocumentChangedAsync(Guid documentId, CancellationToken cancellationToken) =>
        jobs.EnqueueAsync(IndexDocumentJob.For(documentId), cancellationToken);
}

/// <summary>
/// Gives failed extractions (Tika or OCR briefly down, a timeout) a few more tries on a schedule.
/// After <see cref="MaxAttempts"/> they wait for an administrator's "retry failed".
/// </summary>
public sealed class RetryFailedExtractionsJob(IContentExtractionRepository extractions, IJobQueue jobs) : IJobHandler
{
    public const string Type = "search.retry-failed-extractions";

    public const int MaxAttempts = 3;

    public string JobType => Type;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        foreach (var failed in await extractions.ListFailedAsync(MaxAttempts, 500, cancellationToken))
        {
            await jobs.EnqueueAsync(ExtractTextJob.For(failed.StorageObjectId), cancellationToken);
        }
    }
}
