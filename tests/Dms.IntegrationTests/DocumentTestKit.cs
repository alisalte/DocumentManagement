using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Shouldly;

namespace Dms.IntegrationTests;

/// <summary>
/// Arranges users, categories, ACL entries and documents through the public API only, so the
/// tests exercise exactly the paths a client would. Every name is made unique per call because
/// all tests in the collection share one database.
/// </summary>
internal static class DocumentTestKit
{
    public const string UserPassword = "OrdinaryUserPassword!1";

    public static async Task<HttpClient> AdminAsync(this DmsApiFactory factory)
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(DmsApiFactory.AdminUsername, DmsApiFactory.AdminPassword);
        return client.WithToken(tokens.AccessToken);
    }

    public static async Task<(HttpClient Client, Guid UserId)> CreateUserAsync(this DmsApiFactory factory, string prefix)
    {
        var admin = await factory.AdminAsync();
        var username = $"{prefix}.{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 60)];

        var created = await admin.PostAsJsonAsync("/api/v1/admin/users", new
        {
            username,
            displayName = username,
            email = (string?)null,
            password = UserPassword,
            isSystemAdmin = false,
            mustChangePassword = false,
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var userId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(username, UserPassword);
        return (client.WithToken(tokens.AccessToken), userId);
    }

    public static async Task<Guid> CreateCategoryAsync(this HttpClient admin, Guid? parentId = null, string? name = null)
    {
        var code = $"C{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var response = await admin.PostAsJsonAsync("/api/v1/admin/categories", new
        {
            parentId,
            name = name ?? $"دسته {code}",
            code,
            description = (string?)null,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> GrantAsync(
        this HttpClient admin,
        string resourceType,
        Guid resourceId,
        Guid userId,
        string permission,
        string effect = "Allow",
        bool inherit = false)
    {
        var response = await admin.PostAsJsonAsync(
            $"/api/v1/resources/{resourceType}/{resourceId}/permissions",
            new { subjectType = "User", subjectId = userId, permissionCode = permission, effect, inherit });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task GrantManyAsync(
        this HttpClient admin,
        string resourceType,
        Guid resourceId,
        Guid userId,
        params string[] permissions)
    {
        foreach (var permission in permissions)
        {
            await admin.GrantAsync(resourceType, resourceId, userId, permission);
        }
    }

    public static async Task<Guid> GeneralTypeIdAsync(this HttpClient client)
    {
        var types = await client.GetFromJsonAsync<JsonElement>("/api/v1/document-types");
        return types.EnumerateArray()
            .First(type => type.GetProperty("code").GetString() == "GENERAL")
            .GetProperty("id").GetGuid();
    }

    public static async Task<HttpResponseMessage> UploadRawAsync(this HttpClient client, byte[] content, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return await client.PostAsync("/api/v1/uploads", form);
    }

    public static async Task<JsonElement> UploadAsync(this HttpClient client, byte[] content, string fileName = "file.pdf")
    {
        var response = await client.UploadRawAsync(content, fileName);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<HttpResponseMessage> CreateDocumentRawAsync(
        this HttpClient client,
        Guid categoryId,
        Guid uploadId,
        string title,
        string[]? tags = null,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documents")
        {
            Content = JsonContent.Create(new
            {
                title,
                description = "توضیح",
                categoryId,
                documentTypeId = await client.GeneralTypeIdAsync(),
                uploadId,
                tags = tags ?? [],
                changeDescription = "نسخه‌ی اول",
            }),
        };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Uploads a file and files it; returns the new document and its first version.</summary>
    public static async Task<(Guid DocumentId, Guid VersionId)> CreateDocumentAsync(
        this HttpClient client,
        Guid categoryId,
        string title = "قرارداد نمونه",
        byte[]? content = null,
        string fileName = "contract.pdf")
    {
        var upload = await client.UploadAsync(content ?? Pdf($"{title} {Guid.NewGuid()}"), fileName);
        var response = await client.CreateDocumentRawAsync(categoryId, upload.GetProperty("uploadId").GetGuid(), title);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("documentId").GetGuid(), body.GetProperty("versionId").GetGuid());
    }

    public static async Task<HttpResponseMessage> AddVersionRawAsync(
        this HttpClient client,
        Guid documentId,
        byte[] content,
        Guid? baseVersionId = null)
    {
        var upload = await client.UploadAsync(content, "revised.pdf");
        return await client.PostAsJsonAsync($"/api/v1/documents/{documentId}/versions", new
        {
            uploadId = upload.GetProperty("uploadId").GetGuid(),
            changeDescription = "اصلاح",
            baseVersionId,
        });
    }

    /// <summary>A tiny but real PDF header, so MIME sniffing has something to recognise.</summary>
    public static byte[] Pdf(string marker) => Encoding.UTF8.GetBytes($"%PDF-1.7\n% {marker}\n%%EOF\n");
}
