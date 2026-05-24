using AgenticWorkforce.Agents;
using AgenticWorkforce.Infrastructure;
using AgenticWorkforce.ServiceDefaults;
using AgenticWorkforce.ServiceDefaults.Observability;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddObservability("AgenticWorkforce.Worker", source: "worker");

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAgentServices(builder.Configuration);

// Durable Task wiring lands in Phase 8.

var host = builder.Build();
await host.RunAsync();
