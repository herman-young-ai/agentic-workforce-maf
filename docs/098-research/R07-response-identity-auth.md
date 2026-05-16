# Production-Ready Entra ID, API Key, and Managed Identity Patterns for ASP.NET Core 8/9 on Azure Container Apps

**Target stack:** .NET 8/9, ASP.NET Core 8/9, Azure Container Apps, Microsoft Entra ID. **Date of currency:** 10 May 2026.

## TL;DR

- **Use `Microsoft.Identity.Web` 4.9.x (latest 4.x stable on NuGet) with `AddMicrosoftIdentityWebApi`** for Entra ID JWT validation, layer a custom `AuthenticationHandler<T>` for API keys, and stitch them together with an `AddPolicyScheme` + `ForwardDefaultSelector` so a single `[Authorize]` attribute accepts either credential. Map app roles via `roles` claim, declare them in the Microsoft Graph app manifest (`appRoles[]`), and use `[Authorize(Roles = "...")]` plus a custom `IAuthorizationHandler` for resource (mission) checks.
- **For agent workloads on Azure Container Apps**, attach a User‑Assigned Managed Identity in the Container App's `identity` block, set `AZURE_CLIENT_ID` (or `ManagedIdentityClientId` on `DefaultAzureCredentialOptions`) to bind that specific identity, and instantiate `AzureOpenAIClient`, `SecretClient`, and `BlobServiceClient` with that single `DefaultAzureCredential` — no secrets stored in the app.
- **For SSE under MSAL.js**, mint a single‑use, 30‑second Redis‑backed exchange token from an authenticated `POST /api/sse/token` endpoint, append it to the EventSource URL as `?t=...`, and validate + atomically delete it server‑side on connection. Bicep with the `Microsoft.Graph/applications@v1.0` extension (GA July 2025) lets you declare the Entra app registration and the Container App together as IaC.

---

## Key Findings

| Area | Decision / Current Best Practice (May 2026) |
|---|---|
| Identity library for ASP.NET Core | `Microsoft.Identity.Web` 4.9.x. v4 dropped net6/net7, targets net8.0, net9.0, net462+. `AddMicrosoftIdentityWebApi` remains the entry point. |
| Azure SDK | `Azure.Identity` 1.21.x. (Note: `Azure.Core` 1.53+ has begun moving identity types into Core — pin transitives carefully to avoid `CS0433` collisions.) `Azure.AI.OpenAI` 2.1.0 GA / 2.2.0‑beta. |
| MSAL.js (SPA) | `@azure/msal-browser` 5.x + `@azure/msal-react` 5.3.x. Supports React 16.8+, 17, 18, and 19 (19.2.1+ — 19.0.0–19.2.0 are excluded by the package due to CVE‑2025‑55182). |
| SSE in .NET | Manual `text/event-stream` writes (works on .NET 8/9). .NET 10 ships `Results.ServerSentEvents(IAsyncEnumerable<T>)` and `SseItem<T>`, but not yet on the LTS line targeted here. |
| App Registration IaC | `Microsoft.Graph/applications@v1.0` Bicep extension is GA (29 Jul 2025). Use it to declare app registration, app roles, identifier URIs, and service principals next to your Container App. |
| Multi‑scheme auth | The canonical pattern is `AddPolicyScheme("JWT_OR_APIKEY", …, options.ForwardDefaultSelector = …)` that inspects the request and forwards to either `Bearer` (Entra) or `ApiKey` (custom). |
| Managed identity on Container Apps | User‑assigned MI is the production default. Set the `identity` block on `Microsoft.App/containerApps`, plus `AZURE_CLIENT_ID` env var so `DefaultAzureCredential()` in code resolves to that exact MI. |

---

## Details

### Q1. ASP.NET Core + Entra ID setup in Program.cs

#### NuGet packages (latest stable, May 2026)

```xml
<ItemGroup>
  <!-- Identity / auth -->
  <PackageReference Include="Microsoft.Identity.Web" Version="4.9.0" />
  <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.4" />
  <!-- Azure SDK -->
  <PackageReference Include="Azure.Identity" Version="1.21.0" />
  <PackageReference Include="Azure.Security.KeyVault.Secrets" Version="4.7.0" />
  <PackageReference Include="Azure.Storage.Blobs" Version="12.24.0" />
  <PackageReference Include="Azure.AI.OpenAI" Version="2.1.0" />
  <!-- Distributed cache for SSE token store -->
  <PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="9.0.4" />
</ItemGroup>
```

`Microsoft.Identity.Web` already pulls in `Microsoft.AspNetCore.Authentication.JwtBearer` transitively, but pinning it explicitly to the framework version of ASP.NET Core you ship on avoids unwanted resolutions.

> **Migration note (3.x → 4.x).** The 4.0 release removed `IDownstreamWebApi`/`AddDownstreamWebApi` (replaced by `IDownstreamApi`/`AddDownstreamApi`), removed `TokenAcquisitionTokenCredential`/`TokenAcquisitionAppTokenCredential` (replaced by `MicrosoftIdentityTokenCredential`), removed the synchronous `WithClientCredentials`, and removed `IMsalTokenCacheProvider.InitializeAsync`. Scopes are now `string[]` rather than space‑separated strings on the new APIs. Plan these refactors when moving to 4.x.

#### `appsettings.json`

```json
{
  "AzureAd": {
    "Instance":  "https://login.microsoftonline.com/",
    "TenantId":  "<your-tenant-guid>",
    "ClientId":  "<api-app-registration-client-id>",
    "Audience":  "api://<api-app-registration-client-id>"
  },
  "ApiKey": {
    "HeaderName": "X-Api-Key"
  },
  "Redis": {
    "Endpoint": "<your-managed-redis>.<region>.redis.azure.net:10000"
  }
}
```

#### `Program.cs` (Entra ID + API Key + dual policy scheme)

```csharp
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Net.Http.Headers;
using Investec.Agents.Api.Auth;       // ApiKeyAuthenticationHandler / Options
using Investec.Agents.Api.Authorization; // MissionMembershipRequirement / Handler

const string ApiKeyScheme   = "ApiKey";
const string CompositeScheme = "JwtOrApiKey";

var builder = WebApplication.CreateBuilder(args);

// 1) Entra ID JWT bearer (named "Bearer" — the JwtBearerDefaults.AuthenticationScheme).
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme          = CompositeScheme;
        options.DefaultChallengeScheme = CompositeScheme;
    })
    .AddMicrosoftIdentityWebApi(
        jwtBearerOptions =>
        {
            builder.Configuration.Bind("AzureAd", jwtBearerOptions);
            // Entra App Roles arrive in the "roles" claim. Tell ASP.NET Core
            // that this is the role claim so [Authorize(Roles=...)] works.
            jwtBearerOptions.TokenValidationParameters.RoleClaimType = "roles";
            jwtBearerOptions.TokenValidationParameters.NameClaimType  = "preferred_username";
        },
        identityOptions => builder.Configuration.Bind("AzureAd", identityOptions),
        jwtBearerScheme: JwtBearerDefaults.AuthenticationScheme)
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

// 2) Custom API Key handler (separate scheme).
builder.Services
    .AddAuthentication() // chain another scheme onto the same builder
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyScheme,
        options =>
        {
            options.HeaderName = builder.Configuration["ApiKey:HeaderName"] ?? "X-Api-Key";
        });

// 3) Composite policy scheme — selects the real scheme based on the request.
builder.Services
    .AddAuthentication()
    .AddPolicyScheme(CompositeScheme, "JWT or API Key", options =>
    {
        options.ForwardDefaultSelector = ctx =>
        {
            // Prefer API key if the caller sent one (no Authorization header involvement)
            if (ctx.Request.Headers.ContainsKey(
                    builder.Configuration["ApiKey:HeaderName"] ?? "X-Api-Key"))
                return ApiKeyScheme;

            // Otherwise look for a Bearer token
            string? auth = ctx.Request.Headers[HeaderNames.Authorization];
            if (!string.IsNullOrEmpty(auth) &&
                auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = auth["Bearer ".Length..].Trim();
                if (new JwtSecurityTokenHandler().CanReadToken(token))
                    return JwtBearerDefaults.AuthenticationScheme;
            }

            // No credential — let JwtBearer handle the 401 challenge.
            return JwtBearerDefaults.AuthenticationScheme;
        };
    });

// 4) Authorization — define a default policy that accepts EITHER scheme,
//    plus role and resource policies.
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyScheme)
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy("RequireAdmin",   p => p.RequireRole("admin", "sysadmin"));
    options.AddPolicy("RequireSysadmin", p => p.RequireRole("sysadmin"));

    options.AddPolicy("MissionMember", p =>
        p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyScheme)
         .RequireAuthenticatedUser()
         .Requirements.Add(new MissionMembershipRequirement()));
});

builder.Services.AddSingleton<IAuthorizationHandler, MissionMembershipHandler>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
```

#### Custom API Key handler

```csharp
// Auth/ApiKeyAuthenticationOptions.cs
using Microsoft.AspNetCore.Authentication;

namespace Investec.Agents.Api.Auth;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-Api-Key";
}
```

```csharp
// Auth/ApiKeyAuthenticationHandler.cs
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Investec.Agents.Api.Auth;

public sealed class ApiKeyAuthenticationHandler
    : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IApiKeyValidator _keys;

    // .NET 8/9 constructor (no ISystemClock — that overload was removed).
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyValidator keys)
        : base(options, logger, encoder)
    {
        _keys = keys;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var headerVal))
            return AuthenticateResult.NoResult();

        var raw = headerVal.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return AuthenticateResult.Fail("Empty API key.");

        var record = await _keys.ValidateAsync(raw, Context.RequestAborted);
        if (record is null)
            return AuthenticateResult.Fail("Invalid or revoked API key.");

        // Map API key -> ClaimsPrincipal. Encode the same role names used by Entra
        // so [Authorize(Roles="...")] checks work uniformly across both schemes.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, record.PrincipalId),
            new("sub",                     record.PrincipalId),
            new(ClaimTypes.Name,           record.Name),
            new("client_type",             "api_key"),
            new("api_key_id",              record.KeyId)
        };
        foreach (var role in record.Roles) // e.g. "viewer","user","admin","sysadmin"
            claims.Add(new Claim("roles", role));

        var identity  = new ClaimsIdentity(claims, Scheme.Name, "sub", "roles");
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = $"ApiKey realm=\"investec-agents\"";
        return Task.CompletedTask;
    }
}

public interface IApiKeyValidator
{
    Task<ApiKeyRecord?> ValidateAsync(string presented, CancellationToken ct);
}

public sealed record ApiKeyRecord(
    string KeyId, string PrincipalId, string Name, IReadOnlyList<string> Roles);
```

`IApiKeyValidator` should hash the presented key (Argon2id or HMAC‑SHA256 with a per‑key salt), look up the record by key id (the public prefix), constant‑time compare the hash, check expiry and revocation, and only then return the record. Persist hashes — never the plaintext — in your store.

---

### Q2. Entra ID App Roles

#### App registration manifest (Microsoft Graph format)

App roles for the API app registration. Each `id` must be a distinct GUID; assigning multiple `allowedMemberTypes` makes the role assignable both to humans (via the Enterprise Application) and to other applications (client credentials):

```json
"appRoles": [
  {
    "id":   "11111111-1111-1111-1111-111111111111",
    "allowedMemberTypes": ["User", "Application"],
    "displayName": "Viewer",
    "description": "Read-only access to missions and audit logs.",
    "value": "viewer",
    "isEnabled": true
  },
  {
    "id":   "22222222-2222-2222-2222-222222222222",
    "allowedMemberTypes": ["User", "Application"],
    "displayName": "User",
    "description": "Standard user; can create and run missions.",
    "value": "user",
    "isEnabled": true
  },
  {
    "id":   "33333333-3333-3333-3333-333333333333",
    "allowedMemberTypes": ["User", "Application"],
    "displayName": "Admin",
    "description": "Tenant-wide read/write on missions and agents.",
    "value": "admin",
    "isEnabled": true
  },
  {
    "id":   "44444444-4444-4444-4444-444444444444",
    "allowedMemberTypes": ["User", "Application"],
    "displayName": "Sysadmin",
    "description": "Platform-level administration including identity provider config.",
    "value": "sysadmin",
    "isEnabled": true
  }
]
```

After saving, assign roles to users / groups in **Enterprise Applications → Users and groups** (this only works after **App registration → Properties → Assignment required = Yes** if you want to enforce explicit assignment). For service‑to‑service callers, grant the role as an **Application permission** under **API permissions** of the calling app and complete admin consent.

#### Token shape

Entra ID emits app roles in a **`roles`** claim (an array of role `value`s) in the JWT access token. ASP.NET Core's default role claim type is `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`, which is why `[Authorize(Roles = "admin")]` will silently fail unless you set:

```csharp
jwtBearerOptions.TokenValidationParameters.RoleClaimType = "roles";
```

(shown in the `Program.cs` above). With that one line, `User.IsInRole("admin")` and the `[Authorize(Roles = "...")]` attribute work as intended.

#### Controller usage

```csharp
[ApiController]
[Route("api/missions")]
public class MissionsController : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "viewer,user,admin,sysadmin")] // any of these
    public IActionResult List() => Ok();

    [HttpPost]
    [Authorize(Roles = "user,admin,sysadmin")]
    public IActionResult Create([FromBody] CreateMissionRequest req) => Ok();

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RequireAdmin")] // admin OR sysadmin
    public IActionResult Delete(Guid id) => NoContent();

    [HttpPost("identity-provider")]
    [Authorize(Policy = "RequireSysadmin")]
    public IActionResult ConfigureIdp() => Ok();
}
```

#### Hierarchical role checking

The cleanest way to express “admin can do everything user can do” is to **fan‑out** in policy definitions (already done above with `Roles = "user,admin,sysadmin"` and `RequireAdmin = admin OR sysadmin`). For more elaborate hierarchies, implement a `ClaimsTransformation` that adds implied roles:

```csharp
// Auth/RoleHierarchyClaimsTransformation.cs
public sealed class RoleHierarchyClaimsTransformation : IClaimsTransformation
{
    private static readonly Dictionary<string, string[]> Implied = new()
    {
        ["sysadmin"] = ["admin", "user", "viewer"],
        ["admin"]    = ["user", "viewer"],
        ["user"]     = ["viewer"]
    };

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = (ClaimsIdentity)principal.Identity!;
        var existing = identity.FindAll("roles").Select(c => c.Value).ToHashSet();

        foreach (var role in existing.ToArray())
            if (Implied.TryGetValue(role, out var more))
                foreach (var implied in more)
                    if (existing.Add(implied))
                        identity.AddClaim(new Claim("roles", implied));

        return Task.FromResult(principal);
    }
}

// In Program.cs:
builder.Services.AddTransient<IClaimsTransformation, RoleHierarchyClaimsTransformation>();
```

Now `[Authorize(Roles = "viewer")]` is satisfied by any of `viewer/user/admin/sysadmin`.

---

### Q3. Workload Identity / Managed Identity for Azure Container Apps

#### How `DefaultAzureCredential` resolves on a Container App

`DefaultAzureCredential` walks a chain (`EnvironmentCredential → WorkloadIdentityCredential → ManagedIdentityCredential → … → developer credentials`). On a Container App with **only one** system‑assigned identity, the parameterless `new DefaultAzureCredential()` works. **If you assign a user‑assigned identity (or multiple identities), you must tell DAC which one to use** — IMDS will otherwise return 400 for ambiguous requests. Two ways:

1. Set the `AZURE_CLIENT_ID` environment variable on the container — `DefaultAzureCredentialOptions.ManagedIdentityClientId` and `WorkloadIdentityClientId` both default to that env var. This is the most idiomatic option for Container Apps.
2. Pass it explicitly:

```csharp
var clientId = builder.Configuration["AzureAd:ManagedIdentityClientId"];
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = clientId,
    // ExcludeInteractiveBrowserCredential = true (default), etc.
});
```

#### Wiring the credential to clients (Azure AI Foundry / OpenAI, Key Vault, Storage)

```csharp
// In Program.cs — register a single TokenCredential and reuse it.
builder.Services.AddSingleton<TokenCredential>(_ =>
{
    var clientId = builder.Configuration["AzureAd:ManagedIdentityClientId"];
    return new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = clientId,
        // optional: ExcludeAzureCliCredential = !builder.Environment.IsDevelopment()
    });
});

// Azure AI Foundry / Azure OpenAI (Azure.AI.OpenAI 2.1+).
builder.Services.AddSingleton(sp =>
{
    var cred     = sp.GetRequiredService<TokenCredential>();
    var endpoint = new Uri(builder.Configuration["AzureOpenAI:Endpoint"]!);
    return new AzureOpenAIClient(endpoint, cred);
});
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<AzureOpenAIClient>()
      .GetChatClient(builder.Configuration["AzureOpenAI:Deployment"]!));

// Azure Key Vault.
builder.Services.AddSingleton(sp =>
    new SecretClient(
        new Uri(builder.Configuration["KeyVault:Uri"]!),
        sp.GetRequiredService<TokenCredential>()));

// Azure Storage (Blobs).
builder.Services.AddSingleton(sp =>
    new BlobServiceClient(
        new Uri(builder.Configuration["Storage:BlobEndpoint"]!),
        sp.GetRequiredService<TokenCredential>()));
```

The required RBAC role assignments on the user‑assigned identity's principal:

| Resource | Built‑in role |
|---|---|
| Azure OpenAI / Azure AI Services | **Cognitive Services OpenAI User** (`5e0bd9bd-7b93-4f28-af87-19fc36ad61bd`) |
| Azure Key Vault (RBAC mode) | **Key Vault Secrets User** (`4633458b-17de-408a-b874-0445c86b69e6`) |
| Azure Storage (Blobs) | **Storage Blob Data Contributor** (`ba92f5b4-2d11-453d-a403-e96b0029c9fe`) |

#### Bicep — User‑Assigned Managed Identity + Container App + role assignments

```bicep
// infra/main.bicep
@description('Region for all resources.')
param location string = resourceGroup().location
param appName     string = 'investec-agents'
param envName     string
param imageRef    string                                        // e.g. 'myacr.azurecr.io/agents:1.0.0'
param openAiResourceId   string
param keyVaultResourceId string
param storageAccountId   string

// 1) User-Assigned Managed Identity
resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name:     '${appName}-uami'
  location: location
}

// 2) Container Apps Environment (assumed pre-existing or co-deployed)
resource caEnv 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: envName
}

// 3) Container App with the UAMI bound and AZURE_CLIENT_ID injected
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name:     appName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${uami.id}': {}
    }
  }
  properties: {
    environmentId: caEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport:  'auto'   // SSE works fine over HTTP/1.1 streaming
        allowInsecure: false
        stickySessions: { affinity: 'sticky' } // helpful for SSE behind multi-replica
      }
      registries: [
        {
          server:   'myacr.azurecr.io'
          identity: uami.id
        }
      ]
    }
    template: {
      containers: [
        {
          name:  'api'
          image: imageRef
          resources: { cpu: json('1.0'), memory: '2Gi' }
          env: [
            // The single source of truth for DefaultAzureCredential.
            { name: 'AZURE_CLIENT_ID', value: uami.properties.clientId }
            { name: 'AzureAd__ManagedIdentityClientId', value: uami.properties.clientId }
            { name: 'AzureAd__TenantId', value: tenant().tenantId }
            { name: 'AzureOpenAI__Endpoint',  value: 'https://<your-openai>.openai.azure.com' }
            { name: 'AzureOpenAI__Deployment', value: 'gpt-4o-mini' }
            { name: 'KeyVault__Uri',          value: 'https://<your-kv>.vault.azure.net/' }
            { name: 'Storage__BlobEndpoint',  value: 'https://<your-sa>.blob.core.windows.net' }
          ]
        }
      ]
      scale: { minReplicas: 2, maxReplicas: 10 }
    }
  }
}

// 4) RBAC — grant the UAMI access to the data planes
var openAiUserRole          = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
var kvSecretsUserRole       = '4633458b-17de-408a-b874-0445c86b69e6'
var blobContributorRole     = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource roleOpenAi 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name:  guid(uami.id, openAiResourceId, openAiUserRole)
  scope: resourceId('Microsoft.CognitiveServices/accounts', last(split(openAiResourceId, '/')))
  properties: {
    principalId:      uami.properties.principalId
    principalType:    'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', openAiUserRole)
  }
}

resource roleKv 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name:  guid(uami.id, keyVaultResourceId, kvSecretsUserRole)
  scope: resourceId('Microsoft.KeyVault/vaults', last(split(keyVaultResourceId, '/')))
  properties: {
    principalId:      uami.properties.principalId
    principalType:    'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRole)
  }
}

resource roleStorage 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name:  guid(uami.id, storageAccountId, blobContributorRole)
  scope: resourceId('Microsoft.Storage/storageAccounts', last(split(storageAccountId, '/')))
  properties: {
    principalId:      uami.properties.principalId
    principalType:    'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobContributorRole)
  }
}

output uamiClientId   string = uami.properties.clientId
output uamiPrincipalId string = uami.properties.principalId
```

#### Bicep — Entra ID App Registration via Microsoft Graph extension (GA Jul 2025)

`bicepconfig.json`:

```json
{
  "extensions": {
    "microsoftGraphV1": "br:mcr.microsoft.com/bicep/extensions/microsoftgraph/v1.0:1.0.0"
  }
}
```

`infra/entra.bicep`:

```bicep
extension microsoftGraphV1

param apiDisplayName string = 'Investec Agents API'

resource apiApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName:  'investec-agents-api'
  displayName: apiDisplayName
  signInAudience: 'AzureADMyOrg'
  api: {
    requestedAccessTokenVersion: 2
    oauth2PermissionScopes: [
      {
        id: '55555555-5555-5555-5555-555555555555'
        adminConsentDescription: 'Access Investec Agents API as the signed-in user.'
        adminConsentDisplayName: 'Access as user'
        userConsentDescription:  'Access Investec Agents API as you.'
        userConsentDisplayName:  'Access as you'
        type: 'User'
        value: 'access_as_user'
        isEnabled: true
      }
    ]
  }
  identifierUris: [ 'api://investec-agents-api' ]
  appRoles: [
    { id: '11111111-1111-1111-1111-111111111111', allowedMemberTypes: ['User','Application'], displayName: 'Viewer',   description: 'Read-only.',   value: 'viewer',   isEnabled: true }
    { id: '22222222-2222-2222-2222-222222222222', allowedMemberTypes: ['User','Application'], displayName: 'User',     description: 'Standard.',    value: 'user',     isEnabled: true }
    { id: '33333333-3333-3333-3333-333333333333', allowedMemberTypes: ['User','Application'], displayName: 'Admin',    description: 'Tenant admin.', value: 'admin',    isEnabled: true }
    { id: '44444444-4444-4444-4444-444444444444', allowedMemberTypes: ['User','Application'], displayName: 'Sysadmin', description: 'Platform admin.', value: 'sysadmin', isEnabled: true }
  ]
}

resource apiSp 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: apiApp.appId
}

resource spaApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName:  'investec-agents-spa'
  displayName: 'Investec Agents SPA'
  signInAudience: 'AzureADMyOrg'
  spa: {
    redirectUris: [ 'https://agents.investec.com', 'http://localhost:5173' ]
  }
  requiredResourceAccess: [
    {
      resourceAppId: apiApp.appId
      resourceAccess: [
        { id: '55555555-5555-5555-5555-555555555555', type: 'Scope' } // access_as_user
      ]
    }
  ]
}

output apiAppId string = apiApp.appId
output spaAppId string = spaApp.appId
```

The deploying principal needs Microsoft Graph permissions `Application.ReadWrite.All`, `AppRoleAssignment.ReadWrite.All`, and `DelegatedPermissionGrant.ReadWrite.All`.

---

### Q4. SSE token‑exchange pattern for `EventSource`

**Why it's needed.** The browser's `EventSource` API does not allow custom headers (no `Authorization: Bearer …`). Cookies are the other native option but cross‑site cookies require `SameSite=None; Secure` plus careful CSRF design. The standard production pattern for SPA → SSE behind Entra is:

1. SPA holds an Entra ID access token (acquired with MSAL).
2. SPA `POST`s that token to `/api/sse/token` (a normal authenticated endpoint).
3. Server mints a 128‑bit random opaque token, stores it in Redis with 30‑second TTL keyed by `sse:tok:{token}` → JSON value `{ sub, name, roles, missionId }`.
4. SPA opens `new EventSource('/api/sse/missions/{id}/events?t={ssetoken}')`.
5. The SSE endpoint atomically `GETDEL`s the token from Redis (single‑use), reconstructs a `ClaimsPrincipal`, and starts streaming. The token is never reusable; if the connection drops, the client fetches a new one.

#### Token‑exchange endpoint

```csharp
// Sse/SseTokenService.cs
using System.Security.Cryptography;
using System.Text.Json;
using StackExchange.Redis;

namespace Investec.Agents.Api.Sse;

public sealed record SseTicket(
    string Sub, string Name, string[] Roles, string? MissionId);

public interface ISseTokenStore
{
    Task<string> CreateAsync(SseTicket ticket, CancellationToken ct);
    Task<SseTicket?> ConsumeAsync(string token, CancellationToken ct);
}

public sealed class RedisSseTokenStore(IConnectionMultiplexer redis) : ISseTokenStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    public async Task<string> CreateAsync(SseTicket ticket, CancellationToken ct)
    {
        var raw    = RandomNumberGenerator.GetBytes(32);
        var token  = Convert.ToBase64String(raw)
                            .TrimEnd('=').Replace('+','-').Replace('/','_');
        var key    = $"sse:tok:{token}";
        var json   = JsonSerializer.Serialize(ticket);

        var db = redis.GetDatabase();
        // SET NX with TTL — guarantees uniqueness.
        var ok = await db.StringSetAsync(key, json, Ttl, When.NotExists);
        if (!ok) throw new InvalidOperationException("Token collision; retry.");
        return token;
    }

    public async Task<SseTicket?> ConsumeAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var db = redis.GetDatabase();
        // Atomic single-use: GETDEL is a single round-trip on Redis 6.2+.
        var raw = await db.StringGetDeleteAsync($"sse:tok:{token}");
        if (raw.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<SseTicket>(raw!);
    }
}
```

```csharp
// In Program.cs
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    // For Azure Managed Redis with Entra ID auth (recommended in production):
    var endpoint = builder.Configuration["Redis:Endpoint"]!;
    var options  = new ConfigurationOptions
    {
        EndPoints = { endpoint },
        Protocol  = RedisProtocol.Resp3,
        AbortOnConnectFail = false
    };
    options.ConfigureForAzureWithTokenCredentialAsync(
        new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = builder.Configuration["AzureAd:ManagedIdentityClientId"]
        })).GetAwaiter().GetResult();
    return ConnectionMultiplexer.Connect(options);
});
builder.Services.AddSingleton<ISseTokenStore, RedisSseTokenStore>();
```

#### Mint endpoint and consuming SSE endpoint

```csharp
// Sse/SseEndpoints.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Investec.Agents.Api.Sse;

public static class SseEndpoints
{
    public static void MapSseEndpoints(this IEndpointRouteBuilder routes)
    {
        // 1) Mint a one-time SSE token. Authenticated with Entra JWT (or API key).
        routes.MapPost("/api/sse/token",
            async (HttpContext ctx, ISseTokenStore store, string? missionId) =>
            {
                var user = ctx.User;
                var ticket = new SseTicket(
                    Sub:       user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!,
                    Name:      user.Identity?.Name ?? "",
                    Roles:     user.FindAll("roles").Select(c => c.Value).ToArray(),
                    MissionId: missionId);

                var token = await store.CreateAsync(ticket, ctx.RequestAborted);
                return Results.Ok(new { token, expiresInSeconds = 30 });
            })
            .RequireAuthorization(); // uses default policy = JWT or ApiKey

        // 2) The SSE stream itself — anonymous at the framework level; we
        //    authenticate via the single-use ticket.
        routes.MapGet("/api/sse/missions/{missionId}/events",
            async (string missionId, string t, HttpContext ctx,
                   ISseTokenStore store, IMissionEventBus bus,
                   IAuthorizationService authz) =>
            {
                var ticket = await store.ConsumeAsync(t, ctx.RequestAborted);
                if (ticket is null) { ctx.Response.StatusCode = 401; return; }

                // Reconstruct a ClaimsPrincipal so [Authorize] / handlers work.
                var claims = new List<Claim>
                {
                    new("sub", ticket.Sub),
                    new(ClaimTypes.Name, ticket.Name)
                };
                claims.AddRange(ticket.Roles.Select(r => new Claim("roles", r)));
                ctx.User = new ClaimsPrincipal(
                    new ClaimsIdentity(claims, "SseTicket", "sub", "roles"));

                // Resource-based authz (mission membership OR admin/sysadmin).
                var ok = await authz.AuthorizeAsync(ctx.User, missionId, "MissionMember");
                if (!ok.Succeeded) { ctx.Response.StatusCode = 403; return; }

                ctx.Response.Headers.ContentType  = "text/event-stream";
                ctx.Response.Headers.CacheControl = "no-cache, no-transform";
                ctx.Response.Headers["X-Accel-Buffering"] = "no"; // disable proxy buffering
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

                await foreach (var evt in bus.SubscribeAsync(missionId, ctx.RequestAborted))
                {
                    await ctx.Response.WriteAsync(
                        $"id: {evt.Id}\nevent: {evt.Type}\ndata: {evt.Json}\n\n",
                        ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
            })
            .AllowAnonymous();
    }
}
```

The browser side:

```ts
// SPA
const { token } = await fetch('/api/sse/token?missionId=' + id, {
  method: 'POST',
  headers: { Authorization: `Bearer ${msalAccessToken}` }
}).then(r => r.json());

const es = new EventSource(`/api/sse/missions/${id}/events?t=${encodeURIComponent(token)}`);
es.addEventListener('agent.message', e => render(JSON.parse(e.data)));
es.onerror = () => { /* will refetch a new ticket on reconnect */ };
```

**Why query parameter is acceptable here, with caveats:** the ticket is opaque, single‑use, expires in 30 s, and is **not** the user's real Entra access token. Exposure in proxy logs is therefore low‑impact, but you should still configure your reverse proxy / Application Gateway to scrub the `t` query parameter from access logs, and never log the URL of the SSE endpoint with this parameter intact.

> **.NET 10 note (forward‑looking, not in the LTS line).** .NET 10 ships `Results.ServerSentEvents(IAsyncEnumerable<T>)` and `SseItem<T>` for a typed SSE pipeline with built‑in `Last-Event-ID` support. When you upgrade, the streaming endpoint above can be reduced to a `MapGet` returning `Results.ServerSentEvents(...)` while keeping the same ticket‑exchange middleware in front.

---

### Q5. Resource‑based authorization for missions

```csharp
// Authorization/MissionMembershipRequirement.cs
using Microsoft.AspNetCore.Authorization;

namespace Investec.Agents.Api.Authorization;

public sealed class MissionMembershipRequirement : IAuthorizationRequirement { }
```

```csharp
// Authorization/MissionMembershipHandler.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Investec.Agents.Api.Authorization;

public sealed class MissionMembershipHandler(
    IMissionMembershipReader memberships,
    IHttpContextAccessor http)
    : AuthorizationHandler<MissionMembershipRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MissionMembershipRequirement requirement)
    {
        // 1) admin/sysadmin always wins.
        if (context.User.IsInRole("admin") || context.User.IsInRole("sysadmin"))
        {
            context.Succeed(requirement);
            return;
        }

        // 2) Resolve the mission id from the resource OR the route.
        string? missionId = context.Resource switch
        {
            string s => s,
            Mission m => m.Id.ToString(),
            HttpContext h => h.GetRouteValue("missionId")?.ToString(),
            _ => http.HttpContext?.GetRouteValue("missionId")?.ToString()
        };
        if (string.IsNullOrWhiteSpace(missionId)) return; // no resource -> fail

        // 3) Stable subject identifier (oid for Entra, principal id for API key).
        var subject = context.User.FindFirstValue("oid")
                   ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? context.User.FindFirstValue("sub");
        if (subject is null) return;

        if (await memberships.IsMemberAsync(missionId, subject, default))
            context.Succeed(requirement);
    }
}

public interface IMissionMembershipReader
{
    Task<bool> IsMemberAsync(string missionId, string subjectId, CancellationToken ct);
}
```

Registered in `Program.cs` (already shown):

```csharp
builder.Services.AddSingleton<IAuthorizationHandler, MissionMembershipHandler>();
builder.Services.AddScoped<IMissionMembershipReader, SqlMissionMembershipReader>();
```

Imperative use for actions that load a mission first:

```csharp
public sealed class MissionsController(IAuthorizationService authz, IMissionRepo repo) : ControllerBase
{
    [HttpGet("{missionId:guid}/messages")]
    public async Task<IActionResult> Messages(Guid missionId)
    {
        var mission = await repo.GetAsync(missionId);
        if (mission is null) return NotFound();

        var result = await authz.AuthorizeAsync(User, mission, "MissionMember");
        if (!result.Succeeded) return Forbid();

        return Ok(await repo.MessagesAsync(missionId));
    }
}
```

Declarative use (route value‑driven):

```csharp
[HttpPost("missions/{missionId:guid}/run")]
[Authorize(Policy = "MissionMember")]
public Task<IActionResult> Run(Guid missionId) { /* ... */ }
```

---

### Q6. Dual authentication scheme — recap

The combination shown in **Q1** is the canonical production pattern:

- `AddMicrosoftIdentityWebApi` registers the `Bearer` JWT scheme (Entra ID).
- `AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKey", …)` registers the API‑key scheme.
- `AddPolicyScheme("JwtOrApiKey", …)` is set as the **default** scheme; its `ForwardDefaultSelector` inspects the request and returns either `"Bearer"` or `"ApiKey"`. Only one underlying handler runs per request — avoiding the well‑known “signature key not found” spam you get when ASP.NET Core invokes every JwtBearer handler in turn.
- `AddAuthorization(...).DefaultPolicy` is rebuilt to call `AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyScheme)` so any `[Authorize]` accepts either.
- For endpoints that should accept **only** Entra (e.g. user‑identity‑specific actions), apply `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`. For agent‑only endpoints, apply `[Authorize(AuthenticationSchemes = "ApiKey")]`.

---

### Entra ID App Registration checklist (what to set in the portal / Bicep)

For the **API** (`Investec Agents API`):

1. **Supported account types** — *Single tenant* (`AzureADMyOrg`).
2. **Authentication** — leave empty (no redirect URIs needed for a Web API).
3. **Expose an API**
   - Set Application ID URI → `api://<client-id>` (or a verified domain).
   - Add scope `access_as_user` — admin and user consent.
   - **Add a client application** (the SPA's appId) so it's pre‑authorized for that scope.
4. **App roles** — declare `viewer`, `user`, `admin`, `sysadmin` (`allowedMemberTypes: ['User','Application']`) as in Q2.
5. **Token configuration**
   - Add optional claim `idtyp`/`xms_idrel` if you need to distinguish app vs delegated tokens.
   - Tokens v2 (already implied by `requestedAccessTokenVersion: 2` in the manifest).
6. **API permissions** — Microsoft Graph `User.Read` (delegated) only if your API itself calls Graph; otherwise nothing.
7. **Manifest** — `accessTokenAcceptedVersion: 2`, `groupMembershipClaims: null` (use app roles, not group claims, to avoid the 200‑group cap), `signInAudience: "AzureADMyOrg"`.
8. **Enterprise application → Properties** — set **Assignment required = Yes** so only assigned users/groups receive tokens.
9. **Enterprise application → Users and groups** — assign each user/group to one of the four app roles.

For the **SPA** (`Investec Agents SPA`):

1. **Supported account types** — single tenant.
2. **Authentication → Single‑page application** redirect URIs: `https://agents.investec.com`, `http://localhost:5173`. Mark **Access tokens** *not* required (PKCE flow); MSAL handles this.
3. **API permissions** — delegated `api://<api-client-id>/access_as_user` + admin consent.
4. **Token configuration** — optional `email`, `family_name`, `given_name`, `preferred_username` claims as needed by the UI.

---

### MSAL.js current best practice with an ASP.NET Core backend (May 2026)

- Use `@azure/msal-browser` 5.x and `@azure/msal-react` 5.3.x (both via NPM; the CDN shipped with msal‑browser was fully deprecated as of v3).
- React 16.8+, 17, 18, and 19 (≥19.2.1) are supported. `MsalProvider` wraps the app; use `useMsalAuthentication`, `MsalAuthenticationTemplate`, `useIsAuthenticated`, `AuthenticatedTemplate`/`UnauthenticatedTemplate` for declarative gates.
- Acquire access tokens for the API with `acquireTokenSilent({ scopes: ['api://<api-client-id>/access_as_user'] })`, falling back to `acquireTokenRedirect` on `InteractionRequiredAuthError`.
- Configure the `PublicClientApplication` with `cacheLocation: 'localStorage'` only when you need cross‑tab SSO; otherwise `sessionStorage` is safer. Set `authority: 'https://login.microsoftonline.com/<tenant-id>'`. PKCE is mandatory and is the default — never use the implicit flow.
- Always call `await pca.initialize()` (5.x requirement) before rendering MsalProvider.
- For your ASP.NET Core backend, the SPA appId must be in the API's **knownClientApplications** or **preAuthorizedApplications** list so consent flows through cleanly.

---

### Notes on recent changes you should plan for

- **Microsoft.Identity.Web 4.0.0** dropped `IDownstreamWebApi`, `AddDownstreamWebApi`, the synchronous `WithClientCredentials`, `IMsalTokenCacheProvider.InitializeAsync`, `TokenAcquisitionTokenCredential`, and `TokenAcquisitionAppTokenCredential`. Replace with `IDownstreamApi`/`AddDownstreamApi` and `MicrosoftIdentityTokenCredential` (set `RequestAppToken = true` for app tokens). Scopes are `string[]` everywhere.
- **Azure.Identity 1.21+ / Azure.Core 1.53+.** `Azure.Core` has begun hosting identity types directly. If you also pull in `Azure.Identity` transitively (e.g. via `Microsoft.Extensions.Azure` 1.13.x or `Azure.Extensions.AspNetCore.DataProtection.Blobs` 1.5.2+), you may hit `CS0433: type 'DefaultAzureCredential' exists in both 'Azure.Core' and 'Azure.Identity'` until Microsoft realigns the dependency tree. Workaround: `<PackageReference Include="Azure.Identity" ExcludeAssets="all" PrivateAssets="all" />` to suppress the transitive when you've moved to Core‑hosted credentials, or pin all Azure SDK packages to a known‑good combination.
- **`AZURE_TOKEN_CREDENTIALS`** environment variable (added late 2025) lets you force `DefaultAzureCredential` into either `prod` (Environment + Workload + ManagedIdentity) or `dev` (developer credentials only). Set `AZURE_TOKEN_CREDENTIALS=prod` on Container Apps to skip developer‑credential probes and improve cold‑start time.
- **Microsoft Graph Bicep extension** is GA (`v1.0:1.0.0`). You can now declare app registrations, service principals, app role assignments, federated identity credentials, OAuth2 permission grants, and groups in Bicep — eliminating the script‑plus‑Bicep split. You still need an interactive deploy (or a deploying SP with appropriate Graph permissions) to apply Graph resources.
- **App Roles vs Groups.** Continue to prefer app roles (`roles` claim) over group claims for authorization — group claims are limited to ~200 IDs in a JWT before Graph fall‑through is required, while app roles do not have that limit and surface in `roles` directly.
- **SSE on .NET 10.** Once you move to .NET 10, replace the manual writer in Q4 with `Results.ServerSentEvents(IAsyncEnumerable<SseItem<T>>)` and the framework will emit `id`, `event`, and `retry` fields and respect `Last-Event-ID` re‑subscription automatically.

---

## Caveats

- **`DefaultAzureCredential` in production.** Microsoft's own guidance increasingly recommends using `ManagedIdentityCredential` (or `WorkloadIdentityCredential`) **directly** in production instead of `DefaultAzureCredential` — the credential chain is convenient for local‑to‑cloud parity but pays cold‑start latency for probes you don't need. For Investec's Container Apps deployment, consider `new ManagedIdentityCredential(new ResourceIdentifier(uami.id))` (or the client‑ID overload) once you've stabilised the local‑dev story, or set `AZURE_TOKEN_CREDENTIALS=prod` to short‑circuit the developer‑tool branch.
- **API key tokens in URLs.** SSE tickets in query strings are an accepted compromise but they *will* end up in HTTP access logs and proxy traces. The 30‑second TTL + single‑use semantics make this acceptable for short‑lived session tickets, not for long‑lived secrets. Confirm your front door (App Gateway / Front Door / Nginx ingress) scrubs the `t` parameter from logs.
- **Sticky sessions for SSE.** Multi‑replica Container Apps without sticky sessions will load‑balance the long‑lived SSE connection independently of the publish path, so events written on replica A may not reach a subscriber on replica B. Configure `stickySessions: { affinity: 'sticky' }` (shown in the Bicep) **and** back the event bus with Redis pub/sub or Azure Service Bus so any replica can serve any subscriber.
- **Bicep app registration deployment.** `Microsoft.Graph/applications@v1.0` resources are POST‑created in Graph, not PUT, so re‑deployments use `uniqueName` as the idempotency key — don't change `uniqueName` after first deploy or you'll create a duplicate app registration. Personal Microsoft account principals cannot be used to deploy these resources.
- **`Microsoft.Identity.Web` v4 vs older docs.** A lot of search results still reference `AddDownstreamWebApi`, `TokenAcquisitionTokenCredential`, etc. Those APIs are removed in v4; do not paste them into new code.
- **Role hierarchy via `ClaimsTransformation`.** It is convenient but runs on every request. If you have hot paths where the principal arrives from a JWT, you may prefer to bake the hierarchy into the policy definitions (as shown) or memoize the transformation per token‑hash to avoid repeated work.
- **Conflicting search snippets.** Some sources describe `AddJwtBearer` being called twice and combined via `DefaultPolicy.AddAuthenticationSchemes(...)`. Microsoft's own docs note that this causes both handlers to validate every request and emit `Failed to validate the token` log noise for the wrong issuer. The `AddPolicyScheme` + `ForwardDefaultSelector` approach used here is the correct one for "either/or" auth.
- **Library versions are accurate as of the date of writing (10 May 2026).** Check NuGet for newer 4.x patch releases of `Microsoft.Identity.Web` and 1.2x of `Azure.Identity`; both libraries follow semver and patch‑level updates are expected to be drop‑in.