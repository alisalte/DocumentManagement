using System.Text.Json;
using Dms.SharedKernel.Rules;
using Shouldly;

namespace Dms.DocumentTypes.UnitTests;

/// <summary>
/// The C# half of the shared rule vectors. frontend/src/lib/rules.test.ts runs the same file, so a
/// case that passes here and fails there (or the reverse) means the two evaluators have drifted.
/// </summary>
public sealed class RuleLanguageTests
{
    private static readonly JsonElement Cases =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rule-cases.json"))).RootElement;

    public static TheoryData<string> EvaluateCases() => Names("evaluate");

    public static TheoryData<string> InvalidCases() => Names("invalid");

    [Theory]
    [MemberData(nameof(EvaluateCases))]
    public void Evaluates_the_shared_vector(string name)
    {
        var testCase = Find("evaluate", name);

        var parsed = RuleParser.Parse(testCase.GetProperty("rule"));

        parsed.IsSuccess.ShouldBeTrue(parsed.IsFailure ? parsed.Error.Message : null);
        RuleEvaluator.Evaluate(parsed.Value, testCase.GetProperty("data"))
            .ShouldBe(testCase.GetProperty("expected").GetBoolean(), name);
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Rejects_the_shared_invalid_rule(string name)
    {
        RuleParser.Parse(Find("invalid", name).GetProperty("rule")).IsFailure.ShouldBeTrue(name);
    }

    [Fact]
    public void Lists_the_fields_a_rule_reads()
    {
        var rule = RuleParser.Parse("""
            { "all": [ { "field": "amount", "op": "gt", "value": 1 },
                       { "not": { "field": "vendor", "op": "is_empty" } } ] }
            """).Value;

        RuleParser.ReferencedFields(rule).Order().ShouldBe(["amount", "vendor"]);
    }

    [Fact]
    public void A_rule_with_too_many_parts_is_refused()
    {
        var comparisons = string.Join(",", Enumerable.Range(0, RuleParser.MaxNodes + 1)
            .Select(_ => """{ "field": "a", "op": "is_empty" }"""));

        RuleParser.Parse($$"""{ "any": [ {{comparisons}} ] }""").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Numbers_compare_as_decimals_beyond_double_precision()
    {
        // 2^53 + 1 is not representable as a double; the server must still tell them apart.
        var rule = RuleParser.Parse("""{ "field": "amount", "op": "gt", "value": 9007199254740992 }""").Value;

        RuleEvaluator.Evaluate(rule, JsonDocument.Parse("""{ "amount": 9007199254740993 }""").RootElement).ShouldBeTrue();
    }

    private static TheoryData<string> Names(string group)
    {
        var data = new TheoryData<string>();
        foreach (var item in Cases.GetProperty(group).EnumerateArray())
        {
            data.Add(item.GetProperty("name").GetString()!);
        }

        return data;
    }

    private static JsonElement Find(string group, string name) =>
        Cases.GetProperty(group).EnumerateArray().Single(item => item.GetProperty("name").GetString() == name);
}
