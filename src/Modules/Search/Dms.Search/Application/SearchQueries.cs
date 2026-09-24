using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dms.Application;
using Dms.Audit.Contracts;
using Dms.Authorization.Contracts;
using Dms.DocumentTypes.Contracts;
using Dms.Documents.Contracts;
using Dms.Search.Domain;
using Dms.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Dms.Search.Application;

/// <summary>A typed condition on one metadata field of one document type ("amount gte 12e9").</summary>
public sealed record MetadataFilter(string Field, string Op, JsonElement Value);

public sealed record SearchDocumentsQuery(
    string? Text,
    Guid? CategoryId,
    Guid? DocumentTypeId,
    string? MimeType,
    string? Tag,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool AllVersions,
    IReadOnlyList<MetadataFilter>? Metadata,
    int? Page,
    int? PageSize) : IQuery<Result<SearchResultDto>>;

public sealed record SearchHitDto(
    Guid DocumentId,
    Guid VersionId,
    string Label,
    string Title,
    string? FileName,
    string? MimeType,
    Guid? CategoryId,
    Guid? DocumentTypeId,
    bool IsCurrent,
    bool IsEffective,
    string? ApprovalStatus,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<string> Highlights);

public sealed record FacetBucket(string Key, long Count);

public sealed record SearchResultDto(
    IReadOnlyList<SearchHitDto> Hits,
    long Total,
    int Page,
    int PageSize,
    IReadOnlyDictionary<string, IReadOnlyList<FacetBucket>> Facets,
    bool Degraded);

public sealed record SearchStatusDto(bool EngineEnabled, bool ExtractorEnabled, string? Index, long IndexedVersions, IReadOnlyDictionary<string, int> Extractions);

public sealed record SearchStatusQuery : IQuery<Result<SearchStatusDto>>;

public sealed record StartReindexCommand : ICommand<Result>;

/// <summary>
/// Secured search (section 8.3). Access is enforced three times over, and the engine is never
/// the authority:
/// <list type="number">
/// <item>the caller's access scope (section 5.7) is ANDed into the query, so hits, totals and
/// facet counts only ever come from documents they may see;</item>
/// <item>unpublished versions are filtered to their author (decision D6);</item>
/// <item>every hit on the returned page is checked again against Postgres before it leaves.</item>
/// </list>
/// Permissions are not in the index, so a revoked grant takes effect at once without reindexing.
/// </summary>
public sealed class SearchDocumentsHandler(
    ISearchEngine engine,
    IAccessScopeProvider scopes,
    IDmsAuthorizer authorizer,
    IDocumentTypeCatalog documentTypes,
    IDocumentTitleSearch titles,
    ICurrentUser currentUser,
    ILogger<SearchDocumentsHandler> logger) : IQueryHandler<SearchDocumentsQuery, Result<SearchResultDto>>
{
    private static readonly Dictionary<string, string> FacetFields = new(StringComparer.Ordinal)
    {
        ["category"] = "category_id",
        ["documentType"] = "document_type_id",
        ["mimeType"] = "mime_type",
        ["tag"] = "tags",
    };

    public async Task<Result<SearchResultDto>> HandleAsync(SearchDocumentsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } viewer)
        {
            return Result.Failure<SearchResultDto>(Error.Unauthorized("auth.unauthenticated", "Not authenticated."));
        }

        var page = Math.Max(query.Page ?? 1, 1);
        var pageSize = Math.Clamp(query.PageSize ?? 20, 1, 100);

        if (!engine.IsEnabled)
        {
            return Result.Success(await DegradedAsync(query, page, pageSize, cancellationToken));
        }

        var filters = new JsonArray();
        var scope = await scopes.GetScopeAsync(viewer, PermissionCodes.DocumentView, cancellationToken);
        filters.Add(ScopeFilter(scope));
        filters.Add(Term("is_deleted", false));

        // Decision D6: a version that is not published yet is found only by its author.
        filters.Add(Should(Term("is_published", true), Term("created_by", viewer.Value.ToString())));

        // By default one row per document: the version a reader gets, or the author's own newest
        // draft. "All versions" searches the history too, which is what version-aware search is for.
        if (!query.AllVersions)
        {
            filters.Add(Should(Term("is_effective", true), Bool(must: [Term("is_current", true), Term("created_by", viewer.Value.ToString())])));
        }

        if (query.CategoryId is { } category)
        {
            filters.Add(Term("category_path", category.ToString()));
        }

        if (query.DocumentTypeId is { } typeId)
        {
            filters.Add(Term("document_type_id", typeId.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(query.MimeType))
        {
            filters.Add(Term("mime_type", query.MimeType));
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            filters.Add(Term("tags", query.Tag));
        }

        if (query.From is not null || query.To is not null)
        {
            var range = new JsonObject();
            if (query.From is { } from) range["gte"] = from.ToString("O", CultureInfo.InvariantCulture);
            if (query.To is { } to) range["lte"] = to.ToString("O", CultureInfo.InvariantCulture);
            filters.Add(new JsonObject { ["range"] = new JsonObject { ["created_at"] = range } });
        }

        if (query.Metadata is { Count: > 0 })
        {
            var metadata = await MetadataFiltersAsync(query.DocumentTypeId, query.Metadata, cancellationToken);
            if (metadata.IsFailure)
            {
                return Result.Failure<SearchResultDto>(metadata.Error);
            }

            foreach (var filter in metadata.Value)
            {
                filters.Add(filter);
            }
        }

        var body = BuildQuery(query.Text, filters, page, pageSize);

        EngineResult result;
        try
        {
            result = await engine.SearchAsync(body, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Section 8.4: search keeps working on titles when the engine is down.
            logger.LogError(exception, "The search engine failed; falling back to title search.");
            return Result.Success(await DegradedAsync(query, page, pageSize, cancellationToken));
        }

        var hits = new List<SearchHitDto>();
        foreach (var hit in result.Hits)
        {
            if (await StillVisibleAsync(hit.Source, viewer, cancellationToken) && ToHit(hit) is { } dto)
            {
                hits.Add(dto);
            }
        }

        return Result.Success(new SearchResultDto(hits, result.Total, page, pageSize, Facets(result.Aggregations), Degraded: false));
    }

    /// <summary>Section 5.7 as an engine filter: allowed somewhere applicable, denied nowhere.</summary>
    private static JsonObject ScopeFilter(AccessScope scope) => Bool(
        should:
        [
            Terms("category_id", scope.AllowedCategories),
            Terms("document_id", scope.AllowedResources),
        ],
        mustNot:
        [
            Terms("category_id", scope.DeniedCategories),
            Terms("document_id", scope.DeniedResources),
        ]);

    private static JsonObject BuildQuery(string? text, JsonArray filters, int page, int pageSize)
    {
        JsonNode match = string.IsNullOrWhiteSpace(text)
            ? new JsonObject { ["match_all"] = new JsonObject() }
            : new JsonObject
            {
                ["multi_match"] = new JsonObject
                {
                    ["query"] = text.Trim(),
                    ["fields"] = new JsonArray("title^4", "title.en^2", "tags.text^3", "file_name^2", "meta_text^2", "description", "content", "content.en"),
                    ["type"] = "best_fields",
                    ["operator"] = "and",
                },
            };

        var aggregations = new JsonObject();
        foreach (var (name, field) in FacetFields)
        {
            aggregations[name] = new JsonObject { ["terms"] = new JsonObject { ["field"] = field, ["size"] = 20 } };
        }

        return new JsonObject
        {
            ["from"] = (page - 1) * pageSize,
            ["size"] = pageSize,
            ["track_total_hits"] = true,
            ["query"] = new JsonObject { ["bool"] = new JsonObject { ["must"] = new JsonArray(match), ["filter"] = filters } },
            ["_source"] = new JsonObject { ["excludes"] = new JsonArray("content", "meta_text", "meta") },
            ["sort"] = string.IsNullOrWhiteSpace(text)
                ? new JsonArray(new JsonObject { ["updated_at"] = "desc" })
                : new JsonArray("_score", new JsonObject { ["updated_at"] = "desc" }),
            // Facets are counted over the same filtered query: they cannot reveal hidden documents.
            ["aggs"] = aggregations,
            ["highlight"] = new JsonObject
            {
                // HTML-escaped by the engine; only <mark> is ours.
                ["encoder"] = "html",
                ["pre_tags"] = new JsonArray("<mark>"),
                ["post_tags"] = new JsonArray("</mark>"),
                ["fields"] = new JsonObject
                {
                    ["content"] = new JsonObject { ["fragment_size"] = 160, ["number_of_fragments"] = 2 },
                    ["meta_text"] = new JsonObject { ["number_of_fragments"] = 1 },
                    ["title"] = new JsonObject { ["number_of_fragments"] = 0 },
                },
            },
        };
    }

    private async Task<Result<IReadOnlyList<JsonObject>>> MetadataFiltersAsync(
        Guid? documentTypeId,
        IReadOnlyList<MetadataFilter> conditions,
        CancellationToken cancellationToken)
    {
        // A metadata condition only means something within one type: codes are per type.
        if (documentTypeId is not { } typeId
            || await documentTypes.FindAsync(new DocumentTypeId(typeId), cancellationToken) is not { LatestPublishedVersionId: { } latest } type
            || await documentTypes.GetSchemaAsync(latest, cancellationToken) is not { } schema)
        {
            return Result.Failure<IReadOnlyList<JsonObject>>(Error.Validation(
                "search.metadata_needs_type",
                "Metadata conditions need a document type."));
        }

        var filters = new List<JsonObject>();
        foreach (var condition in conditions)
        {
            if (schema.Find(condition.Field) is not { } field)
            {
                return Result.Failure<IReadOnlyList<JsonObject>>(Error.Validation("search.unknown_field", $"'{condition.Field}' is not a field of this type."));
            }

            var path = SearchDocumentBuilder.MetaPath(type.Code, field.Code, field.Type);
            JsonNode? value = condition.Value.ValueKind == JsonValueKind.Number
                ? JsonValue.Create(condition.Value.GetDouble())
                : condition.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? JsonValue.Create(condition.Value.GetBoolean())
                    : JsonValue.Create(Normalize(condition.Value.ToString(), field.Type));

            JsonObject? filter = condition.Op switch
            {
                "eq" => field.Type is FieldType.Text or FieldType.LongText
                    ? new JsonObject { ["match_phrase"] = new JsonObject { [path] = value } }
                    : new JsonObject { ["term"] = new JsonObject { [path] = value } },
                "gt" or "gte" or "lt" or "lte" => new JsonObject { ["range"] = new JsonObject { [path] = new JsonObject { [condition.Op] = value } } },
                "contains" => new JsonObject { ["match"] = new JsonObject { [path] = value } },
                _ => null,
            };

            if (filter is null)
            {
                return Result.Failure<IReadOnlyList<JsonObject>>(Error.Validation("search.unknown_op", $"Unsupported operator '{condition.Op}'."));
            }

            filters.Add(filter);
        }

        return Result.Success<IReadOnlyList<JsonObject>>(filters);
    }

    /// <summary>Numbers typed with Persian digits and separators still compare as numbers.</summary>
    private static object Normalize(string text, FieldType type)
    {
        if (type is not (FieldType.Integer or FieldType.Decimal))
        {
            return text;
        }

        var ascii = new string(text.Select(character => character switch
        {
            >= '۰' and <= '۹' => (char)('0' + (character - '۰')),
            >= '٠' and <= '٩' => (char)('0' + (character - '٠')),
            '٫' => '.',
            _ => character,
        }).Where(character => character is not (',' or '٬')).ToArray());

        return double.TryParse(ascii, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : text;
    }

    /// <summary>The Postgres re-check (defence in depth): the index may lag a revoked grant by a moment.</summary>
    private async Task<bool> StillVisibleAsync(JsonObject source, UserId viewer, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(source["document_id"]?.GetValue<string>(), out var documentId))
        {
            return false;
        }

        var resource = ResourceRef.Document(documentId);
        if (!(await authorizer.AuthorizeAsync(PermissionCodes.DocumentView, resource, cancellationToken)).Allowed)
        {
            return false;
        }

        if (source["is_published"]?.GetValue<bool>() == true || source["created_by"]?.GetValue<string>() == viewer.Value.ToString())
        {
            return true;
        }

        return (await authorizer.AuthorizeAsync(PermissionCodes.DocumentViewDraft, resource, cancellationToken)).Allowed;
    }

    private static SearchHitDto? ToHit(EngineHit hit)
    {
        var source = hit.Source;
        if (!Guid.TryParse(source["document_id"]?.GetValue<string>(), out var documentId)
            || !Guid.TryParse(source["version_id"]?.GetValue<string>(), out var versionId))
        {
            return null;
        }

        var highlights = new List<string>();
        foreach (var field in new[] { "content", "meta_text" })
        {
            if (hit.Highlight?[field] is JsonArray fragments)
            {
                highlights.AddRange(fragments.Select(fragment => fragment!.GetValue<string>()));
            }
        }

        return new SearchHitDto(
            documentId,
            versionId,
            source["label"]?.GetValue<string>() ?? string.Empty,
            source["title"]?.GetValue<string>() ?? string.Empty,
            source["file_name"]?.GetValue<string>(),
            source["mime_type"]?.GetValue<string>(),
            Guid.TryParse(source["category_id"]?.GetValue<string>(), out var category) ? category : null,
            Guid.TryParse(source["document_type_id"]?.GetValue<string>(), out var type) ? type : null,
            source["is_current"]?.GetValue<bool>() ?? false,
            source["is_effective"]?.GetValue<bool>() ?? false,
            source["approval_status"]?.GetValue<string>(),
            DateTimeOffset.TryParse(source["updated_at"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var updated) ? updated : null,
            highlights);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<FacetBucket>> Facets(JsonObject? aggregations)
    {
        var facets = new Dictionary<string, IReadOnlyList<FacetBucket>>(StringComparer.Ordinal);
        foreach (var name in FacetFields.Keys)
        {
            facets[name] = aggregations?[name]?["buckets"] is JsonArray buckets
                ? buckets.Select(bucket => new FacetBucket(bucket!["key"]!.ToString(), bucket["doc_count"]!.GetValue<long>())).ToList()
                : [];
        }

        return facets;
    }

    private async Task<SearchResultDto> DegradedAsync(SearchDocumentsQuery query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var (hits, total) = await titles.SearchAsync(query.Text, page, pageSize, cancellationToken);
        return new SearchResultDto(
            hits.Select(hit => new SearchHitDto(hit.DocumentId, Guid.Empty, hit.VersionLabel ?? string.Empty, hit.Title, hit.FileName, null, null, null, true, true, null, hit.UpdatedAt, [])).ToList(),
            total,
            page,
            pageSize,
            new Dictionary<string, IReadOnlyList<FacetBucket>>(),
            Degraded: true);
    }

    private static JsonObject Term(string field, object value) =>
        new() { ["term"] = new JsonObject { [field] = JsonValue.Create(value) } };

    private static JsonObject Terms(string field, IReadOnlySet<Guid> values) =>
        new() { ["terms"] = new JsonObject { [field] = new JsonArray(values.Select(value => (JsonNode?)JsonValue.Create(value.ToString())).ToArray()) } };

    private static JsonObject Should(params JsonObject[] clauses) => Bool(should: clauses);

    private static JsonObject Bool(JsonObject[]? must = null, JsonObject[]? should = null, JsonObject[]? mustNot = null)
    {
        var body = new JsonObject();
        if (must is { Length: > 0 }) body["must"] = new JsonArray(must.Select(clause => (JsonNode?)clause).ToArray());
        if (should is { Length: > 0 })
        {
            body["should"] = new JsonArray(should.Select(clause => (JsonNode?)clause).ToArray());
            body["minimum_should_match"] = 1;
        }

        if (mustNot is { Length: > 0 }) body["must_not"] = new JsonArray(mustNot.Select(clause => (JsonNode?)clause).ToArray());
        return new JsonObject { ["bool"] = body };
    }
}

public sealed class SearchStatusHandler(
    IDmsAuthorizer authorizer,
    ISearchEngine engine,
    ITextExtractor extractor,
    IContentExtractionRepository extractions) : IQueryHandler<SearchStatusQuery, Result<SearchStatusDto>>
{
    public async Task<Result<SearchStatusDto>> HandleAsync(SearchStatusQuery query, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageSearch, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure<SearchStatusDto>(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        (string? Index, long Count) status = (null, 0);
        if (engine.IsEnabled)
        {
            try
            {
                status = await engine.StatusAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                status = (null, -1);
            }
        }

        var counts = await extractions.CountByStatusAsync(cancellationToken);
        return Result.Success(new SearchStatusDto(
            engine.IsEnabled,
            extractor.IsEnabled,
            status.Index,
            status.Count,
            counts.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value)));
    }
}

public sealed class StartReindexHandler(IDmsAuthorizer authorizer, IJobQueue jobs, IAuditWriter audit)
    : ICommandHandler<StartReindexCommand, Result>
{
    public async Task<Result> HandleAsync(StartReindexCommand command, CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeSystemAsync(PermissionCodes.AdminManageSearch, cancellationToken);
        if (!decision.Allowed)
        {
            return Result.Failure(Error.Forbidden("auth.forbidden", decision.Explanation));
        }

        await jobs.EnqueueAsync(new JobRequest(ReindexJob.Type, IdempotencyKey: ReindexJob.Type), cancellationToken);
        await audit.WriteAsync(new AuditRecord { Action = AuditActions.SearchReindexStarted, EntityType = "SearchIndex" }, cancellationToken);
        return Result.Success();
    }
}
