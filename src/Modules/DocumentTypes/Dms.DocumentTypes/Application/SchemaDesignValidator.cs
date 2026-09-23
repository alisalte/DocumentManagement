using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dms.DocumentTypes.Contracts;
using Dms.SharedKernel;
using Dms.SharedKernel.Rules;

namespace Dms.DocumentTypes.Application;

/// <summary>
/// Checks a schema design before it is saved as a draft and again before it is published, so
/// nothing that reaches documents can be malformed: codes, options, validation settings that
/// fit the field type, patterns that compile, defaults that pass their own field, and rules that
/// parse and refer only to fields that exist. Messages are keyed by path, for example
/// "fields[2].code" or "rules[0].condition", so the admin UI can put them next to the input.
/// </summary>
public static class SchemaDesignValidator
{
    public const int MaxFields = 200;
    public const int MaxRules = 200;
    public const int MaxOptions = 500;

    private static readonly HashSet<FieldType> TextTypes =
        [FieldType.Text, FieldType.LongText, FieldType.Url, FieldType.Email, FieldType.Phone];

    private static readonly HashSet<FieldType> NumberTypes = [FieldType.Integer, FieldType.Decimal];

    private static readonly HashSet<FieldType> OptionTypes = [FieldType.Select, FieldType.MultiSelect];

    /// <param name="publishedTypes">Field codes already published in earlier versions, with their type.</param>
    public static Result Validate(
        IReadOnlyList<FieldSchema> fields,
        IReadOnlyList<FieldRuleSchema> rules,
        IReadOnlyDictionary<string, FieldType> publishedTypes)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string path, string message)
        {
            if (!errors.TryGetValue(path, out var list))
            {
                errors[path] = list = [];
            }

            list.Add(message);
        }

        if (fields.Count > MaxFields)
        {
            Add("fields", $"A document type can have at most {MaxFields} fields.");
        }

        if (rules.Count > MaxRules)
        {
            Add("rules", $"A document type can have at most {MaxRules} rules.");
        }

        var codes = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            var path = $"fields[{index}]";

            if (field.Code is null || !RuleParser.IsValidFieldCode(field.Code))
            {
                Add($"{path}.code", "Use lower-case letters, digits and underscores, starting with a letter (at most 63).");
            }
            else if (!codes.Add(field.Code))
            {
                Add($"{path}.code", $"The code '{field.Code}' is used twice.");
            }
            else if (publishedTypes.TryGetValue(field.Code, out var publishedType) && publishedType != field.Type)
            {
                // Old documents keep their values under this key; a new meaning would misread them.
                Add($"{path}.type", $"'{field.Code}' was published as {publishedType}; a field code keeps its type forever. Use a new code.");
            }

            if (string.IsNullOrWhiteSpace(field.Label?.Fa) || field.Label.Fa.Length > 200)
            {
                Add($"{path}.label", "A Persian label of at most 200 characters is required.");
            }

            ValidateOptions(field, path, Add);
            ValidateSettings(field, path, Add);
            ValidateDefault(field, path, Add);
        }

        var known = fields.Where(field => field.Code is not null).Select(field => field.Code).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < rules.Count; index++)
        {
            ValidateRule(rules[index], $"rules[{index}]", known, Add);
        }

        return errors.Count == 0
            ? Result.Success()
            : Result.Failure(Error.ValidationFields(
                "document_type.schema_invalid",
                "The schema has errors.",
                errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray())));
    }

    private static void ValidateOptions(FieldSchema field, string path, Action<string, string> add)
    {
        var options = field.Options ?? [];
        if (!OptionTypes.Contains(field.Type))
        {
            if (options.Count > 0)
            {
                add($"{path}.options", "Only SELECT and MULTI_SELECT fields have options.");
            }

            return;
        }

        if (options.Count > MaxOptions)
        {
            add($"{path}.options", $"At most {MaxOptions} options.");
        }

        if (!options.Any(option => option.IsActive))
        {
            add($"{path}.options", "A choice field needs at least one active option.");
        }

        var values = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            if (string.IsNullOrWhiteSpace(option.Value) || option.Value.Length > 100 || option.Value != option.Value.Trim())
            {
                add($"{path}.options[{index}].value", "An option value is 1 to 100 characters, without surrounding spaces.");
            }
            else if (!values.Add(option.Value))
            {
                add($"{path}.options[{index}].value", $"The value '{option.Value}' is used twice.");
            }

            if (string.IsNullOrWhiteSpace(option.Label?.Fa))
            {
                add($"{path}.options[{index}].label", "A Persian label is required.");
            }
        }
    }

    private static void ValidateSettings(FieldSchema field, string path, Action<string, string> add)
    {
        var rules = field.Validation ?? new FieldValidation();
        var at = $"{path}.validation";

        if ((rules.MinLength is not null || rules.MaxLength is not null || rules.Pattern is not null) && !TextTypes.Contains(field.Type))
        {
            add(at, "Length and pattern checks apply to text fields only.");
        }

        if (rules.MinLength is < 0 || rules.MaxLength is < 1 || rules.MinLength > rules.MaxLength)
        {
            add(at, "The length limits are not consistent.");
        }

        if ((rules.Min is not null || rules.Max is not null) && !NumberTypes.Contains(field.Type))
        {
            add(at, "Minimum and maximum apply to number fields only.");
        }

        if (rules.Min > rules.Max)
        {
            add(at, "The minimum is greater than the maximum.");
        }

        if (rules.Scale is not null && (field.Type != FieldType.Decimal || rules.Scale is < 0 or > 10))
        {
            add(at, "Scale (0 to 10 decimal places) applies to DECIMAL fields only.");
        }

        if (rules.MaxItems is not null && (field.Type != FieldType.MultiSelect || rules.MaxItems < 1))
        {
            add(at, "Max items applies to MULTI_SELECT fields and is at least 1.");
        }

        if (rules.MinDate is not null || rules.MaxDate is not null)
        {
            if (field.Type != FieldType.Date)
            {
                add(at, "Date limits apply to DATE fields only.");
            }

            foreach (var limit in new[] { rules.MinDate, rules.MaxDate }.OfType<string>())
            {
                if (!DateOnly.TryParseExact(limit, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    add(at, $"'{limit}' is not a yyyy-MM-dd date.");
                }
            }

            if (rules.MinDate is { } from && rules.MaxDate is { } to && string.CompareOrdinal(from, to) > 0)
            {
                add(at, "The earliest date is after the latest date.");
            }
        }

        if (rules.Pattern is { } pattern)
        {
            // NonBacktracking refuses the constructs that make ReDoS possible, and refusing them
            // here means a pattern that reaches documents can always be evaluated safely.
            try
            {
                _ = new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                add($"{at}.pattern", $"The pattern is not supported: {exception.Message}");
            }
        }
    }

    private static void ValidateDefault(FieldSchema field, string path, Action<string, string> add)
    {
        if (field.DefaultValue is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } value || field.Code is null)
        {
            return;
        }

        // The default has to pass the field's own checks, or every new document would fail.
        var alone = new DocumentTypeSchema(
            default,
            default,
            0,
            IsPublished: false,
            [field with { IsRequired = false }],
            []);

        var input = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [field.Code] = value });
        var outcome = MetadataValidator.Validate(alone, input);
        foreach (var message in outcome.Errors.Values.SelectMany(list => list))
        {
            add($"{path}.defaultValue", message);
        }
    }

    private static void ValidateRule(FieldRuleSchema rule, string path, HashSet<string> known, Action<string, string> add)
    {
        var targets = rule.Targets ?? [];
        if (targets.Count == 0)
        {
            add($"{path}.targets", "A rule needs at least one target field.");
        }

        foreach (var target in targets.Where(target => !known.Contains(target)))
        {
            add($"{path}.targets", $"'{target}' is not a field of this type.");
        }

        if (rule.Kind is FieldRuleKind.Show or FieldRuleKind.Require && rule.Condition is null)
        {
            add($"{path}.condition", "SHOW and REQUIRE rules need a condition.");
        }

        if (rule.Kind == FieldRuleKind.Validate && rule.Assertion is null)
        {
            add($"{path}.assertion", "A VALIDATE rule needs an assertion.");
        }

        if (rule.Kind != FieldRuleKind.Validate && rule.Assertion is not null)
        {
            add($"{path}.assertion", "Only VALIDATE rules have an assertion.");
        }

        CheckExpression(rule.Condition, $"{path}.condition", known, add);
        CheckExpression(rule.Assertion, $"{path}.assertion", known, add);

        if (rule.Kind == FieldRuleKind.Show && rule.Condition is { } condition
            && RuleParser.Parse(condition) is { IsSuccess: true } parsed
            && RuleParser.ReferencedFields(parsed.Value).Overlaps(targets))
        {
            // A field whose visibility depends on its own value can never be filled in.
            add($"{path}.condition", "A SHOW rule cannot depend on the fields it shows.");
        }
    }

    private static void CheckExpression(JsonElement? expression, string path, HashSet<string> known, Action<string, string> add)
    {
        if (expression is not { } value)
        {
            return;
        }

        var parsed = RuleParser.Parse(value);
        if (parsed.IsFailure)
        {
            add(path, parsed.Error.Message);
            return;
        }

        foreach (var field in RuleParser.ReferencedFields(parsed.Value).Where(field => !known.Contains(field)))
        {
            add(path, $"'{field}' is not a field of this type.");
        }
    }
}
