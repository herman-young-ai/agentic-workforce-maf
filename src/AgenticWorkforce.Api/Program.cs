using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using NetEscapades.AspNetCore.SecurityHeaders;
using StackExchange.Redis;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Core.Exceptions;
using AgenticWorkforce.Api.Core.Extensions;
using AgenticWorkforce.Api.Core.Health;
using AgenticWorkforce.Api.Core.Middleware;
using AgenticWorkforce.Api.Hubs;
using AgenticWorkforce.Api.Services;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure;
using AgenticWorkforce.Infrastructure.Data;
using AgenticWorkforce.ServiceDefaults;
using AgenticWorkforce.ServiceDefaults.Observability;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// -- Kestrel default request-body cap kept at the ASP.NET Core default of
//    30 MB. JSON endpoints don't need more; the document-upload slice opts
//    in to a larger payload via per-route IRequestSizeLimitMetadata
//    (Principle 19: bounded resource usage, scoped narrowly). --

// -- Aspire ServiceDefaults (OTel, health checks, service discovery) --
builder.AddServiceDefaults();

// -- Observability (Serilog + PII masking) --
builder.AddObservability("AgenticWorkforce.Api", source: "web");

// -- Application Insights (Api-only telemetry sink) --
var aiConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrEmpty(aiConnectionString))
    builder.Services.AddApplicationInsightsTelemetry(opts =>
        opts.ConnectionString = aiConnectionString);

// -- Azure Key Vault (production) --
if (builder.Environment.IsProduction())
{
    var kvUri = builder.Configuration["KeyVault:Uri"];
    if (string.IsNullOrWhiteSpace(kvUri))
        throw new InvalidOperationException(
            "KeyVault:Uri is required in production (null or empty value supplied).");
    builder.Configuration.AddAzureKeyVault(new Uri(kvUri), new DefaultAzureCredential());
}

// -- Authentication (Entra ID) --
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// SSE token scheme — registered separately because the Microsoft.Identity.Web
// builder doesn't expose AddScheme. Subsequent AddAuthentication() returns
// the same underlying builder so the scheme is appended. Opt-in via the
// "SseStream" policy; non-stream endpoints keep the default JWT-only policy.
builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, SseTokenAuthHandler>(
        SseTokenAuthHandler.SchemeName, _ => { });

builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
{
    // Missing client-id used to silently fall back to "" — which then made
    // every JWT with an empty audience pass validation. Fail-fast on
    // missing config; Audience legitimately falls back to ClientId per
    // Microsoft.Identity.Web convention but ClientId is non-negotiable.
    var clientId = builder.Configuration["AzureAd:ClientId"];
    if (string.IsNullOrWhiteSpace(clientId))
        throw new InvalidOperationException(
            "AzureAd:ClientId is required (set via Aspire reference, env var, user-secrets, or Key Vault).");
    var audience = builder.Configuration["AzureAd:Audience"] ?? clientId;

    opts.TokenValidationParameters.ValidAudiences          = [audience, clientId, $"api://{clientId}"];
    opts.TokenValidationParameters.ValidateIssuer           = true;
    opts.TokenValidationParameters.ValidateAudience         = true;
    opts.TokenValidationParameters.ValidateLifetime         = true;
    opts.TokenValidationParameters.ValidateIssuerSigningKey = true;
    opts.TokenValidationParameters.RequireSignedTokens      = true;
    opts.TokenValidationParameters.RequireExpirationTime    = true;
    opts.TokenValidationParameters.ClockSkew               = TimeSpan.FromMinutes(2);
});

// -- Authorization (hierarchical roles: ADR-007) --
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build())
    .AddPolicy(Policies.RequirePlatformAdmin, p => p.RequireRole(
        Roles.PlatformAdmin))
    .AddPolicy(Policies.RequireOwner, p => p.RequireRole(
        Roles.Owner, Roles.PlatformAdmin))
    .AddPolicy(Policies.RequireReviewer, p => p.RequireRole(
        Roles.Reviewer, Roles.Owner, Roles.PlatformAdmin))
    .AddPolicy(Policies.RequireOperator, p => p.RequireRole(
        Roles.Operator, Roles.Reviewer, Roles.Owner, Roles.PlatformAdmin))
    .AddPolicy(Policies.RequireViewer, p => p.RequireRole(
        Roles.Viewer, Roles.Operator, Roles.Reviewer, Roles.Owner, Roles.PlatformAdmin))
    .AddPolicy(Policies.RequireAgent, p => p.RequireRole(
        Roles.Agent))
    .AddPolicy(Policies.RequireAgentReadOnly, p => p.RequireRole(
        Roles.AgentReadOnly, Roles.Agent))
    .AddPolicy(Policies.RequireAuthenticatedAny, p => p.RequireRole(
        Roles.Viewer, Roles.Operator, Roles.Reviewer, Roles.Owner, Roles.PlatformAdmin,
        Roles.Agent, Roles.AgentReadOnly))
    // SSE stream policy accepts EITHER the JWT bearer scheme (rich CLI
    // and server-to-server clients) OR the single-use SseToken scheme
    // (browser EventSource). Endpoint-scoped opt-in via
    // `.RequireAuthorization("SseStream")` on the stream routes only.
    .AddPolicy("SseStream", p => p
        .AddAuthenticationSchemes(
            SseTokenAuthHandler.SchemeName,
            JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());

// -- Database + Infrastructure (PostgreSQL + pgvector, Redis, repositories, services) --
builder.Services.AddInfrastructure(builder.Configuration);

// -- SignalR + Redis backplane --
// ConnectionFactory is invoked lazily by SignalR at hub-start time, AFTER
// ConfigureAppConfiguration overrides have landed on builder.Configuration.
// Reading the connection string at registration time (the eager overload)
// would freeze the appsettings.json value and break integration tests'
// Testcontainers override — same timing trap we hit with the Npgsql data
// source in Phase 4.
builder.Services.AddSignalR()
    .AddStackExchangeRedis(opts =>
    {
        opts.Configuration.ChannelPrefix = RedisChannel.Literal("agentic:");
        opts.ConnectionFactory = async writer =>
        {
            var cs = builder.Configuration.GetConnectionString("redis")
                ?? throw new InvalidOperationException(
                    "Connection string 'redis' is required for the SignalR backplane.");
            return await ConnectionMultiplexer.ConnectAsync(cs, writer);
        };
    });

// SignalREventRelay bridges Redis pub/sub → SignalR groups so the Worker
// (or any publisher) can fan out without depending on hub types.
builder.Services.AddHostedService<SignalREventRelay>();

// -- Core services --
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
builder.Services.AddScoped<IProjectAuthorizationService, ProjectAuthorizationService>();
// RedisIdempotencyService replaces the Phase-4 in-memory stub so the cache
// survives Api replica scale-out. The in-memory class is kept in source
// for dev fallback / unit tests but is no longer the production binding.
// IdempotencyOptions exposes the claim/response TTLs as configuration so
// ops can tune them per environment without code changes.
builder.Services
    .AddOptions<IdempotencyOptions>()
    .Bind(builder.Configuration.GetSection(IdempotencyOptions.SectionName));
builder.Services.AddSingleton<IIdempotencyService, RedisIdempotencyService>();

// -- Health checks --
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

// -- Rate limiting (per-user sliding window) --
var rl = builder.Configuration.GetSection("RateLimiting");
builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    opts.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var key = ctx.User.FindFirst("oid")?.Value
               ?? ctx.Connection.RemoteIpAddress?.ToString()
               ?? "anon";
        return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit       = rl.GetValue<int>("PermitLimit", 600),
                Window            = TimeSpan.FromSeconds(rl.GetValue<int>("WindowSeconds", 60)),
                SegmentsPerWindow = 6,
                QueueLimit        = 0,
                AutoReplenishment = true
            });
    });

    opts.AddPolicy("strict", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rl.GetValue<int>("StrictPermitLimit", 10),
                Window      = TimeSpan.FromMinutes(1)
            }));

    opts.OnRejected = async (ctx, ct) =>
    {
        // Rate-limit responses must match the rest of the API's error
        // contract (RFC 9457 ProblemDetails with a machine-readable code).
        // Plain text here meant clients couldn't structurally identify
        // 429s — see docs/005-standards/05-api-design-standards.md.
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.Headers["Retry-After"] = "60";
        ctx.HttpContext.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Status     = StatusCodes.Status429TooManyRequests,
            Title      = "Too many requests. Please retry after 60 seconds.",
            Extensions =
            {
                ["code"]    = ErrorCodes.RateLimited,
                ["traceId"] = ctx.HttpContext.TraceIdentifier
            }
        };
        await ctx.HttpContext.Response.WriteAsJsonAsync(problem, ct);
    };
});

// -- CORS --
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p
        .WithOrigins(allowedOrigins)
        .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
        .WithHeaders("Authorization", "Content-Type", "If-Match", "X-Correlation-ID", "X-Idempotency-Key")
        .AllowCredentials()
        .SetPreflightMaxAge(TimeSpan.FromHours(1))));

// -- Security headers --
builder.Services.AddSecurityHeaderPolicies(options =>
    options.SetDefaultPolicy(new HeaderPolicyCollection()
        .AddDefaultApiSecurityHeaders()
        .AddStrictTransportSecurityMaxAgeIncludeSubDomains(maxAgeInSeconds: 60 * 60 * 24 * 365)
        .AddContentTypeOptionsNoSniff()
        .AddReferrerPolicyStrictOriginWhenCrossOrigin()
        .AddFrameOptionsDeny()
        .AddContentSecurityPolicy(b =>
        {
            b.AddDefaultSrc().None();
            b.AddFrameAncestors().None();
            b.AddFormAction().None();
        })
        .RemoveServerHeader()));

// -- Swagger --
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opts =>
{
    opts.SwaggerDoc("v1", new() { Title = "Agentic Workforce API", Version = "v1" });
    opts.AddSecurityDefinition("Bearer", new()
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header
    });
    opts.AddSecurityRequirement(new()
    {
        [new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
});

// -- Exception handling --
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ---------------------------------------------------------------------------
var app = builder.Build();
// ---------------------------------------------------------------------------

// Schema must be present before any IHostedService.StartAsync runs (Phase 7d's
// PlatformActorSeeder inserts into the Users table, so a missing schema would
// crash the host). Testing env shares this need with Development — integration
// tests use a fresh Testcontainers PostgreSQL on every run.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>()
        .Database.MigrateAsync();
}
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSecurityHeaders();
app.UseExceptionHandler();
app.UseCors();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Plugin-style discovery: every static class under Features/* with a
// `public static void MapEndpoints(IEndpointRouteBuilder app)` is registered
// automatically. Adding an endpoint is a single-file change.
app.MapFeatureSlices();

// SignalR hub for live project + session updates.
app.MapHub<ProjectHub>("/hubs/project");

app.MapDefaultEndpoints();

await app.RunAsync();

#pragma warning disable S1118
public partial class Program;
#pragma warning restore S1118
