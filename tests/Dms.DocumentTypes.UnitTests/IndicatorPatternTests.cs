using Dms.DocumentTypes.Contracts;
using Shouldly;

namespace Dms.DocumentTypes.UnitTests;

public sealed class IndicatorPatternTests
{
    private static IndicatorPattern Parse(string text)
    {
        var result = IndicatorPattern.Parse(text);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value!;
    }

    [Fact]
    public void The_default_pattern_formats_type_year_and_padded_sequence()
    {
        Parse(IndicatorPattern.Default).Format("CTR", 1405, "FIN", 45).ShouldBe("CTR-1405-00045");
    }

    [Fact]
    public void Prefix_category_and_separators_are_kept()
    {
        Parse("DM/{cat}/{YEAR}/{SEQ:3}").Format("CTR", 1405, "FIN", 7).ShouldBe("DM/FIN/1405/007");
    }

    [Fact]
    public void A_sequence_wider_than_its_digits_is_not_truncated()
    {
        Parse("{YEAR}-{SEQ:2}").Format("X", 1405, null, 1234).ShouldBe("1405-1234");
    }

    [Fact]
    public void A_missing_category_code_leaves_its_place_empty()
    {
        Parse("{CAT}{YEAR}-{SEQ:1}").Format("X", 1405, null, 3).ShouldBe("1405-3");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{TYPE}-{SEQ:4}")]
    [InlineData("{TYPE}-{YEAR}")]
    [InlineData("{YEAR}-{SEQ:4}-{SEQ:2}")]
    [InlineData("{YEAR}-{SEQ:0}")]
    [InlineData("{YEAR}-{SEQ:11}")]
    [InlineData("{YEAR}-{SEQ:x}")]
    [InlineData("{YEAR}-{NOPE}-{SEQ:4}")]
    [InlineData("{YEAR}-{SEQ:4")]
    [InlineData("{YEAR}}-{SEQ:4}")]
    [InlineData("{YEAR}-{SEQ:4}-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Invalid_patterns_are_rejected(string text)
    {
        var result = IndicatorPattern.Parse(text);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("indicator_pattern.invalid");
    }

    [Fact]
    public void Tokens_are_case_insensitive_and_stored_canonically()
    {
        Parse(" {type}-{ year }-{seq:4} ").Text.ShouldBe("{TYPE}-{YEAR}-{SEQ:4}");
    }

    [Fact]
    public void Resolve_fills_the_type_code_so_collisions_can_be_detected()
    {
        var shared = Parse(IndicatorPattern.Default);
        var own = Parse("ctr-{YEAR}-{SEQ:5}");

        own.Resolve("INV").ShouldBe(shared.Resolve("CTR"));
        shared.Resolve("INV").ShouldNotBe(shared.Resolve("CTR"));
    }

    [Fact]
    public void HasTypeCode_reports_the_type_token()
    {
        Parse(IndicatorPattern.Default).HasTypeCode.ShouldBeTrue();
        Parse("A-{YEAR}-{SEQ:4}").HasTypeCode.ShouldBeFalse();
    }
}
