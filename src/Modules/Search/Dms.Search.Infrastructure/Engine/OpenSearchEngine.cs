using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dms.Search.Application;
using Microsoft.Extensions.Options;

namespace Dms.Search.Infrastructure.Engine;

/// <summary>
/// OpenSearch over its REST API with plain JSON: the query builder already speaks the engine's
/// DSL, and a client library would add a large dependency for a handful of calls.
/// </summary>
public sealed class OpenSearchEngine : ISearchEngine
{
    private readonly HttpClient _http;
    private readonly SearchOptions _options;

    public OpenSearchEngine(HttpClient http, IOptions<SearchOptions> options)
    {
        _http = http;
        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.OpenSearchUrl))
        {
            _http.BaseAddress = new Uri(_options.OpenSearchUrl.TrimEnd('/') + "/");
            if (!string.IsNullOrEmpty(_options.Username))
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
        }
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.OpenSearchUrl);

    private string Alias => _options.IndexAlias;

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        using var head = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"_alias/{Alias}"), cancellationToken);
        if (head.StatusCode == HttpStatusCode.OK)
        {
            return;
        }

        var index = await CreateGenerationAsync(cancellationToken);
        var body = new JsonObject
        {
            ["actions"] = new JsonArray(new JsonObject { ["add"] = new JsonObject { ["index"] = index, ["alias"] = Alias } }),
        };

        using var response = await _http.PostAsJsonAsync("_aliases", body, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Another worker won the race and created its own generation: keep theirs, drop ours.
            await _http.DeleteAsync(index, cancellationToken);
            using var again = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"_alias/{Alias}"), cancellationToken);
            if (again.StatusCode != HttpStatusCode.OK)
            {
                await EnsureSuccessAsync(response, cancellationToken);
            }
        }
    }

    public async Task ReplaceDocumentAsync(Guid documentId, IReadOnlyList<JsonObject> versions, string? index, CancellationToken cancellationToken)
    {
        var target = index ?? Alias;

        // New state first, then whatever is left over (a purged revision): the document never
        // disappears from results in between.
        if (versions.Count > 0)
        {
            var bulk = new StringBuilder();
            foreach (var version in versions)
            {
                var action = new JsonObject { ["index"] = new JsonObject { ["_id"] = version["version_id"]!.GetValue<string>() } };
                bulk.Append(action.ToJsonString()).Append('\n');
                bulk.Append(version.ToJsonString()).Append('\n');
            }

            using var content = new StringContent(bulk.ToString(), Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
            using var bulkResponse = await _http.PostAsync($"{target}/_bulk?refresh={Refresh(index)}", content, cancellationToken);
            await EnsureSuccessAsync(bulkResponse, cancellationToken);

            var result = await bulkResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            if (result?["errors"]?.GetValue<bool>() == true)
            {
                var first = result["items"]?.AsArray()
                    .Select(item => item?["index"]?["error"])
                    .FirstOrDefault(error => error is not null);
                throw new InvalidOperationException($"Indexing document {documentId} failed: {first?.ToJsonString()}");
            }
        }

        var keep = new JsonArray(versions.Select(version => (JsonNode?)JsonValue.Create(version["version_id"]!.GetValue<string>())).ToArray());
        var stale = new JsonObject
        {
            ["query"] = new JsonObject
            {
                ["bool"] = new JsonObject
                {
                    ["filter"] = new JsonArray(new JsonObject { ["term"] = new JsonObject { ["document_id"] = documentId.ToString() } }),
                    ["must_not"] = new JsonArray(new JsonObject { ["terms"] = new JsonObject { ["version_id"] = keep } }),
                },
            },
        };

        // _delete_by_query only knows true/false for refresh, not wait_for.
        var refresh = index is null && _options.Refresh != "false" ? "true" : "false";
        using var response = await _http.PostAsJsonAsync($"{target}/_delete_by_query?conflicts=proceed&refresh={refresh}", stale, cancellationToken);

        // An index that does not exist yet has nothing to delete.
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
    }

    public async Task<EngineResult> SearchAsync(JsonObject query, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{Alias}/_search", query, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Nothing has been indexed yet.
            return new EngineResult(0, [], null);
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken)
            ?? throw new InvalidOperationException("The search engine returned no body.");

        var hits = body["hits"]?["hits"]?.AsArray()
            .Select(hit => new EngineHit(
                hit!["_id"]!.GetValue<string>(),
                hit["_source"]!.AsObject(),
                hit["highlight"] as JsonObject))
            .ToList() ?? [];

        var total = body["hits"]?["total"]?["value"]?.GetValue<long>() ?? hits.Count;
        return new EngineResult(total, hits, body["aggregations"] as JsonObject);
    }

    public async Task<string> CreateGenerationAsync(CancellationToken cancellationToken)
    {
        var index = $"{Alias}-v{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        using var response = await _http.PutAsJsonAsync(index, IndexDefinition.Build(), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return index;
    }

    public async Task SwapAliasAsync(string index, CancellationToken cancellationToken)
    {
        var current = await AliasedIndicesAsync(cancellationToken);

        var actions = new JsonArray(new JsonObject { ["add"] = new JsonObject { ["index"] = index, ["alias"] = Alias } });
        foreach (var old in current.Where(name => name != index))
        {
            actions.Add(new JsonObject { ["remove_index"] = new JsonObject { ["index"] = old } });
        }

        // One atomic call: searches see either the old index or the new one, never neither.
        using var response = await _http.PostAsJsonAsync("_aliases", new JsonObject { ["actions"] = actions }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<(string? Index, long Count)> StatusAsync(CancellationToken cancellationToken)
    {
        var indices = await AliasedIndicesAsync(cancellationToken);
        if (indices.Count == 0)
        {
            return (null, 0);
        }

        using var response = await _http.GetAsync($"{Alias}/_count", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return (indices[0], body?["count"]?.GetValue<long>() ?? 0);
    }

    /// <summary>Deletes every index of this alias. Tests only.</summary>
    public async Task DropAllAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync($"{Alias}-v*", cancellationToken);
    }

    private async Task<IReadOnlyList<string>> AliasedIndicesAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"_alias/{Alias}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return body?.Select(pair => pair.Key).ToList() ?? [];
    }

    /// <summary>A rebuild writes into an index nobody reads yet, so it never waits for refreshes.</summary>
    private string Refresh(string? index) => index is null ? _options.Refresh : "false";

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"OpenSearch answered {(int)response.StatusCode}: {(detail.Length > 500 ? detail[..500] : detail)}",
            null,
            response.StatusCode);
    }
}

/// <summary>
/// Mappings and analysers (section 8.2). Persian text is normalised before it is split: Arabic
/// yeh and kaf become Persian, and Persian and Arabic digits become ASCII so "۱۴۰۳" finds "1403".
/// People write "می‌شود" with a zero-width non-joiner, a space or nothing at all, so text is
/// analysed twice: with the non-joiner as a space ("می شود") and with it removed ("میشود"), and
/// queries run against both.
/// </summary>
internal static class IndexDefinition
{
    public static JsonObject Build() => JsonNode.Parse(Json)!.AsObject();

    private const string Json = """
    {
      "settings": {
        "number_of_shards": 1,
        "analysis": {
          "char_filter": {
            "zwnj_to_space": {
              "type": "mapping",
              "mappings": ["\\u200C=>\\u0020", "\\u200D=>", "\\u0640=>"]
            },
            "zwnj_removed": {
              "type": "mapping",
              "mappings": ["\\u200C=>", "\\u200D=>", "\\u0640=>"]
            }
          },
          "filter": {
            "fa_stop": { "type": "stop", "stopwords": "_persian_" }
          },
          "normalizer": {
            "kw_lower": { "type": "custom", "filter": ["lowercase", "decimal_digit", "arabic_normalization", "persian_normalization"] }
          },
          "analyzer": {
            "fa": {
              "type": "custom",
              "char_filter": ["zwnj_to_space"],
              "tokenizer": "standard",
              "filter": ["lowercase", "decimal_digit", "arabic_normalization", "persian_normalization", "fa_stop"]
            },
            "fa_joined": {
              "type": "custom",
              "char_filter": ["zwnj_removed"],
              "tokenizer": "standard",
              "filter": ["lowercase", "decimal_digit", "arabic_normalization", "persian_normalization", "fa_stop"]
            },
            "fa_en": {
              "type": "custom",
              "tokenizer": "standard",
              "filter": ["lowercase", "decimal_digit", "porter_stem"]
            }
          }
        }
      },
      "mappings": {
        "dynamic": "strict",
        "properties": {
          "document_id": { "type": "keyword" },
          "version_id": { "type": "keyword" },
          "version_number": { "type": "integer" },
          "revision_number": { "type": "integer" },
          "label": { "type": "keyword" },
          "is_current": { "type": "boolean" },
          "is_effective": { "type": "boolean" },
          "is_published": { "type": "boolean" },
          "approval_status": { "type": "keyword" },
          "is_deleted": { "type": "boolean" },
          "category_id": { "type": "keyword" },
          "category_path": { "type": "keyword" },
          "document_type_id": { "type": "keyword" },
          "document_type_code": { "type": "keyword" },
          "owner_id": { "type": "keyword" },
          "created_by": { "type": "keyword" },
          "created_at": { "type": "date" },
          "updated_at": { "type": "date" },
          "title": { "type": "text", "analyzer": "fa", "fields": { "joined": { "type": "text", "analyzer": "fa_joined" }, "en": { "type": "text", "analyzer": "fa_en" }, "raw": { "type": "keyword", "ignore_above": 512 } } },
          "description": { "type": "text", "analyzer": "fa" },
          "tags": { "type": "keyword", "fields": { "text": { "type": "text", "analyzer": "fa" } } },
          "file_name": { "type": "text", "analyzer": "fa", "fields": { "raw": { "type": "keyword", "ignore_above": 255 } } },
          "mime_type": { "type": "keyword" },
          "meta_text": { "type": "text", "analyzer": "fa", "fields": { "joined": { "type": "text", "analyzer": "fa_joined" } } },
          "content": { "type": "text", "analyzer": "fa", "fields": { "joined": { "type": "text", "analyzer": "fa_joined" }, "en": { "type": "text", "analyzer": "fa_en" } } },
          "meta": {
            "type": "object",
            "dynamic": true
          }
        },
        "dynamic_templates": [
          { "meta_num": { "path_match": "meta.*", "match": "*__num", "mapping": { "type": "double" } } },
          { "meta_date": { "path_match": "meta.*", "match": "*__date", "mapping": { "type": "date" } } },
          { "meta_bool": { "path_match": "meta.*", "match": "*__bool", "mapping": { "type": "boolean" } } },
          { "meta_txt": { "path_match": "meta.*", "match": "*__txt", "mapping": { "type": "text", "analyzer": "fa", "fields": { "raw": { "type": "keyword", "ignore_above": 256 } } } } },
          { "meta_kw": { "path_match": "meta.*", "match": "*__kw", "mapping": { "type": "keyword", "normalizer": "kw_lower", "ignore_above": 256 } } },
          { "meta_type": { "path_match": "meta.*", "match_mapping_type": "object", "mapping": { "type": "object" } } }
        ]
      }
    }
    """;
}

/// <summary>No OpenSearch configured: search falls back to titles in Postgres (section 8.4).</summary>
public sealed class DisabledSearchEngine : ISearchEngine
{
    public bool IsEnabled => false;

    public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReplaceDocumentAsync(Guid documentId, IReadOnlyList<JsonObject> versions, string? index, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<EngineResult> SearchAsync(JsonObject query, CancellationToken cancellationToken) =>
        Task.FromResult(new EngineResult(0, [], null));

    public Task<string> CreateGenerationAsync(CancellationToken cancellationToken) => Task.FromResult(string.Empty);

    public Task SwapAliasAsync(string index, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<(string? Index, long Count)> StatusAsync(CancellationToken cancellationToken) => Task.FromResult<(string?, long)>((null, 0));
}
