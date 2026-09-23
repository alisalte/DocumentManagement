using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dms.SharedKernel.Rules;

/// <summary>
/// The shared rule language (docs/architecture.md sections 2.3 and 6.4): a JSON expression tree,
/// never code. Document types use it for conditional fields and cross-field validation; workflow
/// uses it for step conditions. The browser runs a line-for-line port (frontend/src/lib/rules.ts),
/// and both are held to the same test vectors in tests/rules/rule-cases.json.
///
/// <code>
/// { "all": [ { "field": "amount", "op": "gt", "value": 10000000000 },
///            { "not": { "field": "status", "op": "in", "value": ["draft", "void"] } } ] }
/// </code>
/// </summary>
public abstract record RuleExpression;

public sealed record AllRule(IReadOnlyList<RuleExpression> Rules) : RuleExpression;

public sealed record AnyRule(IReadOnlyList<RuleExpression> Rules) : RuleExpression;

public sealed record NotRule(RuleExpression Rule) : RuleExpression;

public sealed record ComparisonRule(string Field, RuleOperator Operator, JsonElement? Value) : RuleExpression;

public enum RuleOperator
{
    Eq,
    Ne,
    Gt,
    Gte,
    Lt,
    Lte,
    In,
    NotIn,
    Contains,
    IsEmpty,
}

public static partial class RuleParser
{
    /// <summary>Deep enough for any real condition, shallow enough that nothing can blow the stack.</summary>
    public const int MaxDepth = 8;

    public const int MaxNodes = 100;

    private static readonly Dictionary<string, RuleOperator> Operators = new(StringComparer.Ordinal)
    {
        ["eq"] = RuleOperator.Eq,
        ["ne"] = RuleOperator.Ne,
        ["gt"] = RuleOperator.Gt,
        ["gte"] = RuleOperator.Gte,
        ["lt"] = RuleOperator.Lt,
        ["lte"] = RuleOperator.Lte,
        ["in"] = RuleOperator.In,
        ["not_in"] = RuleOperator.NotIn,
        ["contains"] = RuleOperator.Contains,
        ["is_empty"] = RuleOperator.IsEmpty,
    };

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex FieldCodePattern();

    public static bool IsValidFieldCode(string code) => FieldCodePattern().IsMatch(code);

    public static Result<RuleExpression> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Parse(document.RootElement);
        }
        catch (JsonException)
        {
            return Invalid("The rule is not valid JSON.");
        }
    }

    public static Result<RuleExpression> Parse(JsonElement element)
    {
        var nodes = 0;
        return ParseNode(element, depth: 1, ref nodes);
    }

    /// <summary>Every field the rule reads, so publishing can check them against the schema.</summary>
    public static IReadOnlySet<string> ReferencedFields(RuleExpression rule)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        Collect(rule, fields);
        return fields;

        static void Collect(RuleExpression node, HashSet<string> into)
        {
            switch (node)
            {
                case AllRule all:
                    all.Rules.ToList().ForEach(child => Collect(child, into));
                    break;
                case AnyRule any:
                    any.Rules.ToList().ForEach(child => Collect(child, into));
                    break;
                case NotRule not:
                    Collect(not.Rule, into);
                    break;
                case ComparisonRule comparison:
                    into.Add(comparison.Field);
                    break;
            }
        }
    }

    private static Result<RuleExpression> ParseNode(JsonElement element, int depth, ref int nodes)
    {
        if (depth > MaxDepth)
        {
            return Invalid($"The rule is nested more than {MaxDepth} levels deep.");
        }

        if (++nodes > MaxNodes)
        {
            return Invalid($"The rule has more than {MaxNodes} parts.");
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return Invalid("Every part of a rule is a JSON object.");
        }

        var properties = element.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        if (properties.SetEquals(["all"]) || properties.SetEquals(["any"]))
        {
            var key = properties.Single();
            var list = element.GetProperty(key);
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
            {
                return Invalid($"'{key}' takes a non-empty list of rules.");
            }

            var children = new List<RuleExpression>();
            foreach (var item in list.EnumerateArray())
            {
                var child = ParseNode(item, depth + 1, ref nodes);
                if (child.IsFailure)
                {
                    return child;
                }

                children.Add(child.Value);
            }

            return key == "all" ? new AllRule(children) : new AnyRule(children);
        }

        if (properties.SetEquals(["not"]))
        {
            var inner = ParseNode(element.GetProperty("not"), depth + 1, ref nodes);
            return inner.IsFailure ? inner : new NotRule(inner.Value);
        }

        if (!properties.Contains("field") || !properties.Contains("op") || !properties.IsSubsetOf(["field", "op", "value"]))
        {
            return Invalid("A comparison has exactly 'field', 'op' and (except for is_empty) 'value'.");
        }

        var field = element.GetProperty("field");
        if (field.ValueKind != JsonValueKind.String || !IsValidFieldCode(field.GetString()!))
        {
            return Invalid("'field' must be a field code such as \"contract_amount\".");
        }

        var op = element.GetProperty("op");
        if (op.ValueKind != JsonValueKind.String || !Operators.TryGetValue(op.GetString()!, out var @operator))
        {
            return Invalid($"Unknown operator. Allowed: {string.Join(", ", Operators.Keys)}.");
        }

        JsonElement? value = properties.Contains("value") ? element.GetProperty("value").Clone() : null;

        if (@operator == RuleOperator.IsEmpty)
        {
            return value is null
                ? new ComparisonRule(field.GetString()!, @operator, null)
                : Invalid("'is_empty' takes no value.");
        }

        if (value is not { } given)
        {
            return Invalid($"'{op.GetString()}' needs a value.");
        }

        switch (@operator)
        {
            case RuleOperator.In or RuleOperator.NotIn when given.ValueKind != JsonValueKind.Array:
                return Invalid($"'{op.GetString()}' compares against a list.");
            case RuleOperator.Gt or RuleOperator.Gte or RuleOperator.Lt or RuleOperator.Lte
                when given.ValueKind is not (JsonValueKind.Number or JsonValueKind.String):
                return Invalid($"'{op.GetString()}' compares against a number or a date.");
            case not (RuleOperator.In or RuleOperator.NotIn) when given.ValueKind is JsonValueKind.Object or JsonValueKind.Array:
                return Invalid($"'{op.GetString()}' compares against a single value.");
        }

        return new ComparisonRule(field.GetString()!, @operator, given);
    }

    private static Result<RuleExpression> Invalid(string message) =>
        Result.Failure<RuleExpression>(Error.Validation("rule.invalid", message));
}

/// <summary>
/// Evaluates a parsed rule against field values. Total: every input gives true or false, never an
/// exception, so a malformed or missing value can never break a form or a workflow.
///
/// Semantics, identical in the TypeScript port:
/// - a missing field is null;
/// - numbers compare as decimals; strings compare ordinally, which orders ISO dates correctly;
/// - comparing values of different kinds is false (and <c>ne</c> is simply "not eq");
/// - on a list field (multi-select), <c>eq</c>/<c>in</c> hold when any element matches, and
///   <c>contains</c> tests membership;
/// - on text, <c>contains</c> is a case-insensitive substring test;
/// - <c>is_empty</c> holds for null, "", whitespace and [].
/// </summary>
public static class RuleEvaluator
{
    public static bool Evaluate(RuleExpression rule, Func<string, JsonElement?> valueOf) => rule switch
    {
        AllRule all => all.Rules.All(child => Evaluate(child, valueOf)),
        AnyRule any => any.Rules.Any(child => Evaluate(child, valueOf)),
        NotRule not => !Evaluate(not.Rule, valueOf),
        ComparisonRule comparison => Compare(comparison, Normalize(valueOf(comparison.Field))),
        _ => false,
    };

    public static bool Evaluate(RuleExpression rule, JsonElement data) =>
        Evaluate(rule, field =>
            data.ValueKind == JsonValueKind.Object && data.TryGetProperty(field, out var value) ? value : null);

    private static JsonElement? Normalize(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } ? null : value;

    private static bool Compare(ComparisonRule rule, JsonElement? actual)
    {
        if (rule.Operator == RuleOperator.IsEmpty)
        {
            return IsEmpty(actual);
        }

        var expected = rule.Value!.Value;
        return rule.Operator switch
        {
            RuleOperator.Eq => Matches(actual, element => ScalarEquals(element, expected), nullMatches: expected.ValueKind == JsonValueKind.Null),
            RuleOperator.Ne => !Matches(actual, element => ScalarEquals(element, expected), nullMatches: expected.ValueKind == JsonValueKind.Null),
            RuleOperator.Gt => Order(actual, expected) is > 0,
            RuleOperator.Gte => Order(actual, expected) is >= 0,
            RuleOperator.Lt => Order(actual, expected) is < 0,
            RuleOperator.Lte => Order(actual, expected) is <= 0,
            RuleOperator.In => InList(actual, expected),
            RuleOperator.NotIn => !InList(actual, expected),
            RuleOperator.Contains => Contains(actual, expected),
            _ => false,
        };
    }

    private static bool IsEmpty(JsonElement? value) => value switch
    {
        null => true,
        { ValueKind: JsonValueKind.String } text => string.IsNullOrWhiteSpace(text.GetString()),
        { ValueKind: JsonValueKind.Array } list => list.GetArrayLength() == 0,
        _ => false,
    };

    private static bool Matches(JsonElement? actual, Func<JsonElement, bool> test, bool nullMatches)
    {
        if (actual is not { } value)
        {
            return nullMatches;
        }

        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Any(test)
            : test(value);
    }

    private static bool InList(JsonElement? actual, JsonElement list) =>
        list.EnumerateArray().Any(candidate =>
            Matches(actual, element => ScalarEquals(element, candidate), nullMatches: candidate.ValueKind == JsonValueKind.Null));

    private static bool Contains(JsonElement? actual, JsonElement expected) => actual switch
    {
        { ValueKind: JsonValueKind.Array } list => list.EnumerateArray().Any(element => ScalarEquals(element, expected)),
        { ValueKind: JsonValueKind.String } text when expected.ValueKind == JsonValueKind.String =>
            text.GetString()!.Contains(expected.GetString()!, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static bool ScalarEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number)
        {
            return ToDecimal(left) is { } a && ToDecimal(right) is { } b && a == b;
        }

        return (left.ValueKind, right.ValueKind) switch
        {
            (JsonValueKind.String, JsonValueKind.String) => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            (JsonValueKind.True or JsonValueKind.False, JsonValueKind.True or JsonValueKind.False) => left.ValueKind == right.ValueKind,
            (JsonValueKind.Null, JsonValueKind.Null) => true,
            _ => false,
        };
    }

    /// <summary>Sign of actual minus expected, or null when the two cannot be ordered.</summary>
    private static int? Order(JsonElement? actual, JsonElement expected)
    {
        if (actual is not { } value)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && expected.ValueKind == JsonValueKind.Number)
        {
            return ToDecimal(value) is { } a && ToDecimal(expected) is { } b ? a.CompareTo(b) : null;
        }

        if (value.ValueKind == JsonValueKind.String && expected.ValueKind == JsonValueKind.String)
        {
            return Math.Sign(string.CompareOrdinal(value.GetString(), expected.GetString()));
        }

        return null;
    }

    private static decimal? ToDecimal(JsonElement number) =>
        decimal.TryParse(number.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
