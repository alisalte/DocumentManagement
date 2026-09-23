using System.Text.Json;
using Dms.DocumentTypes.Application;
using Dms.DocumentTypes.Contracts;
using Shouldly;

namespace Dms.DocumentTypes.UnitTests;

/// <summary>
/// The validation matrix required for phase 3: every field type with a good value, a bad value
/// and its normalised form; declarative limits; defaults; and conditional fields.
/// </summary>
public sealed class MetadataValidatorTests
{
    private static FieldSchema Field(string code, FieldType type, Func<FieldSchema, FieldSchema>? configure = null)
    {
        var field = new FieldSchema { Code = code, Label = new LocalizedText(code), Type = type };
        return configure is null ? field : configure(field);
    }

    private static readonly FieldOptionSchema[] Regions =
    [
        new("north", new LocalizedText("شمال"), 1),
        new("south", new LocalizedText("جنوب"), 2),
        new("west", new LocalizedText("غرب"), 3, IsActive: false),
    ];

    private static DocumentTypeSchema Schema(params FieldSchema[] fields) => Schema(fields, []);

    private static DocumentTypeSchema Schema(FieldSchema[] fields, FieldRuleSchema[] rules) =>
        new(new DocumentTypeId(Guid.NewGuid()), new DocumentTypeVersionId(Guid.NewGuid()), 1, true, fields, rules);

    private static MetadataValidationOutcome Validate(DocumentTypeSchema schema, string json) =>
        MetadataValidator.Validate(schema, JsonDocument.Parse(json).RootElement);

    private static JsonElement Rule(string json) => JsonDocument.Parse(json).RootElement;

    public static TheoryData<FieldType, string, string?> Accepted => new()
    {
        // type, input value (JSON), normalised value (JSON) or null when unchanged
        { FieldType.Text, "\"  قرارداد  \"", "\"قرارداد\"" },
        { FieldType.LongText, "\"line 1\\nline 2\"", null },
        { FieldType.Integer, "42", null },
        { FieldType.Integer, "\"۱۲٬۰۰۰\"", "12000" },
        { FieldType.Decimal, "12.50", "12.50" },
        { FieldType.Decimal, "\"12345678901234.56\"", "12345678901234.56" },
        { FieldType.Decimal, "\"۳٫۵\"", "3.5" },
        { FieldType.Boolean, "true", null },
        { FieldType.Boolean, "\"false\"", "false" },
        { FieldType.Date, "\"2024-03-20\"", null },
        { FieldType.Date, "\"۲۰۲۴-۰۳-۲۰\"", "\"2024-03-20\"" },
        { FieldType.DateTime, "\"2024-03-20T12:30:00+03:30\"", "\"2024-03-20T09:00:00Z\"" },
        { FieldType.Select, "\"north\"", null },
        { FieldType.MultiSelect, "[\"south\", \"north\", \"south\"]", "[\"north\",\"south\"]" },
        { FieldType.User, "\"0f8fad5b-d9cb-469f-a165-70867728950e\"", null },
        { FieldType.Group, "\"0f8fad5b-d9cb-469f-a165-70867728950e\"", null },
        { FieldType.DocumentReference, "\"0f8fad5b-d9cb-469f-a165-70867728950e\"", null },
        { FieldType.Url, "\"https://example.com/a?b=1\"", null },
        { FieldType.Email, "\"Someone@Example.COM\"", "\"Someone@example.com\"" },
        { FieldType.Phone, "\"+98 (21) ۸۸۱۲-۳۴۵۶\"", "\"+982188123456\"" },
    };

    public static TheoryData<FieldType, string> Rejected => new()
    {
        { FieldType.Text, "12" },
        { FieldType.Integer, "1.5" },
        { FieldType.Integer, "\"abc\"" },
        { FieldType.Decimal, "true" },
        { FieldType.Boolean, "\"maybe\"" },
        { FieldType.Date, "\"2024-02-30\"" },
        { FieldType.Date, "\"20/03/2024\"" },
        { FieldType.Date, "\"1403-01-01T00:00\"" },
        { FieldType.DateTime, "\"2024-03-20T12:30:00\"" },
        { FieldType.Select, "\"east\"" },
        { FieldType.Select, "\"west\"" },
        { FieldType.MultiSelect, "\"north\"" },
        { FieldType.MultiSelect, "[\"north\", \"east\"]" },
        { FieldType.User, "\"not-a-guid\"" },
        { FieldType.User, "\"00000000-0000-0000-0000-000000000000\"" },
        { FieldType.Url, "\"javascript:alert(1)\"" },
        { FieldType.Url, "\"example.com\"" },
        { FieldType.Email, "\"not an email\"" },
        { FieldType.Phone, "\"12\"" },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void Accepts_and_normalises_every_field_type(FieldType type, string input, string? normalised)
    {
        var field = Field("value", type, field => type is FieldType.Select or FieldType.MultiSelect
            ? field with { Options = Regions }
            : field);

        var outcome = Validate(Schema(field), $$"""{ "value": {{input}} }""");

        outcome.IsValid.ShouldBeTrue(string.Join("; ", outcome.Errors.SelectMany(error => error.Value)));
        var expected = System.Text.Json.Nodes.JsonNode.Parse(normalised ?? input);
        System.Text.Json.Nodes.JsonNode.DeepEquals(outcome.Normalized["value"], expected)
            .ShouldBeTrue($"{outcome.Normalized["value"]} vs {expected}");
    }

    [Theory]
    [MemberData(nameof(Rejected))]
    public void Rejects_values_of_the_wrong_shape(FieldType type, string input)
    {
        var field = Field("value", type, field => type is FieldType.Select or FieldType.MultiSelect
            ? field with { Options = Regions }
            : field);

        var outcome = Validate(Schema(field), $$"""{ "value": {{input}} }""");

        outcome.Errors.ShouldContainKey("value");
    }

    [Fact]
    public void Unknown_fields_are_refused_rather_than_silently_stored()
    {
        var outcome = Validate(Schema(Field("amount", FieldType.Integer)), """{ "amount": 1, "sneaky": "x" }""");

        outcome.Errors.Keys.ShouldBe(["sneaky"]);
    }

    [Fact]
    public void Required_fields_must_be_present_and_not_blank()
    {
        var schema = Schema(Field("title", FieldType.Text, field => field with { IsRequired = true }));

        Validate(schema, "{}").Errors.ShouldContainKey("title");
        Validate(schema, """{ "title": "   " }""").Errors.ShouldContainKey("title");
        Validate(schema, """{ "title": null }""").Errors.ShouldContainKey("title");
        Validate(schema, """{ "title": "ok" }""").IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Declarative_limits_are_enforced()
    {
        var schema = Schema(
            Field("code", FieldType.Text, field => field with
            {
                Validation = new FieldValidation { MinLength = 3, MaxLength = 5, Pattern = "^[A-Z]+$" },
            }),
            Field("amount", FieldType.Decimal, field => field with
            {
                Validation = new FieldValidation { Min = 0, Max = 100, Scale = 2 },
            }),
            Field("signed_on", FieldType.Date, field => field with
            {
                Validation = new FieldValidation { MinDate = "2020-01-01", MaxDate = "2030-12-31" },
            }),
            Field("regions", FieldType.MultiSelect, field => field with
            {
                Options = Regions,
                Validation = new FieldValidation { MaxItems = 1 },
            }));

        Validate(schema, """{ "code": "ABC", "amount": 99.99, "signed_on": "2024-01-01", "regions": ["north"] }""")
            .IsValid.ShouldBeTrue();

        var outcome = Validate(schema, """{ "code": "ab", "amount": 100.001, "signed_on": "2019-12-31", "regions": ["north", "south"] }""");
        outcome.Errors.Keys.Order().ShouldBe(["amount", "code", "regions", "signed_on"]);
        outcome.Errors["code"].Count.ShouldBe(2, "too short and not upper case");
        outcome.Errors["amount"].Count.ShouldBe(2, "above maximum and too many decimals");

        Validate(schema, """{ "code": "ABCDEF" }""").Errors.ShouldContainKey("code");
        Validate(schema, """{ "amount": -1 }""").Errors.ShouldContainKey("amount");
        Validate(schema, """{ "signed_on": "2031-01-01" }""").Errors.ShouldContainKey("signed_on");
    }

    [Fact]
    public void Text_has_an_implicit_cap_even_without_limits()
    {
        var schema = Schema(Field("note", FieldType.Text));

        Validate(schema, $$"""{ "note": "{{new string('x', MetadataValidator.TextCap + 1)}}" }""")
            .Errors.ShouldContainKey("note");
    }

    [Fact]
    public void A_catastrophic_pattern_cannot_hang_validation()
    {
        // Exponential with a backtracking engine; linear with NonBacktracking.
        var schema = Schema(Field("value", FieldType.Text, field => field with
        {
            Validation = new FieldValidation { Pattern = "^(a+)+$" },
        }));

        var started = DateTime.UtcNow;
        Validate(schema, $$"""{ "value": "{{new string('a', 900)}}!" }""").Errors.ShouldContainKey("value");
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Defaults_fill_absent_fields_but_not_explicit_nulls()
    {
        var schema = Schema(Field("status", FieldType.Select, field => field with
        {
            Options = Regions,
            DefaultValue = JsonDocument.Parse("\"north\"").RootElement,
        }));

        Validate(schema, "{}").Normalized["status"]!.GetValue<string>().ShouldBe("north");
        Validate(schema, """{ "status": null }""").Normalized.ContainsKey("status").ShouldBeFalse();
    }

    [Fact]
    public void Show_rules_hide_fields_and_hidden_values_are_dropped()
    {
        var schema = Schema(
            [
                Field("kind", FieldType.Select, field => field with
                {
                    Options = [new("contract", new LocalizedText("قرارداد")), new("letter", new LocalizedText("نامه"))],
                }),
                Field("amount", FieldType.Decimal, field => field with { IsRequired = true }),
            ],
            [
                new FieldRuleSchema
                {
                    Kind = FieldRuleKind.Show,
                    Targets = ["amount"],
                    Condition = Rule("""{ "field": "kind", "op": "eq", "value": "contract" }"""),
                },
            ]);

        // Shown and required for a contract.
        Validate(schema, """{ "kind": "contract" }""").Errors.ShouldContainKey("amount");
        Validate(schema, """{ "kind": "contract", "amount": 5 }""").IsValid.ShouldBeTrue();

        // Hidden for a letter: not required, and a stale value is not stored.
        var letter = Validate(schema, """{ "kind": "letter", "amount": 5 }""");
        letter.IsValid.ShouldBeTrue();
        letter.Normalized.ContainsKey("amount").ShouldBeFalse();
    }

    [Fact]
    public void Hiding_cascades_through_dependent_show_rules()
    {
        var schema = Schema(
            [
                Field("has_vendor", FieldType.Boolean),
                Field("vendor", FieldType.Text),
                Field("vendor_email", FieldType.Email),
            ],
            [
                new FieldRuleSchema
                {
                    Kind = FieldRuleKind.Show,
                    Targets = ["vendor"],
                    Condition = Rule("""{ "field": "has_vendor", "op": "eq", "value": true }"""),
                },
                new FieldRuleSchema
                {
                    Kind = FieldRuleKind.Show,
                    Targets = ["vendor_email"],
                    Condition = Rule("""{ "not": { "field": "vendor", "op": "is_empty" } }"""),
                },
            ]);

        // vendor is hidden, so it counts as empty, so vendor_email is hidden too.
        var outcome = Validate(schema, """{ "has_vendor": false, "vendor": "ACME", "vendor_email": "a@acme.com" }""");

        outcome.IsValid.ShouldBeTrue();
        outcome.Normalized.Select(pair => pair.Key).ShouldBe(["has_vendor"]);
    }

    [Fact]
    public void Require_rules_make_fields_conditionally_mandatory()
    {
        var schema = Schema(
            [Field("amount", FieldType.Decimal), Field("approver", FieldType.User)],
            [
                new FieldRuleSchema
                {
                    Kind = FieldRuleKind.Require,
                    Targets = ["approver"],
                    Condition = Rule("""{ "field": "amount", "op": "gt", "value": 10000000000 }"""),
                },
            ]);

        Validate(schema, """{ "amount": 8000000000 }""").IsValid.ShouldBeTrue();
        Validate(schema, """{ "amount": 12000000000 }""").Errors.ShouldContainKey("approver");
    }

    [Fact]
    public void Validate_rules_check_fields_against_each_other()
    {
        var schema = Schema(
            [Field("starts_on", FieldType.Date), Field("ends_on", FieldType.Date)],
            [
                new FieldRuleSchema
                {
                    Kind = FieldRuleKind.Validate,
                    Targets = ["ends_on"],
                    Condition = Rule("""{ "all": [ { "not": { "field": "starts_on", "op": "is_empty" } }, { "not": { "field": "ends_on", "op": "is_empty" } } ] }"""),
                    Assertion = Rule("""{ "field": "ends_on", "op": "gte", "value": "2024-01-01" }"""),
                    Message = new LocalizedText("پایان باید پس از ۱۴۰۲ باشد."),
                },
            ]);

        Validate(schema, """{ "starts_on": "2023-01-01", "ends_on": "2023-06-01" }""")
            .Errors["ends_on"].ShouldBe(["پایان باید پس از ۱۴۰۲ باشد."]);
        Validate(schema, """{ "starts_on": "2023-01-01", "ends_on": "2024-06-01" }""").IsValid.ShouldBeTrue();
        Validate(schema, """{ "ends_on": "2023-06-01" }""").IsValid.ShouldBeTrue("the condition does not hold");
    }

    [Fact]
    public void Inactive_fields_keep_old_values_readable_but_take_no_input()
    {
        var schema = Schema(Field("legacy", FieldType.Text, field => field with { IsActive = false, IsRequired = true }));

        var outcome = Validate(schema, """{ "legacy": "ignored" }""");

        outcome.IsValid.ShouldBeTrue("an inactive required field is not demanded");
        outcome.Normalized.ContainsKey("legacy").ShouldBeFalse();
    }

    [Fact]
    public void References_are_collected_for_the_caller_to_check()
    {
        var id = Guid.NewGuid();
        var schema = Schema(Field("owner", FieldType.User), Field("team", FieldType.Group), Field("related", FieldType.DocumentReference));

        var outcome = Validate(schema, $$"""{ "owner": "{{id}}", "team": "{{id}}", "related": "{{id}}" }""");

        outcome.UserReferences.ShouldBe([id]);
        outcome.GroupReferences.ShouldBe([id]);
        outcome.DocumentReferences.ShouldBe([id]);
    }

    [Fact]
    public void Metadata_that_is_not_an_object_is_refused()
    {
        MetadataValidator.Validate(Schema(), JsonDocument.Parse("[1]").RootElement).IsValid.ShouldBeFalse();
    }
}
