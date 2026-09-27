using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dms.SharedKernel;

namespace Dms.Documents.Application;

public sealed class LegacyManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("entries")]
    public List<LegacyManifestEntry> Entries { get; set; } = [];
}

public sealed class LegacyManifestEntry
{
    /// <summary>Stable legacy identity. Required for schemaVersion ≥ 2; derived from file path when absent.</summary>
    [JsonPropertyName("sourceId")]
    public string? SourceId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("categoryPath")]
    public string? CategoryPath { get; set; }

    [JsonPropertyName("documentTypeCode")]
    public string? DocumentTypeCode { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("ownerUsername")]
    public string? OwnerUsername { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("classification")]
    public string? Classification { get; set; }

    [JsonPropertyName("record")]
    public bool? Record { get; set; }

    [JsonPropertyName("recordClassCode")]
    public string? RecordClassCode { get; set; }

    [JsonPropertyName("recordSeriesCode")]
    public string? RecordSeriesCode { get; set; }

    [JsonPropertyName("retentionPolicyCode")]
    public string? RetentionPolicyCode { get; set; }

    [JsonPropertyName("legalHoldReason")]
    public string? LegalHoldReason { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    [JsonPropertyName("acl")]
    public List<LegacyAclEntry>? Acl { get; set; }

    [JsonPropertyName("versions")]
    public List<LegacyVersionEntry>? Versions { get; set; }

    public string ResolveRelativePath() =>
        !string.IsNullOrWhiteSpace(File) ? File!
        : !string.IsNullOrWhiteSpace(Path) ? Path!
        : string.Empty;

    public string ResolveSourceId(string sourceSystem)
    {
        if (!string.IsNullOrWhiteSpace(SourceId))
        {
            return SourceId.Trim();
        }

        var seed = $"{sourceSystem}\n{ResolveRelativePath()}\n{Title}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..32];
    }
}

public sealed class LegacyAclEntry
{
    [JsonPropertyName("subjectType")]
    public string? SubjectType { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("permission")]
    public string? Permission { get; set; }

    [JsonPropertyName("effect")]
    public string? Effect { get; set; }
}

public sealed class LegacyVersionEntry
{
    [JsonPropertyName("sourceVersionId")]
    public string? SourceVersionId { get; set; }

    [JsonPropertyName("versionNumber")]
    public int? VersionNumber { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("isCurrent")]
    public bool? IsCurrent { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("changeDescription")]
    public string? ChangeDescription { get; set; }
}

public static class ImportPathSafety
{
    public static Result<string> ResolveSafePath(string filesRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(filesRoot))
        {
            return Result.Failure<string>(Error.Validation(
                "import.files_root",
                "Import files root is not configured on the server."));
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Result.Failure<string>(Error.Validation("import.path_required", "File path is required."));
        }

        var trimmed = relativePath.Trim().Replace('\\', '/');
        if (trimmed.Contains('\0', StringComparison.Ordinal)
            || trimmed.StartsWith('/')
            || trimmed.Contains("://", StringComparison.Ordinal)
            || trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            return Result.Failure<string>(Error.Validation(
                "import.path_traversal",
                "Manifest file path is not allowed."));
        }

        // Percent-encoded traversal / odd encodings.
        var decoded = Uri.UnescapeDataString(trimmed);
        if (!string.Equals(decoded, trimmed, StringComparison.Ordinal)
            && decoded.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            return Result.Failure<string>(Error.Validation(
                "import.path_traversal",
                "Manifest file path is not allowed."));
        }

        var root = Path.GetFullPath(filesRoot);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        var combined = Path.GetFullPath(Path.Combine(root, trimmed.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(root, StringComparison.Ordinal))
        {
            return Result.Failure<string>(Error.Validation(
                "import.path_traversal",
                "Manifest file path escapes the files root."));
        }

        return Result.Success(combined);
    }
}

public static class ManifestSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Result<LegacyManifest> Parse(string json)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<LegacyManifest>(json, Options);
            if (manifest is null)
            {
                return Result.Failure<LegacyManifest>(Error.Validation("import.manifest", "Manifest is empty."));
            }

            if (manifest.SchemaVersion is not (1 or 2))
            {
                return Result.Failure<LegacyManifest>(Error.Validation(
                    "import.schema",
                    $"Unsupported schemaVersion: {manifest.SchemaVersion}."));
            }

            if (manifest.Entries.Count == 0)
            {
                return Result.Failure<LegacyManifest>(Error.Validation(
                    "import.entries",
                    "manifest.entries must be a non-empty list."));
            }

            return Result.Success(manifest);
        }
        catch (JsonException ex)
        {
            return Result.Failure<LegacyManifest>(Error.Validation("import.manifest_json", ex.Message));
        }
    }

    public static string Hash(string json) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
