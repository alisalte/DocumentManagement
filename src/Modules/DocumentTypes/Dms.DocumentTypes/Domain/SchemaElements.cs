using System.Text.Json;
using Dms.DocumentTypes.Contracts;
using Dms.SharedKernel;

namespace Dms.DocumentTypes.Domain;

/// <summary>
/// One field of one schema version. Rows belong to their version: editing a draft replaces them,
/// and a trigger refuses any change once the version is published (section 4.5).
/// </summary>
public sealed class FieldDefinition : Entity<Guid>
{
    private readonly List<FieldOption> _options = [];

    private FieldDefinition()
    {
    }

    private FieldDefinition(Guid id)
        : base(id)
    {
    }

    public DocumentTypeVersionId TypeVersionId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public LocalizedText Label { get; private set; } = new(string.Empty);

    public FieldType FieldType { get; private set; }

    public bool IsRequired { get; private set; }

    public bool IsSearchable { get; private set; }

    public bool IsSortable { get; private set; }

    public bool ShowInList { get; private set; }

    public bool IsApprovalRelevant { get; private set; }

    /// <summary>JSON text of the default value, or null.</summary>
    public string? DefaultValue { get; private set; }

    public FieldValidation Validation { get; private set; } = new();

    public LocalizedText? HelpText { get; private set; }

    public int DisplayOrder { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<FieldOption> Options => _options;

    internal static FieldDefinition From(DocumentTypeVersionId versionId, FieldSchema schema)
    {
        var field = new FieldDefinition(Guid.CreateVersion7())
        {
            TypeVersionId = versionId,
            Code = schema.Code,
            Label = schema.Label,
            FieldType = schema.Type,
            IsRequired = schema.IsRequired,
            IsSearchable = schema.IsSearchable,
            IsSortable = schema.IsSortable,
            ShowInList = schema.ShowInList,
            IsApprovalRelevant = schema.IsApprovalRelevant,
            DefaultValue = schema.DefaultValue is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } value
                ? value.GetRawText()
                : null,
            Validation = schema.Validation,
            HelpText = schema.HelpText,
            DisplayOrder = schema.DisplayOrder,
            IsActive = schema.IsActive,
        };

        field._options.AddRange(schema.Options.Select(option => FieldOption.From(field.Id, option)));
        return field;
    }

    public FieldSchema ToSchema() => new()
    {
        Code = Code,
        Label = Label,
        Type = FieldType,
        IsRequired = IsRequired,
        IsSearchable = IsSearchable,
        IsSortable = IsSortable,
        ShowInList = ShowInList,
        IsApprovalRelevant = IsApprovalRelevant,
        DefaultValue = DefaultValue is null ? null : JsonDocument.Parse(DefaultValue).RootElement.Clone(),
        Validation = Validation,
        HelpText = HelpText,
        DisplayOrder = DisplayOrder,
        IsActive = IsActive,
        Options = _options
            .OrderBy(option => option.DisplayOrder)
            .ThenBy(option => option.Value, StringComparer.Ordinal)
            .Select(option => option.ToSchema())
            .ToList(),
    };
}

public sealed class FieldOption : Entity<Guid>
{
    private FieldOption()
    {
    }

    private FieldOption(Guid id)
        : base(id)
    {
    }

    public Guid FieldDefinitionId { get; private set; }

    public string Value { get; private set; } = string.Empty;

    public LocalizedText Label { get; private set; } = new(string.Empty);

    public int DisplayOrder { get; private set; }

    public bool IsActive { get; private set; }

    internal static FieldOption From(Guid fieldId, FieldOptionSchema schema) => new(Guid.CreateVersion7())
    {
        FieldDefinitionId = fieldId,
        Value = schema.Value,
        Label = schema.Label,
        DisplayOrder = schema.DisplayOrder,
        IsActive = schema.IsActive,
    };

    public FieldOptionSchema ToSchema() => new(Value, Label, DisplayOrder, IsActive);
}

/// <summary>A SHOW, REQUIRE or VALIDATE rule. Conditions are JSON expression trees in the shared rule language.</summary>
public sealed class FieldRule : Entity<Guid>
{
    private FieldRule()
    {
    }

    private FieldRule(Guid id)
        : base(id)
    {
    }

    public DocumentTypeVersionId TypeVersionId { get; private set; }

    public FieldRuleKind Kind { get; private set; }

    public string? Condition { get; private set; }

    public string[] TargetFieldCodes { get; private set; } = [];

    public string? Assertion { get; private set; }

    public LocalizedText? Message { get; private set; }

    public int DisplayOrder { get; private set; }

    internal static FieldRule From(DocumentTypeVersionId versionId, FieldRuleSchema schema) => new(Guid.CreateVersion7())
    {
        TypeVersionId = versionId,
        Kind = schema.Kind,
        Condition = Raw(schema.Condition),
        TargetFieldCodes = [.. schema.Targets],
        Assertion = Raw(schema.Assertion),
        Message = schema.Message,
        DisplayOrder = schema.DisplayOrder,
    };

    public FieldRuleSchema ToSchema() => new()
    {
        Kind = Kind,
        Condition = Parse(Condition),
        Targets = TargetFieldCodes,
        Assertion = Parse(Assertion),
        Message = Message,
        DisplayOrder = DisplayOrder,
    };

    private static string? Raw(JsonElement? value) =>
        value is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element ? element.GetRawText() : null;

    private static JsonElement? Parse(string? json) =>
        json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
}
