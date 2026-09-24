using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dms.Authorization.Contracts;
using Dms.DocumentTypes.Contracts;
using Dms.Documents.Contracts;
using Dms.SharedKernel;

namespace Dms.Documents.Application;

/// <summary>
/// Validates metadata for a version: the document type's rules, then the one check only this
/// module can make. A DOCUMENT_REFERENCE may only point at a document the author can see;
/// otherwise the field would confirm that a hidden document exists.
/// </summary>
public sealed class MetadataGate(IDocumentTypeCatalog catalog, DocumentAccess access)
{
    public async Task<Result<string>> ValidateAsync(
        DocumentTypeVersionId schemaVersion,
        JsonElement? metadata,
        CancellationToken cancellationToken)
    {
        var validated = await catalog.ValidateMetadataAsync(schemaVersion, metadata, cancellationToken);
        if (validated.IsFailure)
        {
            return Result.Failure<string>(validated.Error);
        }

        if (validated.Value.DocumentReferences.Count == 0)
        {
            return Result.Success(validated.Value.Json);
        }

        var schema = (await catalog.GetSchemaAsync(schemaVersion, cancellationToken))!;
        var data = JsonNode.Parse(validated.Value.Json)!.AsObject();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var field in schema.Fields.Where(field => field.Type == FieldType.DocumentReference))
        {
            if (data[field.Code]?.GetValue<string>() is { } text
                && Guid.TryParse(text, out var id)
                && !await access.IsAllowedAsync(id, PermissionCodes.DocumentView, cancellationToken))
            {
                errors[field.Code] = ["سند مرتبط پیدا نشد."];
            }
        }

        return errors.Count == 0
            ? Result.Success(validated.Value.Json)
            : Result.Failure<string>(Error.ValidationFields("metadata.invalid", "Some fields are not valid.", errors));
    }
}

/// <summary>
/// Shapes stored metadata for the API, using the schema version the row was written against
/// (section 5.10: one mapper per document type version). DECIMAL values leave as strings, so a
/// JavaScript client never rounds 12345678901234.56 through a double (section 4.4).
/// </summary>
public static class MetadataPresenter
{
    public static JsonElement ForApi(DocumentTypeSchema? schema, string? json)
    {
        var data = string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json)!.AsObject();

        if (schema is not null)
        {
            foreach (var field in schema.Fields.Where(field => field.Type == FieldType.Decimal))
            {
                if (data[field.Code] is JsonValue value && value.GetValueKind() == JsonValueKind.Number)
                {
                    data[field.Code] = value.GetValue<decimal>().ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        return JsonSerializer.SerializeToElement(data);
    }

    /// <summary>Codes whose value differs between two metadata objects, for audit diffs and edit policies.</summary>
    public static IReadOnlyList<string> ChangedFields(string? before, string? after)
    {
        var left = string.IsNullOrWhiteSpace(before) ? new JsonObject() : JsonNode.Parse(before)!.AsObject();
        var right = string.IsNullOrWhiteSpace(after) ? new JsonObject() : JsonNode.Parse(after)!.AsObject();

        return left.Select(pair => pair.Key)
            .Union(right.Select(pair => pair.Key), StringComparer.Ordinal)
            .Where(key => !JsonNode.DeepEquals(left[key], right[key]))
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>Tells every listener (search) that a document changed, in the caller's transaction.</summary>
public sealed class DocumentChanges(IEnumerable<IDocumentChangeListener> listeners)
{
    public async Task NotifyAsync(Guid documentId, CancellationToken cancellationToken)
    {
        foreach (var listener in listeners)
        {
            await listener.OnDocumentChangedAsync(documentId, cancellationToken);
        }
    }
}
