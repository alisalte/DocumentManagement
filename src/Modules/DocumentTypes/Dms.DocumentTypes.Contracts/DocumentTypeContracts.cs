using System.Text.Json;
using Dms.SharedKernel;

namespace Dms.DocumentTypes.Contracts;

public readonly record struct DocumentTypeId(Guid Value)
{
    public static DocumentTypeId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct DocumentTypeVersionId(Guid Value)
{
    public static DocumentTypeVersionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// How a metadata-only edit behaves for this type (ADR 0001).
/// </summary>
public enum MetadataEditPolicy
{
    /// <summary>Governed types: any metadata change produces a new revision of the same file.</summary>
    NewRevision,

    /// <summary>
    /// Ungoverned types: the latest revision is updated in place, and audited with a before/after
    /// diff. A change to a field marked approval-relevant still produces a new revision.
    /// </summary>
    InPlace,
}

/// <summary>How versions of a type enter their workflow (section 6.1).</summary>
public enum WorkflowMode
{
    /// <summary>No approval: every version is published as soon as it is created.</summary>
    None,

    /// <summary>Versions start as drafts; the author starts the workflow when ready.</summary>
    Manual,

    /// <summary>Every new version or revision starts the workflow in the same transaction.</summary>
    AutoOnVersion,
}

public sealed record DocumentTypeSettings
{
    /// <summary>The workflow definition versions of this type go through; required unless the mode is None.</summary>
    public Guid? WorkflowId { get; init; }

    public WorkflowMode WorkflowMode { get; init; } = WorkflowMode.None;

    /// <summary>Empty means any extension is accepted.</summary>
    public IReadOnlyList<string> AllowedExtensions { get; init; } = [];

    /// <summary>Null falls back to the global storage limit.</summary>
    public long? MaxUploadBytes { get; init; }

    public MetadataEditPolicy MetadataEditPolicy { get; init; } = MetadataEditPolicy.NewRevision;

    public bool AllowExternalSharing { get; init; } = true;

    /// <summary>
    /// Phase 10 (D12): days a soft-deleted document must stay in the recycle bin before purge.
    /// Null or ≤ 0 means purge may happen immediately after soft-delete.
    /// </summary>
    public int? RetentionDaysAfterDelete { get; init; }

    /// <summary>
    /// Phase 10 (D12): type may use legal hold. Per-document hold storage and APIs follow.
    /// </summary>
    public bool SupportsLegalHold { get; init; }
}

public sealed record DocumentTypeSummary(
    DocumentTypeId Id,
    string Code,
    string Name,
    bool IsActive,
    DocumentTypeVersionId? LatestPublishedVersionId,
    DocumentTypeSettings Settings);

/// <summary>Section 4.5. FILE, IMAGE, FORMULA, AUTONUMBER and LOOKUP are future types.</summary>
public enum FieldType
{
    Text,
    LongText,
    Integer,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Select,
    MultiSelect,
    User,
    Group,
    DocumentReference,
    Url,
    Email,
    Phone,
}

/// <summary>Labels are bilingual from the start; Persian is required, English optional.</summary>
public sealed record LocalizedText(string Fa, string? En = null)
{
    public override string ToString() => Fa;
}

/// <summary>
/// Declarative checks on a single field. Every property is optional; which ones make sense
/// depends on the field type, and publishing rejects combinations that do not.
/// </summary>
public sealed record FieldValidation
{
    public int? MinLength { get; init; }

    public int? MaxLength { get; init; }

    public decimal? Min { get; init; }

    public decimal? Max { get; init; }

    /// <summary>Run with RegexOptions.NonBacktracking and a timeout, so it cannot be used for ReDoS.</summary>
    public string? Pattern { get; init; }

    public LocalizedText? PatternMessage { get; init; }

    /// <summary>ISO dates (yyyy-MM-dd), inclusive.</summary>
    public string? MinDate { get; init; }

    public string? MaxDate { get; init; }

    /// <summary>Decimal places allowed for DECIMAL fields.</summary>
    public int? Scale { get; init; }

    /// <summary>Most options that may be picked in a MULTI_SELECT.</summary>
    public int? MaxItems { get; init; }
}

public sealed record FieldOptionSchema(string Value, LocalizedText Label, int DisplayOrder = 0, bool IsActive = true);

public sealed record FieldSchema
{
    /// <summary>Stable identity: it names the key in dynamic_data, search mappings and rules.</summary>
    public required string Code { get; init; }

    public required LocalizedText Label { get; init; }

    public required FieldType Type { get; init; }

    public bool IsRequired { get; init; }

    public bool IsSearchable { get; init; }

    public bool IsSortable { get; init; }

    public bool ShowInList { get; init; }

    /// <summary>
    /// Under the in-place edit policy, changing this field still creates a new revision, so an
    /// approved amount cannot be edited underneath its approval (ADR 0001).
    /// </summary>
    public bool IsApprovalRelevant { get; init; }

    public JsonElement? DefaultValue { get; init; }

    public FieldValidation Validation { get; init; } = new();

    public IReadOnlyList<FieldOptionSchema> Options { get; init; } = [];

    public LocalizedText? HelpText { get; init; }

    public int DisplayOrder { get; init; }

    /// <summary>Inactive fields stay in the schema so old values still render, but take no new input.</summary>
    public bool IsActive { get; init; } = true;
}

public enum FieldRuleKind
{
    /// <summary>The targets are shown only while the condition holds; hidden fields store nothing.</summary>
    Show,

    /// <summary>The targets are required while the condition holds.</summary>
    Require,

    /// <summary>While the condition holds (or always, without one), the assertion must hold.</summary>
    Validate,
}

public sealed record FieldRuleSchema
{
    public required FieldRuleKind Kind { get; init; }

    public JsonElement? Condition { get; init; }

    public required IReadOnlyList<string> Targets { get; init; }

    public JsonElement? Assertion { get; init; }

    public LocalizedText? Message { get; init; }

    public int DisplayOrder { get; init; }
}

/// <summary>One schema version, published or draft. Published schemas never change.</summary>
public sealed record DocumentTypeSchema(
    DocumentTypeId DocumentTypeId,
    DocumentTypeVersionId VersionId,
    int VersionNumber,
    bool IsPublished,
    IReadOnlyList<FieldSchema> Fields,
    IReadOnlyList<FieldRuleSchema> Rules)
{
    public FieldSchema? Find(string code) => Fields.FirstOrDefault(field => field.Code == code);
}

/// <summary>Metadata that passed validation, normalised, plus what still has to be checked against other modules.</summary>
/// <param name="Json">Canonical JSON object: known fields only, normalised values, hidden fields removed.</param>
/// <param name="DocumentReferences">Documents referenced by DOCUMENT_REFERENCE fields. The caller must check the author may see them.</param>
public sealed record ValidatedMetadata(string Json, IReadOnlyList<Guid> DocumentReferences);

/// <summary>
/// What other modules need from the document type registry.
/// </summary>
public interface IDocumentTypeCatalog
{
    Task<DocumentTypeSummary?> FindAsync(DocumentTypeId id, CancellationToken cancellationToken);

    /// <summary>The schema version a new document or version binds to.</summary>
    Task<Result<DocumentTypeVersionId>> ResolveVersionForNewDocumentAsync(
        DocumentTypeId id,
        CancellationToken cancellationToken);

    /// <summary>A published schema. Immutable, so safe to cache for the life of the process.</summary>
    Task<DocumentTypeSchema?> GetSchemaAsync(DocumentTypeVersionId versionId, CancellationToken cancellationToken);

    /// <summary>
    /// Validates dynamic metadata against a published schema: types, required fields, options,
    /// declarative checks and SHOW/REQUIRE/VALIDATE rules, plus that referenced users and groups
    /// exist. Failures carry per-field messages.
    /// </summary>
    Task<Result<ValidatedMetadata>> ValidateMetadataAsync(
        DocumentTypeVersionId versionId,
        JsonElement? metadata,
        CancellationToken cancellationToken);
}
