using AgenticWorkforce.Infrastructure;
using AgenticWorkforce.ServiceDefaults;
using AgenticWorkforce.ServiceDefaults.Observability;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddObservability("AgenticWorkforce.Worker", source: "worker");

builder.Services.AddInfrastructure(builder.Configuration);

// Durable Task, Agent Runtime registered in Phase 6+

var host = builder.Build();
await host.RunAsync();
