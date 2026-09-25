using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Dms.IntegrationTests;

internal sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    JsonElement User);

internal static class ApiHelpers
{
    public static async Task<TokenResponse> LoginAsync(this HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    public static HttpClient WithToken(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class AuthenticationApiTests(DmsApiFactory factory)
{
    [Fact]
    public async Task The_bootstrap_administrator_can_sign_in()
    {
        var client = factory.CreateClient();

        var tokens = await client.LoginAsync(DmsApiFactory.AdminUsername, DmsApiFactory.AdminPassword);

        tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        factory.BootstrapAdminHadToChangePassword.ShouldBeTrue();
        await factory.ShouldHaveAuditAsync("LOGIN");
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_audited()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { username = DmsApiFactory.AdminUsername, password = "not the password" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await factory.ShouldHaveAuditAsync("LOGIN_FAILED");
    }

    [Fact]
    public async Task An_unknown_username_gives_the_same_answer_as_a_wrong_password()
    {
        var client = factory.CreateClient();

        var unknown = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { username = "nobody-here", password = "whatever12345" });

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { username = DmsApiFactory.AdminUsername, password = "whatever12345" });

        // Account enumeration: both paths must look identical to the caller, apart from the
        // per-request trace id that every ProblemDetails response carries.
        unknown.StatusCode.ShouldBe(wrongPassword.StatusCode);
        Scrub(await unknown.Content.ReadAsStringAsync())
            .ShouldBe(Scrub(await wrongPassword.Content.ReadAsStringAsync()));

        static string Scrub(string body) =>
            System.Text.RegularExpressions.Regex.Replace(body, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"*\"");
    }

    [Fact]
    public async Task The_me_endpoint_needs_a_token()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_me_endpoint_returns_the_signed_in_user()
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(DmsApiFactory.AdminUsername, DmsApiFactory.AdminPassword);

        var response = await client.WithToken(tokens.AccessToken).GetAsync("/api/v1/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("username").GetString().ShouldBe(DmsApiFactory.AdminUsername);
        body.GetProperty("isSystemAdmin").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_refresh_token_can_be_exchanged_once_and_is_then_dead()
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(DmsApiFactory.AdminUsername, DmsApiFactory.AdminPassword);

        var refreshed = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = tokens.RefreshToken });

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Rotation: replaying the old token must fail.
        var replay = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = tokens.RefreshToken });

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await factory.ShouldHaveAuditAsync("TOKEN_REUSE_DETECTED");
    }

    [Fact]
    public async Task A_garbage_token_is_rejected()
    {
        var response = await factory.CreateClient()
            .WithToken("not.a.real.token")
            .GetAsync("/api/v1/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
