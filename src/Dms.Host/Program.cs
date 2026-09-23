using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Dms.Application;
using Dms.Audit.Infrastructure;
using Dms.Authorization.Infrastructure;
using Dms.DocumentTypes.Infrastructure;
using Dms.Documents.Infrastructure;
using Dms.Workflow.Infrastructure;
using Dms.Host;
using Dms.Identity.Application;
using Dms.Identity.Infrastructure;
using Dms.Infrastructure;
using Dms.Infrastructure.Jobs;
using Dms.Storage.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole();

var connectionString = builder.Configuration.GetConnectionString("Dms")
    ?? throw new InvalidOperationException(
        "No database connection string. Set ConnectionStrings__Dms in the environment.");

// Modules. Each owns a schema and registers its own DbContext on the shared connection.
builder.Services.AddDmsBuildingBlocks(connectionString);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddAuthorizationModule();
builder.Services.AddAuditModule();
builder.Services.AddStorageModule(builder.Configuration);
builder.Services.AddDocumentTypesModule();
builder.Services.AddDocumentsModule();
builder.Services.AddWorkflowModule();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

builder.Services.Configure<JobWorkerOptions>(builder.Configuration.GetSection("Dms:Jobs"));

// Roles: "api", "worker" or "all". One image, two jobs, so OCR later cannot starve the API.
var role = builder.Configuration["Dms:Role"] ?? "all";
if (role is "worker" or "all")
{
    builder.Services.AddHostedService<JobWorker>();
    builder.Services.AddHostedService<RecurringJobScheduler>();
    builder.Services.Configure<JobWorkerOptions>(options => options.Enabled = true);
}

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwt = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.SigningKey))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "No JWT signing key. Set Dms__Jwt__SigningKey in the environment.");
    }

    // Development convenience: an ephemeral key, so no secret is ever committed. Restarting the
    // API invalidates outstanding tokens, which is fine locally.
    jwt.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    builder.Services.Configure<JwtOptions>(options => options.SigningKey = jwt.SigningKey);
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

// Enums travel as names, not numbers: "Allow"/"Deny" survives a reordering of the enum, a number
// does not, and permission decisions are the last place we want a silent off-by-one.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());

    // Typed ids travel as plain GUID strings, never as {"value": ...} objects.
    options.SerializerOptions.Converters.Add(new Dms.Web.StronglyTypedIdJsonConverterFactory());
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DmsExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Sign-in is the one anonymous write endpoint, so it gets its own budget per client address.
    var loginPerMinute = builder.Configuration.GetValue("Dms:RateLimits:LoginPerMinute", 10);
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var corsOrigins = builder.Configuration.GetSection("Dms:Cors:Origins").Get<string[]>() ?? [];
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()

        // Downloads carry the file name here; browsers hide it from scripts unless exposed.
        .WithExposedHeaders("Content-Disposition")));
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    // Only proxies we name are trusted, otherwise a client could forge its own audit IP address.
    foreach (var proxy in builder.Configuration.GetSection("Dms:KnownProxies").Get<string[]>() ?? [])
    {
        if (System.Net.IPAddress.TryParse(proxy, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseRateLimiter();

if (corsOrigins.Length > 0)
{
    app.UseCors();
}

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" })).AllowAnonymous().ExcludeFromDescription();

app.MapGet("/health/ready", async (NpgsqlDataSource dataSource, CancellationToken ct) =>
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(ct);
            return Results.Ok(new { status = "ready" });
        }
        catch (NpgsqlException)
        {
            return Results.Problem("The database is not reachable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .AllowAnonymous()
    .ExcludeFromDescription();

if (role is "api" or "all")
{
    app.MapIdentityEndpoints();
    app.MapAuthorizationEndpoints();
    app.MapAuditEndpoints();
    app.MapDocumentTypeEndpoints();
    app.MapDocumentEndpoints();
    app.MapWorkflowEndpoints();
}

app.Run();

/// <summary>Exposed so the integration tests can boot the real host with WebApplicationFactory.</summary>
public partial class Program;
