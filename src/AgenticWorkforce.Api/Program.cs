using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using NetEscapades.AspNetCore.SecurityHeaders;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Api.Core.Exceptions;
using AgenticWorkforce.Api.Core.Health;
using AgenticWorkforce.Api.Core.Middleware;
using AgenticWorkforce.Api.Core.Observability;
using AgenticWorkforce.Infrastructure;
using AgenticWorkforce.Infrastructure.Data;
using AgenticWorkforce.ServiceDefaults;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// -- Aspire ServiceDefaults (OTel, health checks, service discovery) --
builder.AddServiceDefaults();

// -- Observability (Serilog + PII masking) --
builder.AddObservability();

// -- Azure Key Vault (production) --
if (builder.Environment.IsProduction())
{
    var kvUri = builder.Configuration["KeyVault:Uri"]
        ?? throw new InvalidOperationException("KeyVault:Uri is required in production.");
    builder.Configuration.AddAzureKeyVault(new Uri(kvUri), new DefaultAzureCredential());
}

// -- Authentication (Entra ID) --
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
{
    var clientId = builder.Configuration["AzureAd:ClientId"] ?? "";
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
        Roles.Agent, Roles.AgentReadOnly));

// -- Database + Infrastructure (PostgreSQL + pgvector, repositories, services) --
var connectionString = builder.Configuration.GetConnectionString("agenticworkforce")
    ?? throw new InvalidOperationException("Connection string 'agenticworkforce' is required.");
builder.Services.AddInfrastructure(connectionString);

// -- Core services --
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();

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
        ctx.HttpContext.Response.Headers["Retry-After"] = "60";
        await ctx.HttpContext.Response.WriteAsync("Too many requests.", ct);
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

// -- Controllers & Swagger --
builder.Services.AddControllers();
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

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>()
        .Database.MigrateAsync();
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
app.MapControllers();
app.MapDefaultEndpoints();

await app.RunAsync();

#pragma warning disable S1118
public partial class Program;
#pragma warning restore S1118
