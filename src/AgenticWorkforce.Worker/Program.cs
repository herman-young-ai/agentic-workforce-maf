using AgenticWorkforce.Agents;
using AgenticWorkforce.Infrastructure;
using AgenticWorkforce.ServiceDefaults;
using AgenticWorkforce.ServiceDefaults.Observability;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddObservability("AgenticWorkforce.Worker", source: "worker");

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAgentServices(builder.Configuration);

// Bridge for AgentSeedService (Phase 7): Infrastructure cannot reference Agents directly
// (layer rule), so the Worker passes the Agents assembly handle here. The assembly owns
// the embedded YAML resources scanned by EmbeddedYamlAgentSeedSource.
builder.Services.AddAgentSeedingFromAssembly(typeof(AgentsAssemblyMarker).Assembly);

// Durable Task wiring lands in Phase 8.

var host = builder.Build();
await host.RunAsync();
