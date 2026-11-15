using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Indicator numbers (docs/requirements/indicator-number): patterns per document type with a
/// global default, one counter per type and fiscal year, manual registration, import and backfill.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class IndicatorNumberTests(DmsApiFactory factory)
{
    private const string Admin = "/api/v1/admin/document-types";

    /// <summary>A published, field-less type with a fresh code, optionally with its own pattern.</summary>
    private static async Task<(Guid Id, string Code)> CreateTypeAsync(HttpClient admin, string? pattern = null)
    {
        var code = $"I{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var created = await admin.PostAsJsonAsync(Admin, new { code, name = $"نوع {code}", description = (string?)null });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var published = await admin.PostAsync($"{Admin}/{id}/publish", null);
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync());

        if (pattern is not null)
        {
            (await SetTypePatternAsync(admin, id, code, pattern)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        return (id, code);
    }

    private static Task<HttpResponseMessage> SetTypePatternAsync(HttpClient admin, Guid id, string code, string? pattern) =>
        admin.PutAsJsonAsync($"{Admin}/{id}", new
        {
            name = $"نوع {code}",
            description = (string?)null,
            settings = new { indicatorPattern = pattern },
            isActive = true,
        });

    private static async Task<string?> TypePatternAsync(HttpClient admin, Guid id)
    {
        var type = await admin.GetFromJsonAsync<JsonElement>($"{Admin}/{id}");
        var settings = type.GetProperty("type").GetProperty("settings");
        return settings.TryGetProperty("indicatorPattern", out var pattern) ? pattern.GetString() : null;
    }

    [Fact]
    public async Task The_default_pattern_applies_until_an_administrator_changes_it()
    {
        var admin = await factory.AdminAsync();

        var settings = await admin.GetFromJsonAsync<JsonElement>($"{Admin}/indicator-settings");

        settings.GetProperty("defaultPattern").GetString().ShouldBe("{TYPE}-{YEAR}-{SEQ:5}");
    }

    [Fact]
    public async Task The_default_pattern_must_contain_the_type_code()
    {
        var admin = await factory.AdminAsync();

        var response = await admin.PutAsJsonAsync($"{Admin}/indicator-settings", new { defaultPattern = "DOC-{YEAR}-{SEQ:5}" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("indicator_pattern.default_needs_type");
    }

    [Fact]
    public async Task A_type_pattern_is_stored_canonically_and_blank_means_default()
    {
        var admin = await factory.AdminAsync();
        var (id, code) = await CreateTypeAsync(admin, " {type}/{year}/{seq:3} ");

        (await TypePatternAsync(admin, id)).ShouldBe("{TYPE}/{YEAR}/{SEQ:3}");

        (await SetTypePatternAsync(admin, id, code, "  ")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await TypePatternAsync(admin, id)).ShouldBeNull();
    }

    [Fact]
    public async Task An_invalid_type_pattern_is_rejected()
    {
        var admin = await factory.AdminAsync();
        var (id, code) = await CreateTypeAsync(admin);

        var response = await SetTypePatternAsync(admin, id, code, "{TYPE}-{SEQ:4}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("indicator_pattern.invalid");
    }

    [Fact]
    public async Task A_type_pattern_that_would_repeat_another_types_numbers_is_rejected()
    {
        var admin = await factory.AdminAsync();
        var (_, first) = await CreateTypeAsync(admin);
        var (secondId, second) = await CreateTypeAsync(admin);

        // The first type uses the default {TYPE}-{YEAR}-{SEQ:5}, which reads exactly like this.
        var response = await SetTypePatternAsync(admin, secondId, second, $"{first}-{{YEAR}}-{{SEQ:5}}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("indicator_pattern.collides");
    }

    [Fact]
    public async Task Preview_shows_a_sample_with_the_types_code()
    {
        var admin = await factory.AdminAsync();
        var (id, code) = await CreateTypeAsync(admin);

        var response = await admin.PostAsJsonAsync($"{Admin}/indicator-pattern/preview", new { pattern = "{TYPE}-{CAT}-{YEAR}-{SEQ:4}", documentTypeId = id });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var sample = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sample").GetString();
        sample.ShouldStartWith($"{code}-CAT-14");
        sample.ShouldEndWith("-0001");
    }

    [Fact]
    public async Task Only_administrators_manage_patterns()
    {
        var (user, _) = await factory.CreateUserAsync("indicator-pattern");

        (await user.GetAsync($"{Admin}/indicator-settings")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.PutAsJsonAsync($"{Admin}/indicator-settings", new { defaultPattern = "{TYPE}-{YEAR}-{SEQ:5}" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
