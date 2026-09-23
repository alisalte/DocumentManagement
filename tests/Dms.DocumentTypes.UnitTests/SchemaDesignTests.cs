using System.Text.Json;
using Dms.DocumentTypes.Application;
using Dms.DocumentTypes.Contracts;
using Dms.DocumentTypes.Domain;
using Dms.SharedKernel;
using Shouldly;

namespace Dms.DocumentTypes.UnitTests;

public sealed class SchemaDesignTests
{
    private static readonly IReadOnlyDictionary<string, FieldType> NothingPublished = new Dictionary<string, FieldType>();

    private static FieldSchema Field(string code, FieldType type = FieldType.Text) =>
        new() { Code = code, Label = new LocalizedText(code), Type = type };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static IReadOnlyDictionary<string, string[]> Errors(Result result) =>
        result.IsFailure ? result.Error.FieldErrors ?? new Dictionary<string, string[]>() : new Dictionary<string, string[]>();

    [Fact]
    public void A_sound_schema_passes()
    {
        var result = SchemaDesignValidator.Validate(
            [
                Field("kind", FieldType.Select) with { Options = [new("a", new LocalizedText("الف"))] },
                Field("amount", FieldType.Decimal) with { Validation = new FieldValidation { Min = 0, Scale = 2 } },
            ],
            [
                new FieldRuleSchema
                {
                    Kind = FieldRuleKind.Show,
                    Targets = ["amount"],
                    Condition = Json("""{ "field": "kind", "op": "eq", "value": "a" }"""),
                },
            ],
            NothingPublished);

        result.IsSuccess.ShouldBeTrue(string.Join("; ", Errors(result).SelectMany(error => error.Value)));
    }

    [Fact]
    public void Bad_codes_duplicates_and_missing_labels_are_reported_by_path()
    {
        var result = SchemaDesignValidator.Validate(
            [Field("Amount"), Field("dup"), Field("dup"), Field("ok") with { Label = new LocalizedText(" ") }],
            [],
            NothingPublished);

        Errors(result).Keys.Order().ShouldBe(["fields[0].code", "fields[2].code", "fields[3].label"]);
    }

    [Fact]
    public void A_field_code_keeps_its_type_forever()
    {
        var published = new Dictionary<string, FieldType> { ["amount"] = FieldType.Integer };

        var result = SchemaDesignValidator.Validate([Field("amount", FieldType.Text)], [], published);

        Errors(result).ShouldContainKey("fields[0].type");
    }

    [Fact]
    public void Options_belong_to_choice_fields_and_must_be_unique()
    {
        var result = SchemaDesignValidator.Validate(
            [
                Field("text") with { Options = [new("a", new LocalizedText("الف"))] },
                Field("pick", FieldType.Select),
                Field("dups", FieldType.MultiSelect) with
                {
                    Options = [new("a", new LocalizedText("الف")), new("a", new LocalizedText("ب"))],
                },
            ],
            [],
            NothingPublished);

        Errors(result).Keys.Order().ShouldBe(["fields[0].options", "fields[1].options", "fields[2].options[1].value"]);
    }

    [Fact]
    public void Validation_settings_must_fit_the_field_type()
    {
        var result = SchemaDesignValidator.Validate(
            [
                Field("n", FieldType.Integer) with { Validation = new FieldValidation { MaxLength = 3 } },
                Field("t") with { Validation = new FieldValidation { Min = 1 } },
                Field("d", FieldType.Date) with { Validation = new FieldValidation { MinDate = "2024-13-01" } },
                Field("m", FieldType.Decimal) with { Validation = new FieldValidation { Min = 5, Max = 1 } },
            ],
            [],
            NothingPublished);

        Errors(result).Keys.Order().ShouldBe(
            ["fields[0].validation", "fields[1].validation", "fields[2].validation", "fields[3].validation"]);
    }

    [Fact]
    public void Patterns_that_need_backtracking_are_refused_at_design_time()
    {
        // Backreferences are not supported by the non-backtracking engine.
        var result = SchemaDesignValidator.Validate(
            [Field("t") with { Validation = new FieldValidation { Pattern = @"^(\w)\1$" } }],
            [],
            NothingPublished);

        Errors(result).ShouldContainKey("fields[0].validation.pattern");
    }

    [Fact]
    public void A_default_must_pass_its_own_field()
    {
        var result = SchemaDesignValidator.Validate(
            [Field("n", FieldType.Integer) with { DefaultValue = Json("\"abc\""), Validation = new FieldValidation { Max = 10 } }],
            [],
            NothingPublished);

        Errors(result).ShouldContainKey("fields[0].defaultValue");
    }

    [Fact]
    public void Rules_must_parse_and_refer_to_real_fields()
    {
        var result = SchemaDesignValidator.Validate(
            [Field("a"), Field("b")],
            [
                new FieldRuleSchema { Kind = FieldRuleKind.Show, Targets = ["ghost"], Condition = Json("""{ "field": "a", "op": "is_empty" }""") },
                new FieldRuleSchema { Kind = FieldRuleKind.Require, Targets = ["b"], Condition = Json("""{ "field": "nope", "op": "is_empty" }""") },
                new FieldRuleSchema { Kind = FieldRuleKind.Require, Targets = ["b"], Condition = Json("""{ "field": "a", "op": "like", "value": 1 }""") },
                new FieldRuleSchema { Kind = FieldRuleKind.Validate, Targets = ["b"] },
                new FieldRuleSchema { Kind = FieldRuleKind.Show, Targets = ["a"], Condition = Json("""{ "field": "a", "op": "is_empty" }""") },
            ],
            NothingPublished);

        Errors(result).Keys.Order().ShouldBe(
            ["rules[0].targets", "rules[1].condition", "rules[2].condition", "rules[3].assertion", "rules[4].condition"]);
    }

    [Fact]
    public void Publishing_freezes_the_version_and_starts_a_draft_copy()
    {
        var now = DateTimeOffset.UtcNow;
        var type = DocumentType.Create("CONTRACT", "قرارداد", null, now);
        type.ReplaceDraftSchema([Field("amount", FieldType.Decimal)], [], now).IsSuccess.ShouldBeTrue();

        var published = type.PublishDraft(new UserId(Guid.NewGuid()), now).Value;

        type.Draft!.VersionNumber.ShouldBe(2);
        type.Draft.ToSchema().Fields.Select(field => field.Code).ShouldBe(["amount"]);

        // Editing now only ever touches the new draft; the published version is left as it was.
        type.ReplaceDraftSchema([Field("amount", FieldType.Decimal), Field("vendor")], [], now);
        type.Versions.Single(version => version.Id == published).ToSchema().Fields
            .Select(field => field.Code).ShouldBe(["amount"]);
        type.PublishedFieldTypes()["amount"].ShouldBe(FieldType.Decimal);
    }
}
