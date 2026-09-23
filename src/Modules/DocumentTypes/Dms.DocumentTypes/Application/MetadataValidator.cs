using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dms.DocumentTypes.Contracts;
using Dms.SharedKernel.Rules;

namespace Dms.DocumentTypes.Application;

/// <summary>The outcome of validating one metadata object against one schema version.</summary>
public sealed class MetadataValidationOutcome
{
    public Dictionary<string, List<string>> Errors { get; } = new(StringComparer.Ordinal);

    public JsonObject Normalized { get; } = [];

    public HashSet<Guid> UserReferences { get; } = [];

    public HashSet<Guid> GroupReferences { get; } = [];

    public HashSet<Guid> DocumentReferences { get; } = [];

    public bool IsValid => Errors.Count == 0;

    public void Add(string field, string message)
    {
        if (!Errors.TryGetValue(field, out var list))
        {
            list = [];
            Errors[field] = list;
        }

        if (!list.Contains(message))
        {
            list.Add(message);
        }
    }
}

/// <summary>
/// The server-side, authoritative validation of dynamic metadata (section 4.5). Pure: no database
/// and no clock, so the whole matrix is unit tested. Checks that need other modules (does this
/// user exist, may the author see that document) are returned as references for the caller.
///
/// Order: parse and normalise each value by type; apply defaults; resolve SHOW rules to a fixed
/// point and drop hidden values; then required, declarative checks and VALIDATE rules on what is
/// left. Messages are Persian: they are shown next to the field as they are.
/// </summary>
public static class MetadataValidator
{
    /// <summary>Implicit caps, so a field without explicit limits still cannot hold a novel.</summary>
    public const int TextCap = 1000;
    public const int LongTextCap = 20000;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    public static MetadataValidationOutcome Validate(DocumentTypeSchema schema, JsonElement? input)
    {
        var outcome = new MetadataValidationOutcome();
        var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

        if (input is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } given)
        {
            if (given.ValueKind != JsonValueKind.Object)
            {
                outcome.Add("_", "اطلاعات سند باید یک شیء JSON باشد.");
                return outcome;
            }

            foreach (var property in given.EnumerateObject())
            {
                var field = schema.Find(property.Name);
                if (field is null)
                {
                    outcome.Add(property.Name, "این فیلد در نوع سند تعریف نشده است.");
                    continue;
                }

                // An inactive field keeps rendering old values but takes no new input.
                if (!field.IsActive)
                {
                    continue;
                }

                var parsed = Parse(field, property.Value, outcome);
                if (parsed is not null)
                {
                    values[field.Code] = parsed;
                }
            }
        }

        foreach (var field in schema.Fields.Where(field => field.IsActive && field.DefaultValue is not null))
        {
            // Only when the key is absent: an explicit null means "cleared on purpose".
            if (!values.ContainsKey(field.Code) && !Has(input, field.Code))
            {
                var fallback = Parse(field, field.DefaultValue!.Value, new MetadataValidationOutcome());
                if (fallback is not null)
                {
                    values[field.Code] = fallback;
                }
            }
        }

        var rules = ParseRules(schema);
        var hidden = ResolveHidden(schema, rules, values);
        foreach (var code in hidden)
        {
            values.Remove(code);
        }

        var snapshot = Snapshot(values);

        foreach (var field in schema.Fields.Where(field => field.IsActive && !hidden.Contains(field.Code)))
        {
            var required = field.IsRequired || rules.Any(rule =>
                rule.Schema.Kind == FieldRuleKind.Require
                && rule.Schema.Targets.Contains(field.Code)
                && (rule.Condition is null || RuleEvaluator.Evaluate(rule.Condition, snapshot)));

            if (!values.TryGetValue(field.Code, out var value))
            {
                if (required && !outcome.Errors.ContainsKey(field.Code))
                {
                    outcome.Add(field.Code, "این فیلد الزامی است.");
                }

                continue;
            }

            CheckLimits(field, value!, outcome);
        }

        foreach (var rule in rules.Where(rule => rule.Schema.Kind == FieldRuleKind.Validate && rule.Assertion is not null))
        {
            var targets = rule.Schema.Targets.Where(target => !hidden.Contains(target)).ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            var applies = rule.Condition is null || RuleEvaluator.Evaluate(rule.Condition, snapshot);
            if (applies && !RuleEvaluator.Evaluate(rule.Assertion!, snapshot))
            {
                var message = rule.Schema.Message?.Fa ?? "مقدار این فیلد با قواعد نوع سند سازگار نیست.";
                targets.ForEach(target => outcome.Add(target, message));
            }
        }

        foreach (var field in schema.Fields.OrderBy(field => field.DisplayOrder).ThenBy(field => field.Code, StringComparer.Ordinal))
        {
            if (values.TryGetValue(field.Code, out var value) && value is not null)
            {
                outcome.Normalized[field.Code] = value.DeepClone();
                Collect(field, value, outcome);
            }
        }

        return outcome;
    }

    private static bool Has(JsonElement? input, string code) =>
        input is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(code, out _);

    private sealed record ParsedRule(FieldRuleSchema Schema, RuleExpression? Condition, RuleExpression? Assertion);

    /// <summary>Rules were validated at publish time; a rule that somehow does not parse is ignored, never fatal.</summary>
    private static List<ParsedRule> ParseRules(DocumentTypeSchema schema) =>
        schema.Rules
            .Select(rule => new ParsedRule(
                rule,
                rule.Condition is { } condition && RuleParser.Parse(condition) is { IsSuccess: true } parsedCondition
                    ? parsedCondition.Value
                    : null,
                rule.Assertion is { } assertion && RuleParser.Parse(assertion) is { IsSuccess: true } parsedAssertion
                    ? parsedAssertion.Value
                    : null))
            .ToList();

    /// <summary>
    /// A field is hidden when any SHOW rule targeting it is false. Hiding a field can change
    /// another rule's outcome, so this repeats until nothing changes; each round can only hide
    /// more, so it ends within one round per field.
    /// </summary>
    private static HashSet<string> ResolveHidden(
        DocumentTypeSchema schema,
        List<ParsedRule> rules,
        Dictionary<string, JsonNode?> values)
    {
        var showRules = rules.Where(rule => rule.Schema.Kind == FieldRuleKind.Show && rule.Condition is not null).ToList();
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        if (showRules.Count == 0)
        {
            return hidden;
        }

        for (var round = 0; round <= schema.Fields.Count; round++)
        {
            var visible = values.Where(pair => !hidden.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
            var snapshot = Snapshot(visible);
            var next = new HashSet<string>(hidden, StringComparer.Ordinal);

            foreach (var rule in showRules.Where(rule => !RuleEvaluator.Evaluate(rule.Condition!, snapshot)))
            {
                next.UnionWith(rule.Schema.Targets);
            }

            if (next.SetEquals(hidden))
            {
                break;
            }

            hidden = next;
        }

        return hidden;
    }

    private static JsonElement Snapshot(Dictionary<string, JsonNode?> values)
    {
        var copy = new JsonObject();
        foreach (var (key, value) in values)
        {
            copy[key] = value?.DeepClone();
        }

        return JsonSerializer.SerializeToElement(copy);
    }

    private static JsonNode? Parse(FieldSchema field, JsonElement raw, MetadataValidationOutcome outcome)
    {
        if (raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        switch (field.Type)
        {
            case FieldType.Text or FieldType.LongText:
            {
                if (raw.ValueKind != JsonValueKind.String)
                {
                    return Fail(field, outcome, "باید متن باشد.");
                }

                var text = raw.GetString()!.Normalize(NormalizationForm.FormC).Trim();
                return text.Length == 0 ? null : JsonValue.Create(text);
            }

            case FieldType.Integer:
            {
                var number = ReadDecimal(raw);
                if (number is null || number != decimal.Truncate(number.Value) || number < long.MinValue || number > long.MaxValue)
                {
                    return Fail(field, outcome, "باید عدد صحیح باشد.");
                }

                return JsonValue.Create((long)number.Value);
            }

            case FieldType.Decimal:
            {
                var number = ReadDecimal(raw);
                return number is null ? Fail(field, outcome, "باید عدد باشد.") : JsonValue.Create(number.Value);
            }

            case FieldType.Boolean:
                return raw.ValueKind switch
                {
                    JsonValueKind.True => JsonValue.Create(true),
                    JsonValueKind.False => JsonValue.Create(false),
                    JsonValueKind.String when bool.TryParse(raw.GetString(), out var flag) => JsonValue.Create(flag),
                    _ => Fail(field, outcome, "باید بله یا خیر باشد."),
                };

            case FieldType.Date:
            {
                // Gregorian ISO on the wire and in storage; the browser shows Jalali (decision D4).
                var text = raw.ValueKind == JsonValueKind.String ? Digits(raw.GetString()!.Trim()) : null;
                return text is not null && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? JsonValue.Create(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    : Fail(field, outcome, "تاریخ باید به شکل yyyy-MM-dd (میلادی) باشد.");
            }

            case FieldType.DateTime:
            {
                // An offset is mandatory: a bare local time is ambiguous, and storage is UTC.
                var text = raw.ValueKind == JsonValueKind.String ? Digits(raw.GetString()!.Trim()) : null;
                var hasZone = text is not null && (text.EndsWith('Z') || Regex.IsMatch(text, @"[+-]\d{2}:?\d{2}$"));
                return hasZone && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)
                    ? JsonValue.Create(moment.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
                    : Fail(field, outcome, "زمان باید به شکل ISO-8601 همراه با منطقه‌ی زمانی باشد.");
            }

            case FieldType.Select:
            {
                if (raw.ValueKind != JsonValueKind.String)
                {
                    return Fail(field, outcome, "یکی از گزینه‌ها را انتخاب کنید.");
                }

                var value = raw.GetString()!;
                return field.Options.Any(option => option.IsActive && option.Value == value)
                    ? JsonValue.Create(value)
                    : Fail(field, outcome, "گزینه‌ی انتخاب‌شده معتبر نیست.");
            }

            case FieldType.MultiSelect:
            {
                if (raw.ValueKind != JsonValueKind.Array || raw.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                {
                    return Fail(field, outcome, "فهرستی از گزینه‌ها لازم است.");
                }

                var picked = raw.EnumerateArray().Select(item => item.GetString()!).Distinct(StringComparer.Ordinal).ToList();
                if (picked.Any(value => !field.Options.Any(option => option.IsActive && option.Value == value)))
                {
                    return Fail(field, outcome, "یکی از گزینه‌های انتخاب‌شده معتبر نیست.");
                }

                // Stored in option order, so equal selections always serialise identically.
                var ordered = field.Options.Where(option => picked.Contains(option.Value)).Select(option => (JsonNode?)JsonValue.Create(option.Value));
                return picked.Count == 0 ? null : new JsonArray(ordered.ToArray());
            }

            case FieldType.User or FieldType.Group or FieldType.DocumentReference:
                return raw.ValueKind == JsonValueKind.String && Guid.TryParse(raw.GetString(), out var id) && id != Guid.Empty
                    ? JsonValue.Create(id.ToString())
                    : Fail(field, outcome, "شناسه‌ی معتبر لازم است.");

            case FieldType.Url:
            {
                var text = raw.ValueKind == JsonValueKind.String ? raw.GetString()!.Trim() : null;
                return text is { Length: > 0 and <= 2048 }
                    && Uri.TryCreate(text, UriKind.Absolute, out var uri)
                    && uri.Scheme is "http" or "https"
                    ? JsonValue.Create(uri.ToString())
                    : Fail(field, outcome, "نشانی باید با http:// یا https:// شروع شود.");
            }

            case FieldType.Email:
            {
                var text = raw.ValueKind == JsonValueKind.String ? raw.GetString()!.Trim() : null;
                if (text is not { Length: > 0 and <= 254 } || !MailAddress.TryCreate(text, out var address) || address.Address != text)
                {
                    return Fail(field, outcome, "نشانی رایانامه معتبر نیست.");
                }

                return JsonValue.Create($"{address.User}@{address.Host.ToLowerInvariant()}");
            }

            case FieldType.Phone:
            {
                var text = raw.ValueKind == JsonValueKind.String ? Digits(raw.GetString()!) : null;
                var cleaned = text is null ? null : Regex.Replace(text, @"[\s\-().]", string.Empty);
                return cleaned is not null && Regex.IsMatch(cleaned, @"^\+?\d{7,15}$")
                    ? JsonValue.Create(cleaned)
                    : Fail(field, outcome, "شماره‌ی تلفن معتبر نیست.");
            }

            default:
                return Fail(field, outcome, "نوع فیلد پشتیبانی نمی‌شود.");
        }
    }

    private static void CheckLimits(FieldSchema field, JsonNode value, MetadataValidationOutcome outcome)
    {
        var rules = field.Validation;

        if (value is JsonValue textValue && textValue.TryGetValue<string>(out var text)
            && field.Type is FieldType.Text or FieldType.LongText or FieldType.Url or FieldType.Email or FieldType.Phone)
        {
            var cap = field.Type == FieldType.LongText ? LongTextCap : TextCap;
            var max = Math.Min(rules.MaxLength ?? cap, cap);
            if (text.Length > max)
            {
                outcome.Add(field.Code, $"حداکثر {max} نویسه مجاز است.");
            }

            if (rules.MinLength is { } min && text.Length < min)
            {
                outcome.Add(field.Code, $"دست‌کم {min} نویسه لازم است.");
            }

            if (rules.Pattern is { } pattern && !Matches(pattern, text))
            {
                outcome.Add(field.Code, rules.PatternMessage?.Fa ?? "قالب مقدار درست نیست.");
            }
        }

        if (field.Type is FieldType.Integer or FieldType.Decimal && value is JsonValue numberValue)
        {
            decimal number = field.Type == FieldType.Integer ? numberValue.GetValue<long>() : numberValue.GetValue<decimal>();
            if (rules.Min is { } min && number < min)
            {
                outcome.Add(field.Code, $"کمتر از {min.ToString(CultureInfo.InvariantCulture)} مجاز نیست.");
            }

            if (rules.Max is { } max && number > max)
            {
                outcome.Add(field.Code, $"بیشتر از {max.ToString(CultureInfo.InvariantCulture)} مجاز نیست.");
            }

            if (field.Type == FieldType.Decimal && rules.Scale is { } scale && decimal.Round(number, scale) != number)
            {
                outcome.Add(field.Code, $"حداکثر {scale} رقم اعشار مجاز است.");
            }
        }

        if (field.Type == FieldType.Date && value is JsonValue dateValue && dateValue.TryGetValue<string>(out var date))
        {
            // ISO dates order correctly as strings.
            if (rules.MinDate is { } minDate && string.CompareOrdinal(date, minDate) < 0)
            {
                outcome.Add(field.Code, $"تاریخ نباید پیش از {minDate} باشد.");
            }

            if (rules.MaxDate is { } maxDate && string.CompareOrdinal(date, maxDate) > 0)
            {
                outcome.Add(field.Code, $"تاریخ نباید پس از {maxDate} باشد.");
            }
        }

        if (field.Type == FieldType.MultiSelect && value is JsonArray items && rules.MaxItems is { } maxItems && items.Count > maxItems)
        {
            outcome.Add(field.Code, $"حداکثر {maxItems} گزینه را می‌توان انتخاب کرد.");
        }
    }

    private static void Collect(FieldSchema field, JsonNode value, MetadataValidationOutcome outcome)
    {
        if (value is not JsonValue reference || !reference.TryGetValue<string>(out var text) || !Guid.TryParse(text, out var id))
        {
            return;
        }

        switch (field.Type)
        {
            case FieldType.User:
                outcome.UserReferences.Add(id);
                break;
            case FieldType.Group:
                outcome.GroupReferences.Add(id);
                break;
            case FieldType.DocumentReference:
                outcome.DocumentReferences.Add(id);
                break;
        }
    }

    public static bool Matches(string pattern, string input)
    {
        try
        {
            var regex = Patterns.GetOrAdd(pattern, key => new Regex(key, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, RegexTimeout));
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>Accepts numbers, or strings with Persian/Arabic digits and thousands separators.</summary>
    private static decimal? ReadDecimal(JsonElement raw)
    {
        var text = raw.ValueKind switch
        {
            JsonValueKind.Number => raw.GetRawText(),
            JsonValueKind.String => Digits(raw.GetString()!).Replace(",", string.Empty).Replace("٬", string.Empty).Replace("٫", ".").Trim(),
            _ => null,
        };

        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string Digits(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(character switch
            {
                >= '۰' and <= '۹' => (char)('0' + (character - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (character - '٠')),
                _ => character,
            });
        }

        return builder.ToString();
    }

    private static JsonNode? Fail(FieldSchema field, MetadataValidationOutcome outcome, string message)
    {
        outcome.Add(field.Code, message);
        return null;
    }
}
